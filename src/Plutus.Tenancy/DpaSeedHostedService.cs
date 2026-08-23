#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;

namespace Plutus.Tenancy
{
    /// <summary>
    /// Runs <see cref="DpaSeeder"/> once per boot.
    ///
    /// ⚠ Modelled on `RolePermissionReconciler`: additive, idempotent, and NEVER FATAL. A seeder that
    /// can stop the backend booting is a worse problem than the thing it seeds — every till in the
    /// estate goes offline over a document nobody can accept yet.
    ///
    /// ⚠ `Task.Yield()` first, for the reason the reconciler documents: a BackgroundService that
    /// blocks in ExecuteAsync blocks start-up on some hosts.
    /// </summary>
    public sealed class DpaSeedHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<DpaSeedHostedService> _log;

        public DpaSeedHostedService(IServiceScopeFactory scopes, ILogger<DpaSeedHostedService> log)
        {
            _scopes = scopes;
            _log = log;
        }

        /// <summary>
        /// ⚠⚠ IT RETRIES, BECAUSE IT RACES THE MIGRATION AND LOST — observed on the 1.30.0 deploy.
        /// Migrations auto-apply during start-up while hosted services start alongside them, so the
        /// first attempt hit "table DpaDocuments doesn't exist", the catch swallowed it exactly as
        /// designed, and the draft never appeared. Safe because the catch made it a warning rather
        /// than an outage — but a seeder that only works on the SECOND boot is a seeder nobody can
        /// rely on, and the next new table would hit the same thing.
        /// </summary>
        private const int Attempts = 10;
        private static readonly TimeSpan Gap = TimeSpan.FromSeconds(3);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            for (var attempt = 1; attempt <= Attempts && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    if (scope.ServiceProvider.GetService<RepositoryContext>() is not MySqlDbContext db) return;

                    if (await DpaSeeder.SeedAsync(db, stoppingToken))
                        _log.LogInformation(
                            "Seeded DPA {Version} as an UNPUBLISHED DRAFT. It cannot be served or accepted "
                            + "until it is corrected and published — see DpaSeeder for the two blockers.",
                            DpaSeeder.DraftVersion);
                    return;   // seeded, or already present — either way there is nothing more to do
                }
                catch (Exception ex) when (attempt < Attempts)
                {
                    // Almost always "the table is not there yet". Debug, not warning: a retry that is
                    // expected to happen must not read as a fault in the log.
                    _log.LogDebug(ex, "DPA seed attempt {Attempt} failed; retrying after the migration.", attempt);
                    try { await Task.Delay(Gap, stoppingToken); } catch (OperationCanceledException) { return; }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex,
                        "DPA seed gave up after {Attempts} attempts; signup will report no published "
                        + "agreement until one is created in Platform → DPA.", Attempts);
                    return;
                }
            }
        }
    }
}
