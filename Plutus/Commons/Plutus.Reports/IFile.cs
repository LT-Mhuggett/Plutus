using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Plutus.Reports
{
    public interface IFile
    {
        Task<bool> Copy(string srcPath, IDictionary<string, IList<string>> fileTypeChoices, string suggestedName);
        Task<bool> Copy(object src, string destPath, string fileName);
        Task<object> GetFile(IList<string> listFileTypes);
        Task<byte[]> GetFileAsByteArray(IList<string> listFileTypes);
        Task<string> GetFilePath(IList<string> listFileTypes);
        string GetFilePath(object file);
        Task<bool> DeleteFile(object file);
        Task<bool> DeleteFile(string filePath);

        Task<bool> SaveAndView(string fileName, string contentType, MemoryStream stream, IDictionary<string, IList<string>> listFileTypes);
        Task<IList<(string fileName, bool status)>> SaveFiles(IList<(string fileName, string contentType, MemoryStream stream, string extension)> fileData);
    }
}
