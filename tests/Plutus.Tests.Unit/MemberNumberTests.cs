using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Customers;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// FE2 membership numbers (DoD): every customer gets a unique number; a scanned card, a typed
/// number, or the bare sequence all resolve to the same canonical value; a mis-keyed digit is
/// REJECTED by the check character; concurrent creates never share a number.
/// </summary>
public class MemberNumberTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "memberno-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static Customer AddCustomer(MySqlDbContext db, string name, DateTime created, string memberNo = null)
    {
        var c = new Customer
        {
            Id = Uuid7.New(), TenantId = Tenant, Name = name, MemberNo = memberNo,
            Active = true, CreatedAtUtc = created,
        };
        db.Customers.Add(c);
        return c;
    }

    // ── format + check character ──

    [Fact]
    public void Format_is_six_digits_plus_a_check_character()
    {
        var n = MemberNumbers.Format(482);
        Assert.Equal(7, n.Length);
        Assert.StartsWith("000482", n);
        Assert.True(char.IsLetterOrDigit(n[^1]));
        Assert.True(MemberNumbers.IsValid(n));
    }

    [Fact]
    public void Check_character_avoids_symbols_and_the_ambiguous_letters()
    {
        // Code 39's classic mod-43 set includes ' ', '$', '%', '+', '.', '/', '-' — unusable in a
        // number people read aloud — and I/L/O/U are indistinguishable from 1/0/V when hand-written
        // or dictated. Sweep a wide range and prove none of them ever appear.
        for (long seq = 0; seq < 5000; seq++)
        {
            var check = MemberNumbers.Format(seq)[^1];
            Assert.True(char.IsAsciiLetterUpper(check) || char.IsAsciiDigit(check), $"seq {seq}: '{check}'");
            Assert.DoesNotContain(check, "ILOU");
        }
    }

    /// <summary>Regression: the check character is often itself a digit (sequence 1 → "0000011"),
    /// so a complete number can be all-digits. Parsing must key off LENGTH, not "looks numeric",
    /// or every such number silently fails to resolve.</summary>
    [Fact]
    public void Numbers_whose_check_character_is_a_digit_still_resolve()
    {
        var digitChecked = Enumerable.Range(1, 300)
            .Select(i => MemberNumbers.Format(i))
            .Where(n => char.IsDigit(n[^1]))
            .ToList();

        Assert.NotEmpty(digitChecked); // the case exists, so it must be covered
        foreach (var n in digitChecked)
        {
            Assert.True(MemberNumbers.IsValid(n), n);
            Assert.Equal(n, MemberNumbers.TryCanonicalise(n));
            Assert.Equal(n, MemberNumbers.TryCanonicalise(MemberNumbers.BarcodePayload(n)));
        }
    }

    [Fact]
    public void A_single_mis_keyed_digit_is_rejected()
    {
        var good = MemberNumbers.Format(482);
        var bad = good[..5] + (good[5] == '2' ? '3' : '2') + good[^1]; // wrong digit, same check char
        Assert.Null(MemberNumbers.TryCanonicalise(bad));
    }

    [Fact]
    public void Transposed_adjacent_digits_are_rejected()
    {
        // 000482 → 000842 with the ORIGINAL check character must not verify (weighted, not a plain sum)
        var good = MemberNumbers.Format(482);
        var transposed = "000842" + good[^1];
        Assert.Null(MemberNumbers.TryCanonicalise(transposed));
    }

    // ── canonicalising what a scanner or human supplies ──

    [Theory]
    [InlineData("full")]        // exactly as printed on the card
    [InlineData("payload")]     // the barcode payload a scanner types ("C…")
    [InlineData("lower")]       // hand-typed in lower case
    [InlineData("spaced")]      // stray whitespace
    [InlineData("hyphenated")]
    [InlineData("sequence")]    // the sequence without its check character
    [InlineData("short")]       // "482" — nobody types the leading zeros
    public void All_accepted_forms_resolve_to_the_same_number(string form)
    {
        var canonical = MemberNumbers.Format(482);
        var input = form switch
        {
            "full" => canonical,
            "payload" => MemberNumbers.BarcodePayload(canonical),
            "lower" => MemberNumbers.BarcodePayload(canonical).ToLowerInvariant(),
            "spaced" => $" {canonical[..3]} {canonical[3..]} ",
            "hyphenated" => $"{canonical[..6]}-{canonical[^1]}",
            "sequence" => canonical[..6],
            _ => "482",
        };
        Assert.Equal(canonical, MemberNumbers.TryCanonicalise(input));
    }

    /// <summary>Crockford folding: a customer reading their card aloud says "oh" for 0 and "eye"
    /// for 1, and the cashier types what they hear.</summary>
    [Fact]
    public void Ambiguous_characters_are_folded_to_the_intended_digits()
    {
        var canonical = MemberNumbers.Format(482); // 000482…
        Assert.Equal(canonical, MemberNumbers.TryCanonicalise("OOO482" + canonical[^1]));   // letter O → 0
        Assert.Equal(canonical, MemberNumbers.TryCanonicalise("cooo482" + canonical[^1]));  // prefix + O
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ada Lovelace")]        // a name, not a number
    [InlineData("ada@example.com")]
    [InlineData("9780306406157")]       // a 13-digit product EAN — must not look like a member number
    [InlineData("000482Z")]             // wrong check character
    [InlineData("C")]
    public void Non_member_input_is_not_mistaken_for_a_number(string input)
    {
        Assert.Null(MemberNumbers.TryCanonicalise(input));
    }

    [Fact]
    public void Barcode_payload_carries_the_prefix()
    {
        var n = MemberNumbers.Format(1);
        Assert.Equal("C" + n, MemberNumbers.BarcodePayload(n));
        Assert.Equal(n, MemberNumbers.TryCanonicalise(MemberNumbers.BarcodePayload(n)));
    }

    // ── routing a SCAN: member card, or product barcode? (step 27) ──

    [Fact]
    public void A_scanned_member_card_is_recognised_as_one()
    {
        var payload = MemberNumbers.BarcodePayload(MemberNumbers.Format(482));
        Assert.True(MemberNumbers.LooksLikeMemberScan(payload));
        Assert.True(MemberNumbers.LooksLikeMemberScan(payload.ToLowerInvariant()));
        Assert.True(MemberNumbers.LooksLikeMemberScan($" {payload} "));
    }

    /// <summary>⚠ THE WHOLE REASON THIS PREDICATE EXISTS SEPARATELY FROM <c>TryCanonicalise</c>.
    /// A six-digit product barcode canonicalises happily — that is deliberate, because a human
    /// reading a card down the phone says "four eight two". But a SCAN of six digits is far more
    /// likely to be a product, and hijacking it into a customer lookup would make the item
    /// un-scannable with no explanation. So the scan path demands the prefix.</summary>
    [Fact]
    public void A_bare_six_digit_scan_is_NOT_treated_as_a_member_card()
    {
        Assert.False(MemberNumbers.LooksLikeMemberScan("000482"));
        Assert.False(MemberNumbers.LooksLikeMemberScan("482"));
        // ...while the typed/spoken short form still resolves through search, unchanged:
        Assert.Equal(MemberNumbers.Format(482), MemberNumbers.TryCanonicalise("482"));
    }

    /// <summary>A prefixed code whose check character fails must be reported as a bad card scan,
    /// not fall through to an item search that can never match.</summary>
    [Fact]
    public void A_prefixed_code_with_a_broken_check_character_is_not_a_member_scan()
    {
        var good = MemberNumbers.Format(482);
        var broken = MemberNumbers.Prefix + "000842" + good[^1]; // transposed, original check char
        Assert.False(MemberNumbers.LooksLikeMemberScan(broken));
        Assert.Null(MemberNumbers.TryCanonicalise(broken));
    }

    [Theory]
    [InlineData("5012345678900")]   // a 13-digit EAN
    [InlineData("C")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("CANCEL")]
    [InlineData("C000482P9")]       // prefixed but too long
    public void Non_member_scans_are_left_for_the_item_lookup(string? scanned)
    {
        Assert.False(MemberNumbers.LooksLikeMemberScan(scanned));
    }

    /// <summary>Every number the allocator can hand out must survive the round trip its own card
    /// takes: format → print as a barcode → scan → route → canonicalise back.</summary>
    [Fact]
    public void Every_issued_number_round_trips_through_its_own_barcode()
    {
        foreach (var seq in new long[] { 0, 1, 9, 10, 482, 99_999, 999_999 })
        {
            var n = MemberNumbers.Format(seq);
            var payload = MemberNumbers.BarcodePayload(n);
            Assert.Equal(n, MemberNumbers.TryCanonicalise(payload));
            Assert.Equal(n, MemberNumbers.TryCanonicalise(n));
            Assert.True(MemberNumbers.LooksLikeMemberScan(payload), payload);
            Assert.True(MemberNumbers.IsValid(n), n);
        }
    }

    /// <summary>⚠⚠ THE CEILING, PINNED — found by the round-trip test above on 2026-08-13.
    /// <c>Format</c> happily grows a seventh digit past 999,999, but <c>TryCanonicalise</c> keys off
    /// LENGTH (it has to: a check character can itself be a digit) and accepts only
    /// <c>SequenceDigits + 1</c>. So the millionth member of a tenant gets a card that prints, scans
    /// and **resolves to nobody**.
    ///
    /// This test exists to make that boundary a decision rather than a surprise, and to fail loudly
    /// for whoever widens <c>SequenceDigits</c> — which is the supported fix, because both halves
    /// read that one constant and widen in step. ⚠ Loosening the PARSER instead would let a bare
    /// EAN-8 product barcode canonicalise as a member number, which is much worse at a counter.</summary>
    [Fact]
    public void Past_the_sequence_ceiling_a_number_formats_but_cannot_be_read_back()
    {
        var ceiling = (long)Math.Pow(10, MemberNumbers.SequenceDigits);   // 1,000,000 at 6 digits

        var last = MemberNumbers.Format(ceiling - 1);
        Assert.Equal(MemberNumbers.SequenceDigits + 1, last.Length);
        Assert.Equal(last, MemberNumbers.TryCanonicalise(last));          // the last good one

        var over = MemberNumbers.Format(ceiling);
        Assert.Equal(MemberNumbers.SequenceDigits + 2, over.Length);      // it grew a digit...
        Assert.Null(MemberNumbers.TryCanonicalise(over));                 // ...and became unreadable
        Assert.False(MemberNumbers.IsValid(over));
        Assert.False(MemberNumbers.LooksLikeMemberScan(MemberNumbers.BarcodePayload(over)));
    }

    // ── allocation ──

    [Fact]
    public async Task Allocation_is_sequential_and_starts_at_one()
    {
        using var conn = Open();
        using var db = Ctx(conn);
        Assert.Equal(MemberNumbers.Format(1), await MemberNoAllocator.NextAsync(db, Tenant));
        Assert.Equal(MemberNumbers.Format(2), await MemberNoAllocator.NextAsync(db, Tenant));
        Assert.Equal(MemberNumbers.Format(3), await MemberNoAllocator.NextAsync(db, Tenant));
    }

    [Fact]
    public async Task Allocation_never_re_issues_a_number_already_in_use()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            // a customer already holding 000007 but NO counter row (e.g. a restored dataset)
            AddCustomer(db, "Existing", DateTime.UtcNow, MemberNumbers.Format(7));
            db.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            Assert.Equal(MemberNumbers.Format(8), await MemberNoAllocator.NextAsync(db, Tenant));
        }
    }

    [Fact]
    public async Task Concurrent_allocations_do_not_collide()
    {
        using var conn = Open();
        // two independent contexts (separate units of work) racing over the same counter row
        using var a = Ctx(conn);
        using var b = Ctx(conn);
        var first = await MemberNoAllocator.NextAsync(a, Tenant);
        var second = await MemberNoAllocator.NextAsync(b, Tenant); // stale token → retries internally
        Assert.NotEqual(first, second);
        Assert.Equal(MemberNumbers.Format(1), first);
        Assert.Equal(MemberNumbers.Format(2), second);
    }

    // ── backfill ──

    [Fact]
    public async Task Backfill_numbers_existing_customers_oldest_first()
    {
        using var conn = Open();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var db = Ctx(conn))
        {
            AddCustomer(db, "Third", t0.AddDays(2));
            AddCustomer(db, "First", t0);
            AddCustomer(db, "Second", t0.AddDays(1));
            db.SaveChanges();
        }

        using (var db = Ctx(conn))
        {
            Assert.Equal(3, await MemberNoBackfill.ApplyAsync(db));
        }

        using (var db = Ctx(conn))
        {
            var byName = db.Customers.AsNoTracking().ToDictionary(c => c.Name, c => c.MemberNo);
            Assert.Equal(MemberNumbers.Format(1), byName["First"]);
            Assert.Equal(MemberNumbers.Format(2), byName["Second"]);
            Assert.Equal(MemberNumbers.Format(3), byName["Third"]);
            Assert.All(byName.Values, n => Assert.True(MemberNumbers.IsValid(n)));
            Assert.Equal(3, byName.Values.Distinct().Count());
        }
    }

    [Fact]
    public async Task Backfill_is_idempotent_and_leaves_existing_numbers_alone()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            AddCustomer(db, "Numbered", DateTime.UtcNow, MemberNumbers.Format(99));
            AddCustomer(db, "Unnumbered", DateTime.UtcNow);
            db.SaveChanges();
        }
        using (var db = Ctx(conn)) Assert.Equal(1, await MemberNoBackfill.ApplyAsync(db));
        using (var db = Ctx(conn)) Assert.Equal(0, await MemberNoBackfill.ApplyAsync(db)); // second pass

        using (var db = Ctx(conn))
        {
            var byName = db.Customers.AsNoTracking().ToDictionary(c => c.Name, c => c.MemberNo);
            Assert.Equal(MemberNumbers.Format(99), byName["Numbered"]);   // untouched
            Assert.Equal(MemberNumbers.Format(100), byName["Unnumbered"]); // continued past it
        }
    }

    /// <summary>Startup has no request principal and the context refuses to save without an actor
    /// (the trap FE1's backfill hit) — this pass must name itself too.</summary>
    [Fact]
    public async Task Backfill_works_without_a_CurrentUser_set()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            AddCustomer(db, "Someone", DateTime.UtcNow);
            db.SaveChanges();
        }
        using (var db = new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                                          new FixedTenantContext(Tenant)))
        {
            Assert.Equal(1, await MemberNoBackfill.ApplyAsync(db));
        }
    }
}
