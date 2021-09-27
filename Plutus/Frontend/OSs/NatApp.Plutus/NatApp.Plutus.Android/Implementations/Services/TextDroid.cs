using Android.Content.Res;
using Android.Graphics;
using Android.Widget;
using NatApp.Plutus.Droid.Implementations.Services;
using NatApp.Plutus.Services.UIHandeling;
using Plugin.CurrentActivity;
using Xamarin.Forms;

[assembly: Dependency(typeof(TextDroid))]
namespace NatApp.Plutus.Droid.Implementations.Services
{
    public class TextDroid : IText
    {
        public double CalculateWidth(string text)
        {
            Rect bounds = new Rect();
            TextView textView = new TextView(CrossCurrentActivity.Current.Activity);
            textView.Paint.GetTextBounds(text, 0, text.Length, bounds);
            var length = bounds.Width();
            return length / Resources.System.DisplayMetrics.ScaledDensity;
        }
    }
}