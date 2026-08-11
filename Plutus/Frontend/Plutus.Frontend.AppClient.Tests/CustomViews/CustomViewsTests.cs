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

        /// <summary>
        /// ⚠⚠ A HINT BY DEFAULT, A VALUE ONLY WHEN ASKED — finding K, twice reported.
        ///
        /// `InputAlert.CreateLabelEntry` gave an ENABLED field its text as a `Placeholder` (grey
        /// ghost text in an empty box) and a DISABLED field its text as real `Text`. On the item edit
        /// form that inverts the screen: Name, Brand, Description, Cost and Price — the five rows you
        /// CAN change — looked blank, while Tax band, Category and Stock — the three that are
        /// deliberately read-only — were the only rows showing anything. Matt: *"Edit item, I can
        /// ONLY change the tax?"*, and a day later *"it just gives me the tax rates still."*
        ///
        /// ⚠ It also lost edits in silence, because `InputResults` is seeded from `entry.Text` and a
        /// placeholder is not text: retyping only the price submitted an EMPTY name.
        ///
        /// ⚠ THE DEFAULT MUST STAY FALSE. Every other `InputAlert` — sign-in, passwords, "what is it
        /// called?" — wants a hint to stay a hint; flipping it globally would put the word "Email"
        /// inside the email box on the login screen. So this pins the DEFAULT as much as the flag.
        /// </summary>
        [Fact]
        public void A_placeholder_stays_a_hint_unless_the_caller_asks_for_a_prefill()
        {
            var hint = new ViewElementData(
                1, "Email", "you@example.com", System.Array.Empty<object>(),
                isPassword: false, isEnabled: true);

            Assert.False(hint.PrefillWithPlaceholder);

            var prefilled = new ViewElementData(
                1, "Name", "Ghost vol 13", System.Array.Empty<object>(),
                isPassword: false, isEnabled: true, prefillWithPlaceholder: true);

            Assert.True(prefilled.PrefillWithPlaceholder);
            Assert.Equal("Ghost vol 13", prefilled.PlaceholderText);
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
