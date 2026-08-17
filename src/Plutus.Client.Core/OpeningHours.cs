using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Plutus.Client.Core;

/// <summary>How a store's opening hours read.</summary>
public enum OpeningHoursState
{
    /// <summary>Nothing stored. ⚠ The ONLY state that should send somebody to the portal to add them.</summary>
    Unset = 0,
    /// <summary>Hours are stored and understood.</summary>
    Set = 1,
    /// <summary>Something IS stored and this cannot read it. ⚠ Never shown as <see cref="Unset"/>.</summary>
    Unreadable = 2,
}

/// <summary>One open period. ⚠ Times are for DISPLAY — normalised to `HH:mm`, never re-parsed as money.</summary>
public sealed record OpeningSpan(string Open, string Close);

/// <summary>One day. ⚠ No spans means CLOSED, not "unknown".</summary>
public sealed record OpeningDay(string Key, string Label, IReadOnlyList<OpeningSpan> Spans);

/// <summary>The reading. <see cref="Week"/> is empty unless <see cref="State"/> is
/// <see cref="OpeningHoursState.Set"/>; <see cref="Detail"/> is filled only when it is
/// <see cref="OpeningHoursState.Unreadable"/>.</summary>
public sealed record OpeningHoursReading(
    OpeningHoursState State, IReadOnlyList<OpeningDay> Week, string? Detail);

/// <summary>
/// The store's opening hours, as the portal stores them.
///
/// ⚠⚠ WHY THIS EXISTS. Both tills printed <i>"Not set — add opening hours in the management portal"</i>
/// for <b>three different situations</b>: genuinely not set, present but malformed, and present in a
/// shape the reader did not expect. Matt, 2026-08-17: <i>"webtill does not show the opening hours set
/// in the portal"</i> — and from that message nobody could tell which of the three it was, because both
/// screens said the same thing for all of them. <b>One sentence for three states is not information,
/// it is a shrug.</b>
///
/// ⚠⚠ AND THE PORTAL HAD AN UNVALIDATED "Advanced (JSON)" TEXTAREA, whose contents went straight to
/// the server. A trailing comma or an unquoted key saved happily, the portal showed the operator their
/// own text back, and every till read it as "not set". That is the shape of what was reported, which is
/// why this parser is TOLERANT and, when it cannot cope, SAYS SO. The portal is now the strict end of
/// the agreement (it refuses to save what a till would have to stretch for); the tills are the
/// tolerant end, because they have to read what is already in the field.
///
/// ⚠ C2 TWIN of the web till's <c>src/openingHours.ts</c> — same tolerances, same three states, same
/// wording. A shop whose hours read fine on one till and "not set" on the other is the same class of
/// defect as a money divergence, just cheaper. <c>till-design.md</c> C2 pins the pair.
///
/// ⚠ NOT money, so it fails OPEN: anything it cannot understand is reported and the rest of the screen
/// renders. A store-details screen must never be the thing that takes a till down.
/// </summary>
public static class OpeningHours
{
    /// <summary>⚠ The portal's keys in the portal's own order — never <see cref="DayOfWeek"/>, which
    /// starts on Sunday and would silently reorder a shop's week.</summary>
    public static readonly (string Key, string Label)[] Days =
    {
        ("mon", "Monday"), ("tue", "Tuesday"), ("wed", "Wednesday"), ("thu", "Thursday"),
        ("fri", "Friday"), ("sat", "Saturday"), ("sun", "Sunday"),
    };

    /// <summary>
    /// Read the portal's blob.
    ///
    /// ⚠⚠ THE THREE STATES ARE THE POINT. <see cref="OpeningHoursState.Unset"/> sends somebody to the
    /// portal; <see cref="OpeningHoursState.Unreadable"/> tells them the portal already holds something
    /// that needs looking at. Collapsing them — which both tills did until 2026-08-17 — means a shop
    /// with mistyped hours is told to type them again into the box that is already wrong.
    /// </summary>
    public static OpeningHoursReading Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Unset;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            // ⚠ The parser's own message. "Invalid JSON" tells nobody which character to look at.
            return new OpeningHoursReading(OpeningHoursState.Unreadable, Array.Empty<OpeningDay>(), ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Null) return Unset;   // a stored literal null is nothing

