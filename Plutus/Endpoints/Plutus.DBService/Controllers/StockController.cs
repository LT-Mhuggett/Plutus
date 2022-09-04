using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using Newtonsoft.Json;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Entities.Models.Interface;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StockController : ControllerBase
    {
        protected readonly IRepositoryWrapper RepositoryWrapper;
        protected virtual ITriCompositeRepositoryBase<Stock, string, Guid, int> Repository => RepositoryWrapper.StockRepository;

        protected readonly IHttpContextAccessor HttpContextAccessor;

        public StockController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor)
        {
            HttpContextAccessor = httpContextAccessor;
            RepositoryWrapper = repositoryWrapper;

            /*var objectId = this.HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
            RepositoryWrapper.SetCurrentUser(objectId);*/

            RepositoryWrapper.SetCurrentUser(HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value);
        }

        /// <summary>
        /// Add <see cref="Stock"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="ItemBody">Form body post data</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPost]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Post))]
        public async Task<ActionResult<Stock>> Post([FromBody] StockBody stockBody, [FromQuery] bool IsSync = false)
        {
            if (!await RepositoryWrapper.BusinessRepository.Exists(stockBody.BussinessId))
                return BadRequest("Business does not Exists.");
            if (!await RepositoryWrapper.StoreRepository.Exists(stockBody.StoreId))
                return BadRequest("Store does not Exists.");
            if(!await RepositoryWrapper.ItemRepository.Exists(stockBody.ItemIdOne, stockBody.BussinessId))
                return BadRequest("Item does not Exists.");

            var stock = stockBody.GenerateEntity();

            if (!TryValidateModel(stock))
                return BadRequest(ModelState);

            await Repository.Create(stock);
            RepositoryWrapper.SetSyncState(IsSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { idOne = stock.IdOne, idTwo = stock.IdTwo, idThree = stock.IdThree }, stock);
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("{idOne}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Find))]

        public virtual async Task<ActionResult<Stock>> FindById([FromRoute] string idOne, [FromHeader] Guid businessId, [FromHeader] int storeId)
        {
            var stock = await Repository.FindById(idOne, businessId, storeId);
            if (stock == default)
            {
                return NotFound();
            }

            return Ok(stock);
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("Index")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Index))]
        public virtual ActionResult<IEnumerable<Stock>> Index([FromHeader] Guid businessId, [FromHeader] int storeId,  [FromQuery] StockParameters stockParameters)
        {
            if (!stockParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created min date");

            var entities = PagedList<Stock>.ToPagedList(Repository.FindAllByConditionQueryable(
                                                            stockParameters.GetExpression()).Where(s => s.IdTwo.Equals(businessId)
                                                                                                        && s.IdThree.Equals(storeId)).OrderBy(e => e.CreatedAt),
                                                            stockParameters.PageNumber,
                                                            stockParameters.PageSize,
                                                            stockParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { stockParameters.MinCreatedDate, stockParameters.MaxCreatedDate }));
            return Ok(entities);
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPatch("{idOne}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Patch))]
        public async virtual Task<ActionResult> Patch([FromRoute] string idOne, [FromHeader] Guid businessId, [FromHeader] int storeId, [FromBody] JsonPatchDocument<Stock> patchDocument, [FromQuery] bool IsSync = false)
        {
            if (patchDocument == default)
                return BadRequest();

            var stock = await Repository.FindById(idOne, businessId, storeId);
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
                if (!await Repository.Exists(idOne, businessId, storeId))
                    return NotFound();
            }

            return NoContent();
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPut("override/{idOne}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Put))]
        public virtual async Task<ActionResult> Put([FromRoute] string idOne, [FromHeader] Guid businessId, [FromHeader] int storeId, [FromBody] StockBody entity, [FromQuery] bool IsSync = false)
        {
            if (!EqualityComparer<string>.Default.Equals(idOne, entity.Id) &&
                !EqualityComparer<Guid>.Default.Equals(businessId, entity.BussinessId) &&
                !EqualityComparer<int>.Default.Equals(storeId, entity.StoreId))
                return BadRequest();

            if (IsSync)
            {
                var tempEntity = await Repository.FindById(idOne, businessId, storeId);
                if (tempEntity.ModifiedAt > entity.ModifiedAt)
                    return NoContent();

                Repository.SetState(tempEntity, EntityState.Detached);
            }

            Repository.SetState(entity.GenerateEntity());

            try
            {
                RepositoryWrapper.SetSyncState(IsSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(idOne, businessId, storeId))
                    return NotFound();
                throw;
            }

            return NoContent();
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPatch("UpdateQuantity/{idOne}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Patch))]
        public async Task<ActionResult<Stock>> UpdateStockValue([FromRoute] string idOne, [FromHeader] Guid businessId, [FromHeader] int storeId, [FromBody] int numberToChangeBy)
        {
            var stock = await Repository.FindById(idOne, businessId, storeId);
            if (stock == default)
                return NotFound();

            stock.Quantity += numberToChangeBy;

            Repository.SetState(stock);

            try
            {
                await RepositoryWrapper.SaveAsync();
            }
            catch(DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(idOne, businessId, storeId))
                    return NotFound();
            }
            return Ok(stock);
        }
    }
}
