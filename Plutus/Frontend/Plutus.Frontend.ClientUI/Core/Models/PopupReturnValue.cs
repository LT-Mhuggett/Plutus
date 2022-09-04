using Plutus.Frontend.ClientUI.Core.Enum;

namespace Plutus.Frontend.ClientUI.Core.Models
{
    public class PopupReturnValue<T>
    {
        public PopupReturnStatus PopupReturnStatus { get; init; }
        public T ReturnValue { get; init; }

        public PopupReturnValue(PopupReturnStatus popupReturnStatus, T returnValue)
        {
            PopupReturnStatus = popupReturnStatus;
            ReturnValue = returnValue;
        }
    }
}
