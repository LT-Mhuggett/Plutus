using Plutus.Frontend.AppClient.Services.UIHandeling;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Implementations.Services
{
    public class TextPlatform : IText
    {
        public double CalculateWidth(string text)
        {
            var textBlock = new TextBlock() { FontSize = new Label().FontSize };
            textBlock.Text = text;
            var parentBorder = new Microsoft.UI.Xaml.Controls.Border { Child = textBlock };
            textBlock.MaxHeight = 50;
            textBlock.MaxWidth = double.PositiveInfinity;
            parentBorder.Measure(new global::Windows.Foundation.Size(textBlock.MaxWidth, textBlock.MaxHeight));
            parentBorder.Child = null;
            return parentBorder.DesiredSize.Width;
        }
    }
}
