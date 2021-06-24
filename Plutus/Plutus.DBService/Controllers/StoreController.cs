using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StoreController : ApiControllerReadAndUpdate<Store, string, QueryParameters<Store, string>>
    {
        protected override IRepositoryBase<Store, string> Repository => repositoryWrapper.StoreRepository;

        public StoreController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }

        /// <summary>
        /// Add <see cref="SupportService"/> to Database and attach to <see cref="EmotionDefinition"/>
        /// </summary>
        /// <param name="storeBody">Form body post data</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns></returns>
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public async Task<ActionResult<Store>> Post([FromBody] StoreBody storeBody, [FromQuery] bool IsSync = false)
        {
            if (storeBody != null)
            {
                // Validate if Bussiness exists
            }

            var store = new Store
            {
                ContactNumber = storeBody.ContactNumber,
                BussinessId = storeBody.BussinessId,
                AdLine1 = storeBody.AdLine1,
                AdLine2 = storeBody.AdLine2,
                City = storeBody.City,
                PostCode = storeBody.PostCode,
                Country = storeBody.Country
            };

            if (!TryValidateModel(store))
                return BadRequest(ModelState);

            // (Maybe) Add Bussiness

            await Repository.Create(store);
            repositoryWrapper.SetSyncState(IsSync);
            await repositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { id = store.Id }, store);
        }

        [HttpGet]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Get))]
        public Task<IEnumerable<Store>> GetStores()
        {
            return repositoryWrapper.StoreRepository.GetAll();
        }

        [HttpGet("{Id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Get))]
        public async Task<Store> GetStore(string Id)
        {
            var store = await Repository.FindById(Id);

            if (store == null)
            {
                //return NotFound();
            }

            return store;
        }

       /* [HttpPut("{Id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Put))]
        public async Task<IActionResult> Edit(string Id, Store store)
        {
            if (Id != store.Id)
            {
                return BadRequest();
            }

            

        }*/
    }
}
