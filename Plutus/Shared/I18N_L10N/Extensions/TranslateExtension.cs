using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Devices;

namespace I18N_L10N.Extensions
{
    [ContentProperty("Text")]
    public class TranslateExtension : IMarkupExtension
    {
        readonly CultureInfo _ci;
        const string ResourceId = "I18N_L10N.Resx.AppResources";

        private static readonly Lazy<ResourceManager> ResMgr = new Lazy<ResourceManager>(() => new ResourceManager(ResourceId, typeof(TranslateExtension).GetTypeInfo().Assembly));

        public TranslateExtension()
        {
            if (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.Android)
                _ci = IPlatformApplication.Current!.Services.GetRequiredService<ILocalize>().GetCurrentCultureInfo();
        }

        public TranslateExtension(string Key)
        {
            if (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.Android)
                _ci = IPlatformApplication.Current!.Services.GetRequiredService<ILocalize>().GetCurrentCultureInfo();
            ProvideValue(Key);
        }

        public string Text { get; set; }

        public string ProvideValue(string Key)
        {
            Text = Key;
            if (Text == null)
                return "";
            var translate = ResMgr.Value.GetString(Text, _ci);

            if (translate == null)
            {
#if DEBUG
                Debug.WriteLine("Key '{0}' was not found in resources '{1}' for culture '{2}'.", Text, ResourceId, _ci?.Name ?? "(current)");
                throw new ArgumentException(
                    String.Format("Key '{0}' was not found in resources '{1}' for culture '{2}'.", Text, ResourceId, _ci?.Name ?? "(current)"),
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
            var translate = ResMgr.Value.GetString(Text, _ci);

            if (translate == null)
            {
#if DEBUG
                Debug.WriteLine("Key '{0}' was not found in resources '{1}' for culture '{2}'.", Text, ResourceId, _ci?.Name ?? "(current)");
                throw new ArgumentException(
                    String.Format("Key '{0}' was not found in resources '{1}' for culture '{2}'.", Text, ResourceId, _ci?.Name ?? "(current)"),
                    "Text");
#else
                translate = Text; // returns the key, which GETS DISPLAYED TO THE USER
#endif
            }
            return translate;
        }
    }
}
