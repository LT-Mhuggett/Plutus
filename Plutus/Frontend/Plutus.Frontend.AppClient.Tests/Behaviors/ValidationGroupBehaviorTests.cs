using Plutus.Frontend.AppClient.Behaviors;

namespace Plutus.Frontend.AppClient.Tests.Behaviors
{
    // Every Behavior<T> (and any other Microsoft.Maui.Controls.BindableObject) constructor calls
    // Dispatcher.GetForCurrentThread(), which needs a live Microsoft.UI.Dispatching.DispatcherQueue -
    // only available inside a real running WinUI3 app, not a plain xUnit process. Confirmed empirically
    // (COMException "ClassFactory cannot supply requested class"); unlike Microsoft.Maui.Controls.Command,
    // which does NOT derive from BindableObject and constructs fine (see CommandSmokeTest).
    // ValidationBehavior/ValidationGroupBehavior are BindableObject-derived, so they can't be
    // constructed here at all - not just the View-touching parts of them.
    public class ValidationGroupBehaviorTests
    {
        [Fact(Skip = "ValidationGroupBehavior derives from Behavior<View> -> BindableObject, whose " +
            "constructor requires a live WinUI3 dispatcher unavailable in a plain xUnit process.")]
        public void IsValid_DefaultsToFalse()
        {
            Assert.False(new ValidationGroupBehavior().IsValid);
        }
    }
}
