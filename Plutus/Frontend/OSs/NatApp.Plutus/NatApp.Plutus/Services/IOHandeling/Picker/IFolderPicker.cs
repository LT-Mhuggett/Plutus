using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Services.IOHandeling.Picker
{
    public interface IFolderPicker
    {
        void InitFolderPicker(params string[] types);
        Task<object> PickFolderAsync();
    }
}
