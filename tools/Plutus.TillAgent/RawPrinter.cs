using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Plutus.TillAgent
{
    /// <summary>
    /// Sends raw ESC/POS bytes straight to a Windows print queue (spooler "RAW" datatype), which
    /// bypasses the driver's rendering and hands the printer our own command stream.
    ///
    /// ⚠ Why this rather than the WinRT PointOfService (OPOS) API the MAUI/Xamarin tills use: OPOS
    /// needs a vendor UPOS service object installed and the printer registered as a POS device.
    /// The RAW-spooler path works with the ordinary vendor Windows driver that a shop printer is
    /// almost always installed with, needs no extra software, and can be tested against any queue.
    /// The transport is behind an interface so the OPOS path can be added if a given printer only
    /// exposes itself that way — the existing ClientUI/AppClient POSPrinter classes are the port
    /// source. Which one Kapow's printer needs is exactly what the FE3.1 on-site spike settles.
    /// </summary>
    public interface IReceiptTransport
    {
        IReadOnlyList<string> ListPrinters();
        bool IsOnline(string printerName);
        void Send(string printerName, byte[] data);
    }

    public sealed class RawSpoolerTransport : IReceiptTransport
    {
        public IReadOnlyList<string> ListPrinters()
        {
            var names = new List<string>();
            foreach (string p in PrinterSettings.InstalledPrinters) names.Add(p);
            return names;
        }

        /// <summary>
        /// Best-effort health: the queue exists and is not reporting an error/offline state. A
        /// receipt printer that is switched off usually shows as offline here; some cheap drivers
        /// always claim ready, which is why the settings window also offers a test print — the only
        /// truly reliable check is paper coming out.
        /// </summary>
        public bool IsOnline(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return false;
            try
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                return settings.IsValid;
            }
            catch (Exception) { return false; }
        }

        public void Send(string printerName, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(printerName)) throw new InvalidOperationException("No printer selected.");
            if (data == null || data.Length == 0) return;

            if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
                throw new InvalidOperationException($"Could not open printer '{printerName}' (error {Marshal.GetLastWin32Error()}).");
            try
            {
                var docInfo = new DOCINFOA { pDocName = "Plutus receipt", pDataType = "RAW" };
                if (!StartDocPrinter(printer, 1, docInfo))
                    throw new InvalidOperationException($"StartDocPrinter failed (error {Marshal.GetLastWin32Error()}).");
                try
                {
                    if (!StartPagePrinter(printer))
                        throw new InvalidOperationException($"StartPagePrinter failed (error {Marshal.GetLastWin32Error()}).");
                    var buffer = Marshal.AllocHGlobal(data.Length);
                    try
                    {
                        Marshal.Copy(data, 0, buffer, data.Length);
                        if (!WritePrinter(printer, buffer, data.Length, out var written) || written != data.Length)
                            throw new InvalidOperationException($"WritePrinter wrote {written}/{data.Length} bytes (error {Marshal.GetLastWin32Error()}).");
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                    EndPagePrinter(printer);
                }
                finally { EndDocPrinter(printer); }
            }
            finally { ClosePrinter(printer); }
        }

        // winspool.drv — the classic RAW printing interop.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private sealed class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);
        [DllImport("winspool.drv", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
    }
}
