using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace I18N_L10N
{
    public interface ILocalize
    {
        CultureInfo GetCurrentCultureInfo();
        void SetLocale(CultureInfo ci);
    }
}
