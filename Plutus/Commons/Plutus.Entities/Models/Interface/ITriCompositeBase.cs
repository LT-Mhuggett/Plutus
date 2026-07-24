using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models.Interface
{
    public interface ITriCompositeBase<T1, T2, T3> : IAuditable
    {
        /// <summary>
        /// The records Database IdOne
        /// </summary>
        T1 IdOne { get; set; }

        /// <summary>
        /// The records Database IdTwo
        /// </summary>
        T2 IdTwo { get; set; }

        /// <summary>
        /// The records Database IdThree
        /// </summary>
        T3 IdThree { get; set; }
    }
}
