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
    public class CheckoutItemChangeController : ApiControllerBaseCRU<CheckoutItemChange, CheckoutItemChangeBody, int, CheckoutItemChangeParameters>
    {
        protected override IRepositoryBase<CheckoutItemChange, int> Repository => RepositoryWrapper.CheckoutItemChangeRepository;

        public CheckoutItemChangeController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
    }
}
