using Plutus.Entities;

namespace Plutus.DBService
{
    public class RepositoryWrapper : Repository.RepositoryWrapper
    {
        public RepositoryWrapper(MySqlDbContext context) : base(context)
        {

        }
    }
}
