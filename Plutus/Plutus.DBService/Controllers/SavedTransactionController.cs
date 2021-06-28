using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SavedTransactionController : ApiControllerBaseCRUD<SavedTransaction, string, QueryParameters<SavedTransaction, string>>
    {
        protected override IRepositoryBase<SavedTransaction, string> Repository => repositoryWrapper.SavedTransactionRepository;

        public SavedTransactionController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
