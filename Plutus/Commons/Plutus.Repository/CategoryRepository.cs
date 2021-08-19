using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class CategoryRepository : RepositoryBase<Category, int>, ICategoryRepository
    {
        public CategoryRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
