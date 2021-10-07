using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BusinessController : ApiControllerBaseCRU<Business, BusinessBody, string, BusinessParameters>
    {
        protected override IRepositoryBase<Business, string> Repository => RepositoryWrapper.BusinessRepository;

        public BusinessController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        [Authorize(Actions.ReadThings)]
        [HttpGet("Index")]
        [ProducesResponseType(200)]
        public override ActionResult<IEnumerable<Business>> Index([FromQuery] BusinessParameters businessParameters)
        {
            if (string.IsNullOrEmpty(businessParameters.EmployeeObjectId))
                return base.Index(businessParameters);
            if (!businessParameters.ValidEmployeeObjectId)
            {
            }

            var businesses = PagedList<Business>.ToPagedList(Repository.FindAllByConditionQueryable(businessParameters.GetExpression()).Include(b => b.Employees.Where(e => e.ObjectId.Equals(businessParameters.EmployeeObjectId))).OrderBy(b => b.CreatedAt), businessParameters.PageNumber, businessParameters.PageSize, businessParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(businesses.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { businessParameters.MinCreatedDate, businessParameters.MaxCreatedDate }));
            return Ok(businesses);
        }
    }
}
