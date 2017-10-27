using Foundation;
using QuickLook;
using System.IO;

namespace Plutus.iOS
{
    internal class QLPreviewItemBundle : QLPreviewItem
    {
        private string _filename;
        private string _filePath;

        public QLPreviewItemBundle(string filename, string filePath)
        {
            _filename = filename;
            _filePath = filePath;
        }

        public override string ItemTitle
        {
            get
            {
                return _filename;
            }
        }

        public override NSUrl ItemUrl
        {
            get
            {
                var documents = NSBundle.MainBundle.BundlePath;
                var lib = Path.Combine(documents, _filePath);
                var url = NSUrl.FromFilename(lib);
                return url;
            }
        }
    }
}