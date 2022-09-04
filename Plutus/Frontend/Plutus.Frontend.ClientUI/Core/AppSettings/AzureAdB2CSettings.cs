using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core.AppSettings
{
    public class AzureAdB2CSettings
    {
        public string Instance { get; set; }
        public string Domain { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string SignUpSignInPolicyId { get; set; }
        public string[] Scopes { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is null or not AzureAdB2CSettings)
                return false;
            else 
            {
                var aAdB2CSObj = obj as AzureAdB2CSettings;
                return Instance == aAdB2CSObj.Instance &&
                       Domain == aAdB2CSObj.Domain &&
                       TenantId == aAdB2CSObj.TenantId &&
                       ClientId == aAdB2CSObj.ClientId &&
                       SignUpSignInPolicyId == aAdB2CSObj.SignUpSignInPolicyId &&
                       Scopes == aAdB2CSObj.Scopes;
            }
        }

        public override int GetHashCode() => Instance.GetHashCode() ^ Domain.GetHashCode() ^ TenantId.GetHashCode() ^ ClientId.GetHashCode() ^ SignUpSignInPolicyId.GetHashCode() ^ Scopes.GetHashCode();

        public override string ToString() => string.Format("AzureAdB2CSettings(Instance {0}, Domain {1}, TenantId {2}, ClientId {3}, SignUpSignInPolicyId {4}, Scopes {5})", Instance, Domain, TenantId, ClientId, SignUpSignInPolicyId, Scopes.ToString());
    }
}
