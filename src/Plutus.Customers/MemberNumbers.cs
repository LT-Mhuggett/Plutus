using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    /// <summary>
    /// FE2 membership numbers: a short, human-typeable, scannable customer id.
    ///
    /// Shape: <c>NNNNNNC</c> — a 6-digit per-tenant sequence plus one check character, e.g.
    /// <c>000482K</c>. On a card the barcode payload carries a <see cref="Prefix"/> ("C…") so the
    /// till's scan handler can tell a member card from a product EAN or a receipt's sale id.
    ///
    /// Why this shape: short enough to read down a phone line, entirely within the Code 39 charset
    /// (so the existing hand-rolled renderer prints it and cheap scanners read it — unlike a 36-char
    /// UUID), and the check character catches mis-keys and mis-scans before they become a wrong
    /// customer.
    ///
    /// The check character comes from <see cref="Crockford32.Alphabet"/> — the codebase's existing
    /// human-keyable set, which drops I/L/O/U so a card can be read aloud without "was that a one or
    /// an I?". (The classic Code 39 mod-43 set is worse still: it includes space, '$', '%', '+', '.'
    /// and '/'.) Input is folded through <see cref="Crockford32.Normalise"/>, so someone typing O for
    /// 0 or I for 1 still lands on the right customer. Weights (7,3,1 repeating) mean transposing two
    /// adjacent digits changes the sum, which a plain digit-sum would miss.
    /// </summary>
    public static class MemberNumbers
    {
        /// <summary>Barcode-payload prefix that marks a scan as a member card.</summary>
        public const string Prefix = "C";
        public const int SequenceDigits = 6;

        private static readonly int[] Weights = { 7, 3, 1 };

        /// <summary>The check character for a digit string (its canonical form appends this).</summary>
        public static char CheckChar(string digits)
        {
            if (string.IsNullOrEmpty(digits)) throw new ArgumentException("digits required", nameof(digits));
            var sum = 0;
            for (var i = 0; i < digits.Length; i++)
            {
                if (!char.IsDigit(digits[i])) throw new ArgumentException("digits only", nameof(digits));
                sum += (digits[i] - '0') * Weights[i % Weights.Length];
            }
            return Crockford32.Alphabet[sum % Crockford32.Alphabet.Length];
        }

        /// <summary>Sequence number → member number, e.g. 482 → "000482K".</summary>
        public static string Format(long sequence)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            // Past 999,999 the number simply grows a digit rather than wrapping into a collision.
            var digits = sequence.ToString(new string('0', SequenceDigits));
            return digits + CheckChar(digits);
        }

        /// <summary>What goes in the barcode on a printed card.</summary>
        public static string BarcodePayload(string memberNo) => Prefix + memberNo;

        /// <summary>
        /// Turn anything a human or scanner might supply into the canonical member number, or null
        /// when the input is not one. Accepts: the bare number ("000482K"), the barcode payload
        /// ("C000482K"), the sequence without its check character ("000482", "482"), and any of those
        /// in lower case or with spaces/hyphens. A 7+ character candidate whose check character does
        /// not verify is REJECTED (null) — that is the whole point of having one.
        /// </summary>
        public static string TryCanonicalise(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var stripped = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            // Crockford folding (O→0, I/L→1, U→V) so a hand-typed card number still resolves.
            var s = Crockford32.Normalise(stripped);
            if (s.Length > 1 && s.StartsWith(Prefix, StringComparison.Ordinal)) s = s[Prefix.Length..];
            if (s.Length == 0) return null;

            // LENGTH first, not "all digits": a check character is often itself a digit (e.g.
            // sequence 1 → "0000011"), so a full number can be all digits and must not be mistaken
            // for a bare sequence.
            if (s.Length == SequenceDigits + 1)
            {
                var body = s[..^1];
                if (!body.All(char.IsDigit)) return null;
                return CheckChar(body) == s[^1] ? s : null;
            }

            // shorter, all digits → a sequence without its check character ("482" → "000482" + check)
            if (s.Length <= SequenceDigits && s.All(char.IsDigit))
            {
                var digits = s.PadLeft(SequenceDigits, '0');
                return digits + CheckChar(digits);
            }

            return null;
        }

        /// <summary>True when <paramref name="memberNo"/> is a well-formed number with a valid
        /// check character.</summary>
        public static bool IsValid(string memberNo) =>
            !string.IsNullOrWhiteSpace(memberNo) && TryCanonicalise(memberNo) == memberNo.Trim().ToUpperInvariant()
            && memberNo.Trim().Length == SequenceDigits + 1;
    }

    /// <summary>Hands out the next membership number for a tenant.</summary>
    public static class MemberNoAllocator
    {
        private const int MaxAttempts = 5;

        /// <summary>
        /// Allocates and PERSISTS the next number for <paramref name="tenantId"/> (the counter row
        /// is saved here so the sequence is claimed before the caller's own save). Retries on a
        /// concurrent allocation — the counter's value is its concurrency token, so a race makes the
        /// loser re-read and take the next number rather than duplicate one.
        /// </summary>
        public static async Task<string> NextAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                var counter = await db.MemberNoCounters.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
                if (counter == null)
                {
                    // First allocation for this tenant: start past any number already in use, so a
                    // half-backfilled tenant (or a restored dataset) can never re-issue one.
                    var highest = await HighestSequenceAsync(db, tenantId, ct);
                    counter = new MemberNoCounter { TenantId = tenantId, Next = highest + 1 };
                    db.MemberNoCounters.Add(counter);
                }

                var sequence = counter.Next;
                counter.Next = sequence + 1;
                try
                {
                    await db.SaveChangesAsync(ct);
                    return MemberNumbers.Format(sequence);
                }
                catch (DbUpdateException) when (attempt < MaxAttempts)
                {
                    // Someone else allocated (concurrency token mismatch) or inserted the counter
                    // first — drop our tracked copy and try again with fresh state.
                    foreach (var entry in db.ChangeTracker.Entries<MemberNoCounter>().ToList())
                        entry.State = EntityState.Detached;
                }
            }
        }

        /// <summary>The largest sequence already issued to this tenant (0 when none).</summary>
        public static async Task<long> HighestSequenceAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            var numbers = await db.Customers.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.MemberNo != null)
                .Select(c => c.MemberNo)
                .ToListAsync(ct);

            long highest = 0;
            foreach (var n in numbers)
            {
                if (n.Length <= 1) continue;
                if (long.TryParse(n[..^1], out var seq) && seq > highest) highest = seq;
            }
            return highest;
        }
    }
}
