using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Plutus.Contracts;
using Plutus.Authentication;
using Plutus.Entities;
using System;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Plutus.DBService.Extensions
{
    public static class ServiceExtensions
    {
       //public static object DelegatedPermissions { get; private set; }
        
        public static void ConfigureCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
            });
        }

        public static void ConfigureSwaggerDocumentation(this IServiceCollection services, Plutus.Authentication.AuthenticationOptions authenticationOptions)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Plutus.DBService",
                    Version = "v1"
                });

                // Define that the API requires OAuth 2 tokens
                c.AddSecurityDefinition("aad-jwt", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        // We only define implicit though the UI does support authorization code, client credentials and password grants
                        // We don't use authorization code here because it requires a client secret, which makes this sample more complicated by introducing secret management
                        // Client credentials could work, but not when the UI client id == API client id. We'd need a separate registration and granting app permissions to that. And also needs a secret.
                        // Password grant we don't use because... you shouldn't be using it.
                        Implicit = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authenticationOptions.AuthorizationUrl),
                            Scopes = DelegatedPermissions.All.ToDictionary(p => $"{authenticationOptions.ApplicationIdUri}/{p}")
                        }
                    }
                });

                // Add security requirements to operations based on [Authorize] attributes
                c.OperationFilter<OAuthSecurityRequirementOperationFilter>();
            });

            // Include XML comments to documentation
           /* string xmlDocFilePath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "Joonasw.AadTestingDemo.API.xml");
            o.IncludeXmlComments(xmlDocFilePath);*/
        }

        public static void ConfigureAuthentication(this IServiceCollection services, IConfiguration Configuration, Plutus.Authentication.AuthenticationOptions authenticationOptions)
        {
            services.Configure<Plutus.Authentication.AuthenticationOptions>(Configuration.GetSection("Authentication"));
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o =>
                {
                    o.Authority = authenticationOptions.Authority;
                    o.Audience = authenticationOptions.ClientId;
                });
            services.AddSingleton<IClaimsTransformation, ScopeClaimSplitTransformation>();
            /*services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                     .AddMicrosoftIdentityWebApi(Configuration, "AzureAd");*/
        }

        public static void ConfigureAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(o =>
            {
                // Require callers to have at least one valid permission by default
                o.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new AnyValidPermissionRequirement())
                    .Build();
                // Create a policy for each action that can be done in the API
                foreach (string action in Actions.All)
                {
                    o.AddPolicy(action, policy => policy.AddRequirements(new ActionAuthorizationRequirement(action)));
                }
            });
            services.AddSingleton<IAuthorizationHandler, AnyValidPermissionRequirementHandler>();
            services.AddSingleton<IAuthorizationHandler, ActionAuthorizationRequirementHandler>();
        }

        public static void ConfigureDBContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration["ConnectionString"];

            services.AddDbContext<MySqlDbContext>(o => o.UseMySql(ServerVersion.AutoDetect(connectionString)));
        }

        public static void ConfigureMySqlDBContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration["ConnectionString"];

            services.AddDbContext<RepositoryContext>(o => o.UseMySql(connectionString, MySqlServerVersion.LatestSupportedServerVersion));

           /* ObjectIdProvider objectIdProvider = new ObjectIdProvider("1232423");
            services.AddSingleton<ObjectIdProvider>(objectIdProvider);*/

            //HttpContextAccessor 
        }

        public static void ConfigureRepositoryWrapper(this IServiceCollection services)
        {
            services.AddScoped<IRepositoryWrapper, RepositoryWrapper>();
        }

        public static void ConfigureSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen();
        }

        public static void ConfigureControllers(this IServiceCollection services)
        {
            services.AddControllers().AddNewtonsoftJson(options => options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);
        }

        public static void ConfigureHttpAccessor(this IServiceCollection services)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        }
    }
}
