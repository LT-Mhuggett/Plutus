using System;
using System.IO;
using System.Linq;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Cutover step 8 — the tender/channel wire values now live in <c>SharedKernel</c>, and this pins
/// every copy of them to each other.
///
/// ⚠ WHY THEY MOVED. `TenderType` and `SaleChannel` were declared only in
/// `Plutus.Entities.Models.SalesV2` — a BACKEND module the architecture test forbids client
/// libraries from referencing. A till assembling a sale therefore had no shared source for the
/// bytes and would have hand-coded them, which is precisely the drift Part C exists to stop.
///
/// ⚠ WHY THEY ARE CONSTANTS, NOT AN ENUM. Declaring the enums in SharedKernel was tried and
/// reverted: 69 backend files import both namespaces, so every use became CS0104. Renaming the
/// backend's copy would change the CLR type of mapped EF properties and move the model snapshot —
/// a PendingModelChangesWarning against a live database. `IngestTender.TenderType` is a byte on
/// the wire, so the values were all a client ever needed.
///
/// ⚠ WHY THE BACKEND'S ENUM STAYS, and this pins it instead: cutover step 8 sanctions exactly that
/// ("have Plutus.Entities reference SharedKernel, **or assert equality in a test**"). If the two
/// ever diverge, this file fails — rather than a shop's payment-split report quietly
/// re-attributing takings, which is what a byte change actually does.
/// </summary>
public class TenderTypeParityTests
{
    /// <summary>Walk up from the test binaries to the repo root, the same way
    /// <c>ComponentVersionTests</c> does — <c>Repo.Root()</c> lives in the Architecture project and
    /// this suite cannot see it.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Plutus.slnx"))) dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate repo root (Plutus.slnx) above the test bin dir.");
        return dir!.FullName;
    }

    [Theory]
    [InlineData(Tenders.Cash, TenderType.Cash)]
    [InlineData(Tenders.Card, TenderType.Card)]
    [InlineData(Tenders.Online, TenderType.Online)]
    [InlineData(Tenders.Credit, TenderType.Credit)]
    [InlineData(Tenders.GiftCard, TenderType.GiftCard)]
    public void Tender_values_match_the_backend_enum(byte shared, TenderType backend) =>
        Assert.Equal((byte)backend, shared);

    [Theory]
    [InlineData(SaleChannels.Till, SaleChannel.Till)]
    [InlineData(SaleChannels.WebPos, SaleChannel.WebPos)]
    [InlineData(SaleChannels.WebStore, SaleChannel.WebStore)]
    public void Channel_values_match_the_backend_enum(byte shared, SaleChannel backend) =>
        Assert.Equal((byte)backend, shared);

    [Theory]
    [InlineData(Adjustments.Refund, AdjustmentType.Refund)]
    [InlineData(Adjustments.Void, AdjustmentType.Void)]
    public void Adjustment_values_match_the_backend_enum(byte shared, AdjustmentType backend) =>
        Assert.Equal((byte)backend, shared);

    /// <summary>
    /// ⚠ A member added on ONE side only is exactly the drift this file exists to catch — every
    /// theory above still passes when a sixth tender appears on just one of them.
    /// </summary>
    [Fact]
    public void The_backend_has_not_grown_a_tender_the_shared_constants_lack()
    {
        var backend = Enum.GetValues<TenderType>().Select(v => (byte)v).OrderBy(v => v).ToArray();
        var shared = new[] { Tenders.Cash, Tenders.Card, Tenders.Online, Tenders.Credit, Tenders.GiftCard }
            .OrderBy(v => v).ToArray();
        Assert.Equal(backend, shared);

        var backendChannels = Enum.GetValues<SaleChannel>().Select(v => (byte)v).OrderBy(v => v).ToArray();
        var sharedChannels = new[] { SaleChannels.Till, SaleChannels.WebPos, SaleChannels.WebStore }
            .OrderBy(v => v).ToArray();
        Assert.Equal(backendChannels, sharedChannels);
    }

    // ── the TypeScript twin ──

    [Theory]
    [InlineData("Cash", Tenders.Cash)]
    [InlineData("cash on delivery", Tenders.Cash)]
    [InlineData("Online payment", Tenders.Online)]
    [InlineData("Store credit", Tenders.Credit)]
    [InlineData("Visa", Tenders.Card)]
    [InlineData("", Tenders.Card)]
    [InlineData(null, Tenders.Card)]
    public void Method_names_map_the_way_the_web_till_maps_them(string? method, byte expected) =>
        Assert.Equal(expected, Tenders.FromMethodName(method));

    /// <summary>
    /// ⚠ THE ORDERING TRAP, as its own test. "Gift card" contains neither "cash" nor "online", so a
    /// naive chain drops it into the STORE-CREDIT bucket — nothing else matches before the
    /// fallback. The web till checks gift before credit for exactly this reason, and the two are
    /// different liabilities with different reconciliations.
    /// </summary>
    [Theory]
    [InlineData("Gift card")]
    [InlineData("gift voucher")]
    [InlineData("GIFT CARD credit")]   // contains BOTH words — gift must still win
    public void A_gift_card_is_never_filed_as_store_credit(string method) =>
        Assert.Equal(Tenders.GiftCard, Tenders.FromMethodName(method));

    /// <summary>
    /// ⚠ Reads the web till's own mapping out of `api.ts` and asserts the bytes it returns are the
    /// ones declared here. A text assertion is coarse — but the alternative is NO pin at all on the
    /// TypeScript half (there is still no JS test runner; cutover WP15), and a coarse pin that
    /// fails loudly beats a C2 row admitting nothing stops this drifting.
    /// </summary>
    [Fact]
    public void The_web_tills_tenderTypeFor_still_returns_these_bytes()
    {
        var apiTs = Path.Combine(RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.WebApp", "src", "api.ts");
        Assert.True(File.Exists(apiTs), $"expected the web till's api.ts at {apiTs}");

        var text = File.ReadAllText(apiTs);
        var start = text.IndexOf("const tenderTypeFor", StringComparison.Ordinal);
        Assert.True(start >= 0, "api.ts no longer declares tenderTypeFor — the mapping moved, and this pin needs following it.");

        var mapping = text[start..];
        mapping = mapping[..mapping.IndexOf("};", StringComparison.Ordinal)];

        Assert.Contains($"return {Tenders.Cash};", mapping);
        Assert.Contains($"return {Tenders.Online};", mapping);
        Assert.Contains($"return {Tenders.GiftCard};", mapping);
        Assert.Contains($"return {Tenders.Credit};", mapping);
        Assert.Contains($"return {Tenders.Card};", mapping);

        // and gift is still tested BEFORE credit — the ordering trap above
        Assert.True(
            mapping.IndexOf("\"gift\"", StringComparison.Ordinal) < mapping.IndexOf("\"credit\"", StringComparison.Ordinal),
            "api.ts must test 'gift' BEFORE 'credit', or a gift card is filed as store credit.");
    }
}
