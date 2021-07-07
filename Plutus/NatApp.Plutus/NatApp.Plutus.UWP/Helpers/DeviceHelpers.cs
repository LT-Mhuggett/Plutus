using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace NatApp.Plutus.UWP.Helpers
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
                await Task.WhenAll(pendingTasks);

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
            DevicePicker devicePicker = new DevicePicker();
            devicePicker.Filter.SupportedDeviceSelectors.Add(selector);
            return await devicePicker.PickSingleDeviceAsync(new Rect(((Frame)Window.Current.Content).ActualWidth / 3, 0, ((Frame)Window.Current.Content).ActualWidth / 3, ((Frame)Window.Current.Content).ActualHeight / 2), Windows.UI.Popups.Placement.Below);
        }
    }
}
