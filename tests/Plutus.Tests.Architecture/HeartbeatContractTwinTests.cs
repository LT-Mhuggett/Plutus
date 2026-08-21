using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// **The heartbeat's wire contract exists TWICE, and this is what stops them drifting.**
///
/// ⚠⚠ `Plutus.Contracts.Client.HeartbeatResult` is what every till DESERIALISES against;
/// `HeartbeatController.HeartbeatResult` is what the server SERIALISES. Two records, same shape, two
/// assemblies — because `Plutus.Tenancy` does not reference the client contract project, and adding
/// that reference would point the server at a client library.
///
/// ⚠⚠ **UNTIL 2026-08-21 THEY WERE MATCHED BY NAME AND BY LUCK.** The controller's own comment said
/// so and asked whoever touched one to touch the other. A field added to one and not the other is a
/// field the till silently never sees: no error, no log, just a feature that does nothing — the
/// quietest failure this platform can produce.
///
/// ⚠ WP-TICKETS walked straight into it while adding `UnreadSupportReplies`. The field went on the
/// contract, the server carried on compiling against its own copy, and the only reason it surfaced
/// was that the call site used a NAMED argument. A positional one would have shipped.
///
/// ⚠ SOURCE TEXT, NOT REFLECTION — this suite references no product projects on purpose (see
/// `Repo`), so it reads the two declarations off disk and compares them.
///
/// ⚠ Compared by NAME, TYPE **and ORDER**. Order matters as much as the rest: these are positional
/// records, and two lists carrying the same names in a different order deserialise into each
/// other's values without a murmur.
/// </summary>
public class HeartbeatContractTwinTests
{
    private static readonly string ContractFile =
        Path.Combine("src", "Plutus.Contracts.Client", "SyncContracts.cs");

    private static readonly string ServerFile =
        Path.Combine("src", "Plutus.Tenancy", "Controllers", "HeartbeatController.cs");

    /// <summary>
    /// The primary-constructor parameters of `record HeartbeatResult(...)`, normalised.
    ///
    /// ⚠ DEFAULTS AND WHITESPACE ARE STRIPPED, types and names are not. `= null` on one side and not
    /// the other is a compatible difference (it changes nothing on the wire); a different type or a
    /// different order is not.
    /// </summary>
    private static List<string> FieldsIn(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root(), relativePath));

        var m = Regex.Match(text, @"record\s+HeartbeatResult\s*\((?<body>[^)]*)\)", RegexOptions.Singleline);
        Assert.True(m.Success, $"Could not find `record HeartbeatResult(...)` in {relativePath}.");

        return m.Groups["body"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // strip comments and default values
            .Select(p => Regex.Replace(p, @"//.*$", "", RegexOptions.Multiline))
            .Select(p => p.Split('=')[0].Trim())
            .Select(p => Regex.Replace(p, @"\s+", " "))
            .Where(p => p.Length > 0)
            .ToList();
    }

    [Fact]
    public void The_two_heartbeat_records_declare_the_same_shape()
    {
        var contract = FieldsIn(ContractFile);
        var server = FieldsIn(ServerFile);

        Assert.True(contract.Count > 0, "Parsed no fields from the contract — the regex has drifted.");

        Assert.True(contract.Count == server.Count,
            $"The heartbeat twins have different field counts: contract {contract.Count}, server "
            + $"{server.Count}. A field on one and not the other is a field the till silently never "
            + $"sees.\n  contract: {string.Join(" | ", contract)}\n  server:   {string.Join(" | ", server)}");

        for (var i = 0; i < contract.Count; i++)
            Assert.True(contract[i] == server[i],
                $"Heartbeat field {i} differs — contract '{contract[i]}', server '{server[i]}'. "
                + "These are positional records: a mismatch here silently deserialises one field's "
                + "value into another.");
    }
}
