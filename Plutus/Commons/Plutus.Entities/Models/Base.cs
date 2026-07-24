using Plutus.Entities.Attributes;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    public class Base<T> : Auditable, IBase<T>
    {
        #region Properties
        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T Id { get; set; }
        #endregion

        public Base() : base()
        {

        }

        public Base(IBase<T> @base) : base(@base)
        {
            Id = @base.Id;
        }
    }
}
