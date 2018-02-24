using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Plutus.Helpers.Extensions
{
    public static class ViewExtensions
    {
        public static Task<bool> ColorTo(this VisualElement self, Color c1, Color c2, Action<Color> callback,
            uint length = 250, Easing easing = null)
        {
            // ReSharper disable once ConvertToLocalFunction
            Func<double, Color> transform = (t) =>
                Color.FromRgba(
                    c1.R + t * (c2.R - c1.R),
                    c1.G + t * (c2.G - c1.G),
                    c1.B + t * (c2.B - c1.B),
                    c1.A + t * (c2.A - c1.A));
            
            return ColorAnimation(self, "ColorTo", transform, callback, length, easing);
        }

        public static void CancelAnimation(this VisualElement self)
        {
            self.AbortAnimation("ColorTo");
        }

        private static Task<bool> ColorAnimation(VisualElement element, string name, Func<double, Color> transform, Action<Color> callback, uint length, Easing easing)
        {
            easing = easing ?? Easing.Linear;
            var taskCompletionSource = new TaskCompletionSource<bool>();
            element.Animate<Color>(name, transform, callback, 16, length, easing,
                (v, c) => taskCompletionSource.SetResult(c));
            return taskCompletionSource.Task;
        }
    }
}
