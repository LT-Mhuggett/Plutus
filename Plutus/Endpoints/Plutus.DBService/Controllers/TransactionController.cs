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
    public class TransactionController : CompositeApiControllerBaseCR<Transaction, TransactionBody, int, Guid, TransactionParameters>
    {
        protected override ICompositeRepositoryBase<Transaction, int, Guid> Repository => RepositoryWrapper.TransactionRepository;

        public TransactionController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
    }
}
