using System.Security.Claims;

namespace Plutus.DBService.Extensions
{
    internal static class Claims
    {
        internal const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";
        internal const string AppPermissionOrRolesClaimType = ClaimTypes.Role;
    }
}
