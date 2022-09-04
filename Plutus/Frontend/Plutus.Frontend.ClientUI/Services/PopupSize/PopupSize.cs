using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Plutus.Frontend.ClientUI.Core.Exceptions;

namespace Plutus.Frontend.ClientUI.Services.PopupSize
{
    public class PopupSize : IPopupSize
    {
        public Size Tiny { get; private set; }
        public Size Small { get; private set; }
        public Size Medium { get; private set; }
        public Size Large { get; private set; }

        public PopupSize(IDeviceDisplay deviceDisplay)
        {
            UpdatePopupSizes(deviceDisplay);
        }

        private void UpdatePopupSizes(IDeviceDisplay deviceDisplay)
        {
            Tiny = new Size(.2 * (deviceDisplay.MainDisplayInfo.Width / deviceDisplay.MainDisplayInfo.Density),
                              .2 * (deviceDisplay.MainDisplayInfo.Height / deviceDisplay.MainDisplayInfo.Density));
            Small = new Size(.4 * (deviceDisplay.MainDisplayInfo.Width / deviceDisplay.MainDisplayInfo.Density),
                              .3 * (deviceDisplay.MainDisplayInfo.Height / deviceDisplay.MainDisplayInfo.Density));
            Medium = new Size(.6 * (deviceDisplay.MainDisplayInfo.Width / deviceDisplay.MainDisplayInfo.Density),
                              .5 * (deviceDisplay.MainDisplayInfo.Height / deviceDisplay.MainDisplayInfo.Density));
            Large = new Size(.9 * (deviceDisplay.MainDisplayInfo.Width / deviceDisplay.MainDisplayInfo.Density),
                             .8 * (deviceDisplay.MainDisplayInfo.Height / deviceDisplay.MainDisplayInfo.Density));
        }
    }
}
