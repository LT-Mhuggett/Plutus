using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Note model, where <typeparamref name="T1"></typeparamref>Auto Genterated int ID and <typeparamref name="T2"></typerparamref> is Sale ID
    /// </summary>
    [Serializable]
    public class Note : CompositeBase<int, Guid>, IAuditable
    {
        #region Properties
        [Exportable]
        public string Text { get; set; }
        #region Relationships
        #region Collections
        public virtual Sale Sale { get; set; }
        #endregion
        #endregion
        #endregion

        public Note() { }

        public Note(string text)
        {
            Text = text;
        }
    }
}