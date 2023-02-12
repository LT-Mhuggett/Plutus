using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TillController : ApiControllerBaseCRU<Till, TillBody, Guid, TillParameters>
    {
        protected override IRepositoryBase<Till, Guid> Repository => RepositoryWrapper.TillRepository;

        public TillController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        public override async Task<ActionResult<Till>> FindById([FromRoute] Guid id, [FromQuery] TillParameters queryParameters)
        {
            var query = Repository.FindAllByConditionQueryable(queryParameters.GetExpression());
            if (queryParameters.IncludeStore)
                query.Include(e => e.Store);
            if (queryParameters.IncludeBusiness)
                query.Include(e => e.Store.Business);

            var entity = await query.FirstOrDefaultAsync(e => e.Id.Equals(id));

            if (entity == default)
                return NotFound();

            return Ok(entity);
        }
    }
}
