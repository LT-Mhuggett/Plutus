using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP-TZ — the shop's clock, and noticing when a till's disagrees with it.
///
/// ⚠⚠ THE SAFETY NET IS THE POINT. `BusinessDay` is the till's LOCAL wall clock, so a till PC set to
/// the wrong timezone files sales under the wrong trading day — silently, with no clue in the
/// numbers, and the Z-read then balances against takings belonging to a different day. Nothing has
/// ever checked. These pin the check.
/// </summary>
public class StoreClockTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly DateTime Summer = new(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Winter = new(2026, 1, 21, 14, 30, 0, DateTimeKind.Utc);

    /// <summary>⚠ IANA ON WINDOWS TOO — .NET 6+ resolves both forms, which is why the platform stores
    /// the IANA id rather than a Windows one needing translation at every boundary.</summary>
    [Theory]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    public void A_real_iana_zone_is_known(string id)
    {
        Assert.True(StoreClock.IsKnown(id));
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not(string id)
    {
        Assert.False(StoreClock.IsKnown(id));
        Assert.Null(StoreClock.Resolve(id));
    }

    /// <summary>⚠ AN UNRESOLVABLE STORED ZONE IS TREATED AS UNSET, never as an error — a zone retired
    /// between releases must leave reports rendering rather than failing.</summary>
    [Fact]
    public void An_unknown_zone_falls_back_to_the_device()
    {
        var fallback = StoreClock.InStore(Summer, StoreClock.Resolve("Mars/Olympus_Mons"));

        Assert.Equal(ApiTime.AsLocal(Summer), fallback);
    }

    [Fact]
    public void An_instant_renders_on_the_shops_clock()
    {
        Assert.Equal(15, StoreClock.InStore(Summer, London).Hour);   // BST
        Assert.Equal(16, StoreClock.InStore(Summer, Madrid).Hour);   // CEST
        Assert.Equal(14, StoreClock.InStore(Summer, Utc).Hour);
    }

    /// <summary>⚠ DAYLIGHT SAVING IS THE PLATFORM'S PROBLEM — the same zone answers differently in
    /// January and August, and a hard-coded offset anywhere would be wrong twice a year.</summary>
    [Fact]
    public void It_follows_daylight_saving()
    {
        Assert.Equal(14, StoreClock.InStore(Winter, London).Hour);   // GMT
        Assert.Equal(15, StoreClock.InStore(Summer, London).Hour);   // BST
    }

    /// <summary>⚠⚠ THE FAULT THE CHECK EXISTS FOR: the PC is an hour out from the shop, so its
    /// business day rolls at the wrong moment.</summary>
    [Fact]
    public void A_device_on_another_offset_disagrees()
    {
        Assert.True(StoreClock.DeviceDisagrees(London, Summer, device: Madrid));
    }

    /// <summary>
    /// ⚠⚠ TWO NAMES FOR ONE CLOCK ARE NOT A DISAGREEMENT. London and Dublin share an offset all year;
    /// reporting those would train an operator to dismiss the banner, and then they dismiss the one
    /// that matters. This is why the comparison is on OFFSETS and not on zone ids.
    /// </summary>
    [Fact]
    public void Two_names_for_the_same_clock_do_not()
    {
        Assert.False(StoreClock.DeviceDisagrees(London, Summer, device: Dublin));
        Assert.False(StoreClock.DeviceDisagrees(London, Winter, device: Dublin));
    }

    /// <summary>⚠ NO ZONE SET IS NEVER A DISAGREEMENT — the platform's default is "the device is
    /// right", and warning a tenant who has configured nothing would be noise.</summary>
    [Fact]
    public void No_configured_zone_is_never_a_disagreement()
    {
        Assert.False(StoreClock.DeviceDisagrees(null, Summer, device: Madrid));
    }

    /// <summary>
    /// ⚠⚠ ASKED AT A MOMENT, because two zones can agree in one season and differ in another. A till
    /// checked once at install and never again is a till that silently goes wrong at a clock change.
    ///
    /// ⚠ Phoenix never observes DST (UTC−7 all year); Denver is MST (−7) in winter and MDT (−6) in
    /// summer. So they AGREE in January and DIFFER in August.
    ///
    /// ⚠ I wrote this pair the wrong way round first, and the DST arithmetic in the TypeScript twin
    /// too. That is the argument for asserting against real zones rather than against offsets I have
    /// worked out in my head — the platform's tz database is right and I am not.
    /// </summary>
    [Fact]
    public void The_same_pair_can_agree_in_one_season_and_differ_in_another()
    {
        var arizona = TimeZoneInfo.FindSystemTimeZoneById("America/Phoenix");   // never observes DST
        var denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");     // does

        Assert.True(StoreClock.DeviceDisagrees(arizona, Summer, device: denver));   // −7 vs −6
        Assert.False(StoreClock.DeviceDisagrees(arizona, Winter, device: denver));  // both −7
    }

    /// <summary>⚠ THE MESSAGE NAMES BOTH CLOCKS AND WHAT IT COSTS. "Timezone mismatch" tells nobody
    /// anything they can act on; this is what gets a PC's clock fixed rather than dismissed.</summary>
    [Fact]
    public void The_message_names_both_zones_and_the_consequence()
    {
        var msg = StoreClock.DisagreementMessage(London, Summer, device: Madrid);

        Assert.Contains("Europe/London", msg);
        Assert.Contains("Europe/Madrid", msg);
        Assert.Contains("wrong trading day", msg);
    }
}
