using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    public class CompositeBase<T, E> : Auditable, ICompositeBase<T, E>
    {
        #region Properties
        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual T IdOne { get; set; }

        [Exportable]
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual E IdTwo { get; set; }
        #endregion

        public CompositeBase()
        {

        }

        public CompositeBase(ICompositeBase<T, E> @base) : base(@base)
        {
            IdOne = @base.IdOne;
            IdTwo = @base.IdTwo;
        }
    }
}
