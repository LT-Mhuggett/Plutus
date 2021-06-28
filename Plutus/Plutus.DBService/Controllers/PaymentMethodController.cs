using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentMethodController : ApiControllerBaseCRU<PaymentMethod, int, QueryParameters<PaymentMethod, int>>
    {
        protected override IRepositoryBase<PaymentMethod, int> Repository => repositoryWrapper.PaymentMethodRepository;

        public PaymentMethodController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
