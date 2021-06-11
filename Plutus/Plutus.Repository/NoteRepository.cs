using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class NoteRepository : RepositoryBase<Note, int>, INoteRepository
    {
        public NoteRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
