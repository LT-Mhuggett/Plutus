using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Plutus.Frontend.AppClient.Helpers.Compatibility;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Helpers
{
    public partial class DeviceHelpers
    {
        public static async Task<T> GetFirstDeviceAsync<T>(string selector, Func<string, Task<T>> convertAsync) where T : class
        {
            var completionSource = new TaskCompletionSource<T>();
            var pendingTasks = new List<Task>();
            DeviceWatcher watcher = DeviceInformation.CreateWatcher(selector);

            watcher.Added += (DeviceWatcher sender, DeviceInformation device) =>
            {
                Func<string, Task> lambda = async (id) =>
                {
                    T t = await convertAsync(id);
                    if (t != null)
                    {
                        completionSource.TrySetResult(t);
                    }
                };
                pendingTasks.Add(lambda(device.Id));
            };

            watcher.EnumerationCompleted += async (DeviceWatcher sender, object args) =>
            {
                try
                {
                    await Task.WhenAll(pendingTasks);
                }
                catch(Exception ex)
                {
                    AppServices.Get<Plutus.Frontend.AppClient.Services.Analytics.ILogger>().LogError(ex);
                }

                completionSource.TrySetResult(null);
            };

            watcher.Removed += (DeviceWatcher sender, DeviceInformationUpdate args) =>
            {
                // Subscription is required to enable real-time updates
            };

            watcher.Updated += (DeviceWatcher sender, DeviceInformationUpdate args) =>
            {
                // Subscription is required to enable real-time updates
            };

            watcher.Start();

            T result = await completionSource.Task;

            watcher.Stop();

            return result;
        }

        public static async Task<DeviceInformation> GetByDevicePickerAsync(string selector)
        {
            // UWP's DevicePicker anchored off Window.Current.Content (a Frame). WinUI3 desktop apps have no
            // Window.Current singleton, so the picker is initialized with the native window handle instead,
            // and the anchor rect comes from the AppWindow's client size.
            DevicePicker devicePicker = new DevicePicker();
            devicePicker.Filter.SupportedDeviceSelectors.Add(selector);
            WindowInterop.InitializeWithCurrentWindow(devicePicker);

            var mauiWindow = Microsoft.Maui.Controls.Application.Current!.Windows[0];
            var nativeWindow = (Microsoft.UI.Xaml.Window)mauiWindow.Handler!.PlatformView!;
            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            var clientSize = appWindow.ClientSize;

            return await devicePicker.PickSingleDeviceAsync(
                new Rect(clientSize.Width / 3, 0, clientSize.Width / 3, clientSize.Height / 2),
                global::Windows.UI.Popups.Placement.Below);
        }
    }
}
