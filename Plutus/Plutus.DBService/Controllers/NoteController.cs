using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NoteController : ApiControllerBaseCR<Note, int, QueryParameters<Note, int>>
    {
        protected override IRepositoryBase<Note, int> Repository => repositoryWrapper.NoteRepository;

        public NoteController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
