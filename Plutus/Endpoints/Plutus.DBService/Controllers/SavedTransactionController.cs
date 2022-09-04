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
    public class SavedTransactionController : ApiControllerBaseCRUD<SavedTransaction, SavedTransactionBody, Guid, SavedTransactionsParameters>
    {
        protected override IRepositoryBase<SavedTransaction, Guid> Repository => RepositoryWrapper.SavedTransactionRepository;

        public SavedTransactionController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }
    }
}
