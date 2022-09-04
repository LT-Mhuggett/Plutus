using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class NoteRepository : CompositeRepositoryBase<Note, int, Guid>, INoteRepository
    {
        public NoteRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
