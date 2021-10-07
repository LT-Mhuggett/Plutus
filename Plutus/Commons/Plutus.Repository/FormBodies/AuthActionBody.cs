using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class AuthActionBody : FormBody<AuthActions>
    {
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public string Module { get; set; }

        public override AuthActions GenerateEntity() => new AuthActions
        {
            Name = Name,
            Amount = Amount,
            Module = Module
        };
    }
}
