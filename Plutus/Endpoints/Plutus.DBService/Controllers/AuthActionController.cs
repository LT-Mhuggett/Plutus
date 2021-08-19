using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthActionController : ApiControllerBaseCRUD<AuthActions, int, AuthActionParameters>
    {
        protected override IRepositoryBase<AuthActions, int> Repository => repositoryWrapper.AuthActionRepository;

        public AuthActionController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }
    }
}
