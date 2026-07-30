using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    public sealed class GiftCardException : Exception
    {
        /// <summary>The HTTP status the controller should return (409 for a state conflict).</summary>
        public int Status { get; }
        public GiftCardException(string message, int status = 409) : base(message) => Status = status;
    }

    /// <summary>
    /// FE7 gift cards as an append-only liability ledger — the same shape as
    /// <see cref="CreditLedgerService"/> (D15): the balance is ALWAYS Σ entries, never a stored
    /// field, so a card's history and its balance cannot drift apart.
    ///
    /// Rows are added to the tracked context; the CALLER saves, so an activation can be atomic with
    /// whatever else it does. Redeems are idempotent by entry id — a till that replays a queued sale
    /// re-sends the same id and gets the original entry back rather than spending the money twice.
    /// </summary>
    public sealed class GiftCardLedgerService
    {
        private readonly MySqlDbContext _db;
        public GiftCardLedgerService(MySqlDbContext db) => _db = db;

        public async Task<long> BalanceAsync(Guid giftCardId, CancellationToken ct = default) =>
            await _db.GiftCardEntries.Where(e => e.GiftCardId == giftCardId)
                .SumAsync(e => (long?)e.AmountPence, ct) ?? 0;

        /// <summary>Finds a card by anything scannable/typeable. Null when the code is malformed or
        /// unknown — the caller decides whether that is a 404 or a friendlier message.</summary>
        public async Task<GiftCard> FindAsync(string input, CancellationToken ct = default)
        {
            var code = GiftCardCodes.TryCanonicalise(input);
            return code == null ? null : await _db.GiftCards.FirstOrDefaultAsync(c => c.Code == code, ct);
        }

        /// <summary>
        /// Activation: the card is sold and loaded. Writes the Issue entry and stamps IssuedAtUtc +
        /// SoldSaleId. Refuses a card that is already active (a second activation would create money
        /// out of nothing), voided, or expired.
        /// </summary>
        public async Task<GiftCardEntry> ActivateAsync(
            GiftCard card, long amountPence, Guid? saleId, Guid? actor, Guid? entryId = null,
            CancellationToken ct = default)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (amountPence <= 0) throw new GiftCardException("The load amount must be positive.", 400);
            if (card.VoidedAtUtc != null) throw new GiftCardException("That card has been voided.");
            if (card.IssuedAtUtc != null) throw new GiftCardException("That card is already active — sell a new one.");
            if (card.ExpiresAtUtc != null && card.ExpiresAtUtc <= DateTime.UtcNow)
                throw new GiftCardException("That card has expired.");

            // idempotent replay (a re-sent activation): return the entry we already wrote
            if (entryId.HasValue)
            {
                var existing = await _db.GiftCardEntries.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == entryId.Value, ct);
                if (existing != null) return existing;
            }

            card.IssuedAtUtc = DateTime.UtcNow;
            card.SoldSaleId = saleId;
            return Append(card, GiftCardEntryType.Issue, amountPence, "Activated", saleId, actor, entryId);
        }

        /// <summary>
        /// Redemption: spend part or all of the balance as a tender. Refuses an over-redeem, an
        /// unsold card, a voided card and an expired one.
        /// </summary>
        public async Task<GiftCardEntry> RedeemAsync(
            GiftCard card, long amountPence, Guid? saleId, Guid? actor, Guid? entryId = null,
            CancellationToken ct = default)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (amountPence <= 0) throw new GiftCardException("The redeem amount must be positive.", 400);

            // Idempotency FIRST — before the state checks. A replay must succeed even if the card has
            // since been emptied by the very entry being replayed.
            if (entryId.HasValue)
            {
                var existing = await _db.GiftCardEntries.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == entryId.Value, ct);
                if (existing != null) return existing;
            }

            if (card.VoidedAtUtc != null) throw new GiftCardException("That card has been voided.");
            if (card.IssuedAtUtc == null) throw new GiftCardException("That card has not been sold yet — it holds no money.");
            if (card.ExpiresAtUtc != null && card.ExpiresAtUtc <= DateTime.UtcNow)
                throw new GiftCardException("That card has expired.");

            var balance = await BalanceAsync(card.Id, ct);
            if (amountPence > balance)
                throw new GiftCardException($"That card only has {balance / 100m:0.00} left.");

            return Append(card, GiftCardEntryType.Redeem, -amountPence, "Redeemed", saleId, actor, entryId);
        }

        /// <summary>Manual correction, either sign (audited by the caller).</summary>
        public GiftCardEntry Adjust(GiftCard card, long signedAmountPence, string reason, Guid? actor) =>
            Append(card, GiftCardEntryType.Adjust, signedAmountPence, reason, null, actor, null);

        /// <summary>Write off the remaining balance at expiry.</summary>
        public GiftCardEntry Expire(GiftCard card, long balancePence, Guid? actor) =>
            Append(card, GiftCardEntryType.Expire, -Math.Abs(balancePence), "Expired", null, actor, null);

        private GiftCardEntry Append(
            GiftCard card, GiftCardEntryType type, long signedAmount, string reason,
            Guid? saleId, Guid? actor, Guid? entryId)
        {
            var entry = new GiftCardEntry
            {
                Id = entryId ?? Uuid7.New(),
                TenantId = card.TenantId,
                GiftCardId = card.Id,
                Type = type,
                AmountPence = signedAmount,
                Reason = reason,
                SaleId = saleId,
                ActorUserId = actor,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.GiftCardEntries.Add(entry);
            return entry;
        }
    }
}
