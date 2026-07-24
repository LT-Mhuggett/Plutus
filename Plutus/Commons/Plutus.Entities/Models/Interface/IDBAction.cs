using Plutus.Entities.Enums;

namespace Plutus.Entities.Models.Interface
{
    public interface IDBAction : IBase<int>
    {
        /// <summary>
        /// The namespace type of the record
        /// </summary>
        string TypeName { get; set; }

        /// <summary>
        /// The Id of the record, in JSON serialised form
        /// </summary>
        string RecordId { get; set; }

        /// <summary>
        /// The action that was performed on the record
        /// </summary>
        DatabaseActions Action { get; set; }
    }
}
