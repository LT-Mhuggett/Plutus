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
    public class RefundController : ApiControllerBaseCR<Refund, RefundBody, int, RefundParameters>
    {
        protected override IRepositoryBase<Refund, int> Repository => RepositoryWrapper.RefundRepository;

        public RefundController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
    }
}
