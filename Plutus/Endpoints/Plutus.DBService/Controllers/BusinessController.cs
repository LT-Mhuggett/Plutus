using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Repository.Extensions;

namespace Plutus.DBService.Controllers
{
    // 

    [ApiController]
    [Route("api/[controller]")]
    public class BusinessController : ApiControllerBaseCRU<Business, string, BusinessParameters>
    {
        protected override IRepositoryBase<Business, string> Repository => repositoryWrapper.BusinessRepository;

        public BusinessController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }

        /*[HttpGet("Home")]
        [ProducesResponseType(200)]
        public virtual ActionResult<IEnumerable<Business>> Home([FromQuery] BusinessParameters queryParameters)
        {
            var Businesses = PagedList<Business>.ToPagedList(Repository.GetAllQueryable().Include(b => b.Employees).Where(queryParameters.GetExpression()).OrderBy(e => e.CreatedAt), queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            
            
            return Ok(Businesses);
        }*/
    }
}
