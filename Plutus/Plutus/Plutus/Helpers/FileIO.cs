using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Plutus.Helpers
{
    class FileIO
    {
        public static void Save(string fileN, string fileC)
        {
            File.WriteAllText(Path.Combine(GetLib(), fileN), fileC);
        }

        public static void Load(string fileN)
        {
            //extra implementation needed
            File.Open(Path.Combine(GetLib(), fileN), FileMode.Open);
        }

        public static bool Exist(string fileN)
        {
            return File.Exists(Path.Combine(GetLib(), fileN));
        }

        public static string GetLib()
        {
            #if __ANDROID__
                string libPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            #elif __IOS__
                string documentsPath = Environment.GetFolderPath (Environment.SpecialFolder.Personal); // Documents folder
                string libPath = Path.Combine (documentsPath, "..", "Library");
            #else
                string libPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            #endif

            return libPath;
        }
    }
}
