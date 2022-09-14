using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class AuthActionBody : FormBody<AuthActions>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public string Module { get; set; }

        public override AuthActions GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Name = Name;
            entity.Amount = Amount;
            entity.Module = Module;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AuthActionBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public AuthActionBody(AuthActions entity) : base(entity)
        {
            Id = entity.Id;
            Name = entity.Name;
            Amount = entity.Amount;
            Module = entity.Module;
        }
    }
}
