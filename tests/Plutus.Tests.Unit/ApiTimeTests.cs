using System;
using System.Text.Json;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The UTC rule — .NET half.
///
/// ⚠⚠ C2 TWIN of the web till's and the portal's `apiTime.test.ts`. **THE SAME VECTORS RUN IN BOTH
/// LANGUAGES**, and that is the only thing stopping the three copies drifting: this bug existed in
/// TypeScript and in .NET simultaneously, in the same feature, and each was found separately. Add a
/// case here, add it there.
///
/// ⚠ Every assertion below is about an instant, never about a wall-clock rendering — a test that
/// asserted "14:30" would pass or fail on the build agent's timezone, which is exactly the class of
/// mistake being tested for.
/// </summary>
public class ApiTimeTests
{
    private sealed record Stamped(DateTime When, DateTime? Maybe);

    private static readonly JsonSerializerOptions Options = Make();

    private static JsonSerializerOptions Make()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new ApiTime.Utc());
        o.Converters.Add(new ApiTime.UtcNullable());
        return o;
    }

    /// <summary>
    /// ⚠⚠ THE BUG ITSELF. A bare stamp is what EF + System.Text.Json produce for any MySQL `datetime`
    /// column, and reading it as `Unspecified` is what made `.ToLocalTime()` a no-op.
    /// </summary>
    [Fact]
    public void A_bare_stamp_is_read_as_utc()
    {
        var s = JsonSerializer.Deserialize<Stamped>(
            """{"when":"2026-08-21T14:30:00","maybe":null}""", Options);

        Assert.Equal(DateTimeKind.Utc, s!.When.Kind);
        Assert.Equal(new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc), s.When);
        Assert.Null(s.Maybe);
    }

    /// <summary>⚠ THE CLOCK MUST NOT MOVE. `SpecifyKind`, never `ToUniversalTime` — the numbers were
    /// already right and only the label was missing. Getting this wrong looks like a fix in winter.</summary>
    [Fact]
    public void Labelling_a_bare_stamp_does_not_shift_it()
    {
        var bare = new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(TimeSpan.FromHours(14.5), ApiTime.AsUtc(bare).TimeOfDay);
    }

    /// <summary>⚠ THE OTHER HALF OF THE 2026-08-12 FAULT: a value that already says `Z` must be left
    /// alone. The portal's `+ "Z"` produced `…ZZ` and every till read "Invalid Date".</summary>
    [Fact]
    public void A_stamp_that_already_says_utc_is_untouched()
    {
        var s = JsonSerializer.Deserialize<Stamped>(
            """{"when":"2026-08-21T14:30:00Z","maybe":"2026-08-21T14:30:00Z"}""", Options);

        Assert.Equal(new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc), s!.When);
        Assert.Equal(DateTimeKind.Utc, s.Maybe!.Value.Kind);
    }

    /// <summary>⚠ An explicit offset is honoured, not stripped — 15:30+01:00 is 14:30 UTC.</summary>
    [Fact]
    public void An_offset_stamp_is_converted_not_relabelled()
    {
        var s = JsonSerializer.Deserialize<Stamped>(
            """{"when":"2026-08-21T15:30:00+01:00","maybe":null}""", Options);

        Assert.Equal(new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc), s!.When);
    }

    /// <summary>
    /// ⚠⚠ THE NULLABLE CONVERTER IS REGISTERED SEPARATELY OR IT IS NOT REGISTERED. A
    /// `JsonConverter&lt;DateTime&gt;` is not applied to a `DateTime?`, so a half-registered rule shows
    /// up only on the fields that happen to be optional — which is most of the interesting ones.
    /// </summary>
    [Fact]
    public void The_nullable_stamp_gets_the_same_treatment()
    {
        var s = JsonSerializer.Deserialize<Stamped>(
            """{"when":"2026-08-21T14:30:00","maybe":"2026-08-21T09:00:00"}""", Options);

        Assert.Equal(DateTimeKind.Utc, s!.Maybe!.Value.Kind);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc), s.Maybe.Value);
    }

    /// <summary>⚠ THE TILL POSTS TIMESTAMPS TOO. Writing a bare stamp makes the server guess in
    /// exactly the way this rule exists to stop.</summary>
    [Fact]
    public void Writing_always_emits_a_zone()
    {
        var json = JsonSerializer.Serialize(
            new Stamped(new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Unspecified), null), Options);

        Assert.Contains("2026-08-21T14:30:00Z", json);
    }

    /// <summary>⚠ Idempotent, because it will be applied to values that have been through it.</summary>
    [Fact]
    public void As_utc_is_idempotent()
    {
        var once = ApiTime.AsUtc(new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Unspecified));

        Assert.Equal(once, ApiTime.AsUtc(once));
    }

    /// <summary>
    /// ⚠⚠ THE REGRESSION, STATED AS A ROUND TRIP. This is the Sales report: a bare stamp, converted
    /// for display. `AsLocal` must land on the same instant the server meant, whatever the machine
    /// running this test is set to — so it is asserted by converting back, not by reading a clock.
    /// </summary>
    [Fact]
    public void As_local_lands_on_the_instant_the_server_meant()
    {
        var bare = new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(
            new DateTime(2026, 8, 21, 14, 30, 0, DateTimeKind.Utc),
            ApiTime.AsLocal(bare).ToUniversalTime());
    }
}
