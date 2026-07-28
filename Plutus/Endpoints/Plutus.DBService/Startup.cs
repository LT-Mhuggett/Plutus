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
            System.Console.WriteLine("Config ConnectionString is: " + Configuration["ConnectionString"]);
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

            MigrateDatabase(app);

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

        private static void MigrateDatabase(IApplicationBuilder app)
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
        }
    }
}
