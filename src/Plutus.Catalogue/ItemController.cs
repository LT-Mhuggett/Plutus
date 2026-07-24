using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ItemController : CompositeApiControllerBaseCRU<Item, ItemBody, string, Guid, ItemParameters>
    {
        protected override ICompositeRepositoryBase<Item, string, Guid> Repository => RepositoryWrapper.ItemRepository;

        public ItemController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        /// <summary>
        /// VAT guardrail (VAT-Investigation plan §5.4, 2026-07-23): an item's inc/ex prices
        /// must be consistent with its tax band (|Price − ExPrice × rate| ≤ 2p). Free-typed
        /// ex-prices corrupted the live catalogue (47 known items, e.g. a £7.99 item with a
        /// £799.00 ex-price) and with them every downstream VAT figure. Existing bad rows are
        /// deliberately untouched — this blocks NEW damage only. Sync writes (isSync=true)
        /// are exempt so legacy tills replaying historic records don't dead-letter.
        /// </summary>
        private async Task<string> BandInconsistency(Guid businessId, int taxId, decimal price, decimal exPrice)
        {
            var tax = await RepositoryWrapper.TaxRepository.FindById(taxId, businessId);
            if (tax == null) return $"Unknown tax band {taxId}.";

            var expected = Math.Round(exPrice * (decimal)tax.Rate, 2);
            if (Math.Abs(price - expected) > 0.02m)
                return $"Price £{price:0.00} does not match ex-VAT £{exPrice:0.00} at band '{tax.Name}' " +
                       $"(expected ≈ £{expected:0.00}). Correct the ex-VAT price or pick the right band.";
            return null;
        }

        public override async Task<ActionResult<Item>> Post([FromHeader] Guid businessId, [FromBody] ItemBody body, [FromQuery] bool isSync = false)
        {
            if (!isSync && body != null)
            {
                var problem = await BandInconsistency(body.BusinessId == Guid.Empty ? businessId : body.BusinessId, body.TaxId, body.Price, body.ExPrice);
                if (problem != null) return BadRequest(problem);
            }
            return await base.Post(businessId, body, isSync);
        }

        public override async Task<ActionResult<Item>> Put([FromRoute] string id1, [FromHeader] Guid businessId, [FromBody] Item entity, [FromQuery] bool isSync = false)
        {
            if (!isSync && entity != null)
            {
                var problem = await BandInconsistency(businessId, entity.TaxId, entity.Price, entity.ExPrice);
                if (problem != null) return BadRequest(problem);
            }
            return await base.Put(id1, businessId, entity, isSync);
        }
    }
}
