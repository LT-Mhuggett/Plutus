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
    public class TaxController : CompositeApiControllerBaseCRU<Tax, TaxBody, int, Guid, TaxParameters>
    {
        protected override ICompositeRepositoryBase<Tax, int, Guid> Repository => RepositoryWrapper.TaxRepository;
        public TaxController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
    }
}
