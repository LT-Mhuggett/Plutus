using WinRT.Interop;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Helpers
{
    /// <summary>
    /// UWP's FileOpenPicker/FileSavePicker/FolderPicker/DevicePicker implicitly knew which window to
    /// anchor to. A MAUI Windows (WinUI3) app - especially unpackaged, which is how this project runs
    /// in Debug (WindowsPackageType=None) - has no such implicit context, so every picker must be
    /// initialized with the current native window handle via IInitializeWithWindow first, or it throws.
    /// </summary>
    internal static class WindowInterop
    {
        public static void InitializeWithCurrentWindow(object picker)
        {
            var mauiWindow = Microsoft.Maui.Controls.Application.Current!.Windows[0];
            var nativeWindow = (Microsoft.UI.Xaml.Window)mauiWindow.Handler!.PlatformView!;
            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
        }
    }
}
