using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BussinessController : ApiControllerBaseCRU<Bussiness, string, BussinessParameters>
    {
        protected override IRepositoryBase<Bussiness, string> Repository => repositoryWrapper.BussinessRepository;

        public BussinessController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }
    }
}
