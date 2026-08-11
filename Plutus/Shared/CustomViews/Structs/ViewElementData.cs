using System.Collections.Generic;

namespace CustomViews.Structs
{
    /// <summary>
    /// A Structure type holding the data required to construct <see cref="CustomViews.ViewElement"/>
    /// </summary>
    public struct ViewElementData
    {
        public uint Id { get; }
        public string LabelText { get; }
        public string PlaceholderText { get; }
        public IEnumerable<object> Validators { get; }
        public bool IsPassword { get; }
        public bool IsEnabled { get; }

        /// <summary>
        /// Put <see cref="PlaceholderText"/> in the box as REAL, EDITABLE TEXT rather than as grey
        /// hint text. Off by default.
        ///
        /// ⚠⚠ THIS IS THE FIX FOR "I CAN ONLY CHANGE THE TAX" (finding K, Matt 2026-08-10 and again
        /// 2026-08-11). `InputAlert.CreateLabelEntry` gave an **enabled** field its value as a
        /// `Placeholder` and a **disabled** field its value as `Text`. On the item edit form that is
        /// exactly inverted: Name, Brand, Description, Cost and Price — the five fields you CAN
        /// change — rendered as empty boxes with grey ghosts, while Tax band, Category and Stock —
        /// the three that are deliberately read-only — were the only rows showing anything solid.
        /// The form looked like a tax editor with five blanks above it.
        ///
        /// ⚠ AND IT SILENTLY LOST EDITS. `InputResults` is seeded from `entry.Text`, and a
        /// placeholder is not text, so an operator who retyped only the price submitted an EMPTY
        /// name — which the caller rejects with a bare `return`, no message, nothing saved.
        ///
        /// ⚠ OPT-IN, NOT A GLOBAL FLIP. Every other `InputAlert` in the app — sign-in, passwords,
        /// "what is it called?" — wants a placeholder to stay a HINT. Turning them all into
        /// pre-filled values would put the word "Email" inside the email box on the login screen.
        /// </summary>
        public bool PrefillWithPlaceholder { get; }

        public ViewElementData(uint id, string labelText, string placeholderText, IEnumerable<object> validators, bool isPassword, bool isEnabled, bool prefillWithPlaceholder = false)
        {
            Id = id;
            LabelText = labelText;
            PlaceholderText = placeholderText;
            Validators = validators;
            IsPassword = isPassword;
            IsEnabled = isEnabled;
            PrefillWithPlaceholder = prefillWithPlaceholder;
        }
    }
}
