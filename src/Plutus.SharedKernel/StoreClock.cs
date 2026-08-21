using System;

namespace Plutus.SharedKernel;

/// <summary>
/// **What time it is in the SHOP — WP-TZ, 2026-08-22.**
///
/// ⚠⚠ MATT, 2026-08-21, asking for the till clock: *"somehow to set it in the portal I assume."*
/// The clock shipped first, reading the device, and this is the other half.
///
/// ⚠⚠ **THE SURFACE THIS EXISTS FOR IS THE PORTAL, NOT THE TILL.** A till PC sits in the shop and is
/// set to the shop's timezone, so its own clock is already right. The portal is opened from
/// anywhere — a manager at home, an accountant in another country — and every timestamp on it
/// renders in *the browser's* zone. The same sale reads 14:32 on the shop floor and 15:32 in Madrid,
/// and nothing on the screen says which one you are looking at.
///
/// ⚠⚠ **IT DOES NOT MOVE WHICH DAY A SALE FILES UNDER, AND THAT IS DELIBERATE.** `BusinessDay` is
/// the till's LOCAL wall clock and stays that way: it is computed identically by both tills, it is a
/// C2 twin, and re-deriving it from a configured zone would change which VAT period a late-evening
/// sale lands in — a money change that deserves its own package, not a passenger on a display one.
///
/// ⚠ WHAT THIS DOES INSTEAD IS **NOTICE WHEN THEY DISAGREE**. If a till PC is on the wrong timezone
/// its business day is already wrong today, silently, and nothing has ever checked. See
/// <see cref="DeviceDisagrees"/>.
/// </summary>
public static class StoreClock
{
    /// <summary>
    /// Is this a timezone this machine can actually resolve?
    ///
    /// ⚠ ASKED OF THE PLATFORM, NOT MATCHED AGAINST A LIST. .NET 6+ accepts IANA ids on Windows as
    /// well as Linux, and a hand-maintained list of zones is a list that is wrong every time a
    /// country changes its mind about daylight saving.
    /// </summary>
    public static bool IsKnown(string? ianaOrWindowsId)
    {
        if (string.IsNullOrWhiteSpace(ianaOrWindowsId)) return false;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(ianaOrWindowsId);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    /// <summary>
    /// The shop's zone, or null when none is set or the one stored cannot be resolved.
    ///
    /// ⚠ AN UNRESOLVABLE ZONE IS TREATED AS UNSET, never as an error. A stored id that this machine
    /// does not know — an old Windows id on a Linux backend, a zone retired between releases — must
    /// leave the platform rendering device-local rather than failing a report.
    /// </summary>
    public static TimeZoneInfo? Resolve(string? ianaOrWindowsId)
    {
        if (string.IsNullOrWhiteSpace(ianaOrWindowsId)) return null;

        try { return TimeZoneInfo.FindSystemTimeZoneById(ianaOrWindowsId); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    /// <summary>
    /// An instant, on the shop's clock — falling back to this machine's when no zone is set.
    ///
    /// ⚠ THE FALLBACK IS THE OLD BEHAVIOUR EXACTLY (`ApiTime.AsLocal`), so a tenant that never
    /// configures a zone sees precisely what it saw before this package existed.
    /// </summary>
    public static DateTime InStore(DateTime value, TimeZoneInfo? zone) =>
        zone is null
            ? ApiTime.AsLocal(value)
            : TimeZoneInfo.ConvertTimeFromUtc(ApiTime.AsUtc(value), zone);

    /// <summary>
    /// ⚠⚠ **IS THIS MACHINE ON A DIFFERENT CLOCK FROM THE SHOP, RIGHT NOW?**
    ///
    /// This is the safety net the package is really for. `BusinessDay` is the till's LOCAL wall
    /// clock, so a till PC set to the wrong timezone files sales under the wrong trading day —
    /// silently, with no clue in the numbers, and the Z-read balances against takings that belong to
    /// a different day. Nothing has ever checked.
    ///
    /// ⚠ COMPARED AS OFFSETS AT A MOMENT, not as zone ids. `Europe/London` and a Windows "GMT
    /// Standard Time" are the same clock under two names, and a shop in Dublin and one in London
    /// share an offset all year — none of those is a problem, and matching on the id would report
    /// all three.
    ///
    /// ⚠ AND AT THE MOMENT ASKED, because that is the only honest comparison: two zones can agree in
    /// January and differ in July. A till checked once at install and never again is a till that
    /// goes wrong at the clock change.
    /// </summary>
    /// <returns>True only when the two are genuinely showing different times.</returns>
    public static bool DeviceDisagrees(TimeZoneInfo? shop, DateTime utcNow, TimeZoneInfo? device = null)
    {
        if (shop is null) return false;

        device ??= TimeZoneInfo.Local;

        var utc = ApiTime.AsUtc(utcNow);
        return shop.GetUtcOffset(utc) != device.GetUtcOffset(utc);
    }

    /// <summary>
    /// Words for the operator when the two disagree.
    ///
    /// ⚠ IT NAMES BOTH CLOCKS AND SAYS WHAT IT COSTS. "Timezone mismatch" tells somebody nothing
    /// they can act on; naming the two zones and the consequence — sales filing on the wrong trading
    /// day — is what gets a PC's clock fixed rather than the warning dismissed.
    /// </summary>
    public static string DisagreementMessage(TimeZoneInfo shop, DateTime utcNow, TimeZoneInfo? device = null)
    {
        device ??= TimeZoneInfo.Local;

        var utc = ApiTime.AsUtc(utcNow);
        var shopNow = TimeZoneInfo.ConvertTimeFromUtc(utc, shop);
        var deviceNow = TimeZoneInfo.ConvertTimeFromUtc(utc, device);

        return $"This PC's clock says {deviceNow:HH:mm} ({device.Id}) but the shop is set to "
             + $"{shop.Id}, where it is {shopNow:HH:mm}. Sales may be filed under the wrong trading "
             + "day until the PC's timezone is corrected.";
    }
}
