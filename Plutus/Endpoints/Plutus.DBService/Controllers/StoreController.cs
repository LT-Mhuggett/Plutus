using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StoreController : ApiControllerBaseCRU<Store, StoreBody, int, StoreParameters>
    {
        protected override IRepositoryBase<Store, int> Repository => RepositoryWrapper.StoreRepository;

        public StoreController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }
    }
}
