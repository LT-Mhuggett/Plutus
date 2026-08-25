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
/// FE1 loyalty tier catalogue (DoD): a membership assigned a tier follows that tier's CURRENT
/// name + rate (re-rating "Gold" moves every Gold member at once); the legacy free-text path is
/// untouched; the one-off backfill turns pre-catalogue memberships into tiers idempotently.
/// </summary>
public class LoyaltyTierTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "tier-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static Customer AddCustomer(MySqlDbContext db, string name)
    {
        var c = new Customer { Id = Uuid7.New(), TenantId = Tenant, Name = name, Active = true, CreatedAtUtc = DateTime.UtcNow };
        db.Customers.Add(c);
        return c;
    }

    private static LoyaltyTier AddTier(MySqlDbContext db, string name, decimal rate, int months = 12, bool active = true)
    {
        var t = new LoyaltyTier
        {
            Id = Uuid7.New(), TenantId = Tenant, Name = name, AutoDiscountRate = rate,
            DurationMonths = months, Active = active, SortOrder = 0, CreatedAtUtc = DateTime.UtcNow,
        };
        db.LoyaltyTiers.Add(t);
        return t;
    }

    private static Membership AddMembership(MySqlDbContext db, Guid customerId, string tier, decimal rate, Guid? tierId = null)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var m = new Membership
        {
            Id = Uuid7.New(), TenantId = Tenant, CustomerId = customerId, TierId = tierId,
            Tier = tier, AutoDiscountRate = rate, StartDay = start, RenewalDay = start.AddYears(1),
            Active = true, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Memberships.Add(m);
        return m;
    }

    /// <summary>The resolution rule the read endpoints apply (tier wins when assigned).</summary>
    private static (string Name, decimal Rate) Resolve(MySqlDbContext db, Membership m)
    {
        var tier = m.TierId == null ? null : db.LoyaltyTiers.AsNoTracking().FirstOrDefault(t => t.Id == m.TierId.Value);
        return (tier?.Name ?? m.Tier, tier?.AutoDiscountRate ?? m.AutoDiscountRate);
    }

    [Fact]
    public void Tier_assigned_membership_follows_the_tier_when_it_is_re_rated()
    {
        using var conn = Open();
        Guid memberId, tierId;
        using (var db = Ctx(conn))
        {
            var tier = AddTier(db, "Gold", 0.10m);
            var cust = AddCustomer(db, "Jo");
            var m = AddMembership(db, cust.Id, "Gold", 0.10m, tier.Id);
            db.SaveChanges();
            memberId = m.Id; tierId = tier.Id;
        }

        // re-rate the tier: 10% → 15%
        using (var db = Ctx(conn))
        {
            db.LoyaltyTiers.First(t => t.Id == tierId).AutoDiscountRate = 0.15m;
            db.SaveChanges();
        }

        using (var db = Ctx(conn))
        {
            var m = db.Memberships.AsNoTracking().First(x => x.Id == memberId);
            var (name, rate) = Resolve(db, m);
            Assert.Equal("Gold", name);
            Assert.Equal(0.15m, rate);          // live-follow — no re-assignment needed
            Assert.Equal(0.10m, m.AutoDiscountRate); // snapshot column untouched
        }
    }

    [Fact]
    public void Renaming_a_tier_renames_it_for_every_member()
    {
        using var conn = Open();
        Guid tierId;
        using (var db = Ctx(conn))
        {
            var tier = AddTier(db, "Club", 0.05m);
            var a = AddCustomer(db, "A"); var b = AddCustomer(db, "B");
            AddMembership(db, a.Id, "Club", 0.05m, tier.Id);
            AddMembership(db, b.Id, "Club", 0.05m, tier.Id);
            db.SaveChanges();
            tierId = tier.Id;
        }
        using (var db = Ctx(conn))
        {
            db.LoyaltyTiers.First(t => t.Id == tierId).Name = "Club Plus";
            db.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            var names = db.Memberships.AsNoTracking().ToList().Select(m => Resolve(db, m).Name).Distinct().ToList();
            Assert.Equal(new[] { "Club Plus" }, names);
        }
    }

    [Fact]
    public void Legacy_free_text_membership_is_unaffected_by_the_catalogue()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            AddTier(db, "Gold", 0.15m);
            var cust = AddCustomer(db, "Legacy Larry");
            AddMembership(db, cust.Id, "Gold", 0.02m); // TierId null — an old hand-typed 2%
            db.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            var m = db.Memberships.AsNoTracking().First();
            var (name, rate) = Resolve(db, m);
            Assert.Equal("Gold", name);
            Assert.Equal(0.02m, rate); // its own rate stands; the tier does NOT capture it
        }
    }

    [Fact]
    public void Snapshot_covers_a_tier_row_that_disappears()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            var tier = AddTier(db, "Gone", 0.20m);
            var cust = AddCustomer(db, "Jo");
            AddMembership(db, cust.Id, "Gone", 0.20m, tier.Id);
            db.SaveChanges();
            db.LoyaltyTiers.Remove(db.LoyaltyTiers.First(t => t.Id == tier.Id));
            db.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            var m = db.Memberships.AsNoTracking().First();
            var (name, rate) = Resolve(db, m);
            Assert.Equal("Gone", name);
            Assert.Equal(0.20m, rate); // falls back to the as-assigned snapshot, never blank
        }
    }

    // ── backfill ──

    [Fact]
    public async Task Backfill_creates_one_tier_per_distinct_name_and_links_memberships()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            var a = AddCustomer(db, "A"); var b = AddCustomer(db, "B"); var c = AddCustomer(db, "C");
            AddMembership(db, a.Id, "Gold", 0.15m);
            AddMembership(db, b.Id, "gold", 0.15m);   // same tier, different case
            AddMembership(db, c.Id, "Club", 0.05m);
            db.SaveChanges();
        }

        using (var db = Ctx(conn))
        {
            var (created, linked) = await LoyaltyTierBackfill.ApplyAsync(db);
            Assert.Equal(2, created);   // Gold + Club
            Assert.Equal(3, linked);
        }

        using (var db = Ctx(conn))
        {
            Assert.Equal(2, db.LoyaltyTiers.Count());
            Assert.All(db.Memberships.AsNoTracking().ToList(), m => Assert.NotNull(m.TierId));
            // every membership resolves to its tier with the right rate
            foreach (var m in db.Memberships.AsNoTracking().ToList())
            {
                var (_, rate) = Resolve(db, m);
                Assert.Equal(m.AutoDiscountRate, rate);
            }
        }
    }

    /// <summary>The startup pass has no request principal, and the context refuses to save without
    /// an actor — the backfill must name itself (this failed on the first FE1 deploy).</summary>
    [Fact]
    public async Task Backfill_works_without_a_CurrentUser_set()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            var a = AddCustomer(db, "A");
            AddMembership(db, a.Id, "Gold", 0.15m);
            db.SaveChanges();
        }

        // exactly how EnsureSchemaThenSeed resolves it: a fresh context, CurrentUser never assigned
        using (var db = new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                                           new FixedTenantContext(Tenant)))
        {
            var (created, linked) = await LoyaltyTierBackfill.ApplyAsync(db);
            Assert.Equal(1, created);
            Assert.Equal(1, linked);
        }
    }

    [Fact]
    public async Task Backfill_is_idempotent()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            var a = AddCustomer(db, "A");
            AddMembership(db, a.Id, "Gold", 0.15m);
            db.SaveChanges();
        }
        using (var db = Ctx(conn)) await LoyaltyTierBackfill.ApplyAsync(db);

        using (var db = Ctx(conn))
        {
            var (created, linked) = await LoyaltyTierBackfill.ApplyAsync(db); // second pass
            Assert.Equal(0, created);
            Assert.Equal(0, linked);
            Assert.Equal(1, db.LoyaltyTiers.Count());
        }
    }

    [Fact]
    public async Task Backfill_reuses_an_existing_tier_and_picks_the_majority_rate()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            AddTier(db, "Gold", 0.15m);              // already catalogued
            var a = AddCustomer(db, "A"); var b = AddCustomer(db, "B"); var c = AddCustomer(db, "C");
            AddMembership(db, a.Id, "Gold", 0.15m);  // must attach to the existing tier
            AddMembership(db, b.Id, "Silver", 0.07m);
            AddMembership(db, c.Id, "Silver", 0.07m);
            db.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            var (created, _) = await LoyaltyTierBackfill.ApplyAsync(db);
            Assert.Equal(1, created); // only Silver is new
        }
        using (var db = Ctx(conn))
        {
            Assert.Equal(2, db.LoyaltyTiers.Count());
            var silver = db.LoyaltyTiers.AsNoTracking().First(t => t.Name == "Silver");
            Assert.Equal(0.07m, silver.AutoDiscountRate);
            Assert.Equal(12, silver.DurationMonths); // default duration
            Assert.True(silver.Active);
        }
    }
}
