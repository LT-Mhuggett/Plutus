using Plutus.Helpers.Interface;
using Plutus.UWP.Implementations;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Xamarin.Forms;

[assembly: Dependency(typeof(SavePDFImplementation))]
namespace Plutus.UWP.Implementations
{
    class SavePDFImplementation : ISavePDF
    {
        public async Task Save(string filename, string contentTypr, MemoryStream stream)
        {
            StorageFolder local = ApplicationData.Current.LocalFolder;

            StorageFile outFile = await local.CreateFileAsync(filename, CreationCollisionOption.GenerateUniqueName);

            using (Stream outStream = await outFile.OpenStreamForWriteAsync())
            {
                outStream.Write(stream.ToArray(), 0, (int)stream.Length);
            }

            await Windows.System.Launcher.LaunchFileAsync(outFile);
        }
    }
}
