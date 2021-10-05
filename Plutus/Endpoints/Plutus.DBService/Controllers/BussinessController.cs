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
    public class BussinessController : ApiControllerBaseCRU<Bussiness, string, BussinessParameters>
    {
        protected override IRepositoryBase<Bussiness, string> Repository => repositoryWrapper.BussinessRepository;

        public BussinessController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }

        /*[HttpGet("Home")]
        [ProducesResponseType(200)]
        public virtual ActionResult<IEnumerable<Bussiness>> Home([FromQuery] BussinessParameters queryParameters)
        {
            var bussinesses = PagedList<Bussiness>.ToPagedList(Repository.GetAllQueryable().Include(b => b.Employees).Where(queryParameters.GetExpression()).OrderBy(e => e.CreatedAt), queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            
            
            return Ok(bussinesses);
        }*/
    }
}
