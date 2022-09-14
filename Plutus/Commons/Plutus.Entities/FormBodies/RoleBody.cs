using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class RoleBody : FormBody<Role>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Guid BusinessId { get; set; }
        public int ParentRoleId { get; set; }


        public override Role GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Name = Name;
            entity.BusinessId = BusinessId;
            entity.ParentId = ParentRoleId;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public RoleBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public RoleBody(Role entity) : base(entity)
        {
            Id = entity.Id;
            Name = entity.Name;
            BusinessId = entity.BusinessId;
            ParentRoleId = entity.ParentId;
        }
    }
}