            if (root.ValueKind != JsonValueKind.Object)
                return Unreadable(
                    "expected an object of days, e.g. {\"mon\":[{\"open\":\"09:00\",\"close\":\"17:30\"}]} — got "
                    + (root.ValueKind == JsonValueKind.Array ? "a list" : root.ValueKind.ToString().ToLowerInvariant()));

            var properties = root.EnumerateObject().ToList();
            if (properties.Count == 0) return Unset;

            var byKey = new Dictionary<string, List<OpeningSpan>>();
            var unknownKeys = new List<string>();
            var recognisedContent = false;
            var lostContent = false;

            foreach (var property in properties)
            {
                var key = DayKey(property.Name);
                if (key is null)
                {
                    unknownKeys.Add(property.Name);
                    continue;
                }

                var (spans, hadContent, unreadableValue) = SpansOf(property.Value);

                // ⚠ MERGED, not replaced. {"mon":…,"Monday":…} is a mistake somebody could make in the
                // advanced box, and dropping the second would lose an afternoon without saying so.
                if (!byKey.TryGetValue(key, out var existing)) byKey[key] = existing = new List<OpeningSpan>();
                existing.AddRange(spans);

                if (hadContent) recognisedContent = true;
                if (unreadableValue) lostContent = true;
            }

            if (unknownKeys.Count > 0 && byKey.Count == 0)
                return Unreadable($"none of these are days: {string.Join(", ", unknownKeys.Take(7))} — "
                    + $"expected {string.Join(", ", Days.Select(d => d.Key))}");

            if (lostContent && byKey.Values.All(s => s.Count == 0))
                return Unreadable("each day needs times, e.g. {\"open\":\"09:00\",\"close\":\"17:30\"}");

            if (!recognisedContent) return Unset;

            // ⚠ A partly-understood blob still renders — the leftovers are named by
            // `UnknownDayKeys` rather than dropped, because a key nobody reads is a setting somebody
            // believes is in effect.
            var week = Days
                .Select(d => new OpeningDay(d.Key, d.Label,
                    byKey.TryGetValue(d.Key, out var s) ? s : Array.Empty<OpeningSpan>()))
                .ToList();

