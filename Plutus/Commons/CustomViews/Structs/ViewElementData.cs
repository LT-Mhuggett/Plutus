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

        public ViewElementData(uint id, string labelText, string placeholderText, IEnumerable<object> validators, bool isPassword, bool isEnabled)
        {
            Id = id;
            LabelText = labelText;
            PlaceholderText = placeholderText;
            Validators = validators;
            IsPassword = isPassword;
            IsEnabled = isEnabled;
        }
    }
}
