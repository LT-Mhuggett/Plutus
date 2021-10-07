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
    public class StockController : ControllerBase
    {
        protected readonly IRepositoryWrapper RepositoryWrapper;
        protected virtual ITriCompositeRepositoryBase<Stock, string, string, string> Repository => RepositoryWrapper.StockRepository;

        protected readonly IHttpContextAccessor HttpContextAccessor;

        public StockController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor)
        {
            HttpContextAccessor = httpContextAccessor;
            RepositoryWrapper = repositoryWrapper;

            /*var objectId = this.HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
            RepositoryWrapper.SetCurrentUser(objectId);*/
        }

        /// <summary>
        /// Add <see cref="Stock"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="ItemBody">Form body post data</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns></returns>
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public async Task<ActionResult<Stock>> Post([FromBody] StockBody stockBody, [FromQuery] bool IsSync = false)
        {
            var stock = stockBody.GenerateEntity();

            if (!TryValidateModel(stock))
                return BadRequest(ModelState);

            await Repository.Create(stock);
            RepositoryWrapper.SetSyncState(IsSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { idOne = stock.IdOne, idTwo = stock.IdTwo, idThree = stock.IdThree }, stock);
        }

        [HttpGet("{idOne}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
            nameof(DefaultApiConventions.Find))]

        public virtual async Task<ActionResult<Stock>> FindById([FromRoute] string idOne, [FromHeader] string idTwo, [FromHeader] string idThree)
        {
            var stock = await Repository.FindById(idOne, idTwo, idThree);
            if (stock == default)
            {
                return NotFound();
            }

            return Ok(stock);
        }

        [HttpGet("Index")]
        [ProducesResponseType(200)]
        public virtual ActionResult<IEnumerable<Stock>> Index([FromQuery] StockParameters stockParameters)
        {
            if (!stockParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created min date");

            var entities = PagedList<Stock>.ToPagedList(Repository.FindAllByConditionQueryable(stockParameters.GetExpression()).OrderBy(e => e.CreatedAt),
                                                          stockParameters.PageNumber, stockParameters.PageSize, stockParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { stockParameters.MinCreatedDate, stockParameters.MaxCreatedDate }));
            return Ok(entities);
        }

        [HttpPatch("{idOne},{idTwo},{idThree}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Update))]
        public async virtual Task<ActionResult<Stock>> Patch([FromRoute] string idOne, [FromRoute] string idTwo, [FromRoute] string idThree, [FromBody] JsonPatchDocument<Stock> patchDocument, [FromQuery] bool IsSync = false)
        {
            if (patchDocument == default)
                return BadRequest();

            var stock = await Repository.FindById(idOne, idTwo, idThree);
            if (stock == default)
                return NotFound();

            patchDocument.ApplyTo(stock, ModelState);

            if (!TryValidateModel(stock))
                return BadRequest(ModelState);

            Repository.SetState(stock);

            try
            {
                RepositoryWrapper.SetSyncState(IsSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(idOne, idTwo, idThree))
                    return NotFound();
            }

            return NoContent();
        }
    }
}
