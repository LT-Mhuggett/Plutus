using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// How a discount's audit record reaches the platform — binding default 22(c).
///
/// ⚠⚠ THE DESIGN DECISION THESE TESTS PROTECT. The obvious home for `reason` and `authorisedBy` was
/// `LineDiscount`, and it would have been wrong: `LineMeta.discounts[]` is projected straight into
/// legacy `Transaction_Discount` rows keyed on a REAL `DiscountId`
/// (`LegacySaleBridgeConsumer.cs:195`), which is why the members' auto-discount is already
/// deliberately omitted from it. A manual discount has no catalogue id, so an entry there would have
/// carried a synthetic one and FK-failed the projection of every discounted sale. The authority is
/// its own field precisely so the bridge never sees it, and
/// <see cref="An_authority_does_not_add_a_catalogue_discount_the_legacy_bridge_would_FK_fail_on"/>
/// is the test that stops anyone moving it back.
/// </summary>
public class DiscountAuthorityWireTests
{
    private static readonly Guid Cashier = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Supervisor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Business = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static BasketLine Line(long discountPence, IReadOnlyList<DiscountAuthority>? authorities) =>
        new(ItemId: Guid.Empty,
            IdOne: "5010001",
            Name: "Widget",
            UnitIncPence: 1000,
            UnitExPence: 833,
            Quantity: 1,
            DiscountPence: discountPence,
            DiscountAuthorities: authorities);

