using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Plutus.Helpers.Interface;
using Java.IO;
using Xamarin.Forms;
using Plutus.Droid;

[assembly: Dependency(typeof(SavePDFImplementation))]
namespace Plutus.Droid
{
    class SavePDFImplementation : ISavePDF
    {
        public async Task Save(string filename, string contentTypr, MemoryStream stream)
        {
            string root = null;

            if (Android.OS.Environment.IsExternalStorageEmulated)
            {
                root = Android.OS.Environment.ExternalStorageDirectory.ToString();
            }
            else
            {
                root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

                Java.IO.File myDir = new Java.IO.File(root + "/Docs");

                myDir.Mkdir();

                Java.IO.File file = new Java.IO.File(myDir, filename);

                if (file.Exists())
                    file.Delete();

                try
                {
                    FileOutputStream outs = new FileOutputStream(file);
                    outs.Write(stream.ToArray());
                    outs.Flush();
                    outs.Close();
                }
                catch(Exception e)
                {

                }
                if(file.Exists())
                {
                    Android.Net.Uri path = Android.Net.Uri.FromFile(file);

                    string extension = Android.Webkit.MimeTypeMap.GetFileExtensionFromUrl(Android.Net.Uri.FromFile(file).ToString());

                    string mimeType = Android.Webkit.MimeTypeMap.Singleton.GetMimeTypeFromExtension(extension);

                    Intent intent = new Intent(Intent.ActionView);

                    intent.SetDataAndType(path, mimeType);

                    Forms.Context.StartActivity(Intent.CreateChooser(intent, "Choose App"));
                }
            }
        }
    }
}