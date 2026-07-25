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
/// Phase 8 (DoD): store-credit balance == Σ append-only entries always (property test over
/// random issue/redeem sequences); redeem never overdraws; redemption is idempotent by
/// entry id (a replayed till/webstore redeem is a no-op); the outstanding-liability figure
/// (the period-close line) equals the tenant-wide entry sum.
/// </summary>
public class CustomerCreditTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "credit-test" };

    private static (SqliteConnection Conn, Guid AccountId) OpenWithAccount()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        var customer = new Customer { Id = Uuid7.New(), TenantId = Tenant, Name = "Jo", Active = true, CreatedAtUtc = DateTime.UtcNow };
        ctx.Customers.Add(customer);
        var account = new CreditAccount { Id = Uuid7.New(), TenantId = Tenant, CustomerId = customer.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.CreditAccounts.Add(account);
        ctx.SaveChanges();
        return (conn, account.Id);
    }

    [Fact]
    public async Task Balance_always_equals_entry_sum_under_random_issue_redeem()
    {
        var (conn, accountId) = OpenWithAccount();
        using var _ = conn;
        var rng = new Random(808);
        long expected = 0;

        for (var i = 0; i < 300; i++)
        {
            using var db = Ctx(conn);
            var svc = new CreditLedgerService(db);
            var balance = await svc.BalanceAsync(accountId);
            Assert.Equal(expected, balance); // invariant holds at every step

            if (balance == 0 || rng.Next(2) == 0)
            {
                long amt = rng.Next(1, 5000);
                await svc.IssueAsync(Tenant, accountId, amt, "grant", null, null);
                expected += amt;
            }
            else
            {
                long amt = rng.Next(1, (int)balance + 1);
                await svc.RedeemAsync(Tenant, accountId, amt, "redeem", Guid.NewGuid(), null);
                expected -= amt;
            }
        }

        using var check = Ctx(conn);
        Assert.Equal(expected, await new CreditLedgerService(check).BalanceAsync(accountId));
    }

    [Fact]
    public async Task Redeem_cannot_overdraw()
    {
        var (conn, accountId) = OpenWithAccount();
        using var _ = conn;
        using var db = Ctx(conn);
        var svc = new CreditLedgerService(db);
        await svc.IssueAsync(Tenant, accountId, 1000, "grant", null, null);

        await Assert.ThrowsAsync<InsufficientCreditException>(
            () => svc.RedeemAsync(Tenant, accountId, 1001, "too much", null, null));
        Assert.Equal(1000, await svc.BalanceAsync(accountId)); // unchanged
        // exact balance is fine
        await svc.RedeemAsync(Tenant, accountId, 1000, "spend it all", null, null);
        Assert.Equal(0, await svc.BalanceAsync(accountId));
    }

    [Fact]
    public async Task Redeem_is_idempotent_by_entry_id()
    {
        var (conn, accountId) = OpenWithAccount();
        using var _ = conn;
        var redeemId = Uuid7.New();
        using (var db = Ctx(conn))
        {
            var svc = new CreditLedgerService(db);
            await svc.IssueAsync(Tenant, accountId, 500, "grant", null, null);
            await svc.RedeemAsync(Tenant, accountId, 200, "buy", Guid.NewGuid(), null, redeemId);
        }
        // a replayed redemption (same entry id) must NOT deduct twice
        using (var db = Ctx(conn))
        {
            var svc = new CreditLedgerService(db);
            await svc.RedeemAsync(Tenant, accountId, 200, "buy", Guid.NewGuid(), null, redeemId);
            Assert.Equal(300, await svc.BalanceAsync(accountId));
        }
        using var check = Ctx(conn);
        Assert.Equal(1, await check.CreditEntries.CountAsync(e => e.Type == CreditEntryType.Redeem));
    }

    [Fact]
    public async Task Outstanding_liability_equals_tenant_entry_sum()
    {
        var (conn, accountId) = OpenWithAccount();
        using var _ = conn;

        // a second customer/account so the tenant-wide figure spans accounts
        Guid account2;
        using (var db = Ctx(conn))
        {
            var c2 = new Customer { Id = Uuid7.New(), TenantId = Tenant, Name = "Sam", Active = true, CreatedAtUtc = DateTime.UtcNow };
            db.Customers.Add(c2);
            var a2 = new CreditAccount { Id = Uuid7.New(), TenantId = Tenant, CustomerId = c2.Id, CreatedAtUtc = DateTime.UtcNow };
            db.CreditAccounts.Add(a2);
            db.SaveChanges();
            account2 = a2.Id;
        }

        using (var db = Ctx(conn))
        {
            var svc = new CreditLedgerService(db);
            await svc.IssueAsync(Tenant, accountId, 5000, "grant", null, null);
            await svc.RedeemAsync(Tenant, accountId, 1200, "spend", Guid.NewGuid(), null);
            await svc.IssueAsync(Tenant, account2, 3000, "grant", null, null);
            await svc.ExpireAsync(Tenant, account2, 500, "lapsed", null);
        }

        using var check = Ctx(conn);
        var liability = await new CreditLedgerService(check).OutstandingLiabilityAsync(Tenant);
        Assert.Equal(5000 - 1200 + 3000 - 500, liability); // 6300
        // equals the two account balances summed
        var svc2 = new CreditLedgerService(check);
        Assert.Equal(liability, await svc2.BalanceAsync(accountId) + await svc2.BalanceAsync(account2));
    }
}
