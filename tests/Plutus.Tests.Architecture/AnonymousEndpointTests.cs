using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// Every way into this API without a credential is listed here, on purpose.
///
/// ⚠ WHY THIS EXISTS. On 2026-08-08 an audit found `ATestController` — a leftover scaffold with a
/// `GET /api/ATest/testTransaction` that took **no authentication and WROTE A ROW** to the
/// production database on every call, un-rate-limited, on a public hostname. Nobody put it there
/// maliciously and nobody noticed it for months, because nothing in the build had an opinion about
/// a new controller appearing without a gate. There is also no global fallback authorization policy
/// (`ConfigureAuthorization()` is commented out in Startup) — auth is per-attribute, so a forgotten
/// attribute is an open door rather than a closed one.
///
/// So the allow-lists below are the point: adding an anonymous endpoint is now a deliberate edit to
/// a test that someone reviews, instead of an absence nobody sees. If one of these fails, the fix is
/// almost never "add it to the list" — read the endpoint first and ask whether it should be gated.
///
/// Scanned on disk rather than by reflection, matching the rest of this suite (no product refs).
/// </summary>
public class AnonymousEndpointTests
{
    /// <summary>The directories that make up the deployed API.</summary>
    private static readonly string[] ApiRoots = { "src", Path.Combine("Plutus", "Endpoints") };

    /// <summary>
    /// Controllers that deliberately carry NO class-level <c>[Authorize]</c>. Each one is an entry
    /// point that has to work before a credential exists — that is the only justification.
    /// </summary>
    private static readonly Dictionary<string, string> UngatedControllers = new()
    {
        ["AuthController"] = "The password login entry point: /method and /Login run before a session exists. SetPassword carries its own [Authorize].",
        ["TokensController"] = "Device-token exchange — a till presents its enrolment secret, which IS the credential. Rate-limited 5/min per IP.",
        ["PasswordResetController"] = "Reset request/complete: by definition reachable by someone who cannot sign in. Class-level [AllowAnonymous] + rate limit.",
        ["WebstoreWebhookController"] = "Inbound Woo webhook, authenticated by signature over the raw body rather than by a bearer token.",
        ["PingController"] = "WP16a reachability. Anonymous ON PURPOSE and touches no database: a till that is un-enrolled or revoked still needs to learn whether the server is there.",
    };

    /// <summary>
    /// Individual actions marked <c>[AllowAnonymous]</c> inside an otherwise-gated controller —
    /// the sharper case, because the class looks protected at a glance.
    /// </summary>
    private static readonly Dictionary<string, string> AnonymousActions = new()
    {
        ["TillsController"] = "POST /tills/enrol — a device redeems a one-time code it was given; it has no token yet. Rate-limited.",
        ["PlatformController"] = "The billing provider's webhook, HMAC-verified.",
        ["PlatformJobsController"] = "Out-of-process job reporting, HMAC over the raw body (JOBS_REPORT_SECRET); 503 when unconfigured.",
        ["WebstoresController"] = "The webstore callback, signature-checked.",
    };

    private static IEnumerable<string> ApiCsFiles() =>
        ApiRoots.SelectMany(Repo.CsFiles);

    /// <summary>One HTTP action and how it is gated.</summary>
    private sealed record Action(string Controller, string File, string Method, bool Gated, bool Anonymous);

    /// <summary>
    /// Walk the source line by line, tracking the attribute block above each member.
    ///
    /// ⚠ Gating in this codebase is mostly PER ACTION (<c>[Authorize(Policy = "perm:…")]</c> on the
    /// method), not per class — so "the class has no [Authorize]" is not a finding on its own, and a
    /// test that treated it as one would cry wolf on ~28 healthy controllers and get deleted.
    /// What matters is whether each ACTION ends up gated by either.
    /// </summary>
    private static List<Action> ActionsIn(string file)
    {
        var actions = new List<Action>();
        var lines = File.ReadAllLines(file);
        var attrs = new List<string>();
        string currentClass = "";
        bool classGated = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith('[')) { attrs.Add(line); continue; }
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")) continue;

            var cls = Regex.Match(line, @"class\s+(\w*Controller)\s*:\s*(?:ControllerBase|Controller)\b");
            if (cls.Success)
            {
                currentClass = cls.Groups[1].Value;
                classGated = attrs.Any(a => a.Contains("Authorize") && !a.Contains("AllowAnonymous"));
                attrs.Clear();
                continue;
            }

            // A member declaration: anything with a parameter list at method level.
            if (currentClass.Length > 0 && line.Contains('(') && Regex.IsMatch(line, @"\b(public|internal|private|protected)\b"))
            {
                var isAction = attrs.Any(a => Regex.IsMatch(a, @"^\[Http(Get|Post|Put|Delete|Patch|Head)\b"));
                if (isAction)
                {
                    var anonymous = attrs.Any(a => a.Contains("AllowAnonymous"));
                    var gated = classGated || attrs.Any(a => a.Contains("Authorize") && !a.Contains("AllowAnonymous"));
                    var name = Regex.Match(line, @"(\w+)\s*\(").Groups[1].Value;
                    actions.Add(new Action(currentClass, Path.GetFileName(file), name, gated, anonymous));
                }
            }

