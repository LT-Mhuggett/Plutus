using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;
using ZXing;
using ZXing.Mobile;

namespace Plutus.Helpers
{
    class Scanner
    {
        public async static void ShowScanner(bool moreThanOne, Entry field)
        {
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
                field.Text = result.Text;
            }
        }

        private static void HandleMultiScanResult(Result obj)
        {
            if (obj != null && !string.IsNullOrEmpty(obj.Text))
            {
                throw new NotImplementedException();
            }
            else
                throw new NotImplementedException();
        }
    }
}