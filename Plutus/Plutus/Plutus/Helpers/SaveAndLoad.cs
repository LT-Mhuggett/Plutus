using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Plutus.Helpers
{
    class SaveAndLoad
    {
        public static void Save(string fileN, string fileC)
        {
            #if __ANDROID__
                string libPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            #elif __IOS__
            string documentsPath = Environment.GetFolderPath (Environment.SpecialFolder.Personal); // Documents folder
                string libPath = Path.Combine (documentsPath, "..", "Library");
            #else
                string libPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            #endif
            System.IO.File.WriteAllText(Path.Combine(libPath, fileN), fileC);
        }   
    }
}
