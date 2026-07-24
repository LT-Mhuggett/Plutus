using System.Security.Claims;

namespace Plutus.Authentication
{
    public static class Claims
    {
        internal const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";
        internal const string AppPermissionOrRolesClaimType = ClaimTypes.Role;
    }
}
