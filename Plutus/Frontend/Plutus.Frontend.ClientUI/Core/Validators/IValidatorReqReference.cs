using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public interface IValidatorReqReference : IValidator
    {
        Entry ReferenceEntry { get; set; }
    }
}
