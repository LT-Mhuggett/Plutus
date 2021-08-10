using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class SavedTransaction : Base<string>, ISavedTransaction
    {
        #region Properties
        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable(ExportLevels.NonUserFriendly)]
        public string Data { get; set; }
        #endregion
    }
}
