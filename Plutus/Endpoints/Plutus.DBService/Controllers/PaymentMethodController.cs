using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentMethodController : ApiControllerBaseCRU<PaymentMethod, PaymentMethodBody, int, PaymentMethodsParameters>
    {
        protected override IRepositoryBase<PaymentMethod, int> Repository => RepositoryWrapper.PaymentMethodRepository;

        public PaymentMethodController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
    }
}
