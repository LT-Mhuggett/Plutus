#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    /// <summary>Customers / store-credit / loyalty module (Phase 8, architecture §9.3).</summary>
    public static class CustomersModule
    {
        public static IServiceCollection AddPlutusCustomers(this IServiceCollection services)
        {
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Customers require the MySqlDbContext (server build).");
                return new CreditLedgerService(ctx);
            });
            return services;
        }
    }

    public sealed class InsufficientCreditException : Exception
    {
        public InsufficientCreditException(string message) : base(message) { }
    }

    /// <summary>
    /// Store credit as an append-only liability ledger (D15): balance is ALWAYS the sum of the
    /// entries, never a stored field. Issue adds a positive entry; redeem checks the live
    /// balance then adds a negative one; expire writes off. Entries are idempotent by id so a
    /// replayed till/webstore redemption is a no-op. The service adds rows to the tracked
    /// context and saves — callers that need atomicity with a sale wrap it in their own tx.
    /// </summary>
    public sealed class CreditLedgerService
    {
        private readonly MySqlDbContext _db;
        public CreditLedgerService(MySqlDbContext db) => _db = db;

        public async Task<CreditAccount> EnsureAccountAsync(Guid tenantId, Guid customerId)
        {
            var account = await _db.CreditAccounts.FirstOrDefaultAsync(a => a.CustomerId == customerId);
            if (account != null) return account;
            account = new CreditAccount
            {
                Id = Uuid7.New(), TenantId = tenantId, CustomerId = customerId, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.CreditAccounts.Add(account);
            return account;
        }

        public async Task<long> BalanceAsync(Guid accountId) =>
            await _db.CreditEntries.Where(e => e.CreditAccountId == accountId)
                .SumAsync(e => (long?)e.AmountPence) ?? 0;

        /// <summary>Positive entry. entryId lets the caller make issuance idempotent
        /// (refund-to-credit replays); null mints a fresh id.</summary>
        public async Task<CreditEntry> IssueAsync(
            Guid tenantId, Guid accountId, long amountPence, string reason, Guid? saleId, Guid? actor, Guid? entryId = null)
        {
            if (amountPence <= 0) throw new ArgumentOutOfRangeException(nameof(amountPence), "Issue amount must be positive.");
            return await AppendAsync(tenantId, accountId, CreditEntryType.Issue, amountPence, reason, saleId, actor, entryId);
        }

        /// <summary>Negative entry after a live-balance check (D15: no overdraw).</summary>
        public async Task<CreditEntry> RedeemAsync(
            Guid tenantId, Guid accountId, long amountPence, string reason, Guid? saleId, Guid? actor, Guid? entryId = null)
        {
            if (amountPence <= 0) throw new ArgumentOutOfRangeException(nameof(amountPence), "Redeem amount must be positive.");

            // idempotent replay: an already-recorded entry id returns without re-checking balance
            if (entryId.HasValue)
            {
                var existing = await _db.CreditEntries.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == entryId.Value);
                if (existing != null) return existing;
            }

            var balance = await BalanceAsync(accountId);
            if (amountPence > balance)
                throw new InsufficientCreditException($"Redeem {amountPence} exceeds balance {balance}.");
            return await AppendAsync(tenantId, accountId, CreditEntryType.Redeem, -amountPence, reason, saleId, actor, entryId);
        }

        public Task<CreditEntry> ExpireAsync(
            Guid tenantId, Guid accountId, long amountPence, string reason, Guid? actor) =>
            AppendAsync(tenantId, accountId, CreditEntryType.Expire, -Math.Abs(amountPence), reason, null, actor, null);

        private async Task<CreditEntry> AppendAsync(
            Guid tenantId, Guid accountId, CreditEntryType type, long signedAmount,
            string reason, Guid? saleId, Guid? actor, Guid? entryId)
        {
            var entry = new CreditEntry
            {
                Id = entryId ?? Uuid7.New(), TenantId = tenantId, CreditAccountId = accountId,
                Type = type, AmountPence = signedAmount, Reason = reason, SaleId = saleId,
                ActorUserId = actor, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.CreditEntries.Add(entry);
            await _db.SaveChangesAsync();
            return entry;
        }

        /// <summary>Total outstanding store-credit liability for the tenant — the figure the
        /// period close records (§7.1). Sum of every entry across every account.</summary>
        public async Task<long> OutstandingLiabilityAsync(Guid tenantId) =>
            await _db.CreditEntries.Where(e => e.TenantId == tenantId).SumAsync(e => (long?)e.AmountPence) ?? 0;
    }
}
