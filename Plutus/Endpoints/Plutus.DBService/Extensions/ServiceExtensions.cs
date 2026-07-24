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
using Microsoft.Identity.Web;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Options;

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

        public static void ConfigureSwaggerDocumentation(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Plutus.DBService",
                    Version = "v1"
                });

                // B2C OAuth2 scheme/requirement ONLY when B2C is actually configured. Without
                // it the scope keys built from empty config collapse to duplicate "https:///"
                // and Swagger generation throws (500) — which broke every config-less boot
                // (CI drift job, local tooling). The frozen contract is thus config-independent.
                if (!string.IsNullOrEmpty(configuration["AzureAdB2C:Domain"]))
                {
                // Define that the API requires OAuth 2 tokens
                c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    Description = "OAuth2.0 Auth Code with PKCE",
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        Implicit = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{configuration["AzureAdB2C:Instance"]}/{configuration["AzureAdB2C:Domain"]}/oauth2/v2.0/authorize?p={configuration["AzureAdB2C:SignUpSignInPolicyId"]}"),
                            TokenUrl = new Uri($"{configuration["AzureAdB2C:Instance"]}/{configuration["AzureAdB2C:Domain"]}/oauth2/v2.0/token?p={configuration["AzureAdB2C:SignUpSignInPolicyId"]}"),
                            Scopes = new Dictionary<string, string>
                            {
                                { $"https://{configuration["AzureAdB2C:Domain"]}/{configuration["OpenAPI:Scopes:APIRead:Id"]}", configuration["OpenAPI:Scopes:APIRead:Description"] },
                                { $"https://{configuration["AzureAdB2C:Domain"]}/{configuration["OpenAPI:Scopes:APIWrite:Id"]}", configuration["OpenAPI:Scopes:APIWrite:Description"] },
                                { configuration["OpenAPI:Scopes:APIOpenId:Id"], configuration["OpenAPI:Scopes:APIOpenId:Description"] }
                            }
                        },
                        // We only define implicit though the UI does support authorization code, client credentials and password grants
                        // We don't use authorization code here because it requires a client secret, which makes this sample more complicated by introducing secret management
                        // Client credentials could work, but not when the UI client id == API client id. We'd need a separate registration and granting app permissions to that. And also needs a secret.
                        // Password grant we don't use because... you shouldn't be using it.
                        /*Implicit = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authenticationOptions.AuthorizationUrl),
                            Scopes = DelegatedPermissions.All.ToDictionary(p => $"{authenticationOptions.ApplicationIdUri}/{p}")
                        }*/
                    }
                });

                // Add security requirements to operations based on [Authorize] attributes
                //c.OperationFilter<OAuthSecurityRequirementOperationFilter>();
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "oauth2"
                            }
                        },
                        new string[]
                        {
                            configuration["OpenAPI:Scopes:APIRead:Id"],
                            configuration["OpenAPI:Scopes:APIWrite:Id"],
                        }
                    }
                });
                } // end: B2C security scheme only when configured
            });

            // Include XML comments to documentation
           /* string xmlDocFilePath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "Joonasw.AadTestingDemo.API.xml");
            o.IncludeXmlComments(xmlDocFilePath);*/
        }

        public static void ConfigureAuthentication(this IServiceCollection services, IConfiguration Configuration)
        {
            services.AddMicrosoftIdentityWebApiAuthentication(Configuration, "AzureAdB2C");
        }

        /*public static void ConfigureAuthorization(this IServiceCollection services)
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
        }*/
        
        public static void ConfigureMySqlDBContext(this IServiceCollection services, IConfiguration configuration)
        {
#if DEBUG
            services.AddDbContext<RepositoryContext, SqliteDbContext>(options => options.UseSqlite(@$"Data Source=Database.db"));
#else
            services.AddDbContext<RepositoryContext, MySqlDbContext>(o => o.UseMySql(configuration["ConnectionString"], MySqlServerVersion.LatestSupportedServerVersion));
#endif
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
            services.AddControllers()
                // Register controllers that live in module assemblies (T0.2b). MVC's default
                // part discovery usually finds these via the dependency graph, but wiring them
                // explicitly is deterministic and self-documents which modules ship controllers.
                .AddApplicationPart(typeof(Plutus.Catalogue.CatalogueModule).Assembly)
                .AddApplicationPart(typeof(Plutus.Sales.SalesModule).Assembly)
                .AddApplicationPart(typeof(Plutus.Tenancy.TenancyModule).Assembly)
                .AddApplicationPart(typeof(Plutus.Identity.IdentityModule).Assembly)
                .AddNewtonsoftJson(options => options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);
        }

        public static void ConfigureHttpAccessor(this IServiceCollection services)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        }
    }
}
