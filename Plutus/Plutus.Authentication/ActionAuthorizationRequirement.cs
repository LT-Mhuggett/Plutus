using Microsoft.AspNetCore.Authorization;

namespace Plutus.Authentication
{
    public class ActionAuthorizationRequirement : IAuthorizationRequirement
    {
        public ActionAuthorizationRequirement(string action)
        {
            Action = action;
        }

        public string Action { get; }
    }
}
