using System;
using System.Collections.Generic;
using System.Text;
using SQLite;
using System.IO;
using Plutus.Helpers;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    class Database
    {
        internal static SQLiteConnection DB;

        internal static void Connection()
        {
            if (!FileIO.Exists(Path.Combine("Database", "Database.db3"))){
                //FileIO.Save(Path.Combine("Database", "Database.db3"), "");
                Directory.CreateDirectory(Path.Combine(FileIO.GetLib(), "Database"));
            }
            DB = new SQLiteConnection($"{Path.Combine(FileIO.GetLib(), "Database", "Database.db3")}");
        }
    }
}
