using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plutus.DBService.Extensions;
using Plutus.Identity;
using Plutus.Catalogue;
using Plutus.Sales;
using Plutus.Reporting;
using Plutus.Tenancy;
using Plutus.Cash;
using Plutus.Payments;
using Plutus.Customers;
using Plutus.Webstore;
using Plutus.Entities;
using Plutus.Infrastructure.Health;
using Plutus.Infrastructure.Monitoring;
using Plutus.Infrastructure.Notifications;
using Plutus.Infrastructure.Outbox;
using Plutus.Infrastructure.RateLimiting;
using Plutus.Infrastructure.Security;
using System;
using System.Diagnostics;
using System.Net.Http;

namespace Plutus.DBService
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureCors();
            services.ConfigureMySqlDBContext(Configuration);
            services.ConfigureRepositoryWrapper();

            /*services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Plutus.DBService", Version = "v1" });
            });*/

            services.ConfigureControllers();

            services.ConfigureAuthentication(Configuration);
            //services.ConfigureAuthorization();

            // Modules (T0.2b): composition entry points.
            services.AddPlutusIdentity(Configuration);
            services.AddPlutusCatalogue();
            services.AddPlutusSales();
            services.AddPlutusReporting(Configuration);   // WP12.2: LegacyBridge:Enabled gate (default on)
            services.AddPlutusTenancy(Configuration);
            services.AddPlutusCash();       // WP7.2 cash sessions
            services.AddPlutusPayments();   // WP7.1 provider seam + reconciliation
            services.AddPlutusCustomers();  // Phase 8 customers / credit / loyalty
            // Phase 6 Woo connector (WP6.2a): host supplies the sink factory (adapter over
            // SalesIngestService on the delivery's tenant-fixed context) + the secret provider.
            services.AddSingleton<Func<MySqlDbContext, WebstoreConnectionContext, IWebstoreSaleSink>>(
                sp => (db, ctx) => new WebstoreIngestSink(db, ctx));
            // Secrets: config first (pm2 env — the hand-provisioned Kapow connection), then the
            // server-side file the WP6.1 wc-auth callback writes (survives deploys).
            var webstoreSecrets = new FileWebstoreSecretProvider(Configuration,
                Configuration["Webstore:SecretsFile"]
                    ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PLUTUS", "secrets", "webstore-secrets.json"));
            services.AddSingleton<IWebstoreSecretProvider>(webstoreSecrets);
            services.AddSingleton<IWebstoreSecretStore>(webstoreSecrets);
            // Options BEFORE AddPlutusWebstore (its TryAdd keeps this instance).
            services.AddSingleton(new WebstoreOptions { PublicBaseUrl = Configuration["Webstore:PublicBaseUrl"] });
            services.AddPlutusWebstore();
            services.AddPlutusOutbox(); // T1.5 broker-less dispatcher (consumers register their own IEventConsumer)
            services.AddPlutusRequestHealth(); // WP13.2 per-tenant request-health accumulator + per-minute flusher
            services.AddPlutusJobMonitoring(); // WP13.3 IJobHeartbeat + IOperatorAlerter + WP17.1 IConnectorHealth seams
            services.AddPlutusNotifications(); // 17.3 config layer: ConfiguredMessageSender dispatches to the operator-selected provider (simulated until an adapter is wired)
            services.AddPlutusTenantRateLimiting(Configuration); // WP13.5 per-tenant rate limiting
            ConfigureRateLimiting(services, Configuration);
            services.ConfigureSwaggerDocumentation(Configuration);
            services.ConfigureHttpAccessor();

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // ⚠⚠ REDACTED 2026-08-25 — THIS LINE PRINTED THE LIVE MySQL PASSWORD ON EVERY BOOT.
            // It is legacy, and it is ungated: it sits ABOVE the `env.IsDevelopment()` check below,
            // so it ran in every environment. Under pm2 that put the credential in plaintext into
            // `~/.pm2/logs/plutus-backend-out.log` — 37 occurrences across 15 rotated files when it
            // was found.
            //
            // ⚠ The irony worth remembering: it came in with commit 3d2837a2, *"Rebuild branch on
            // upstream/master and remove secret-bearing history"*. The history was scrubbed and the
            // line that REPRINTS the secret every boot survived the scrub. Removing a secret from
            // git says nothing about the code that emits it.
            //
            // ⚠ Kept rather than deleted, because the diagnostic is real: "which database am I
            // actually pointing at" answered a live question on 2026-08-25 (unix socket vs TCP,
            // which is the difference between working and not since the caching_sha2 rotation).
            // Everything except the credential survives.
            System.Console.WriteLine("Config ConnectionString is: " + Plutus.SharedKernel.Redact.ConnectionString(Configuration["ConnectionString"]));
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Plutus.DBService v1");
                c.OAuthClientId(Configuration["OpenAPI:ClientId"]);
                c.OAuthUsePkce();
            });

            EnsureSchemaThenSeed(app);

            app.UseHttpsRedirection();

            app.UseCors("CorsPolicy");

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.All
            });

            app.UseRouting();

            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            // WP13.5: per-tenant rate limiting AFTER auth (tenant resolved) and BEFORE the health
            // middleware so throttled (429) requests don't skew the request-health stats.
            app.UsePlutusTenantRateLimiting();

            // WP14.1: log every impersonated request (actor + target) for the audit trail.
            app.UsePlutusImpersonationAudit();

            // OP1: block pure operators (platform-admin, no tenant identity) from client-data APIs —
            // they reach client data only via the audited impersonation flow. After auth so claims
            // are resolved; after the impersonation audit so impersonated requests are logged then
            // allowed through (they carry a tid).
            app.UsePlutusOperatorBoundary();

            // WP13.2: record per-tenant request health AFTER auth (tenant resolved), wrapping
            // endpoint execution for latency + final status code.
            app.UsePlutusRequestHealth();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            //Plutus.Authentication.AuthenticationOptions authenticationOptions = Configuration.GetSection("Authentication").Get<Plutus.Authentication.AuthenticationOptions>();
        }

        // T1.2: throttle the anonymous enrol + device-token endpoints (default 5/min/IP;
        // RATE_LIMIT_ENROL_PER_MIN overrides — integration tests raise it so the shared
        // loopback IP isn't throttled across cases).
        private static void ConfigureRateLimiting(IServiceCollection services, IConfiguration configuration)
        {
            var permit = configuration.GetValue<int?>("RATE_LIMIT_ENROL_PER_MIN") ?? 5;
            services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = 429;
                o.AddPolicy("enrol", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permit,
                        Window = TimeSpan.FromMinutes(1),
                    }));
            });
        }

        private static void EnsureSchemaThenSeed(IApplicationBuilder app)
        {
            using var serviceScope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
            using var context = serviceScope.ServiceProvider.GetService<RepositoryContext>();
#if DEBUG
            //context.Database.EnsureDeleted();
#endif
            // Integration tests boot the host with a MySqlDbContext on SQLite, which cannot run
            // the MySQL migrations — they create the schema from the model instead. Never set in
            // production, so the real Migrate() path is unchanged.
            if (Environment.GetEnvironmentVariable("PLUTUS_DB_ENSURE_CREATED") == "true")
                context.Database.EnsureCreated();
            else
                context.Database.Migrate();

            // ⚠⚠ THE TWO BACKFILLS THAT USED TO RUN HERE ARE GONE — 2026-08-25.
            //
            // Matt: *"why is there backfill? Everything going forward needs to be current and up to
            // date, not needing backfills."*
            //
            // `LoyaltyTierBackfill` (FE1) and `MemberNoBackfill` (FE2) were **one-off repairs** of
            // data that predated their features — free-text membership tiers, and customers with no
            // member number. Both were verified complete against the live database before removal:
            // **0 memberships without a TierId, 0 customers without a MemberNo.** They had been
            // re-running on every boot of every environment for weeks, doing nothing.
            //
            // ⚠ A permanent boot-time repair pass is not free. It is a race surface — which is
            // exactly what `DpaSeedHostedService` fell into — and it is code nobody dares delete
            // because nobody can tell whether it is still load-bearing. The rule now is: a backfill
            // is a ONE-OFF, run once and removed, and new data is made correct where it is WRITTEN.
            //
            // ⚠ The classes remain in the tree, unreferenced, for anyone restoring an old database.
            // They are not wired into start-up and must not be re-wired.

            // What DOES still belong here: making a tenant's catalogue complete. ⚠ These are
            // PROVISIONING, not repair — every business needs the two rows, and until 2026-08-25
            // this sweep was the only thing creating them.
            //
            // ⚠⚠ AND THE SWEEP NEVER REACHED A NEW TENANT, which is what proved Matt's point. Both
            // helpers price their item against a tax band, and `ProvisioningService` created none —
            // so Test Business and Demo Store had **0 taxes, 0 categories, 0 items** after five
            // reboots. A new tenant is now born with its bands, VAT rate points, categories and both
            // catalogue rows inside the provisioning transaction (`ProvisioningService`).
            //
            // ⚠ This sweep is kept as a SAFETY NET for tenants provisioned before that change, and
            // is a no-op once every business has its rows. It is the last repair pass here; when the
            // pre-2026-08-25 tenants are known good, delete it too.
            try
            {
                using var scope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
                var db = scope.ServiceProvider.GetService<Plutus.Entities.MySqlDbContext>();
                if (db != null)
                {
                    // FE7: the zero-VAT catalogue row a gift-card activation is rung through must
                    // exist before the first card is sold (the legacy sale projection FKs to Items).
                    var cardItems = Plutus.Customers.GiftCardSaleItem.EnsureAsync(db).GetAwaiter().GetResult();
                    if (cardItems > 0)
                        Console.WriteLine($"[giftcards] provisioned the activation item for {cardItems} business(es).");

                    // The catalogue row a card surcharge is rung through — same rule as gift cards:
                    // money at the till must be a real sale line, and the fee's VAT follows the
                    // basket (CardSurchargeVat), never this item's zero band.
                    var feeItems = Plutus.Payments.CardSurchargeSaleItem.EnsureAsync(db).GetAwaiter().GetResult();
                    if (feeItems > 0)
                        Console.WriteLine($"[payments] provisioned the card-surcharge item for {feeItems} business(es).");

                    // ⚠⚠ THE DPA DRAFT, MOVED HERE FROM A HOSTED SERVICE — 2026-08-25. It used to
                    // start alongside schema creation, lose the race, and retry ten times at
                    // three-second intervals. Here the schema is already there, so it simply works:
                    // no retry, and nothing left running after the host is disposed.
                    if (Plutus.Tenancy.DpaSeeder.SeedAsync(db).GetAwaiter().GetResult())
                        Console.WriteLine("[dpa] seeded the draft agreement.");
                }
            }
            catch (Exception ex)
            {
                // ⚠ NEVER FATAL. A catalogue row nobody has sold yet is not worth refusing to boot
                // over — every till in the estate would go offline for it.
                Console.WriteLine($"[startup] catalogue provisioning skipped: {ex.Message}");
            }
        }

    }
}
