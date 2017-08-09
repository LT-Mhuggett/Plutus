using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using ZXing;
using ZXing.Mobile;

namespace Plutus.Helpers
{
    internal class Scanner
    {
        public static async Task<string> ShowScanner(bool moreThanOne)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return null;

            var scanner = new MobileBarcodeScanner()
            {
                UseCustomOverlay = false,
                TopText = "Hold camera to barcode to scan",
                BottomText = "barcode will automatically scan",
                CancelButtonText = "Cancel/Done",
                FlashButtonText = "Flash"
            };

            if (moreThanOne)
            {
                var opt = new MobileBarcodeScanningOptions
                {
                    DelayBetweenContinuousScans = 3000,
                    UseNativeScanning = true,
                    TryHarder = true,
                    TryInverted = true
                };

                scanner.ScanContinuously(opt, HandleMultiScanResult);
                return null;
            }
            else
            {
                var opt = new MobileBarcodeScanningOptions
                {
                    UseNativeScanning = true,
                    TryHarder = true,
                    TryInverted = true
                };
                var result = await scanner.Scan(opt);
                return result.Text;
            }
        }

        private static void HandleMultiScanResult(Result obj)
        {
            if (!string.IsNullOrEmpty(obj?.Text))
            {
                throw new NotImplementedException();
            }
            else
                throw new NotImplementedException();
        }
    }
}