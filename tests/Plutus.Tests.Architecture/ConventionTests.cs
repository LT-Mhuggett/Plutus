using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// T0.4 cross-cutting conventions, enforced by scanning src/ and the backend host on disk
/// (no product references). Scope is the NEW platform code under src/ and the host — the
/// legacy shared libraries under Plutus/Commons (which predate the pence rule and use
/// decimal money) are intentionally out of scope until they are modernised.
/// </summary>
public class ConventionTests
{
    private const string MoneyWords = "price|cost|amount|total|pence|charge|fee";

    [Fact]
    public void No_module_declares_decimal_or_double_money_members()
    {
        // public decimal/double member whose name contains a money word (spec T0.4 rule 2).
        var rx = new Regex(
            $@"public\s+(?:virtual\s+|override\s+|static\s+|readonly\s+)*(?:decimal|double)\??\s+\w*(?:{MoneyWords})\w*\b",
            RegexOptions.IgnoreCase);

        var offenders = new List<string>();
        foreach (var file in Repo.CsFiles("src"))
        {
            if (VerbatimLegacyWireContracts.Contains(Path.GetFileName(file))) continue;
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}: {m.Value.Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "Money must be integer pence in platform code. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⚠ THE ONLY FILES ALLOWED DECIMAL MONEY, and each needs a reason written here rather than a
    /// rename that dodges the regex.
    ///
    /// <b>ItemContracts.cs</b> — a VERBATIM mirror of the legacy `/api/Item` wire contract, whose
    /// columns are `decimal` and which the WEB TILL posts to in production today. The DTO is a
    /// passthrough: read the item, change the one field the operator changed, send it all back
    /// (the PUT binds the whole entity, so anything omitted is written back as its default).
    /// Converting to pence and back would insert a rounding step between the till and the catalogue
    /// on every edit — and this is not a clean dataset: the live catalogue has held items with a
    /// £7.99 price against a £799.00 ex-price, which is what the server's band guard now exists to
    /// stop. Preserving the server's own numbers exactly is safer than normalising them.
    ///
    /// ⚠ This is an exception for a WIRE MIRROR, never for logic. Anything that ADDS, COMPARES or
    /// APPORTIONS money still uses integer pence — see `Pence`, `VatLineMath`, `TenderLoop`.
    /// </summary>
    private static readonly HashSet<string> VerbatimLegacyWireContracts = new()
    {
        "ItemContracts.cs",
    };

    /// <summary>
    /// ⚠ The exclusion above must stay HONEST: an excluded file may mirror the wire, but it must not
    /// grow arithmetic. A `decimal` money field is a passthrough; a `decimal` money CALCULATION is
    /// the thing the pence rule exists to prevent, and putting one behind an exclusion would hide it
    /// from the guard entirely.
    /// </summary>
    [Fact]
    public void The_decimal_money_exclusions_contain_no_arithmetic()
    {
        var arithmetic = new Regex(@"(decimal|Price|Cost|ExPrice)\s*[*/+]\s*|Math\.Round\s*\(", RegexOptions.IgnoreCase);

        var offenders = new List<string>();
        foreach (var file in Repo.CsFiles("src"))
        {
            if (!VerbatimLegacyWireContracts.Contains(Path.GetFileName(file))) continue;
            foreach (Match m in arithmetic.Matches(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}: {m.Value.Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "A file excluded from the pence rule may MIRROR the wire, never compute with it. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_module_mints_entity_ids_with_Guid_NewGuid()
    {
        // Entity IDs use Uuid7.New() (D4); Guid.NewGuid is only allowed in SharedKernel
        // (nowhere currently) and tests. (spec T0.4 rule 4)
        var offenders = new List<string>();
        foreach (var file in Repo.CsFiles("src"))
        {
            if (file.Contains("Plutus.SharedKernel")) continue;
            if (Regex.IsMatch(File.ReadAllText(file), @"Guid\.NewGuid\s*\("))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "Use Uuid7.New() for entity IDs, not Guid.NewGuid(). Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Backend_does_not_use_server_side_ui_frameworks_or_reference_frontends()
    {
        // Frontend isolation (D20): the backend serves no HTML — no Blazor/Razor/SPA-services
        // packages, and no project reference into frontends. (spec T0.4 rule 5)
        var banned = new Regex(@"(SpaServices|Blazor|Mvc\.Razor|Razor\.Components|Microsoft\.AspNetCore\.Components)",
            RegexOptions.IgnoreCase);

        var csprojs = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Append(Path.Combine(Repo.Root(), "Plutus", "Endpoints", "Plutus.DBService", "Plutus.DBService.csproj"));

        var offenders = new List<string>();
        foreach (var csproj in csprojs.Where(File.Exists))
        {
            var text = File.ReadAllText(csproj);
            if (banned.IsMatch(text)) offenders.Add($"{Path.GetFileName(csproj)}: server-side UI package");
            if (Regex.IsMatch(text, @"ProjectReference[^>]*frontends", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"ProjectReference[^>]*Plutus\.Frontend", RegexOptions.IgnoreCase))
                offenders.Add($"{Path.GetFileName(csproj)}: references a frontend project");
        }

        Assert.True(offenders.Count == 0,
            "Backend must stay isolated from frontends (static SPAs, /api only). Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Till_client_libraries_stay_free_of_MAUI_and_backend_modules()
    {
        // MAUI retrofit WP1 (M0.1 rule). Plutus.Client.Core + Plutus.Contracts.Client are the
        // till's half of the platform. Two directions must both hold:
        //   • no MAUI/UI dependency — otherwise the outbox and token policy stop being testable
        //     on a build agent, and the rules that decide whether a shop's takings reach the
        //     server become verifiable only on a physical till;
        //   • no BACKEND module reference — a till must speak the wire contract, never link the
        //     server's internals (that is how a client ends up needing a MySQL context).
        //
        // ⚠ `Plutus.TillAgent.Core` is on the list and it is the ONLY entry that is not under
        // `src/`. It is the print-op WIRE CONTRACT — the shape `POST /print` binds on the Plutus
        // Till Agent — and it is a pure contract project: no MAUI, no packages, no Windows, one
        // TargetFramework and zero ProjectReferences of its own, so it costs this library nothing
        // and stays testable on a build agent. It is here rather than in `Contracts.Client` because
        // the agent defines the endpoint and must not depend on the till's contract assembly.
        // ⚠ The alternative was declaring `PrintOp`/`PrintDocument` a second time in Client.Core,
        // which is a silent drift the first time an op is added: the till would send `kind: 5`, the
        // agent would print nothing, and no error would surface anywhere.
        var allowed = new[] { "Plutus.SharedKernel", "Plutus.Contracts.Client", "Plutus.TillAgent.Core" };
        var offenders = new List<string>();

        foreach (var name in new[] { "Plutus.Client.Core", "Plutus.Contracts.Client" })
        {
            var csproj = Path.Combine(Repo.Root(), "src", name, name + ".csproj");
            if (!File.Exists(csproj)) continue;
            // Scan the REFERENCES, not the file text — these csprojs explain in prose why they
            // must stay MAUI-free, and a naive text match flags its own documentation.
            var text = File.ReadAllText(csproj);

            foreach (Match m in Regex.Matches(text, @"PackageReference\s+Include=""([^""]+)"""))
            {
                if (Regex.IsMatch(m.Groups[1].Value, @"(Maui|Xamarin|Syncfusion|CommunityToolkit)", RegexOptions.IgnoreCase))
                    offenders.Add($"{name}: references UI/MAUI package {m.Groups[1].Value}");
            }

            foreach (Match m in Regex.Matches(text, @"ProjectReference\s+Include=""([^""]+)"""))
            {
                var referenced = Path.GetFileNameWithoutExtension(m.Groups[1].Value);
                if (!allowed.Contains(referenced))
                    offenders.Add($"{name}: references {referenced} (only SharedKernel + Contracts.Client are allowed)");
            }
        }

        Assert.True(offenders.Count == 0,
            "The till client libraries must stay MAUI-free and backend-module-free. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_print_wire_contract_stays_a_pure_contract()
    {
        // ⚠ THIS IS WHAT PAYS FOR THE EXCLUSION ABOVE. `Plutus.Client.Core` is allowed to reference
        // `Plutus.TillAgent.Core` ONLY because that project has nothing in it — no packages, no
        // project references, no Windows. The moment it grows one, `Client.Core` inherits it
        // silently, and the rule that keeps the till's money-handling code testable on a build
        // agent with no device is gone with nothing to say so.
        //
        // A widened allow-list without this test is not a narrower rule; it is no rule.
        var csproj = Path.Combine(Repo.Root(), "tools", "Plutus.TillAgent.Core", "Plutus.TillAgent.Core.csproj");
        Assert.True(File.Exists(csproj), $"Expected the print wire contract at {csproj}");

        var text = File.ReadAllText(csproj);
        var offenders = new List<string>();

        foreach (Match m in Regex.Matches(text, @"PackageReference\s+Include=""([^""]+)"""))
            offenders.Add($"package {m.Groups[1].Value}");
        foreach (Match m in Regex.Matches(text, @"ProjectReference\s+Include=""([^""]+)"""))
            offenders.Add($"project {Path.GetFileNameWithoutExtension(m.Groups[1].Value)}");

        // ⚠ And it must stay platform-neutral: a `net10.0-windows` target here would make the
        // till's client library unbuildable on the Mac and on CI.
        foreach (Match m in Regex.Matches(text, @"<TargetFrameworks?>([^<]+)</TargetFrameworks?>"))
            if (m.Groups[1].Value.Contains("windows", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"platform-specific target {m.Groups[1].Value}");

        Assert.True(offenders.Count == 0,
            "Plutus.TillAgent.Core must stay a dependency-free, platform-neutral contract, because "
            + "Plutus.Client.Core is allowed to reference it. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// WP2c-exempt: NO TILL MAY HOLD A VAT RATE OF ITS OWN.
    ///
    /// Matt's directive is that all VAT guidance comes from the portal down to the tills, and the
    /// reason this needs a test rather than a comment is that Plutus expects tills on Windows, macOS
    /// and Linux. A hard-coded rate on a client is invisible until a government changes a rate, and
    /// then it mis-states VAT on every sale that till takes, silently. Two rules:
    ///   • the till libraries stay platform-neutral (`net10.0`, no OS-specific target), so a macOS or
    ///     Linux till is a build target rather than a rewrite; and
    ///   • the VAT arithmetic lives in SharedKernel, so every .NET till gets ONE implementation.
    /// The one exception is documented in `Cutover.FallbackBandsBp` — a till cutting over before it
    /// has ever reached the server, where the alternative is refusing to let a shop open.
    /// </summary>
    [Fact]
    public void Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own()
    {
        var offenders = new List<string>();
        var clientLibs = new[] { "Plutus.Client.Core", "Plutus.Contracts.Client", "Plutus.Client.Storage" };

        foreach (var name in clientLibs)
        {
            var dir = Path.Combine(Repo.Root(), "src", name);
            if (!Directory.Exists(dir)) continue;

            // 1. Platform-neutral target — an OS-specific TFM here would make a Mac/Linux till a
            //    port instead of a build.
            var csproj = Path.Combine(dir, name + ".csproj");
            if (File.Exists(csproj))
            {
                var tfms = Regex.Matches(File.ReadAllText(csproj), @"<TargetFrameworks?>([^<]+)<")
                    .SelectMany(m => m.Groups[1].Value.Split(';'))
                    .Select(t => t.Trim()).Where(t => t.Length > 0);
                foreach (var tfm in tfms)
                    if (Regex.IsMatch(tfm, @"-(windows|android|ios|maccatalyst|tizen)", RegexOptions.IgnoreCase))
                        offenders.Add($"{name}: platform-specific target framework '{tfm}' — a till library "
                                    + "must build for macOS and Linux unchanged.");
            }

            // 2. No literal VAT rates. Basis points for the real UK bands are the tell.
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

                foreach (var (line, i) in File.ReadAllLines(file).Select((l, i) => (l, i)))
                {
                    // Comments explain WHY these libraries must not hold rates — don't flag the prose.
                    var code = Regex.Replace(line, @"//.*$", "").Trim();
                    if (code.Length == 0 || code.StartsWith("///") || code.StartsWith("*")) continue;
                    // The documented cutover fallback is the single sanctioned exception.
                    if (code.Contains("FallbackBandsBp")) continue;

                    if (Regex.IsMatch(code, @"\b(1750|2000|500)\b") &&
                        Regex.IsMatch(code, @"(?i)(vat|rate|band|tax)"))
                        offenders.Add($"{name}/{Path.GetFileName(file)}:{i + 1}: looks like a hard-coded "
                                    + $"VAT rate — bands come from GET /api/v1/vat/bands. → {code}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A till must never decide a VAT rule, and must build for any OS. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Messaging_seam_has_no_concrete_provider_wired_in_core()
    {
        // WP17.3: like the billing seam, core wires ZERO concrete mail/SMS provider — only the
        // IMessageSender abstraction + the config catalogue (provider *names* + field schemas) live
        // here; the actual SDK/adapter drops in later. So we ban real USAGE (SDK `using`s, the
        // System.Net.Mail SmtpClient type, provider NuGet packages) — NOT provider names as strings,
        // which the notification catalogue legitimately carries.
        var bannedUsage = new Regex(@"\busing\s+(SendGrid|Twilio|MailKit|PostmarkDotNet|Amazon\.SimpleEmail|SparkPost|Mailgun)\b|\bSmtpClient\b|\bnew\s+SendGridClient\b",
            RegexOptions.IgnoreCase);
        var bannedPackage = new Regex(@"PackageReference[^>]*Include\s*=\s*""[^""]*(SendGrid|Mailgun|Twilio|MailKit|Postmark|AWSSDK\.SimpleEmail|SparkPost)[^""]*""",
            RegexOptions.IgnoreCase);

        var offenders = new List<string>();
        foreach (var file in Repo.CsFiles("src"))
            foreach (Match m in bannedUsage.Matches(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}: {m.Value.Trim()}");
        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src"), "*.csproj", SearchOption.AllDirectories))
            if (bannedPackage.IsMatch(File.ReadAllText(csproj))) offenders.Add($"{Path.GetFileName(csproj)}: provider package");

        Assert.True(offenders.Count == 0,
            "The messaging seam must stay provider-free in core — config catalogue only, no wired SDK (WP17.3). Offenders:\n  " + string.Join("\n  ", offenders));
    }

    // spec T0.4 rule 3 (tenant-owned entities carry a global query filter) is now enforced
    // via IModel metadata in Plutus.Tests.Unit.TenancyTests — it needs a product reference
    // (MySqlDbContext), which this disk-scanning project deliberately avoids.
}
