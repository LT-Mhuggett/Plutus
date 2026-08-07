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
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)}: {m.Value.Trim()}");

        Assert.True(offenders.Count == 0,
            "Money must be integer pence in platform code. Offenders:\n  " + string.Join("\n  ", offenders));
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
        var allowed = new[] { "Plutus.SharedKernel", "Plutus.Contracts.Client" };
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
