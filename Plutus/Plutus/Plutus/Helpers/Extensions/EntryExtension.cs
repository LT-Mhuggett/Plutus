using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Plutus.Helpers.Extensions
{
    public static class EntryExtension
    {
        public static async void SetFocusAfterDelay (this Entry entry, int delay)
        {
            await Task.Delay(delay);
            entry.Focus();
        }
    }
}
