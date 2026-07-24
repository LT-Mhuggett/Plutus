using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class SavedTransactionBody : FormBody<SavedTransaction>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Data { get; set; }

        public override SavedTransaction GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Name = Name;
            entity.Data = Data;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public SavedTransactionBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public SavedTransactionBody(SavedTransaction entity)  :base(entity)
        {
            Id = entity.Id;
            Name = entity.Name;
            Data = entity.Data;
        }
    }
}
