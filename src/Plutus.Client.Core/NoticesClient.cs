using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

/// <summary>What one noticeboard poll produced. <see cref="Delivered"/> false means the poll
/// failed — ⚠ which is NOT the same as "there is nothing to show", and a caller that treats an
/// empty list as good news will quietly clear a live incident banner the moment the network
/// hiccups.</summary>
public sealed record NoticesOutcome(
    bool Delivered,
    IReadOnlyList<PickNoteDto> PickNotes,
    IReadOnlyList<AnnouncementDto> Announcements)
{
    public static NoticesOutcome Failed { get; } =
        new(false, Array.Empty<PickNoteDto>(), Array.Empty<AnnouncementDto>());
}

/// <summary>
/// WP5b — the till's noticeboard: pick-from-floor notes, and platform announcements.
///
/// ⚠ NOTHING HERE MAY EVER BLOCK SELLING, on the same principle as <see cref="SyncClient"/>. Every
/// method swallows its exceptions and reports failure in the result instead. A banner that cannot
/// load is a banner that does not appear; it is never a till that will not ring up a customer.
///
/// ⚠ In Client.Core rather than in MAUI because the two filtering rules below decide **what a human
/// is shown**, and two tills standing in the same shop applying them differently is exactly the
/// class of divergence `till-design.md` Part C exists to prevent.
/// </summary>
public sealed class NoticesClient
{
    private readonly PlutusApiClient _api;

    public NoticesClient(PlutusApiClient api) => _api = api ?? throw new ArgumentNullException(nameof(api));

    /// <summary>
    /// Whether an announcement belongs on a TILL at all.
    ///
    /// ⚠ `Info` is portal-only. The till banner interrupts someone mid-transaction, so it is
    /// reserved for the two severities that change what they should do — a maintenance window they
    /// need to finish serving before, or an incident that explains why something is failing right
    /// now. Putting release notes there teaches operators to ignore the banner, and then the
    /// incident goes unread too.
    ///
    /// ⚠ Case-insensitive, and an UNRECOGNISED severity SHOWS. A future backend adding a severity
    /// this build has never heard of will have added something at least as urgent as maintenance —
    /// defaulting to "hide it" would silently blank exactly the messages worth reading.
    /// </summary>
    public static bool ShowsOnATill(string? severity) =>
        !string.Equals(severity?.Trim(), "Info", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a pick note belongs on a till in <paramref name="storeId"/>.
    ///
    /// ⚠ A null <see cref="PickNoteDto.StoreId"/> means EVERY store's tills — the fulfilment store
    /// was never decided, so the note goes to everyone rather than to nobody. A non-null one is
    /// addressed, and showing it elsewhere is worse than useless: staff in the wrong shop go
    /// looking for stock that was never on their shelves, and the shop that does hold it sees the
    /// same note and may assume the other branch has dealt with it.
    ///
    /// ⚠ A till that does not yet know its own store (<paramref name="storeId"/> null) sees
    /// everything. It is newly enrolled or mid-placement-refresh, and showing a note that is not
    /// its problem costs someone a walk; hiding one that IS its problem costs an oversell.
    /// </summary>
    public static bool IsForStore(PickNoteDto note, int? storeId)
    {
        if (note is null) throw new ArgumentNullException(nameof(note));
        return note.StoreId is null || storeId is null || note.StoreId == storeId;
    }

    /// <summary>
    /// Poll both feeds and return what this till should display. Never throws.
    ///
    /// ⚠ Acked notes are dropped even when the caller asked for them, because this is the DISPLAY
    /// list. Pass <paramref name="unackedOnly"/> false only to reconcile history.
    /// </summary>
    public async Task<NoticesOutcome> PollAsync(
        int? storeId, bool unackedOnly = true, CancellationToken ct = default)
    {
        try
        {
            // Sequential, not Task.WhenAll: these ride the 60s background cadence where latency is
            // irrelevant, and a till on a slow shop connection issuing two concurrent requests to
            // the same host is how the pair end up contending with the sale outbox that matters.
            var notes = await _api.GetPickNotesAsync(unackedOnly, ct).ConfigureAwait(false);
            var anns = await _api.GetAnnouncementsAsync(ct).ConfigureAwait(false);

            // ⚠ Both null = both requests failed. Report failure rather than an empty board, or a
            // live incident banner disappears on the first flaky poll.
            if (notes is null && anns is null) return NoticesOutcome.Failed;

            return new NoticesOutcome(
                Delivered: true,
                PickNotes: (notes ?? Array.Empty<PickNoteDto>())
                    .Where(n => n.AckedAtUtc is null)
                    .Where(n => IsForStore(n, storeId))
                    .OrderByDescending(n => n.CreatedAtUtc)
                    .ToList(),
                Announcements: (anns ?? Array.Empty<AnnouncementDto>())
                    .Where(a => ShowsOnATill(a.Severity))
                    .ToList());
        }
        catch
        {
            return NoticesOutcome.Failed;
        }
    }

    /// <summary>
    /// Acknowledge a pick note. True when the platform has recorded it.
    ///
    /// ⚠ THIS DOES NOT WORK OFFLINE, deliberately, and it is the one place on the till where that
    /// is the right answer. An ack is a claim that a human took stock off a shelf, and it has to
    /// reach the shop that is waiting to ship the order. Queuing it in the outbox would let a till
    /// mark somebody else's job done hours before anybody heard about it.
    ///
    /// So a failed ack leaves the note unacked and it reappears on the next poll. That is mildly
    /// irritating and completely safe — the failure mode in the other direction is a note that
    /// silently vanishes and an oversold web order.
    ///
    /// ⚠ 404 counts as SUCCESS: it means the note is already gone — acked by another till, or not
    /// this tenant's. Retrying forever against a note that will never exist would be a permanent
    /// error on the screen with nothing a human could do about it.
    /// </summary>
    public async Task<bool> AckAsync(Guid noteId, CancellationToken ct = default)
    {
        try
        {
            var status = await _api.AckPickNoteAsync(noteId, ct).ConfigureAwait(false);
            return status == HttpStatusCode.OK
                || status == HttpStatusCode.NoContent
                || status == HttpStatusCode.NotFound;
        }
        catch
        {
            return false;
        }
    }
}
