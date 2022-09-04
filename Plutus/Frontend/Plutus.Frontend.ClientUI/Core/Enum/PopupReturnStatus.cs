using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core.Enum
{
    public enum PopupReturnStatus
    {
        /// <summary>
        /// The user has actively canceled the Popup prompt
        /// </summary>
        Canceled = 0,

        /// <summary>
        /// Something interupted the Popup prompt, this could have been the user or another system event
        /// </summary>
        Interupted = 1,

        /// <summary>
        /// The user has succesfully completed the Popup prompt
        /// </summary>
        Completed = 2,
    }
}
