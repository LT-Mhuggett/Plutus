using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Validators.ErrorHandling
{
    public interface IErrorStyle
    {
        /// <summary>
        /// Show error in view
        /// </summary>
        /// <param name="view"></param>
        /// <param name="message"></param>
        void ShowError(View view, string message);
        
        /// <summary>
        /// Remove present error from view
        /// </summary>
        /// <param name="view"></param>
        void RemoveError(View view);
    }
}
