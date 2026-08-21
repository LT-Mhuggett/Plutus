using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plutus.SharedKernel;

/// <summary>
/// **Every timestamp this API sends, read as the instant it is — the ONE rule, .NET half.**
///
/// ⚠⚠ MATT, 2026-08-21: *"Why are the sales a correct time on the portal and an hour earlier on the
/// webtill?"* The web till's fault was in TypeScript, but MAUI had **the same bug, in the same
/// place**, and nobody had looked: `ReportCatalogue`'s Sales report renders
/// `r.OccurredAtUtc.ToLocalTime()`, and that is a **no-op** on a <see cref="DateTime"/> whose
/// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/>. During BST it was an hour
/// early on both tills; in winter both would have been right and the fault would have gone back into
/// hiding until March.
///
/// ⚠⚠ **A BARE TIMESTAMP IS STILL UTC.** Values that come straight off a MySQL column arrive from EF
/// as `Unspecified`, and `System.Text.Json` writes those with **no suffix** — `"2026-08-21T14:30:00"`
/// — then reads them back as `Unspecified`. Values that came from `DateTime.UtcNow` are `Utc` and get
/// a trailing `Z`. **Both formats are on this wire today, from the same API**, which is why nothing
/// may assume one of them: the portal's `+ "Z"` shipped "Invalid Date" across every till on
/// 2026-08-12 by assuming the other.
///
/// ⚠ FIXED AT DESERIALISATION, not at the thirteen call sites. `Utc` is registered on
/// `PlutusApiClient.Json`, so every `DateTime` this client reads arrives with `Kind = Utc` and
/// `.ToLocalTime()` means what it says everywhere — including in code not yet written. Thirteen
/// corrected call sites would have been thirteen chances for the fourteenth to be wrong.
///
/// ⚠ **`till-design.md` C2 pins this to the two TypeScript copies** — `apiTime.ts` in the web till
/// and in the portal. Three implementations of one rule in three languages, because the three apps
/// share no package. Change the rule here, change it there, same commit.
/// </summary>
public static class ApiTime
{
    /// <summary>
    /// Treat a timestamp from the API as the UTC instant it is.
    ///
    /// ⚠ `Unspecified` → `Utc` WITHOUT SHIFTING THE CLOCK (`SpecifyKind`, never `ToUniversalTime`).
    /// The value is already the right numbers; only the label is missing. `ToUniversalTime` on an
    /// `Unspecified` value assumes it is local and subtracts the offset — which would turn a
    /// one-hour error into a one-hour error in the other direction, and look like a fix in winter.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>The same instant on this till's clock — what an operator reads.</summary>
    public static DateTime AsLocal(DateTime value) => AsUtc(value).ToLocalTime();

    /// <summary>
    /// Reads any `DateTime` off the wire as UTC, whether or not it said so.
    ///
    /// ⚠ WRITES `Z`, ALWAYS. This client also POSTs timestamps, and a till that sends a bare stamp
    /// makes the server guess in exactly the way this whole file exists to stop.
    /// </summary>
    public sealed class Utc : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __) =>
            AsUtc(reader.GetDateTime());

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions _) =>
            writer.WriteStringValue(AsUtc(value));
    }

    /// <summary>
    /// The nullable twin. ⚠ Registered SEPARATELY — `System.Text.Json` does not apply a
    /// `JsonConverter&lt;DateTime&gt;` to a `DateTime?`, and a half-registered rule is the shape of a
    /// bug that only shows on the fields that happen to be optional.
    /// </summary>
    public sealed class UtcNullable : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __) =>
            reader.TokenType == JsonTokenType.Null ? null : AsUtc(reader.GetDateTime());

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions _)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(AsUtc(value.Value));
        }
    }
}
