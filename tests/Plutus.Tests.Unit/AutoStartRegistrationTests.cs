using Plutus.TillAgent.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The agent's auto-start registration (2026-08-17).
///
/// ⚠⚠ THESE PIN A FAULT THAT WAS FOUND ON A REAL TILL, and the fault's shape is the reason the tests
/// look like this. The tray checkbox asked only whether a `Run` value EXISTED, and the value was
/// written once when the box was ticked and never revisited. A till that had the agent ticked while
/// it ran out of `Downloads` kept
/// <c>"C:\Users\admin\Downloads\PlutusTillAgent (1).exe"</c> registered long after that file was
/// deleted: every boot launched nothing, silently, while the checkbox went on showing **ticked**.
///
/// A setting that asserts the opposite of the truth is worse than one that is obviously broken —
/// nobody investigates a tick.
/// </summary>
public class AutoStartRegistrationTests
{
    private const string Installed = @"C:\Users\admin\AppData\Local\Plutus\Agent\PlutusTillAgent.exe";

    /// <summary>The exact value found on the till, 2026-08-17.</summary>
    private const string StaleDownloadsValue = @"""C:\Users\admin\Downloads\PlutusTillAgent (1).exe""";

    // ── the value that gets written ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ QUOTED, ALWAYS. Every till path that matters has a space in it — `C:\Program Files\…`,
    /// `C:\Users\First Last\…` — and an unquoted `Run` value is split at the first one, so Windows
    /// tries to launch `C:\Users\First` and reports nothing whatsoever.
    /// </summary>
    [Fact]
    public void The_stored_value_is_quoted()
    {
        var value = AutoStartRegistration.ValueFor(@"C:\Program Files\Plutus\PlutusTillAgent.exe");

        Assert.StartsWith("\"", value);
        Assert.EndsWith("\"", value);
    }

    /// <summary>⚠ And never double-quoted. Feeding an already-quoted path back in — which happens the
    /// moment anybody rewrites from a stored value — must not produce <c>""C:\…""</c>, which resolves
    /// to nothing at all.</summary>
    [Fact]
    public void An_already_quoted_path_is_not_quoted_twice()
    {
        Assert.Equal($"\"{Installed}\"", AutoStartRegistration.ValueFor($"\"{Installed}\""));
    }

    // ── does it point here? ───────────────────────────────────────────────────────────────────

    /// <summary>⚠ Quotes and case are both ignored: the value is STORED quoted, and Windows paths are
    /// case-insensitive. A comparison fussy about either would call a correct registration broken and
    /// rewrite it on every single launch.</summary>
    [Theory]
    [InlineData(@"""C:\P\PlutusTillAgent.exe""", @"C:\P\PlutusTillAgent.exe")]
    [InlineData(@"C:\P\PlutusTillAgent.exe", @"C:\P\PlutusTillAgent.exe")]
    [InlineData(@"""c:\p\plutustillagent.exe""", @"C:\P\PlutusTillAgent.exe")]
    [InlineData(@"  ""C:\P\PlutusTillAgent.exe""  ", @"C:\P\PlutusTillAgent.exe")]
    public void A_registration_for_this_exe_is_recognised(string stored, string exe) =>
        Assert.True(AutoStartRegistration.PointsAt(stored, exe));

    /// <summary>⚠⚠ THE FAULT ITSELF. The stale Downloads registration must NOT be mistaken for a
    /// working one — this is the assertion the old code got wrong.</summary>
    [Fact]
    public void The_stale_downloads_registration_is_not_recognised_as_this_exe() =>
        Assert.False(AutoStartRegistration.PointsAt(StaleDownloadsValue, Installed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void An_empty_registration_points_nowhere(string? stored) =>
        Assert.False(AutoStartRegistration.PointsAt(stored, Installed));

    // ── what to do on launch ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE FIX. A registration pointing at a path this exe does not occupy gets REWRITTEN on
    /// launch, which makes the stale case impossible rather than merely detectable. It matters more
    /// from here on, not less: once the agent ships inside the till package it is replaced on a
    /// schedule, so a frozen path would break on a cadence rather than by accident.
    /// </summary>
    [Fact]
    public void A_stale_registration_is_rewritten_on_launch() =>
        Assert.Equal(AutoStartAction.Rewrite,
            AutoStartRegistration.Reconcile(StaleDownloadsValue, Installed));

    [Fact]
    public void A_correct_registration_is_left_alone() =>
        Assert.Equal(AutoStartAction.LeaveAsIs,
            AutoStartRegistration.Reconcile($"\"{Installed}\"", Installed));

    /// <summary>
    /// ⚠⚠ NO REGISTRATION MEANS LEAVE IT OFF. Absence is the operator having turned auto-start off,
    /// or never having turned it on — an agent that registered itself on first run would be
    /// installing itself without being asked, on somebody's PC.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_registration_is_never_created_by_launching(string? stored) =>
        Assert.Equal(AutoStartAction.LeaveOff, AutoStartRegistration.Reconcile(stored, Installed));

    /// <summary>⚠ An agent that cannot say where it is leaves the registration ALONE rather than
    /// rewriting it to nothing — that would turn auto-start off for a reason the operator never chose
    /// and could not see.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unknown_exe_path_never_damages_a_registration(string? exe) =>
        Assert.Equal(AutoStartAction.LeaveAsIs,
            AutoStartRegistration.Reconcile(StaleDownloadsValue, exe!));

    // ── what the checkbox shows ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE CHECKBOX TELLS THE TRUTH NOW. Under the old rule the stale Downloads value showed as
    /// ticked; it must show as UNTICKED, so that if the rewrite could not be made (a locked or
    /// policy-managed hive) the operator sees something they can act on.
    /// </summary>
    [Fact]
    public void The_checkbox_is_unticked_when_the_registration_points_elsewhere() =>
        Assert.False(AutoStartRegistration.ShowsAsEnabled(StaleDownloadsValue, Installed));

    [Fact]
    public void The_checkbox_is_ticked_when_the_registration_points_here() =>
        Assert.True(AutoStartRegistration.ShowsAsEnabled($"\"{Installed}\"", Installed));

    [Fact]
    public void The_checkbox_is_unticked_when_there_is_no_registration() =>
        Assert.False(AutoStartRegistration.ShowsAsEnabled(null, Installed));

    /// <summary>
    /// ⚠ The round trip a launch performs: write for this exe, read it back, and it must be
    /// recognised — otherwise `Reconcile` would rewrite on every launch for ever.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\admin\AppData\Local\Plutus\Agent\PlutusTillAgent.exe")]
    [InlineData(@"C:\Program Files\Plutus\PlutusTillAgent.exe")]
    [InlineData(@"D:\tmp\plutus-till-1.70.0\agent\PlutusTillAgent.exe")]
    public void Writing_then_reading_is_stable(string exe)
    {
        var written = AutoStartRegistration.ValueFor(exe);

        Assert.True(AutoStartRegistration.PointsAt(written, exe));
        Assert.Equal(AutoStartAction.LeaveAsIs, AutoStartRegistration.Reconcile(written, exe));
    }
}
