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
    public class StoreController : APIControllerBaseCRU<Store, string, QueryParameters<Store, string>>
    {
        protected override IRepositoryBase<Store, string> Repository => repositoryWrapper.StoreRepository;

        public StoreController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
        
        
    }
}
