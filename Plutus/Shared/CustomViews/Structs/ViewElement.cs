using CustomViews.Control;
using Microsoft.Maui.Controls;

namespace CustomViews.Structs
{
    /// <summary>
    /// A Structure type representing pair of <see cref="Xamarin.Forms.Label"/> and <see cref="Xamarin.Forms.Entry"/>
    /// </summary>
    public struct ViewElement
    {
        public Label Label { get; }
        public IdentifiableEntry Entry { get; }

        public ViewElement(Label label, IdentifiableEntry entry)
        {
            Label = label;
            Entry = entry;
        }
    }
}
