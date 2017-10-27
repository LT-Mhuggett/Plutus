using System.IO;
using System.Threading.Tasks;

namespace Plutus.Helpers.Interface
{
    interface ISavePDF
    {
        Task Save(string filename, string contentTypr, MemoryStream stream);
    }
}
