using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoryController : CompositeApiControllerBaseCRUD<Category, CategoryBody, Guid, Guid, CategoryParameters>
    {
        protected override ICompositeRepositoryBase<Category, Guid, Guid> Repository => RepositoryWrapper.CategoryRepository;

        public CategoryController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }
    }
}
