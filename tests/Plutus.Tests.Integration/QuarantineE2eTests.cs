using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// **Platform → Quarantine: seeing what is stuck, and clearing it accountably.**
///
/// ⚠⚠ THE SCREEN EXISTS BECAUSE THE HEALTH DOT COULD ACCUSE AND NOT EXPLAIN. `quarantineOpen > 0`
/// turns a tenant red for ever, and until 2026-08-22 there was no list endpoint anywhere: seven of
/// Kapow's sales were stuck with no way to learn which, why, or what to do.
///
/// ⚠ THE SOURCE CLASSIFICATION IS THE PART WORTH PINNING. `PayloadJson` means three different
/// things depending on who wrote it and nothing records which — the migrator stores a bare LEGACY
/// REF (`PayloadJson = input.LegacyId`), the ingest stores a serialised request, the webstore
/// stores a Woo order. Get it wrong and the screen offers Retry on a row with no sale behind it,
/// which would tell an operator the sale had been recovered when nothing happened.
/// </summary>
public class QuarantineE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public QuarantineE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private HttpRequestMessage R(HttpMethod m, string url, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
        return req;
    }

    /// <summary>
    /// One row of each kind. ⚠ Unique refs per run — this fixture is shared, and a seed that
    /// collides with a sibling's row makes the suite pass or fail on test ORDER (runbook pitfall 24).
    /// </summary>
    private async Task<(Guid migration, Guid ingest)> SeedAsync()
    {
        var migrationId = Uuid7.New();
        var ingestId = Uuid7.New();
        var stamp = Guid.NewGuid().ToString("N")[..8];

        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MySqlDbContext>();
        // ⚠ The context refuses to save without an actor — see RepositoryContext.SaveMethods.
        db.CurrentUser = "quarantine-e2e";

        // The migrator's shape: the payload IS the legacy reference, not a sale.
        db.SaleQuarantine.Add(new SaleQuarantine
        {
            Id = migrationId, TenantId = Kapow, SaleId = Uuid7.New(),
            PayloadJson = $"LEGACY-{stamp}",
            Reason = $"Sale LEGACY-{stamp}: A sale must have at least one line.",
            ReceivedAtUtc = DateTime.UtcNow,
        });

        // The ingest's shape: a full serialised request.
        db.SaleQuarantine.Add(new SaleQuarantine
        {
            Id = ingestId, TenantId = Kapow, SaleId = Uuid7.New(),
            PayloadJson = JsonSerializer.Serialize(new { SaleId = Uuid7.New(), GrossPence = 1200, Lines = Array.Empty<object>() }),
            Reason = $"Ingest {stamp}: net tender does not equal gross.",
            ReceivedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return (migrationId, ingestId);
    }

    private static JsonElement Row(JsonElement list, Guid id) =>
        list.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task Open_rows_are_listed_with_the_source_that_decides_what_can_be_done()
    {
        var (migration, ingest) = await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(R(HttpMethod.Get, "/api/v1/platform/quarantine?state=open"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<JsonElement>();

        var m = Row(list, migration);
        Assert.Equal("migration", m.GetProperty("source").GetString());
        // ⚠ The legacy ref is the ONLY handle a migration row has — it must reach the screen.
        Assert.StartsWith("LEGACY-", m.GetProperty("reference").GetString());
        Assert.False(m.GetProperty("canRetry").GetBoolean());

        var i = Row(list, ingest);
        Assert.Equal("ingest", i.GetProperty("source").GetString());
        Assert.True(i.GetProperty("canRetry").GetBoolean());
    }

    /// <summary>⚠ A dismissal with no reason is the one somebody asks about at year end.</summary>
    [Fact]
    public async Task Resolving_without_a_note_is_refused()
    {
        var (migration, _) = await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/platform/quarantine/{migration}/resolve", new { note = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        // ⚠ And it stayed open — a refused dismissal must not half-apply.
        using var after = await _f.CreateClient().SendAsync(R(HttpMethod.Get, "/api/v1/platform/quarantine?state=open"));
        var list = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Row(list, migration).GetProperty("resolvedAtUtc").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Resolving_records_who_and_why_and_takes_it_off_the_open_list()
    {
        var (migration, _) = await SeedAsync();
        const string note = "Checked the 2019 till roll — voided, no money taken.";

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/platform/quarantine/{migration}/resolve", new { note }));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var open = await _f.CreateClient().SendAsync(R(HttpMethod.Get, "/api/v1/platform/quarantine?state=open"));
        var openList = await open.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(openList.EnumerateArray(), r => r.GetProperty("id").GetGuid() == migration);

        using var done = await _f.CreateClient().SendAsync(R(HttpMethod.Get, "/api/v1/platform/quarantine?state=resolved"));
        var doneList = await done.Content.ReadFromJsonAsync<JsonElement>();
        var row = Row(doneList, migration);
        Assert.Equal(note, row.GetProperty("resolutionNote").GetString());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("resolvedBy").GetString()));
    }

    /// <summary>
    /// ⚠⚠ RETRYING A MIGRATION ROW MUST FAIL LOUDLY. There is no sale behind it — only a legacy
    /// reference — so a cheerful 200 would tell the operator the sale had been recovered.
    /// </summary>
    [Fact]
    public async Task Retrying_a_row_with_no_sale_behind_it_is_refused_rather_than_faked()
    {
        var (migration, _) = await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/platform/quarantine/{migration}/retry"));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("migration", body.GetProperty("source").GetString());
    }

    /// <summary>The payload is fetched per row, never in the list — a Woo order is kilobytes.</summary>
    [Fact]
    public async Task The_stored_payload_is_readable_for_one_row()
    {
        var (migration, _) = await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Get, $"/api/v1/platform/quarantine/{migration}/payload"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("LEGACY-", body.GetProperty("payload").GetString());
    }
}
