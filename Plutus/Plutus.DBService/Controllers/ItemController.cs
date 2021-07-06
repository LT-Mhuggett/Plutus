using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ItemController : ControllerBase
    {
        protected readonly IRepositoryWrapper repositoryWrapper;
        protected virtual ICompositeRepositoryBase<Item, string, string> Repository => repositoryWrapper.ItemRepository;

        public ItemController(IRepositoryWrapper repositoryWrapper)
        {
            this.repositoryWrapper = repositoryWrapper;
        }

        /// <summary>
        /// Add <see cref="Item"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="ItemBody">Form body post data</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns></returns>
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public async Task<ActionResult<Item>> Post([FromBody] ItemBody itemBody, [FromQuery] bool IsSync = false)
        {
    
            var item = new Item
            {
                IdTwo = itemBody.BussinessId,
                Name = itemBody.Name,
                Brand = itemBody.Brand,
                Desc = itemBody.Desc,
                Cost = itemBody.Cost,
                ExPrice = itemBody.ExPrice,
                Price = itemBody.Price,
                Image = itemBody.Image,
                Amount = itemBody.Amount,
                TaxId = itemBody.VatId,
                CatId = itemBody.CatId
            };

            if (!TryValidateModel(item))
                return BadRequest(ModelState);
     
            await Repository.Create(item);
            repositoryWrapper.SetSyncState(IsSync);
            await repositoryWrapper.SaveAsync();
            //return item;
            return CreatedAtAction("FindById", new { idOne = item.IdOne, idTwo = item.IdTwo  }, item);
        }

        [HttpGet("{idOne}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
            nameof(DefaultApiConventions.Find))]

        public virtual async Task<ActionResult<Item>> FindById([FromRoute] string idOne, [FromHeader] string idTwo)
        {
            var item = await Repository.FindById(idOne, idTwo);
            if (item == default)
            {
                return NotFound();
            }

            return Ok(item);
        }

        [HttpGet("Index")]
        [ProducesResponseType(200)]
        public virtual ActionResult<IEnumerable<Item>> Index([FromQuery] ItemParameters queryParameters)
        {
            if (!queryParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created min date");

            var entities = PagedList<Item>.ToPagedList(Repository.FindAllByConditionQueryable(queryParameters.GetExpression()).OrderBy(e => e.CreatedAt),
                                                          queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { queryParameters.MinCreatedDate, queryParameters.MaxCreatedDate }));
            return Ok(entities);
        }

        [HttpPatch("{idOne},{idTwo}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Update))]
        public async virtual Task<ActionResult<Item>> Patch([FromRoute] string idOne, [FromRoute] string idTwo, [FromBody] JsonPatchDocument<Item> patchDocument, [FromQuery] bool IsSync = false)
        {
            if (patchDocument == default)
                return BadRequest();

            var item = await Repository.FindById(idOne, idTwo);
            if (item == default)
                return NotFound();

            patchDocument.ApplyTo(item, ModelState);

            if (!TryValidateModel(item))
                return BadRequest(ModelState);

            Repository.SetState(item);

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
