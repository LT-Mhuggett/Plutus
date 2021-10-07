using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class CategoryBody : FormBody<Category>
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public override Category GenerateEntity() => new Category
        {
            Name = Name,
            Description = Description
        };
    }
}
