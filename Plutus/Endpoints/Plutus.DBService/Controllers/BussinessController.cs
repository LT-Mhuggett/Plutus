using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System.Threading.Tasks;

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
