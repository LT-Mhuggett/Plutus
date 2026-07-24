using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface INoteRepository : ICompositeRepositoryBase<Note, int, Guid, NoteBody, NoteParameters>, Plutus.Contracts.INoteRepository
    {
    }
}
