using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthActionController : ApiControllerBaseCRUD<AuthActions, AuthActionBody, int, AuthActionsParameters>
    {
        protected override IRepositoryBase<AuthActions, int> Repository => RepositoryWrapper.AuthActionRepository;

        public AuthActionController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }

        public override ActionResult<IEnumerable<AuthActions>> Index([FromQuery] AuthActionsParameters authActionsParameters)
        {
            if(authActionsParameters.ValidEmployeeObjectId || string.IsNullOrEmpty(authActionsParameters.BusinessId) || string.IsNullOrEmpty(authActionsParameters.EmployeeObjectId))
                return BadRequest();

            var authActions = PagedList<AuthActions>.ToPagedList(Repository.GetAllQueryable()
                                                                           .Include(aA => aA.EmpAuths)
                                                                               .ThenInclude(eA => eA.EmpId.Equals(authActionsParameters.EmployeeObjectId))
                                                                           .Include(aA => aA.AuthActionAPIMappings)
                                                                               .ThenInclude(aAApiMs => aAApiMs.Role.BusinessId.Equals(authActionsParameters.BusinessId))
                                                                           .OrderBy(aA => aA.CreatedAt),
                                                                 authActionsParameters.PageNumber,
                                                                 authActionsParameters.PageSize,
                                                                 authActionsParameters.IgnorePagination);

            
            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(authActions.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { authActionsParameters.MinCreatedDate, authActionsParameters.MaxCreatedDate }));
            return Ok(authActions);
        }
    }
}
