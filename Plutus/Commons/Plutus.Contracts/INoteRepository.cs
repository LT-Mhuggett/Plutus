using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface INoteRepository : ICompositeRepositoryBase<Note, int, Guid>
    {
    }
}
