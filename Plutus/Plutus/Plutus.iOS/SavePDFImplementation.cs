using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using UIKit;
using Xamarin.Forms;
using Plutus.iOS;
using Plutus.Helpers.Interface;
using QuickLook;

[assembly: Dependency(typeof(SavePDFImplementation))]
namespace Plutus.iOS
{
    class SavePDFImplementation : ISavePDF
    {
        public async Task Save(string filename, string contentTypr, MemoryStream stream)
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.Personal);

            string filePath = Path.Combine(path, filename);

            try
            {
                FileStream fileStream = File.Open(filePath, FileMode.Create);

                stream.Position = 0;
                stream.CopyTo(fileStream);

                fileStream.Flush();
                fileStream.Close();
            }
            catch(Exception e)
            {

            }

            UIViewController currentController = UIApplication.SharedApplication.KeyWindow.RootViewController;

            while (currentController.PresentedViewController != null)
                currentController = currentController.PresentedViewController;
            UIView currentView = currentController.View;
            QLPreviewController preview = new QLPreviewController();
            QLPreviewItem item = new QLPreviewItemBundle(filename, filePath);
            preview.DataSource = new PreviewControllerDS(item);
            currentController.PresentViewController(preview, true, null);
        }
    }
}