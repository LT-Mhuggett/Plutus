using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

namespace Plutus.Authentication
{
    /// <summary>
    /// Adds security requirements to API operations
    /// </summary>
    public class OAuthSecurityRequirementOperationFilter : IOperationFilter
    {
        private readonly string AppIdUri;

        public OAuthSecurityRequirementOperationFilter(IOptions<AuthenticationOptions> authenticationOptions)
        {
            AppIdUri = authenticationOptions.Value.ApplicationIdUri;
        }

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Get custom attributes on action and controller
            object[] attributes = context.ApiDescription.CustomAttributes().ToArray();
            if (attributes.OfType<AllowAnonymousAttribute>().Any())
            {
                // Controller / action allows anonymous calls
                return;
            }

            AuthorizeAttribute[] authorizeAttributes = attributes.OfType<AuthorizeAttribute>().ToArray();
            if (authorizeAttributes.Length == 0)
            {
                return;
            }

            // Policy name is always an action defined in Actions
            // Resolve the actions, from them derive the accepted scopes, get distinct values
            string[] scopes = authorizeAttributes
                .Select(attr => attr.Policy)
                .SelectMany(action => AuthorizedPermissions.DelegatedPermissionsForActions[action])
                .Distinct()
                .Select(scope => $"{AppIdUri}/{scope}") // Combine scope id with app id URI to form full scope id
                .ToArray();

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "aad-jwt"
                        },
                        UnresolvedReference = true
                    },
                    scopes
                }
            });
        }
    }
}
