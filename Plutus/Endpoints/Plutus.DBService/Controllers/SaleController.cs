using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Reports;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SaleController : ApiControllerBaseCR<Sale, SaleBody, Guid, SaleParameters>
    {
        protected override IRepositoryBase<Sale, Guid> Repository => RepositoryWrapper.SaleRepository;

        public SaleController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("SaleReport")]        
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Get))]
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
            var dailySalesSummaries = new SalesReport().fetchdailySalesSummaries(currentDate, endDate, RepositoryWrapper.PaymentMethodRepository.GetAllQueryable(), data);

            // Generate and get File Stream for Excel File containing Sales Breakdowns and Sales Summaries
            var stream = new SalesReport().fetchSalesReportStream(dataSet, dailySalesSummaries, salesBreakdowns);

            string fileName = $"{startDate:yyyy-MM-dd}-{endDate:yyyy-MM-dd}-SalesReport";
            string fileType = "application/vnd.ms-excel";

            return File(stream.ToArray(), fileType, fileName);
        }
    }
}
