using System;
using System.Collections.Generic;
using System.Text;
using Syncfusion.Pdf;
using Xamarin.Forms;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;
using System.IO;
using Plutus.Helpers.Interface;
using Plutus.Models;
using Syncfusion.Pdf.Lists;
using Syncfusion.Pdf.Grid;
using System.Diagnostics;

namespace Plutus.Helpers
{
    internal class PDFCreator
    {
        private PdfDocument doc = new PdfDocument();

        private PdfGrid grid;
        //Minus padding
        private const float WIDTH = 221;
        private float PdfGridHeight;

        public PDFCreator(StoreModel store, Stream image, SaleModel sale, decimal change)
        {
            //Create pdfgrid
            grid = new PdfGrid();

            //Add columns to grid
            grid.Columns.Add(3);

            //Set collunm width
            grid.Style.CellPadding = new PdfPaddings(2, 2, 2, 2);
            
            grid.Columns[0].Width = (WIDTH / 5)*2;
            grid.Columns[1].Width = (WIDTH / 5)*2;
            grid.Columns[2].Width = WIDTH / 5;

            PdfGridCellStyle cellStyle = new PdfGridCellStyle
            {
                Borders = new PdfBorders(),
                Font = new PdfStandardFont(PdfFontFamily.Courier, 10)
            };
            cellStyle.Borders.All = new PdfPen(Syncfusion.Drawing.Color.White);

            //Store header
            var storeName = string.Format("Thank you for shopping at\n{0}\n", store.StoreName);

            var storeAddress = store.AdLine1 != null ? $"{store.AdLine1}\n{store.City}\n{store.PostCode}\n{store.Country}"
                : store.FullAddress;

            var sf = new PdfStringFormat(PdfTextAlignment.Center);

            var index = 0;

            //Add header
            if (image != null)
            {
                grid.Headers.Add(1);

                grid.Headers[index].Style = cellStyle;
                var bitmap = new PdfBitmap(image);
                grid.Headers[index].Cells[0].ColumnSpan = 3;
                grid.Headers[index].Cells[0].Style = cellStyle;
                grid.Headers[index].Cells[0].Style.BackgroundImage = bitmap;
                index++;
            }
            grid.Headers.Add(2);

            grid.Headers[index].Style = cellStyle;
            grid.Headers[index].Cells[0].ColumnSpan = 3;
            grid.Headers[index].Cells[0].Value = storeName + storeAddress;
            grid.Headers[index].Cells[0].StringFormat = sf;
            grid.Headers[index].Cells[0].Style = cellStyle;
            index++;
            grid.Headers[index].Style = cellStyle;

            //Grid headers
            string[] headers = new string[] { "Item Name", "Item ID", "Price" };
            {
                int i = 0;
                foreach (var head in headers)
                {
                    grid.Headers[index].Cells[i].Value = head;
                    grid.Headers[index].Cells[i].Style = cellStyle;
                    i++;
                }
            }

            //Populate item data
            foreach(var item in sale.Transactions)
            {
                for(var i = 1; i <= item.Amount; i++)
                {
                    var gridRow = grid.Rows.Add();
                    gridRow.Cells[0].Value = item.Item.Name;
                    gridRow.Cells[0].Style = cellStyle;

                    gridRow.Cells[1].Value = item.ItemId;
                    gridRow.Cells[1].Style = cellStyle;

                    gridRow.Cells[2].Value = $"{Math.Round(item.Item.Price, 2, MidpointRounding.AwayFromZero)}";
                    gridRow.Cells[2].Style = cellStyle;
                }
            }
            
            //Populate returns data
            if(sale.Refunds.Count > 0)
            {
                {
                    PdfGridRow gridRow = grid.Rows.Add();
                    gridRow.Cells[0].Value = "Returns";
                    gridRow.Cells[0].ColumnSpan = 3;
                    gridRow.Cells[0].Style = cellStyle;
                }

                //Returns headers
                {
                    int i = 0;
                    PdfGridRow gridRow = grid.Rows.Add();
                    foreach (var head in headers)
                    {
                        gridRow.Cells[i].Value = head;
                        gridRow.Cells[i].Style = cellStyle;
                        i++;
                    }
                }

                foreach(var item in sale.Refunds)
                {
                    PdfGridRow gridRow = grid.Rows.Add();
                    gridRow.Cells[0].Value = item.Id;
                    gridRow.Cells[0].Style = cellStyle;

                    gridRow.Cells[1].Value = item.Item.Name;
                    gridRow.Cells[1].Style = cellStyle;

                    gridRow.Cells[2].Value = item.Item.Price;
                }
            }

            if (sale.Notes.Count > 0)
            {
                {
                    PdfGridRow gridRow = grid.Rows.Add();
                    gridRow.Cells[0].Value = "Notes";
                    gridRow.Cells[0].ColumnSpan = 3;
                    gridRow.Cells[0].Style = cellStyle;
                }
                foreach (var item in sale.Notes)
                {
                    PdfGridRow gridRow = grid.Rows.Add();
                    gridRow.Cells[0].ColumnSpan = 3;
                    gridRow.Cells[0].Value = item.Note.Note;
                    gridRow.Cells[0].Style = cellStyle;
                }
            }

            //Set up footer
            {
                {
                    PdfGridRow gridRow = grid.Rows.Add();

                    gridRow.Cells[0].Style = cellStyle;

                    gridRow.Cells[1].Value = "Total:";
                    gridRow.Cells[1].Style = cellStyle;

                    gridRow.Cells[2].Value = $"{Math.Round(sale.Total, 2, MidpointRounding.AwayFromZero)}";
                    gridRow.Cells[2].Style = cellStyle;
                }

                foreach(var item in sale.PaySales)
                {
                    PdfGridRow gridRow = grid.Rows.Add();

                    gridRow.Cells[0].Style = cellStyle;

                    gridRow.Cells[1].Value = item.PayMethod.Name + " Used:";
                    gridRow.Cells[1].Style = cellStyle;

                    gridRow.Cells[2].Value = $"{Math.Round(item.Amount, 2, MidpointRounding.AwayFromZero)}";
                    gridRow.Cells[2].Style = cellStyle;
                }

                if(change!=0.00m)
                {
                    PdfGridRow gridRow = grid.Rows.Add();

                    gridRow.Cells[0].Style = cellStyle;

                    gridRow.Cells[1].Value = "Change:";
                    gridRow.Cells[1].Style = cellStyle;

                    gridRow.Cells[2].Value = $"{Math.Round(change, 2, MidpointRounding.AwayFromZero)}";
                    gridRow.Cells[2].Style = cellStyle;
                }
            }
            //Calculate grid height
            float gridHeight = CalculateGridHeight(grid);

            doc.PageSettings.Margins.All = 10;

            //Set page width and height
            doc.PageSettings.Width = WIDTH + (doc.PageSettings.Margins.Left*2);
            doc.PageSettings.Height = gridHeight + (doc.PageSettings.Margins.Top*2);

            PdfPage page = doc.Pages.Add();

            grid.Draw(page, PointF.Empty);            

            MemoryStream memoryStream2 = new MemoryStream();

            doc.Save(memoryStream2);

            doc.Close();

            DependencyService.Get<ISavePDF>().Save("test.pdf", "application/pdf", memoryStream2);
        }

        public float CalculateGridHeight(PdfGrid pdfGrid)
        {
            //Create a new document.
            PdfDocument doc2 = new PdfDocument();

            //Add a page
            PdfPage page = doc2.Pages.Add();

            //Create Pdf graphics for the page
            PdfGraphics graphics = page.Graphics;

            pdfGrid.EndPageLayout += PdfGrid_EndPageLayout;

            //Draw the table
            try
            {
                PdfLayoutResult result = pdfGrid.Draw(page, new PointF(0, 0));
            }
            catch(Exception e)
            {
                Debug.WriteLine("PDFException: " + e);
            }

            //Close the document
            doc2.Close();
            return PdfGridHeight;
        }

        private void PdfGrid_EndPageLayout(object sender, EndPageLayoutEventArgs e)
        {
            PdfGridHeight += e.Result.Bounds.Height;
        }
    }
}