            attrs.Clear();
        }

        return actions;
    }

    [Fact]
    public void Every_HTTP_action_is_either_gated_or_a_known_anonymous_entry_point()
    {
        // ⚠ THE TEST THAT WOULD HAVE CAUGHT ATestController. Its action carried [HttpGet] and no
        // authorization of any kind, on a class with none either — reachable by anyone on the
        // internet, and writing a row every time.
        var offenders = new List<string>();

        foreach (var file in ApiCsFiles())
            foreach (var a in ActionsIn(file))
            {
                if (a.Gated && !a.Anonymous) continue;
                if (UngatedControllers.ContainsKey(a.Controller) || AnonymousActions.ContainsKey(a.Controller)) continue;
                offenders.Add($"{a.Controller}.{a.Method} ({a.File}){(a.Anonymous ? " — [AllowAnonymous]" : " — NO authorization attribute at all")}");
            }

        Assert.True(offenders.Count == 0,
            "These HTTP actions are reachable without a credential and are not in the reviewed allow-list.\n" +
            "⚠ There is NO global fallback authorization policy in this app, so a missing attribute is an\n" +
            "open door, not a closed one. Read the endpoint before adding it to the list.\n" +
            "Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_allow_listed_controllers_really_do_contain_anonymous_actions()
    {
        // The other direction: an exemption that no longer protects anything is an exemption that
        // will silently cover the NEXT thing added to that controller.
        var anonymousByController = new Dictionary<string, bool>();
        foreach (var file in ApiCsFiles())
            foreach (var a in ActionsIn(file))
                anonymousByController[a.Controller] =
                    anonymousByController.GetValueOrDefault(a.Controller) || a.Anonymous || !a.Gated;

        var pointless = UngatedControllers.Keys.Concat(AnonymousActions.Keys)
            .Where(n => anonymousByController.TryGetValue(n, out var hasAny) && !hasAny)
            .ToList();

        Assert.True(pointless.Count == 0,
            "These controllers are exempted but every action in them is now gated. Remove the entry:\n  "
            + string.Join("\n  ", pointless));
    }

    [Fact]
    public void Every_AllowAnonymous_action_sits_in_a_controller_that_is_meant_to_have_one()
    {
        var offenders = new List<string>();

        foreach (var file in ApiCsFiles())
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("AllowAnonymous")) continue;

            foreach (Match m in Regex.Matches(text, @"class\s+(\w*Controller)\s*:\s*(?:ControllerBase|Controller)\b"))
            {
                var name = m.Groups[1].Value;
                if (UngatedControllers.ContainsKey(name) || AnonymousActions.ContainsKey(name)) continue;
                offenders.Add($"{name} ({Path.GetFileName(file)})");
            }
        }

        Assert.True(offenders.Count == 0,
            "[AllowAnonymous] appears in a controller with no reviewed reason.\n" +
            "This is the sharper case: the class looks protected, and one action is not.\n" +
            "Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_allow_lists_do_not_name_controllers_that_have_been_deleted()
    {
        // ⚠ Keeps the lists honest in the other direction. A stale entry is a licence for a FUTURE
        // controller to reuse a retired name and inherit its exemption silently — which is exactly
        // how `ATestController` would come back.
        var present = new HashSet<string>();
        foreach (var file in ApiCsFiles())
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"class\s+(\w*Controller)\s*:\s*(?:ControllerBase|Controller)\b"))
                present.Add(m.Groups[1].Value);

        var stale = UngatedControllers.Keys.Concat(AnonymousActions.Keys).Where(n => !present.Contains(n)).ToList();

        Assert.True(stale.Count == 0,
            "These controllers are exempted but no longer exist. Delete the entry:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void No_controller_writes_to_the_database_without_authentication()
    {
        // The specific shape of the ATestController hole, named so it cannot recur quietly: an
        // un-gated controller that also persists something. Reading anonymously is a decision;
        // WRITING anonymously is almost always an accident, and an unmetered one at that.
        //
        // The four signature-verified webhooks legitimately write — their credential is the HMAC
        // over the body rather than a bearer token — so they are checked by the lists above instead.
        var writeCall = new Regex(@"\b(SaveChangesAsync|SaveChanges|ExecuteNonQueryAsync|\.Create\(|\.Add\()", RegexOptions.None);
        var webhookOrJob = new[] { "WebstoreWebhookController", "PlatformJobsController", "PlatformController", "WebstoresController" };

        var offenders = new List<string>();
        foreach (var file in ApiCsFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"class\s+(\w*Controller)\s*:\s*(?:ControllerBase|Controller)\b"))
            {
                var name = m.Groups[1].Value;
                if (!UngatedControllers.ContainsKey(name) || webhookOrJob.Contains(name)) continue;

                // AuthController and PasswordResetController legitimately write (credentials,
                // last-login stamps) as part of authenticating someone. Ping and Tokens must not.
                if (name is "AuthController" or "PasswordResetController") continue;

                if (writeCall.IsMatch(text))
                    offenders.Add($"{name} ({Path.GetFileName(file)}) — un-gated AND persists");
            }
        }

        Assert.True(offenders.Count == 0,
            "An anonymous endpoint writes to the database. This is how ATestController let anyone\n" +
            "create unlimited Business rows on a public hostname. Offenders:\n  " + string.Join("\n  ", offenders));
    }
}
