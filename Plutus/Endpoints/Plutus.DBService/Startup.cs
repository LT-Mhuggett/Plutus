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
using Plutus.Entities;
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
            services.AddPlutusReporting();
            services.AddPlutusTenancy(Configuration);
            ConfigureRateLimiting(services);
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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            //Plutus.Authentication.AuthenticationOptions authenticationOptions = Configuration.GetSection("Authentication").Get<Plutus.Authentication.AuthenticationOptions>();
        }

        // T1.2: throttle the anonymous enrol + device-token endpoints (5/min/IP).
        private static void ConfigureRateLimiting(IServiceCollection services)
        {
            services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = 429;
                o.AddPolicy("enrol", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
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
            context.Database.Migrate();
        }
    }
}
