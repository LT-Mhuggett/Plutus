using Microsoft.AspNetCore.Authorization;

namespace Plutus.DBService.Extensions
{
    internal class ActionAuthorizationRequirement : IAuthorizationRequirement
    {
        public ActionAuthorizationRequirement(string action)
        {
            Action = action;
        }

        public string Action { get; }
    }
}
