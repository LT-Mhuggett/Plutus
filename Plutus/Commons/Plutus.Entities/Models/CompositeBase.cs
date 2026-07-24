using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    public class CompositeBase<T1, T2> : Auditable, ICompositeBase<T1, T2>
    {
        #region Properties
        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T1 IdOne { get; set; }

        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T2 IdTwo { get; set; }
        #endregion

        public CompositeBase()
        {

        }

        public CompositeBase(ICompositeBase<T1, T2> @base) : base(@base)
        {
            IdOne = @base.IdOne;
            IdTwo = @base.IdTwo;
        }
    }
}
