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

        public RoleBody() : base()
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
