using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TillController : ApiControllerBaseCRU<Till, TillBody, Guid, TillParameters>
    {
        protected override IRepositoryBase<Till, Guid> Repository => RepositoryWrapper.TillRepository;

        public TillController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
    }
}
