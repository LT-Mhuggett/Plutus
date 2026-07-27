#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;

namespace Plutus.Payments
{
    /// <summary>One provider settlement line (a captured payment the provider will pay out).</summary>
    public sealed record SettlementLine(string ProviderRef, long AmountPence, DateTime CapturedAtUtc);

    /// <summary>
    /// WP7.1 (architecture §9.1): the provider adapter seam. The FIRST CONCRETE ADAPTER
    /// (Dojo / Stripe Terminal / SumUp / Adyen / Zettle) awaits the commercial choice —
    /// everything downstream (capture events, the unresolved-payments queue, reconciliation)
    /// is provider-agnostic and already runs against this interface.
    /// </summary>
    public interface IPaymentProvider
    {
        string Name { get; }
        /// <summary>The provider's settlement report for a day (what they will pay out).</summary>
        Task<IReadOnlyList<SettlementLine>> GetSettlementAsync(DateOnly day, CancellationToken ct = default);
    }

    /// <summary>Stands in until the commercial provider is chosen: no settlement data.</summary>
    public sealed class NullPaymentProvider : IPaymentProvider
    {
        public string Name => "none";
        public Task<IReadOnlyList<SettlementLine>> GetSettlementAsync(DateOnly day, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SettlementLine>>(Array.Empty<SettlementLine>());
    }

    public static class PaymentsModule
    {
        public static IServiceCollection AddPlutusPayments(this IServiceCollection services)
        {
            services.AddSingleton<IPaymentProvider, NullPaymentProvider>();
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Payments require the MySqlDbContext (server build).");
                return new PaymentReconciliationService(ctx);
            });
            return services;
        }
    }
}
