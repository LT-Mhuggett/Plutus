using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoryController : ApiControllerBaseCRUD<Category, int, QueryParameters<Category, int>>
    {
        protected override IRepositoryBase<Category, int> Repository => repositoryWrapper.CategoryRepository;

        public CategoryController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
