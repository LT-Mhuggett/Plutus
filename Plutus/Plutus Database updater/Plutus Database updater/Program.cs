using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Plutus_Database_updater
{
    static class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Where is Sales Data CSV");
            var consoleIn = Console.ReadLine();
            var salesData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    SaleId = x[0],
                    Total = x[1]
                }).ToList();
            Console.WriteLine("Where is Trans Data CSV");
            consoleIn = Console.ReadLine();
            var transData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    TransId = x[0],
                    ItemId = x[1],
                    SaleId = x[2],
                    Amount = x[3],
                    ItemCostExPrice = x[4],
                    ItemCostPrice = x[5],
                    CheckoutItemChangeId = x[6],
                }).ToList();
            Console.WriteLine("Where is Items Data CSV");
            consoleIn = Console.ReadLine();
            var itemsData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    ItemId = x[0],
                    Name = x[1],
                    VatId = x[8]
                }).ToList();
            Console.WriteLine("Where is CheckoutItemChangeModel Data CSV");
            consoleIn = Console.ReadLine();
            var checkoutItemChangeData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    CheckoutItemChangeId = x[0],
                    ItemId = x[1],
                    Price = x[2],
                    ExPrice = x[3],
                }).ToList();
            Console.WriteLine("Where is Notes Data CSV");
            consoleIn = Console.ReadLine();
            var notesData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    NoteId = x[0],
                    Note = x[1]
                }).ToList();
            Console.WriteLine("Where is NotesSale Data CSV");
            consoleIn = Console.ReadLine();
            var notesSaleData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    SaleId = x[0],
                    NoteId = x[1]
                }).ToList();
            Console.WriteLine("Where is Tax Data CSV");
            consoleIn = Console.ReadLine();
            var taxData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    TaxId = x[0],
                    Rate = x[2]
                }).ToList();
            Console.WriteLine("Where is Discounts Data CSV");
            consoleIn = Console.ReadLine();
            var discountsData = File.ReadAllLines(consoleIn)
                .Skip(1)
                .Select(x => x.Split(';'))
                .Select(x => new
                {
                    DiscountId = x[0],
                    Name = x[1],
                    Type = x[5],
                    Amount = x[6]
                }).ToList();

            List<Tuple<string, decimal>> salesExTotal = new List<Tuple<string, decimal>>();
            var salesExTotalTemp = 0m;
            var salesTotalTemp = 0m;
            var transaction_Discounts = new[] { new { TransId = "", DiscountId = "" } }.ToList();
            transaction_Discounts.Clear();

            foreach (var saleDatum in salesData)
            {
                decimal saleExTotal = 0m;
                var noteSales = notesSaleData.Where(nS => nS.SaleId.Equals(saleDatum.SaleId)).ToList();
                if (noteSales.Count != 0)
                {
                    foreach (var noteSale in noteSales)
                    {
                        var note = notesData.First(n => n.NoteId.Equals(noteSale.NoteId));
                        string tempValue = "";
                        foreach (var @char in note.Note.Reverse())
                        {
                            if (@char == '-')
                                break;
                            else if (@char == '.' || Char.IsDigit(@char))
                                tempValue += @char;
                        }
                        tempValue = new string(tempValue.Reverse().ToArray());
                        var value = decimal.Parse(tempValue);
                        var discount = new { DiscountId = "", Name = "", Type = "", Amount = "" };
                        foreach (var discountDatum in discountsData)
                        {
                            if (note.Note.Contains(discountDatum.Name) && !string.IsNullOrEmpty(discountDatum.Name))
                            {
                                discount = discountDatum;
                                break;
                            }
                        }
                        saleExTotal -= value;

                        var transSet = new[] { new { TransId = "", Price = "" } }.ToList();
                        transSet.Clear();

                        foreach (var transDatum in transData.Where(t => t.SaleId.Equals(saleDatum.SaleId)))
                        {
                            if (discount.Type == 0.ToString())
                            {
                                var numberOfDiscounts = value / decimal.Parse(discount.Amount);
                                if (itemsData.Any(i => i.ItemId.Equals(transDatum.ItemId) && i.VatId.Equals(3.ToString())) && numberOfDiscounts != 0)
                                {
                                    transaction_Discounts.Add(new { transDatum.TransId, discount.DiscountId });
                                    numberOfDiscounts--;
                                }
                            }
                            else
                            {
                                var noteSplit = note.Note.Split(',');
                                if (noteSplit.Length == 2)
                                    if (itemsData.Any(i => i.Name.Equals(noteSplit[1])))
                                    {
                                        transaction_Discounts.Add(new { transDatum.TransId, discount.DiscountId });
                                        continue;
                                    }
                                if (string.IsNullOrEmpty(transDatum.CheckoutItemChangeId))
                                    transSet.Add(new { transDatum.TransId, Price = transDatum.ItemCostPrice });
                                else
                                    transSet.Add(new { transDatum.TransId, checkoutItemChangeData.First(c => c.CheckoutItemChangeId == transDatum.CheckoutItemChangeId).Price });
                            }
                        }

                        var matches = from subSet in transSet.SubSetsOff()
                                      where subSet.Sum(t => decimal.Parse(t.Price)) * decimal.Parse(discount.Amount) == value
                                      select subSet;
                        foreach (var match in matches)
                        {
                            foreach (var tran in match)
                                transaction_Discounts.Add(new { tran.TransId, discount.DiscountId });
                            break;
                        }
                    }
                }
                foreach (var transDatum in transData.Where(t => t.SaleId.Equals(saleDatum.SaleId)))
                {
                    if (string.IsNullOrEmpty(transDatum.CheckoutItemChangeId))
                    {
                        var tempTransTotal = decimal.Parse(string.IsNullOrEmpty(transDatum.ItemCostExPrice) && transDatum.ItemCostExPrice == 0.ToString() ? transDatum.ItemCostPrice : transDatum.ItemCostExPrice) * decimal.Parse(transDatum.Amount);
                        if (tempTransTotal == 0)
                            tempTransTotal += .0417m;
                        saleExTotal += tempTransTotal;
                    }
                    else
                    {
                        var checkoutItemChange = checkoutItemChangeData.First(c => c.CheckoutItemChangeId == transDatum.CheckoutItemChangeId);
                        decimal tempTransTotal;
                        if (checkoutItemChange.ExPrice != null)
                            tempTransTotal = decimal.Parse(checkoutItemChange.ExPrice);
                        else
                            tempTransTotal = decimal.Parse(checkoutItemChange.Price);
                        if (tempTransTotal == 0m)
                            tempTransTotal += .0417m;
                        saleExTotal += tempTransTotal;
                    }
                }

                salesExTotal.Add(Tuple.Create(saleDatum.SaleId, saleExTotal));
                salesExTotalTemp += saleExTotal;
                salesTotalTemp += decimal.Parse(saleDatum.Total);
            }
            var jsonData = JsonConvert.SerializeObject(salesExTotal);
            /*var debugFolder = 
            var debugFile = await debugFolder.CreateFileAsync("dumpForItemsAfterConversion.txt", CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteTextAsync(debugFile, Newtonsoft.Json.JsonConvert.SerializeObject(itemQueue));*/
            return;
        }

        public static IEnumerable<IEnumerable<dynamic>> SubSetsOff<dynamic>(this IEnumerable<dynamic> soruce)
        {
            if (!soruce.Any())
                return Enumerable.Repeat(Enumerable.Empty<dynamic>(), 1);

            var element = soruce.Take(1);

            var haveNots = SubSetsOff(soruce.Skip(1));

            var haves = haveNots.Select(set => element.Concat(set));

            return haves.Concat(haveNots);
        }
    }
}
