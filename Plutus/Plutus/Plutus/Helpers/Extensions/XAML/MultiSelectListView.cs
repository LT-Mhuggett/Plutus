using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace Plutus.Helpers.Extensions.XAML
{
    public class MultiSelectListView<T>
    {
        public T Data { get; set; }
        public bool IsSelected { get; set; } = false;
    }
}