            return new OpeningHoursReading(OpeningHoursState.Set, week, null);
        }
    }

    /// <summary>What a day reads as on screen. ⚠ "Closed" — never blank, which on MAUI is
    /// indistinguishable from a binding to a property that does not exist.</summary>
    public static string DayText(OpeningDay day) =>
        day.Spans.Count == 0 ? "Closed" : string.Join(", ", day.Spans.Select(s => $"{s.Open}–{s.Close}"));

    /// <summary>⚠ The keys the portal sent that are not days, so a screen can NAME them rather than
    /// drop them silently. Empty for a clean blob, and empty for junk (the unreadable message covers
    /// that case).</summary>
    public static IReadOnlyList<string> UnknownDayKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
            return doc.RootElement.EnumerateObject()
                .Where(p => DayKey(p.Name) is null)
                .Select(p => p.Name)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static OpeningHoursReading Unset { get; } =
        new(OpeningHoursState.Unset, Array.Empty<OpeningDay>(), null);

    private static OpeningHoursReading Unreadable(string detail) =>
        new(OpeningHoursState.Unreadable, Array.Empty<OpeningDay>(), detail);

    /// <summary>
    /// A day key as the portal writes it, from whatever a human typed.
    ///
    /// ⚠ "Monday", "MON", " mon " and "monday" all mean Monday. The simple editor writes `mon`; the
    /// advanced textarea is typed by a person, and refusing a whole week over a capital letter would be
    /// pedantry with a shop's opening times as the cost.
    /// </summary>
    private static string? DayKey(string raw)
    {
        var trimmed = raw.Trim().ToLowerInvariant();
        if (trimmed.Length < 3) return null;
        var k = trimmed[..3];
        return Days.Any(d => d.Key == k) ? k : null;
    }

    /// <summary>A day's value, which may be one span, several, a string, or an explicit "closed".</summary>
    private static (List<OpeningSpan> Spans, bool HadContent, bool Unreadable) SpansOf(JsonElement value)
    {
        var none = new List<OpeningSpan>();

        switch (value.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return (none, false, false);

            // ⚠ `false` is how a person says shut. An explicit CLOSED, not junk.
            case JsonValueKind.False:
                return (none, true, false);
            case JsonValueKind.True:
                return (none, true, true);

            case JsonValueKind.String:
            {
                var text = value.GetString() ?? string.Empty;
                if (text.Trim().Length == 0
                    || text.Trim().Equals("closed", StringComparison.OrdinalIgnoreCase)
                    || text.Trim().Equals("shut", StringComparison.OrdinalIgnoreCase))
                    return (none, true, false);

                var span = SpanFromText(text);
                return span is null ? (none, true, false) : (new List<OpeningSpan> { span }, true, false);
            }

            case JsonValueKind.Array:
            {
                var items = value.EnumerateArray().ToList();
                // ⚠ `[]` IS "closed", deliberately — it is what the portal's editor leaves behind.
                if (items.Count == 0) return (none, true, false);

                var spans = items.Select(SpanFrom).Where(s => s is not null).Select(s => s!).ToList();
                return (spans, true, spans.Count == 0);
            }

            case JsonValueKind.Object:
            {
                var span = SpanFrom(value);
                return span is null ? (none, true, true) : (new List<OpeningSpan> { span }, true, false);
            }

            default:
                return (none, true, true);
        }
    }

    private static OpeningSpan? SpanFrom(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return SpanFromText(element.GetString() ?? string.Empty);

        if (element.ValueKind != JsonValueKind.Object) return null;

        // ⚠ Case-insensitive field names for the same reason the keys are: this half of the blob can be
        // hand-typed. `from`/`to` are what somebody writes when they have not looked at the format.
        string? open = null, close = null;
        foreach (var field in element.EnumerateObject())
        {
            if (field.Value.ValueKind != JsonValueKind.String) continue;
            var name = field.Name.Trim().ToLowerInvariant();
            if (name is "open" or "from" or "start") open ??= Time(field.Value.GetString());
            else if (name is "close" or "to" or "end") close ??= Time(field.Value.GetString());
        }

        return open is null || close is null ? null : new OpeningSpan(open, close);
    }

    /// <summary>⚠ An en dash included: a range pasted out of a document carries one.</summary>
    private static OpeningSpan? SpanFromText(string text)
    {
        var parts = text.Split(new[] { '-', '–', '—' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            parts = text.Split(new[] { " to " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;
        }

        var open = Time(parts[0]);
        var close = Time(parts[1]);
        return open is null || close is null ? null : new OpeningSpan(open, close);
    }

    /// <summary>
    /// A time, normalised for display.
    ///
    /// ⚠ `9:00` → `09:00` so the column lines up, and `09:00:00` → `09:00` because a shop's door does
    /// not open on a second boundary. Anything else is passed through untouched rather than rejected —
    /// a time this does not recognise is still better shown than swallowed.
    /// </summary>
    private static string? Time(string? raw)
    {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t)) return null;

        var bits = t.Split(':');
        if (bits.Length is 2 or 3
            && int.TryParse(bits[0], out var h) && h is >= 0 and < 24
            && bits[1].Length == 2 && int.TryParse(bits[1], out var m) && m is >= 0 and < 60)
            return $"{h:00}:{m:00}";

        return t;
    }
}
