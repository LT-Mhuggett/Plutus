using CustomViews.Structs;

namespace Plutus.Frontend.AppClient.Tests.CustomViews
{
    public class ViewElementDataTests
    {
        [Fact]
        public void Construction_SetsAllProperties()
        {
            var validators = new object[] { new object() };
            var data = new ViewElementData(3, "Label", "Placeholder", validators, isPassword: true, isEnabled: false);

            Assert.Equal((uint)3, data.Id);
            Assert.Equal("Label", data.LabelText);
            Assert.Equal("Placeholder", data.PlaceholderText);
            Assert.Same(validators, data.Validators);
            Assert.True(data.IsPassword);
            Assert.False(data.IsEnabled);
        }
    }

    public class ViewElementTests
    {
        [Fact]
        public void Construction_StoresLabelAndEntryReferences()
        {
            // Label and IdentifiableEntry are Microsoft.Maui.Controls.BindableObject-derived, so real
            // instances can't be constructed in this test process (see ValidationGroupBehaviorTests) -
            // null is enough to exercise the struct's own construction logic, which just assigns fields.
            var viewElement = new ViewElement(null, null);

            Assert.Null(viewElement.Label);
            Assert.Null(viewElement.Entry);
        }
    }
}
