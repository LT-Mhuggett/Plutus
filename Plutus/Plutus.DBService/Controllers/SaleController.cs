using Microsoft.AspNetCore.Mvc;
using Plutus.Reports;
using System;
using Plutus.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System.Data;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SaleController : ApiControllerBaseR<Sale, string, SaleParameters>
    {
        protected override IRepositoryBase<Sale, string> Repository => repositoryWrapper.SaleRepository;

        public SaleController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }

        [HttpGet]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Get))]
        public FileResult salesReport([FromQuery] SaleParameters queryParameters, [FromQuery(Name = "minDate")] DateTime startDate, [FromQuery(Name = "maxDate")] DateTime endDate)
        {
            //Check Min and Max date are viable
            var entities = Repository.FindAllByConditionQueryable(queryParameters.GetExpression());
            var dataSet = new DataSet();
            var data = entities
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Item)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.CheckoutItemChange)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Transaction_Discounts)
                        .ThenInclude(td => td.Discount)
                .Include(s => s.PaySales)
                    .ThenInclude(ps => ps.PayMethod)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Item)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Authoriser)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.CheckoutItemChange)
                .Include(s => s.Employee).ToList();

            var currentDate = startDate;

            // Fetch Sales Breakdowns
            var salesBreakdowns = new SalesReport().fetchSalesBreakdown(currentDate, endDate, data);

            // Fetch Sales Summaries
            var dailySalesSummaries = new SalesReport().fetchdailySalesSummaries(currentDate, endDate, repositoryWrapper.PaymentMethodRepository.FindAll(), data);

            // Generate and get File Stream for Excel File containing Sales Breakdowns and Sales Summaries
            var stream = new SalesReport().fetchSalesReportStream(dataSet, dailySalesSummaries, salesBreakdowns);

            string fileName = $"{startDate.ToString("yyyy-MM-dd")}-{endDate.ToString("yyyy-MM-dd")}-SalesReport";
            string fileType = "application/vnd.ms-excel";
            
            return File(stream.ToArray(), fileType, fileName);
        }
    }
}
