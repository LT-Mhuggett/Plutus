using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;

namespace Plutus.Reports
{
    public class SalesReport
    {
        public Task<FileResult> createExcel(DataSet dataSet, DateTime startDate, DateTime endDate)
        {
            /*using (var docHandler = new ExcelHandling())
            {
                docHandler.DataTableToWorksheet(dataSet);
                var fileStream = docHandler.Finalize();
                await DependencyService.Get<IFile>().SaveAndView(
                        $"{"SalesReports".Translate()} - {startDate.ToShortDateString()}-{endDate.ToShortDateString()}",
                        "application/vnd.ms-excel",
                        fileStream,
                        new Dictionary<string, IList<string>>() { { "Excel", new List<string>() { ".xlsx", ".xls" } } }
                        );
            }*/

            var stream = new MemoryStream();
            using (var docHandler = new ExcelHandling())
            {
                docHandler.DataTableToWorksheet(dataSet);
                stream = docHandler.Finalize();
              
            }

            string fileName = "SalesReport.xlsx";
            string fileType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            stream.Position = 0;
            return File(stream.ToArray(), fileType, fileName);
        } 
    }
}
