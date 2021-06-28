using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RefundController : ApiControllerBaseCR<Refund, int, QueryParameters<Refund, int>>
    {
        protected override IRepositoryBase<Refund, int> Repository => repositoryWrapper.RefundRepository;

        public RefundController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
