using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Plutus.Frontend.ClientUI.Core.Messages
{
    // Replaces the removed Xamarin.Forms/MAUI MessagingCenter string channels with
    // strongly-typed CommunityToolkit.Mvvm WeakReferenceMessenger messages.

    /// <summary>Raised when the main till UI has finished loading ("MainUILoaded").</summary>
    public sealed class MainUILoadedMessage { }

    /// <summary>Raised to add an item (by id) to the basket ("AddToBasket").</summary>
    public sealed class AddToBasketMessage : ValueChangedMessage<string>
    {
        public AddToBasketMessage(string itemId) : base(itemId) { }
    }
}
