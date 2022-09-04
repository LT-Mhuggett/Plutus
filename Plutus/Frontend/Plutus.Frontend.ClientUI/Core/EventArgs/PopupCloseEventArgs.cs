using Plutus.Frontend.ClientUI.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core.EventArgs
{
    public class PopupCloseRequestEventArgs<T> : System.EventArgs
    {
        public PopupReturnValue<T> PopupReturnValue { get; init; }
        public object Sender { get; init; }

        public PopupCloseRequestEventArgs(object sender, PopupReturnValue<T> popupReturnValue)
        {
            PopupReturnValue = popupReturnValue;
            Sender = sender;
        }
    }
}
