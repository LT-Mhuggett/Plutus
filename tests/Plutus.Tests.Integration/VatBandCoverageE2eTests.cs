using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// VAT pre-flight regression (2026-07-30, before the P3.5 portal VAT port): summary-rich's
/// by-tax-rate table must cover EVERY sale line, including the ETL's barcode-less reconciliation
/// sentinel lines (ItemIdOne == null). Before the fix those lines were filtered out with the
/// topItems barcode filter — on live data the whole 19.81% legacy band disappeared and the zero
/// band under-reported ~£4.8k gross, with the VAT landing in the report's "unallocated" row.
/// The pinned invariant: Σ byTaxRate.vat == header VAT (unallocated == 0 for reconciled sales).
/// </summary>
public class VatBandCoverageE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public VatBandCoverageE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    // An isolated window nothing else seeds into, so the assertions are exact.
    private static readonly DateOnly Day = new(2019, 6, 15);

    private async Task SeedSaleAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "vat-band-e2e-seed";

        var saleId = Uuid7.New();
        // Two lines: a normal barcode line at 20% and a reconciliation sentinel (ItemIdOne = null)
        // at the legacy 19.81% oddity — exactly the live-data shape that exposed the bug.
        db.SalesV2.Add(new SaleV2
        {
            Id = saleId, TenantId = Kapow, TillId = Uuid7.New(), DeviceId = Uuid7.New(), DeviceSeq = 1,
            Channel = SaleChannel.Till, BusinessDay = Day,
            OccurredAtUtc = Day.ToDateTime(TimeOnly.MinValue), ReceivedAtUtc = Day.ToDateTime(TimeOnly.MinValue),
            GrossPence = 1200 + 225, VatPence = 200 + 37, VatReconstructed = true,
        });
        db.SaleLines.Add(new SaleLine
        {
            Id = Uuid7.New(), TenantId = Kapow, SaleId = saleId, LineNo = 1,
            ItemId = Uuid7.New(), ItemIdOne = "VATBAND-1", Qty = 1,
            UnitPricePence = 1200, LineGrossPence = 1200, DiscountPence = 0,
            VatRateBp = 2000, VatAmountPence = 200,
        });
        db.SaleLines.Add(new SaleLine
        {
            Id = Uuid7.New(), TenantId = Kapow, SaleId = saleId, LineNo = 2,
            ItemId = Uuid7.New(), ItemIdOne = null, Qty = 1,
            UnitPricePence = 225, LineGrossPence = 225, DiscountPence = 0,
            VatRateBp = 1981, VatAmountPence = 37,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ByTaxRate_covers_barcodeless_lines_and_sums_to_header_vat()
    {
        await SeedSaleAsync();
        var client = _f.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/reports/summary-rich?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var bands = body.GetProperty("byTaxRate").EnumerateArray()
            .ToDictionary(b => b.GetProperty("tax").GetString()!, b => b);

        // Both bands present — including the one whose only line is barcode-less.
        Assert.True(bands.ContainsKey("20%"), "20% band missing");
        Assert.True(bands.ContainsKey("19.81%"), "19.81% band missing — barcode-less lines dropped from byTaxRate");
        Assert.Equal(0.37m, bands["19.81%"].GetProperty("vat").GetDecimal());
        Assert.Equal(2.25m, bands["19.81%"].GetProperty("gross").GetDecimal());

        // The pinned invariant: band VAT sums exactly to the header VAT (nothing unallocated),
        // and the headline gross covers the whole sale.
        var bandVat = bands.Values.Sum(b => b.GetProperty("vat").GetDecimal());
        var headerVat = body.GetProperty("totalSales").GetDecimal() - body.GetProperty("totalSalesExTax").GetDecimal();
        Assert.Equal(headerVat, bandVat);
        Assert.Equal(14.25m, body.GetProperty("totalSales").GetDecimal());

        // topItems must still exclude the sentinel (it has no barcode to group by).
        var topItems = body.GetProperty("topItems").EnumerateArray().Select(t => t.GetProperty("itemId").GetString()).ToList();
        Assert.Contains("VATBAND-1", topItems);
        Assert.DoesNotContain(null, topItems);
    }
}
