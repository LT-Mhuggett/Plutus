using System;

namespace Plutus.Entities.Models.Interface
{
    public interface IAuditable
    {
        /// <summary>
        /// When was the record created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// When was the record last eddited
        /// </summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Who created the record
        /// </summary>
        public string CreatedBy { get; set; }

        /// <summary>
        /// Who last edited the record
        /// </summary>
        public string ModifiedBy { get; set; }
    }
}
