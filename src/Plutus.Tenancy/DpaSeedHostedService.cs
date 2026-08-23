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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            try
            {
                using var scope = _scopes.CreateScope();
                if (scope.ServiceProvider.GetService<RepositoryContext>() is not MySqlDbContext db) return;

                if (await DpaSeeder.SeedAsync(db, stoppingToken))
                    _log.LogInformation(
                        "Seeded DPA {Version} as an UNPUBLISHED DRAFT. It cannot be served or accepted "
                        + "until it is corrected and published — see DpaSeeder for the two blockers.",
                        DpaSeeder.DraftVersion);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DPA seed skipped; signup will report no published agreement until one exists.");
            }
        }
    }
}
