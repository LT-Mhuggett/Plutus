using Newtonsoft.Json;
using Plutus.Entities;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class NoteRepository : Base.CompositeRepositoryBase<Note, int, Guid, NoteBody, NoteParameters>, INoteRepository
    {
        public NoteRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Note";
        }

        protected override string ConvertIdToString(int id1, Guid id2)
        {
            return JsonConvert.SerializeObject(new { id1 = id1, id2 = id2 });
        }

        protected override (int id1, Guid id2) ConvertStringToId(string id)
        {
            return JsonConvert.DeserializeObject<(int id1, Guid id2)>(id);
        }
    }
}
