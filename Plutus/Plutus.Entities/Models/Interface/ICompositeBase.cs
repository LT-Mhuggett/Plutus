using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models.Interface
{
    public interface ICompositeBase<T1, T2> : IAuditable
    {
        /// <summary>
        /// The records Database ID
        /// </summary>
        T1 IdOne { get; set; }

        T2 IdTwo { get; set; }
    }
}
