using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoppperToCSV
{
    class Program
    {

        static void Main(string[] args)
        {
            var FileDataByFolder = new Dictionary<string, List<string>>();
            Console.WriteLine("Enter top level directory of the copper backup: ");
            var topDirectoryPath = Console.ReadLine();
            var folders = Directory.GetDirectories(topDirectoryPath);

            foreach (var folder in folders)
            {
                var folderName = new DirectoryInfo(folder).Name;
                FileDataByFolder.Add(folderName, new List<string>());
                var files = Directory.GetFiles(folder);
                foreach (var file in files)
                {
                    if (folderName == "Items" || folderName == "Transactions" || folderName == "TransactionsDrafts" || folderName == "TransactionsRefunds")
                    {
                        if (FileDataByFolder[folderName].Count == 0)
                        {
                            var header = "ID&";
                            var text = File.ReadAllText(file);
                            var splitText1 = text.Split('&');
                            foreach (var textToSplit in splitText1)
                            {
                                var splitText2 = textToSplit.Split('=');
                                header += $"{splitText2[0]}&";
                            }
                            FileDataByFolder[folderName].Add(header);
                        }
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file);
                            var finalText = $"{fileName}&";
                            var text = File.ReadAllText(file);
                            var splitText1 = text.Split('&');
                            foreach (var textToSplit in splitText1)
                            {
                                try
                                {
                                    var splitText2 = textToSplit.Split('=');
                                    finalText += $"{splitText2[1]}&";
                                }
                                catch (Exception)
                                {
                                    finalText += $"{textToSplit}&";
                                }
                            }
                            FileDataByFolder[folderName].Add(finalText);
                        }
                    }
                    else if (folderName == "Logs")
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        FileDataByFolder[folderName].Add($"{fileName}&{File.ReadAllText(file)}");
                    }
                    else
                    { 
                        {
                            var header = "";
                            var text = File.ReadAllText(file);
                            var splitText1 = text.Split('&');
                            foreach (var textToSplit in splitText1)
                            {
                                var splitText2 = textToSplit.Split('=');
                                header += $"{splitText2[0]}&";
                            }
                            FileDataByFolder[folderName].Add(header);
                        }
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file);
                            var finalText = "";
                            var text = File.ReadAllText(file);
                            var splitText1 = text.Split('&');
                            foreach (var textToSplit in splitText1)
                            {
                                try
                                {
                                    var splitText2 = textToSplit.Split('=');
                                    finalText += $"{splitText2[1]}&";
                                }
                                catch(Exception)
                                {
                                    finalText += $"{textToSplit}&";
                                }
                            }
                            FileDataByFolder[folderName].Add(finalText);
                        }
                        FileDataByFolder[folderName].Add(File.ReadAllText(file));
                    }
                }
            }

            Console.WriteLine("Eneter where to place the CSV files: ");
            var destPath = Console.ReadLine();
            foreach (var datum in FileDataByFolder)
            {
                using (StreamWriter outputFile = new StreamWriter(Path.Combine(destPath, $"{datum.Key}.csv")))
                {
                    foreach (var line in datum.Value)
                        outputFile.WriteLine(line);
                }
            }
            Console.ReadKey();
        }
    }
}
