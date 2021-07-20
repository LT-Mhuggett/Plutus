using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plutus.DBService.Extensions;
using Plutus.Entities;
using AuthenticationOptions = Plutus.Authentication.AuthenticationOptions;

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
            services.ConfigureDBContext(Configuration);
            services.ConfigureRepositoryWrapper();
            services.ConfigureMySqlDBContext(Configuration);
            AuthenticationOptions authenticationOptions = Configuration.GetSection("Authentication").Get<AuthenticationOptions>();

            /*services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Plutus.DBService", Version = "v1" });
            });*/

            services.ConfigureControllers();

            ServiceExtensions.ConfigureAuthentication(services, Configuration, authenticationOptions);
            ServiceExtensions.ConfigureAuthorization(services);
            ServiceExtensions.ConfigureSwaggerDocumentation(services, authenticationOptions);

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Plutus.DBService v1"));
            }

            app.UseHttpsRedirection();

            app.UseCors("CorsPolicy");

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.All
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            AuthenticationOptions authenticationOptions = Configuration.GetSection("Authentication").Get<AuthenticationOptions>();

            // Swagger / OpenAPI document
            app.UseSwagger();
            // The interactive documentation
            app.UseSwaggerUI(o =>
            {
                o.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
                o.OAuthClientId(authenticationOptions.ClientId);
            });
        }

        private static void MigrateDatase(IApplicationBuilder app)
        {
            using var serviceScope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
            using var context = serviceScope.ServiceProvider.GetService<MySqlDbContext>();
            context.Database.Migrate();
        }
    }
}
