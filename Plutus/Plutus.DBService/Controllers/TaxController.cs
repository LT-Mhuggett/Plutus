using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaxController : ApiControllerBaseCRU<Tax, int, TaxParameters>
    {
        protected override IRepositoryBase<Tax, int> Repository => repositoryWrapper.TaxRepository;

        public TaxController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
