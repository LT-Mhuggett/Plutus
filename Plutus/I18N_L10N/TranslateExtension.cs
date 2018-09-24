using System;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace I18N_L10N
{
    [ContentProperty("Text")]
    public class TranslateExtension : IMarkupExtension
    {
        readonly CultureInfo ci;
        const string ResourceId = "I18N_L10N.Resx.AppResources";

        private static readonly Lazy<ResourceManager> resMgr = new Lazy<ResourceManager>(() => new ResourceManager(ResourceId, typeof(TranslateExtension).GetTypeInfo().Assembly));

        public TranslateExtension()
        {
            if (Device.RuntimePlatform == Device.iOS || Device.RuntimePlatform == Device.Android)
                ci = DependencyService.Get<ILocalize>().GetCurrentCultureInfo();
        }

        public TranslateExtension(string Key)
        {
            if (Device.RuntimePlatform == Device.iOS || Device.RuntimePlatform == Device.Android)
                ci = DependencyService.Get<ILocalize>().GetCurrentCultureInfo();
            ProvideValue(Key);
        }

        public string Text { get; set; }

        public string ProvideValue(string Key)
        {
            Text = Key;
            if (Text == null)
                return "";
            var translate = resMgr.Value.GetString(Text, ci);

            if (translate == null)
            {
#if DEBUG
                throw new ArgumentException(
                    String.Format("Key '{0}' was not found in resources '{1}' for culture '{2}'.", Text, ResourceId, ci.Name),
                    "Text");
#else
                translate = Text; // returns the key, which GETS DISPLAYED TO THE USER
#endif
            }
            return translate;
        }

        public object ProvideValue(IServiceProvider serviceProvider)
        {
            if (Text == null)
                return "";
            var translate = resMgr.Value.GetString(Text, ci);

            if (translate == null)
            {
#if DEBUG
                throw new ArgumentException(
                    String.Format("Key '{0}' was not found in resources '{1}' for culture '{2}'.", Text, ResourceId, ci.Name),
                    "Text");
#else
                translate = Text; // returns the key, which GETS DISPLAYED TO THE USER
#endif
            }
            return translate;
        }
    }
}
