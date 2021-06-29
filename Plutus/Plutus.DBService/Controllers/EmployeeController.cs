using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ApiControllerBaseCRU<Employee, string, EmployeeParameters>
    {
        protected override IRepositoryBase<Employee, string> Repository => repositoryWrapper.EmployeeRepository;

        public EmployeeController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }
    }
}
