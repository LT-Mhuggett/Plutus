using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BussinessController : ApiControllerBaseRU<Bussiness, string, QueryParameters<Bussiness, string>>
    {
        protected override IRepositoryBase<Bussiness, string> Repository => repositoryWrapper.BussinessRepository;

        public BussinessController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
