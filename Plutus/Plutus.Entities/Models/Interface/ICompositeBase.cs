using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models.Interface
{
    public interface ICompositeBase<T, E> : IAuditable
    {
        /// <summary>
        /// The records Database ID
        /// </summary>
        T IdOne { get; set; }

        E IdTwo { get; set; }
    }
}
