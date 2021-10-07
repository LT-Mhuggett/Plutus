using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ApiControllerBaseCRU<Employee, EmployeeBody, string, EmployeeParameters>
    {
        protected override IRepositoryBase<Employee, string> Repository => RepositoryWrapper.EmployeeRepository;

        public EmployeeController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }

        /// <summary>
        /// Add <see cref="Employee"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="employeeBody">Form body post data</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns></returns>
        [Authorize(Actions.WritePermission)]
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public override async Task<ActionResult<Employee>> Post([FromBody] EmployeeBody employeeBody, [FromQuery] bool isSync = false)
        {
            var employee = employeeBody.GenerateEntity();
            employee.ObjectId = RepositoryWrapper.GetCurrentUser();

            if (!TryValidateModel(employee))
                return BadRequest(ModelState);

            await Repository.Create(employee);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();
            //return item;
            return CreatedAtAction("FindById", new { id = employee.Id }, employee);
        }
    }
}
