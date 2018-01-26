using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Xamarin.Forms;
#if __ANDROID__
using Com.Cloudrail;
using Com.Cloudrail.SI.Types;
using Com.Cloudrail.SI.Services;
using Com.Cloudrail.SI;
using Com.Cloudrail.SI.Interfaces;
#elif __IOS__

#else
using Windows.Storage;
#endif

namespace Plutus.Helpers
{
    /// <summary>
    /// This handels all File Input and Output for all devices using #if defs.
    /// </summary>
    public class FileIO
    {
        /// <summary>
        /// This handles File Saving
        /// This calls the GetLib() and then uses WriteAllLines() to save the file to the local system.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        /// <param name="fileC">This is the File's Content</param>
        public static void Save(string fileN, string[] fileC)
        {
            File.WriteAllLines(Path.Combine(GetLib(), fileN), fileC);
        }

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
        /// <returns>Bool</returns>
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

        public static async Task<bool> BackUp()
        {
#if __ANDROID__
            /*CloudRail.AppKey = "59bff56c3d70425997876e19";

            ICloudStorage service;

            Box box = new Box
                (
                Android.App.Application.Context,
                "yx4h29y5i1xxr6rawcjugsi49sbhurrv",
                "aOZBqB53lFu5RvmAzS3aEr7XsWNtKcxr"
                );

            Com.Cloudrail.SI.Services.Dropbox dropbox = new Com.Cloudrail.SI.Services.Dropbox
                (
                Android.App.Application.Context,
                "t8en46ucho8bzx9",
                "cj5m2va847fgem0"
                );

            dropbox.UseAdvancedAuthentication();

            GoogleDrive googleDrive = new GoogleDrive
                (
                Android.App.Application.Context,
                "467494168983-jstrn11o4v7uchs2cqp8euv4riar0ta3.apps.googleusercontent.com ",
                "",
                "Plutus.Plutus:/oauth2redirect",
                "state"
                );

            googleDrive.UseAdvancedAuthentication();

            OneDrive oneDrive = new OneDrive
                (
                Android.App.Application.Context,
                "3d2ca2df-bc21-425c-9d5d-5f38bb2b228d",
                "Lob77ow5rkGZsCnxjRWe9U7"
                );



            var selection = await App.Current.MainPage.DisplayActionSheet(App.Translate.ProvideValue("SelectCloudService"), App.Translate.ProvideValue("Cancel"), null, "box", "Dropbox", "Google Drive", "OneDrive");

            switch (selection)
            {
                case "box":
                    service = box;
                    break;
                case "Dropbox":
                    service = dropbox;
                    break;
                case "Google Drive":
                    service = googleDrive;
                    break;
                case "OneDrive":
                    service = oneDrive;
                    break;
                default:
                    return false;
            }

            service.UserLogin

            return true;*/
            return false;
#elif __IOS__
            return false;
#else
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("SQL Database", new List<string> { ".db" });
            savePicker.SuggestedFileName = String.Format("{0}-Database-{1}", App.Store.StoreName, DateTime.Now.ToString());

            StorageFile file = await savePicker.PickSaveFileAsync();
            
            StorageFile dbFile = await StorageFile.GetFileFromPathAsync(Path.Combine(GetLib(), "Database.db"));
            if(file != null)
            {
                try
                {
                    dbFile.CopyAndReplaceAsync(file);
                    return true;
                }
                catch(Exception e)
                {
#if DEBUG
                    Console.Write("File Transfer error: "+e);
#endif
                    return false;
                }
            }
            return false;
#endif
        }

        public async static Task<bool> Restore()
        {
#if __ANDROID__
            return false;
#elif __IOS__
            return false;
#else
            var dbPicker = new Windows.Storage.Pickers.FileOpenPicker();
            dbPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            dbPicker.FileTypeFilter.Add(".db");
            StorageFile dbFile = await dbPicker.PickSingleFileAsync();

            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.Combine(GetLib(), "Database.db"));

            if(dbFile!=null)
            { 
                try
                {
                    dbFile.CopyAndReplaceAsync(file);
                    return true;
                }
                catch(Exception e)
                {
#if DEBUG
                    Console.Write("File Transfer error: "+e);
#endif
                    return false;
                }
            }
            return false;
#endif
        }
    }
}