    private static IngestSaleRequest Assemble(params BasketLine[] lines) =>
        SaleAssembler.Assemble(
            saleId: Guid.NewGuid(),
            deviceId: Guid.NewGuid(),
            deviceSeq: 1,
            businessId: Business,
            lines: lines,
            tenders: new[] { new IngestTender { TenderType = 0, AmountPence = 1 } },
            businessDay: new DateOnly(2026, 8, 14),
            occurredAtUtc: new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc),
            operatorUserId: Cashier);

    [Fact]
    public void A_discounts_reason_and_authoriser_reach_the_wire()
    {
        var authority = new DiscountAuthority("damaged box", 300, Cashier, Supervisor, "Sam Supervisor");

        var request = Assemble(Line(300, new[] { authority }));

        var meta = LineMeta.FromJson(request.Lines[0].DiscountsJson);
        var wire = Assert.Single(meta!.DiscountAuthority!);

        Assert.Equal("damaged box", wire.Reason);
        Assert.Equal(300, wire.AmountPence);
        Assert.Equal(Cashier.ToString("D"), wire.RequestedBy);
        Assert.Equal(Supervisor.ToString("D"), wire.AuthorisedBy);
        Assert.Equal("Sam Supervisor", wire.AuthorisedByName);
    }

    /// <summary>
    /// ⚠⚠ THE FK TRAP, PINNED. Putting the audit fields on `LineDiscount` would have meant emitting
    /// a `discounts[]` entry for a manual discount, which has no catalogue id — and
    /// `LegacySaleBridgeConsumer` turns every entry into a `Transaction_Discount` row keyed on a real
    /// `DiscountId`. Every discounted sale's projection would have failed.
    ///
    /// ⚠ This test fails the moment somebody "tidies" the authority back onto `LineDiscount`.
    /// </summary>
    [Fact]
    public void An_authority_does_not_add_a_catalogue_discount_the_legacy_bridge_would_FK_fail_on()
    {
        var authority = new DiscountAuthority("damaged box", 300, Cashier, null, null);

        var request = Assemble(Line(300, new[] { authority }));

        var meta = LineMeta.FromJson(request.Lines[0].DiscountsJson);

        Assert.Null(meta!.Discounts);
        Assert.NotNull(meta.DiscountAuthority);
    }

    /// <summary>
    /// ⚠ AN UNDISCOUNTED LINE'S JSON IS UNCHANGED, BYTE FOR BYTE. `LineMeta` serialises with
    /// `WhenWritingNull`, and the assembler sends null rather than an empty list — so this field
    /// costs nothing on the overwhelming majority of lines, and a sale compared against its own
    /// stored meta does not show a diff on every line of every basket.
    /// </summary>
    [Fact]
    public void A_line_with_no_discount_carries_no_authority_key_at_all()
    {
        var request = Assemble(Line(0, null));

        Assert.DoesNotContain("discountAuthority", request.Lines[0].DiscountsJson);
    }

    /// <summary>⚠ Same for an empty list — a caller that hands over "none, but explicitly" must not
    /// produce a different payload from one that hands over nothing.</summary>
    [Fact]
    public void An_empty_authority_list_is_also_omitted_rather_than_written_as_an_empty_array()
    {
        var request = Assemble(Line(0, Array.Empty<DiscountAuthority>()));

        Assert.DoesNotContain("discountAuthority", request.Lines[0].DiscountsJson);
    }

    /// <summary>
    /// ⚠ TWO DISCOUNTS ON ONE LINE IS THE CASE `IngestLine.DiscountPence` CANNOT ANSWER. A member's
    /// tier rate plus a manual discount is a single summed figure on the line; only the authority
    /// list can say that £3 of the £5 was approved by a supervisor and £2 was automatic.
    /// </summary>
    [Fact]
    public void Two_discounts_on_one_line_are_recorded_separately_because_their_sum_cannot_say_which_was_which()
    {
        var automatic = DiscountAudit.Automatic("Gold member 10%", 200, Cashier);
        var manual = new DiscountAuthority("damaged box", 300, Cashier, Supervisor, "Sam Supervisor");

        var request = Assemble(Line(500, new[] { automatic, manual }));

        var meta = LineMeta.FromJson(request.Lines[0].DiscountsJson);

        Assert.Equal(2, meta!.DiscountAuthority!.Count);
        Assert.Equal(500, meta.DiscountAuthority.Sum(a => a.AmountPence));
        Assert.Null(meta.DiscountAuthority[0].AuthorisedBy);
        Assert.Equal(Supervisor.ToString("D"), meta.DiscountAuthority[1].AuthorisedBy);
    }

    /// <summary>
    /// ⚠ "NO STEP-UP WAS NEEDED" MUST NOT SERIALISE AS A PRESENT-BUT-EMPTY AUTHORISER. A reader
    /// checking `authorisedBy != null` has to get a straight answer; `""` would read as authorised
    /// by somebody whose id is blank.
    /// </summary>
    [Fact]
    public void An_unauthorised_discount_omits_the_authoriser_rather_than_writing_a_blank_one()
    {
        var authority = new DiscountAuthority("small discount", 100, Cashier, null, null);

        var request = Assemble(Line(100, new[] { authority }));

        Assert.DoesNotContain("authorisedBy", request.Lines[0].DiscountsJson);
    }

    /// <summary>
    /// ⚠ Guid.Empty is not a person, on the wire either — it is written as absent, not as
    /// "00000000-0000-0000-0000-000000000000", which a report would render as a real user id.
    /// </summary>
    [Fact]
    public void An_empty_guid_authoriser_is_written_as_absent_not_as_a_zero_id()
    {
        var authority = new DiscountAuthority("reason", 100, Cashier, Guid.Empty, null);

        Assert.Null(authority.ToWire().AuthorisedBy);
    }

    // ── Reading it back ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ EVERY SALE WRITTEN BEFORE 2026-08-14 HAS NONE OF THIS, and a refund screen reading one must
    /// not throw. Absent means "this till did not record it", NOT "nobody authorised it" — a caller
    /// that renders the two identically is asserting something it does not know.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"itemIdOne\":\"5010001\",\"exUnitPence\":833}")]
    [InlineData("not json at all")]
    public void A_line_written_before_this_field_existed_reads_back_as_nothing_recorded(string? json)
    {
        Assert.Empty(DiscountAuthorityWire.AuthoritiesOn(json));
    }

    [Fact]
    public void An_authority_survives_the_round_trip_through_stored_json()
    {
        var authority = new DiscountAuthority("damaged box", 300, Cashier, Supervisor, "Sam Supervisor");
        var request = Assemble(Line(300, new[] { authority }));

        // ⚠ The SERVER stores `DiscountsJson` verbatim into a longtext column and only ever plucks
        // named fields back out with JsonDocument, so what a till sent is what a reader gets.
        var readBack = Assert.Single(DiscountAuthorityWire.AuthoritiesOn(request.Lines[0].DiscountsJson));

        Assert.Equal("damaged box", readBack.Reason);
        Assert.Equal(Supervisor.ToString("D"), readBack.AuthorisedBy);
    }

    /// <summary>
    /// ⚠ THE SERVER'S OWN EXTRACTORS MUST STILL WORK. `SalesIngestService` reads `itemIdOne` and
    /// `vatBand` out of this same blob with `JsonDocument`, and `itemIdOne` is the LOAD-BEARING one:
    /// a line it cannot read is skipped by the stock projection with no error at all. Adding a field
    /// alongside them must not disturb either.
    /// </summary>
    [Fact]
    public void Adding_an_authority_leaves_the_fields_the_server_actually_parses_untouched()
    {
        var authority = new DiscountAuthority("damaged box", 300, Cashier, null, null);
        var request = Assemble(Line(300, new[] { authority }));

        using var doc = JsonDocument.Parse(request.Lines[0].DiscountsJson!);

        Assert.Equal("5010001", doc.RootElement.GetProperty("itemIdOne").GetString());
        Assert.Equal(833, doc.RootElement.GetProperty("exUnitPence").GetInt64());
    }
}
