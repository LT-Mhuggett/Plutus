using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TillController : ApiControllerBaseCRU<Till, string, TillParameters>
    {
        protected override IRepositoryBase<Till, string> Repository => repositoryWrapper.TillRepository;

        public TillController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
