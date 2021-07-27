using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaxController : ControllerBase
    {
        protected readonly IRepositoryWrapper repositoryWrapper;
        protected virtual ICompositeRepositoryBase<Tax, Guid, string> Repository => repositoryWrapper.TaxRepository;

        protected readonly IHttpContextAccessor httpContextAccessor;

        public TaxController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor)
        {
            this.httpContextAccessor = httpContextAccessor;
            this.repositoryWrapper = repositoryWrapper;

            /*var objectId = this.httpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
            repositoryWrapper.SetCurrentUser(objectId);*/
        }

        /// <summary>
        /// Add <see cref="Tax"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="TaxBody">Form body post data</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns></returns>
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public async Task<ActionResult<Tax>> Post([FromBody] TaxBody taxBody, [FromQuery] bool IsSync = false)
        {

            var tax = new Tax
            {
                IdTwo = taxBody.BussinessId,
                Name = taxBody.Name,
                Rate = taxBody.Rate
            };

            if (!TryValidateModel(tax))
                return BadRequest(ModelState);

            await Repository.Create(tax);
            repositoryWrapper.SetSyncState(IsSync);
            await repositoryWrapper.SaveAsync();
            //return item;
            return CreatedAtAction("FindById", new { idOne = tax.IdOne, idTwo = tax.IdTwo }, tax);
        }

        [HttpGet("{idOne}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
            nameof(DefaultApiConventions.Find))]

        public virtual async Task<ActionResult<Tax>> FindById([FromRoute] Guid idOne, [FromHeader] string idTwo)
        {
            var tax = await Repository.FindById(idOne, idTwo);
            if (tax == default)
            {
                return NotFound();
            }

            return Ok(tax);
        }

        [HttpGet("Index")]
        [ProducesResponseType(200)]
        public virtual ActionResult<IEnumerable<Tax>> Index([FromQuery] TaxParameters queryParameters)
        {
            if (!queryParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created min date");

            var entities = PagedList<Tax>.ToPagedList(Repository.FindAllByConditionQueryable(queryParameters.GetExpression()).OrderBy(e => e.CreatedAt),
                                                          queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { queryParameters.MinCreatedDate, queryParameters.MaxCreatedDate }));
            return Ok(entities);
        }

        [HttpPatch("{idOne},{idTwo}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Update))]
        public async virtual Task<ActionResult<Tax>> Patch([FromRoute] Guid idOne, [FromRoute] string idTwo, [FromBody] JsonPatchDocument<Tax> patchDocument, [FromQuery] bool IsSync = false)
        {
            if (patchDocument == default)
                return BadRequest();

            var tax = await Repository.FindById(idOne, idTwo);
            if (tax == default)
                return NotFound();

            patchDocument.ApplyTo(tax, ModelState);

            if (!TryValidateModel(tax))
                return BadRequest(ModelState);

            Repository.SetState(tax);

            try
            {
                repositoryWrapper.SetSyncState(IsSync);
                await repositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(idOne, idTwo))
                    return NotFound();
            }

            return NoContent();
        }
    }
}
