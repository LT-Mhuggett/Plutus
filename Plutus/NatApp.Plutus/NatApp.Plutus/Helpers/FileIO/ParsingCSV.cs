using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Helpers.FileIO
{
    public static class ParsingCSV
    {
        public static async Task<string[]> GetDataFromCSV(Stream stream, char delimiter = ',')
        {
            using (var strReader = new StreamReader(stream))
            {
                var @string = await strReader.ReadToEndAsync();
                return @string.Split(delimiter);
            }
        }
    }
}
