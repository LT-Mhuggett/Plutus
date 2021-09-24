using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    public class TriCompositeBase<T1, T2, T3> : Auditable, ITriCompositeBase<T1, T2, T3>
    {
        #region Properties
        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T1 IdOne { get; set; }

        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T2 IdTwo { get; set; }

        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T3 IdThree { get; set; }
        #endregion

        public TriCompositeBase()
        {

        }

        public TriCompositeBase(ITriCompositeBase<T1, T2, T3> @base) : base(@base)
        {
            IdOne = @base.IdOne;
            IdTwo = @base.IdTwo;
            IdThree = @base.IdThree;
        }
    }
}
