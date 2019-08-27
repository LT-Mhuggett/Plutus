using NatApp.Plutus.iOS.Implemnetations.Services;
using NatApp.Plutus.Services.UIHandeling;
using UIKit;
using Xamarin.Forms;

[assembly: Dependency(typeof(TextiOS))]
namespace NatApp.Plutus.iOS.Implemnetations.Services
{
    public class TextiOS : IText
    {
        public double CalculateWidth(string text)
        {
            var uiLabel = new UILabel();
            uiLabel.Text = text;
            var length = uiLabel.Text.StringSize(uiLabel.Font);
            return length.Width;
        }
    }
}