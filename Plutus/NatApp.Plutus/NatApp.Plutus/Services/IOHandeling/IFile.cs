using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Services.IOHandeling
{
    public interface IFile
    {
        Task<bool> Copy(string srcPath, List<KeyValuePair<string, List<string>>> fileTypeChoices, string suggestedName);
        Task<bool> Copy(object src, string destPath, string fileName);
        Task<object> GetFile(List<string> listFileTypes);
        Task<string> GetFilePath(List<string> listFileTypes);
        string GetFilePath(object file);
        Task<bool> DeleteFile(object file);
        Task<bool> DeleteFile(string filePath);

        Task<bool> SaveAndView(string filename, string contentType, MemoryStream stream, IDictionary<string, List<string>> listFileTypes);
    }
}
