using System;
using System.Collections.Generic;
using Plutus.Entities.Models;
using Plutus.Migration.Kapow;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.8 Kapow→v2 transforms + sale mapper (gap-analysis F1–F3).</summary>
public class KapowMigrationTests
{
    [Theory]
    [InlineData("3.49", 349)]
    [InlineData("1.5", 150)]
    [InlineData("1.50", 150)]
    [InlineData("0.0", 0)]
    [InlineData("10", 1000)]
    public void ParsePence_parses_decimal_text(string text, long expected)
        => Assert.Equal(expected, KapowMoney.ParsePence(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParsePence_rejects_junk(string? text)
        => Assert.False(KapowMoney.TryParsePence(text, out _));

    [Theory]
    [InlineData(1.2, 2000)]
    [InlineData(1.05, 500)]
    [InlineData(1.0, 0)]
    public void Vat_multiplier_to_basis_points(double mult, int bp)
        => Assert.Equal(bp, KapowVat.ToBasisPoints(mult));

    [Theory]
    [InlineData(120, 1.2, 20)]   // £1.20 inc @20% → 20p VAT
    [InlineData(1000, 1.0, 0)]   // zero-rated → 0
    [InlineData(105, 1.05, 5)]   // @5%
    public void Vat_from_inclusive(long inc, double mult, long vat)
        => Assert.Equal(vat, KapowVat.VatFromInclusive(inc, mult));

    [Fact]
    public void Time_converts_local_to_utc_and_businessday()
    {
        // BST (summer): London is UTC+1 → 10:00 local = 09:00 UTC.
        var utc = KapowTime.ToUtc("2026-07-15 10:00:00");
        Assert.Equal(new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal(new DateOnly(2026, 7, 15), KapowTime.BusinessDay("2026-07-15 10:00:00"));

        // GMT (winter): no offset.
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), KapowTime.ToUtc("2026-01-15 10:00:00"));
    }

    [Fact]
    public void IdRemap_is_stable_per_old_id()
    {
        var remap = new IdRemap<string>();
        var a1 = remap.GetOrMint("9781401238384");
        var a2 = remap.GetOrMint("9781401238384");
        var b = remap.GetOrMint("Club");
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.Equal(2, remap.Count);
    }

    private static KapowSaleInput ValidSale()
    {
        var tenant = Guid.NewGuid();
        return new KapowSaleInput
        {
            LegacyId = "202672314534618",
            CreatedLocal = "2026-07-15 14:05:34.6186178",
            DateOfSaleLocal = "2026-07-15 14:03:00",
            TotalText = "3.60", // 2×1.80
            TenantId = tenant, TillId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), DeviceSeq = 1,
            Lines = new List<KapowLineInput>
            {
                new() { OldItemId = "9781401238384", Qty = 2, UnitPriceText = "1.80", VatMultiplier = 1.2 },
            },
            Tenders = new List<KapowTenderInput>
            {
                new() { TenderType = 0, AmountText = "3.60", ChangeText = "0" },
            },
        };
    }

    [Fact]
    public void MapSale_maps_a_valid_sale_with_reconstructed_vat_and_legacy_ref()
    {
        var input = ValidSale();
        var result = KapowSaleMapper.MapSale(input, new IdRemap<string>());

        Assert.False(result.IsQuarantined);
        var s = result.Sale!;
        Assert.Equal("202672314534618", s.LegacyRef);
        Assert.True(s.VatReconstructed);
        Assert.Equal(360, s.GrossPence);
        Assert.Equal(new DateOnly(2026, 7, 15), s.BusinessDay);
        Assert.Equal(new DateTime(2026, 7, 15, 13, 5, 34, DateTimeKind.Utc), s.OccurredAtUtc.AddTicks(-s.OccurredAtUtc.Ticks % TimeSpan.TicksPerSecond));
        var line = Assert.Single(s.Lines);
        Assert.Equal(2000, line.VatRateBp);
        Assert.Equal(60, line.VatAmountPence);      // £3.60 inc @20% → 60p
        Assert.Equal(60, s.VatPence);
        s.Validate(); // invariants hold
    }

    [Fact]
    public void MapSale_folds_line_discount_rate_into_gross()
    {
        // 2×£3.15 = £6.30, 10% off → £5.67 (the observed real-data pattern).
        var input = ValidSale();
        input.TotalText = "5.67";
        input.Lines[0].UnitPriceText = "3.15";
        input.Lines[0].DiscountRate = 0.10;
        input.Tenders[0].AmountText = "5.67";

        var result = KapowSaleMapper.MapSale(input, new IdRemap<string>());
        Assert.False(result.IsQuarantined);
        Assert.Equal(567, result.Sale!.GrossPence);
        Assert.Equal(63, result.Sale.Lines[0].DiscountPence);
    }

    [Fact]
    public void MapSale_quarantines_a_total_mismatch()
    {
        var input = ValidSale();
        input.TotalText = "9.99"; // != Σ lines (3.60)
        var result = KapowSaleMapper.MapSale(input, new IdRemap<string>());
        Assert.True(result.IsQuarantined);
        Assert.Contains("Total", result.QuarantineReason);
    }

    [Fact]
    public void MapSale_quarantines_a_tender_mismatch()
    {
        var input = ValidSale();
        input.Tenders[0].AmountText = "2.00"; // net tender != gross
        var result = KapowSaleMapper.MapSale(input, new IdRemap<string>());
        Assert.True(result.IsQuarantined);
    }
}
