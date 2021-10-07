using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SavedTransactionController : ApiControllerBaseCRUD<SavedTransaction, SavedTransactionBody, string, SavedTransactionsParameters>
    {
        protected override IRepositoryBase<SavedTransaction, string> Repository => RepositoryWrapper.SavedTransactionRepository;

        public SavedTransactionController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }
    }
}
