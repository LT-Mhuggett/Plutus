using NatApp.Plutus.Services.UIHandeling;
using NatApp.Plutus.UWP.Implementations.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Xamarin.Forms;

[assembly: Dependency(typeof(TextUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    public class TextUWP : IText
    {
        public double CalculateWidth(string text)
        {
            var textBlock = new TextBlock() { FontSize = Device.GetNamedSize(NamedSize.Default, typeof(Label)) };
            textBlock.Text = text;
            var parentBorder = new Border { Child = textBlock };
            textBlock.MaxHeight = 50;
            textBlock.MaxWidth = double.PositiveInfinity;
            parentBorder.Measure(new Windows.Foundation.Size(textBlock.MaxWidth, textBlock.MaxHeight));
            parentBorder.Child = null;
            return parentBorder.DesiredSize.Width;
        }
    }
}
