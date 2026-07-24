using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Services.IOHandeling.Picker
{
    public interface IFolderPicker
    {
        void InitFolderPicker(params string[] types);
        Task<object> PickFolderAsync();
    }
}
