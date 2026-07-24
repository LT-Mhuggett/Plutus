using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Item model, where <typeparamref name="T1"></typeparamref> is EAN/UPC product code or other Priamry Identifier, <typeparamref name="T2"></typerparamref> is Business ID, <typeparamref neme="T3"></typeparamref> is Store ID
    /// </summary>
    [Serializable]
    public class Stock : TriCompositeBase<string, Guid, int>, IStock
    {
        [Exportable]
        [Key, MaxLength(20)]
        public override string IdOne { get; set; }

        #region Properties

        [Exportable] 
        public int Quantity { get; set; }

        #region Relationships
        public virtual Item? Item { get; set; }
        public virtual Store? Store { get; set; }
        #endregion
        #endregion
    }
}
