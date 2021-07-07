using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.UWP.Services.POS
{
    public static class EscPosComands
    {
        public const string ESC = "\u001B";
        public const string GS = "\u001D";
        public const string InitializePrinter = ESC + "@";

        #region Emphasize Mode
        public const string BoldOn = ESC + "E" + "\u0001";
        public const string BoldOff = ESC + "E" + "\0";
        #endregion

        #region Underline Mode
        public const string Underline_On_Thin = ESC + "-" + "\u0001";
        public const string Underline_On_Thick = ESC + "-" + "\u0002";
        public const string Underline_Off = ESC + "-" + "\u0000";
        #endregion

        #region Character Size
        public const string Double_W_On = GS + "!" + "\u0010";
        public const string Double_H_On = GS + "!" + "\u0001";
        public const string Double_W_H_On = GS + "!" + "\u0011";  // 2x sized text (double-high + double-wide)
        public const string Double_W_H_Off = GS + "!" + "\u0000";
        #endregion

        #region Justification Mode
        public const string Align_Left = ESC + "a" + "\u0000";
        public const string Align_Center = ESC + "a" + "\u0001";
        public const string Align_Right = ESC + "a" + "\u0002";
        #endregion
    }
}
