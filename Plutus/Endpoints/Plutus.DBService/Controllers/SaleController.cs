using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Reports;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Data;
using System.Linq;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SaleController : ApiControllerBaseCR<Sale, SaleBody, string, SaleParameters>
    {
        protected override IRepositoryBase<Sale, string> Repository => RepositoryWrapper.SaleRepository;

        public SaleController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        [HttpGet]
        [Authorize(Actions.ReadThings)]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Get))]
        public FileResult SalesReport([FromQuery] SaleParameters queryParameters, [FromQuery(Name = "minDate")] DateTime startDate, [FromQuery(Name = "maxDate")] DateTime endDate)
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
            var dailySalesSummaries = new SalesReport().fetchdailySalesSummaries(currentDate, endDate, RepositoryWrapper.PaymentMethodRepository.FindAll(), data);

            // Generate and get File Stream for Excel File containing Sales Breakdowns and Sales Summaries
            var stream = new SalesReport().fetchSalesReportStream(dataSet, dailySalesSummaries, salesBreakdowns);

            string fileName = $"{startDate:yyyy-MM-dd}-{endDate:yyyy-MM-dd}-SalesReport";
            string fileType = "application/vnd.ms-excel";

            return File(stream.ToArray(), fileType, fileName);
        }
    }
}
