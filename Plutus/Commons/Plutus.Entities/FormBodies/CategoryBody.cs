using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class CategoryBody : FormBody<Category>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Guid BusinessId { get; set; }

        public override Category GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.IdOne = Id;
            entity.Name = Name;
            entity.Description = Description;
            entity.IdTwo = BusinessId;
            return entity;
        }

        public CategoryBody() : base()
        {
            BusinessId = Guid.Empty;
        }

        public CategoryBody(Category entity) : base(entity)
        {
            Id = entity.IdOne;
            Name = entity.Name;
            Description = entity.Description;
            BusinessId = entity.IdTwo;
        }
    }
}
