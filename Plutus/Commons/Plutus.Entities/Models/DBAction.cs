using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;

namespace Plutus.Entities.Models
{
    public class DBAction : Base<int>, IDBAction
    {
        public string TypeName { get; set; }
        public string RecordId { get; set; }
        public DatabaseActions Action { get; set; }

        public DBAction(string typeName, string recordId, DatabaseActions action)
        {
            TypeName = typeName;
            RecordId = recordId;
            Action = action;
        }
    }
}
