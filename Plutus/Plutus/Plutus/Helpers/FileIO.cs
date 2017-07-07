using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Plutus.Helpers
{
    /// <summary>
    /// This handels all File Input and Output for all devices using #if defs.
    /// </summary>
    class FileIO
    {
        /// <summary>
        /// This handles File Saving
        /// This calls the GetLib() and then uses WriteAllText() to save the file to the local system.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        /// <param name="fileC">This is the File's Content</param>
        public static void Save(string fileN, string fileC)
        {
            File.WriteAllText(Path.Combine(GetLib(), fileN), fileC);
        }

        /// <summary>
        /// This handles File Loading.
        /// This calls the GetLib() and then uses File.Open() to open the file and read it's contents to memory.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        public static void Load(string fileN)
        {
            //extra implementation needed
            File.Open(Path.Combine(GetLib(), fileN), FileMode.Open);
        }

        /// <summary>
        /// This handles File Existence.
        /// This calls the GetLib() and then uses File.Exists() to check if the file is in it's directory.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        /// <returns></returns>
        public static bool Exists(string fileN)
        {
            return File.Exists(Path.Combine(GetLib(), fileN));
        }

        /// <summary>
        /// This Handles all device specfic code to find the correct directory for files, per device specifiction.
        /// it uses #if def to only compile code that's required for that device.
        /// </summary>
        /// <returns>string libPath - this contains the correct file directory based on device</returns>
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
