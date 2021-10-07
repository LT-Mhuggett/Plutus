using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class SavedTransactionBody : FormBody<SavedTransaction>
    {
        public string Name { get; set; }
        public string Data { get; set; }

        public override SavedTransaction GenerateEntity() => new SavedTransaction
        {
            Name = Name,
            Data = Data
        };
    }
}
