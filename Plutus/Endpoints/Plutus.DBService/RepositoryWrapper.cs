using Microsoft.EntityFrameworkCore.Storage;
using Plutus.Entities;
using System.Data;
using System.Transactions;

namespace Plutus.DBService
{
    public class RepositoryWrapper : Repository.RepositoryWrapper
    {
        public RepositoryWrapper(RepositoryContext context) : base(context)
        {

        }

        public IDbContextTransaction GetNewTransaction() => repositoryContext.Database.BeginTransaction();
    }
}
