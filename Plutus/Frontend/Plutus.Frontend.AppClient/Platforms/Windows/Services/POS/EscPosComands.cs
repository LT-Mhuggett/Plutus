namespace Plutus.Frontend.AppClient.Platforms.Windows.Services.POS
{
    /// <summary>
    /// ESC/POS Commands
    /// </summary>
    public static class EscPosComands
    {
        /// <summary>
        /// Escape Character
        /// </summary>
        private const string ESC = "\u001B";

        /// <summary>
        /// Information Separator 'GS' for character formating 
        /// </summary>
        private const string GS = "\u001D";

        /// <summary>
        /// Initialize Printer
        /// </summary>
        public const string InitializePrinter = ESC + "@";

        #region Emphasize Mode
        /// <summary>
        /// Turn on Bold (Emphasize)
        /// </summary>
        public const string BoldOn = ESC + "E" + "\u0001";

        /// <summary>
        /// Turn off Bold (Emphasize)
        /// </summary>
        /// <remarks>'0' is passed instead of '\u0000' as there was a problem present on Windows 10 machines not handling Unicode null correctly</remarks>
        public const string BoldOff = ESC + "E" + "0";
        #endregion

        #region Underline Mode
        /// <summary>
        /// Turn on underlining, single thickness
        /// </summary>
        public const string Underline_On_Thin = ESC + "-" + "\u0001";

        /// <summary>
        /// Turn on underlining, double thickness
        /// </summary>
        public const string Underline_On_Thick = ESC + "-" + "\u0002";

        /// <summary>
        /// Turn off underlining
        /// </summary>
        /// <remarks>'0' is passed instead of '\u0000' as there was a problem present on Windows 10 machines not handling Unicode null correctly</remarks>
        public const string Underline_Off = ESC + "-" + "0";
        #endregion

        #region Character Size
        /// <summary>
        /// Turn on Double Width characters
        /// </summary>
        public const string Double_W_On = GS + "!" + "\u0010";

        /// <summary>
        /// Turn on Double Height characters
        /// </summary>
        public const string Double_H_On = GS + "!" + "\u0001";

        /// <summary>
        /// Turn on Double Width and Height characters
        /// </summary>
        public const string Double_W_H_On = GS + "!" + "\u0011";  // 2x sized text (double-high + double-wide)

        /// <summary>
        /// Turn off Double Width and Height characters
        /// </summary>
        public const string Double_W_H_Off = GS + "!" + "0";
        #endregion

        #region Justification Mode
        /// <summary>
        /// Force Left Alginment
        /// </summary>
        /// <remarks>'0' is passed instead of '\u0000' as there was a problem present on Windows 10 machines not handling Unicode null correctly</remarks>
        public const string Align_Left = ESC + "a" + "0";

        /// <summary>
        /// Force Center Alginment
        /// </summary>
        public const string Align_Center = ESC + "a" + "\u0001";

        /// <summary>
        /// Force Right Alignment
        /// </summary>
        public const string Align_Right = ESC + "a" + "\u0002";
        #endregion
    }
}
