using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    public class Address<T> : Base<T>, IAddress<T>
    {
        #region Properties
        [Exportable]
        public string AdLine1 { get; set; }
        [Exportable]
        public string AdLine2 { get; set; }
        [Exportable]
        public string City { get; set; }

        [Exportable]
        [Required]
        public string PostCode { get; set; }
        [Exportable]
        public string Country { get; set; }
        [Exportable]
        public string FullAddress { get; set; }

        [NotMapped]
        public string ReadableAddress
        {
            get => FullAddress ?? $"{AdLine1}, {AdLine2}, {City}, {PostCode}, {Country}";
        }
        #endregion
    }
}
