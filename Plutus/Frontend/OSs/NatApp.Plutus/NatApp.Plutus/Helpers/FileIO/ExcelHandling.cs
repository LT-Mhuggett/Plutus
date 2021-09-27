using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NatApp.Plutus.Exceptions;
using NatApp.Plutus.Helpers.Enums;
using Syncfusion.Compression.Zip;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace NatApp.Plutus.Helpers.FileIO
{
    public class ExcelHandling : IDisposable
    {
        private MemoryStream _documentStream { get; set; }
        private SpreadsheetDocument _spreadsheet { get; set; }

        public ExcelHandling(SpreadsheetDocumentType docType = SpreadsheetDocumentType.Workbook)
        {
            CreateExcelDocument(docType);
        }

        /// <summary>
        /// Create the document
        /// </summary>
        /// <param name="fileName">Intended fileName</param>
        /// <param name="docType">Document Type</param>
        /// <exception cref="ConflictException">When fileName already exists</exception>
        private void CreateExcelDocument(SpreadsheetDocumentType docType)
        {
            _documentStream = new MemoryStream();
            _spreadsheet = SpreadsheetDocument.Create(_documentStream, docType);
            _spreadsheet.AddWorkbookPart();
            _spreadsheet.WorkbookPart.Workbook = new Workbook();
            _spreadsheet.WorkbookPart.Workbook.Sheets = new Sheets();
            _spreadsheet.WorkbookPart.Workbook.Save();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dateSet"></param>
        public void DataTableToWorksheet(DataSet dataSet)
        {
            foreach(DataTable table in dataSet.Tables)
            {
                var sheetPart = _spreadsheet.WorkbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                sheetPart.Worksheet = new Worksheet(sheetData);

                var sheets = _spreadsheet.WorkbookPart.Workbook.GetFirstChild<Sheets>();
                var relationshipId = _spreadsheet.WorkbookPart.GetIdOfPart(sheetPart);

                uint sheetId = 1;
                if (sheets.Elements<Sheet>().Count() > 0)
                {
                    sheetId = sheets.Elements<Sheet>().Select(s => s.SheetId.Value).Max() + 1;
                }
                
                var sheet = new Sheet() { Id = relationshipId, SheetId = sheetId, Name = table.TableName };
                sheets.Append(sheet);

                var headerRow = new Row();

                var columns = new List<string>();
                foreach(DataColumn column in table.Columns)
                {
                    columns.Add(column.ColumnName);

                    var cell = new Cell()
                    {
                        DataType = CellValues.String,
                        CellValue = new CellValue(column.ColumnName)
                    };
                    headerRow.AppendChild(cell);
                }

                sheetData.AppendChild(headerRow);

                foreach(DataRow dataRow in table.Rows)
                {
                    var newRow = new Row();
                    foreach(var column in columns)
                    {
                        Cell cell = new Cell()
                        {
                            DataType = CellValues.String,
                            CellValue = new CellValue(dataRow[column].ToString())
                        };
                        newRow.AppendChild(cell);
                    }
                    sheetData.AppendChild(newRow);
                }
                _spreadsheet.WorkbookPart.Workbook.Save();
            }
        }

        public MemoryStream Finalize()
        {
            _spreadsheet.Close();
            _documentStream.Position = 0;
            return _documentStream;
        }

        #region IDisposable Support
        private bool _disposedValue = false;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _documentStream = null;
                    _spreadsheet = null;
                }

                _disposedValue = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
