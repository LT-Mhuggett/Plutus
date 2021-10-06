using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using Plutus.Repository.FormBodies;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ApiControllerBaseCRU<Employee, string, EmployeeParameters>
    {
        protected override IRepositoryBase<Employee, string> Repository => repositoryWrapper.EmployeeRepository;

        public EmployeeController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }

       /* /// <summary>
        /// Add <see cref="Tax"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="TaxBody">Form body post data</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns></returns>
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public async Task<ActionResult<Employee>> Post([FromBody] EmployeeBody employeeBody, [FromQuery] bool IsSync = false)
        {
            var employee = new Employee
            {
                Wage = employeeBody.Wage,
                ContractedHours = employeeBody.ContractedHours,
                StoreId = employeeBody.StoreId,
                FName = employeeBody.FName,
                LName = employeeBody.LName,
                Mobile = employeeBody.Mobile,
                Email = employeeBody.Email,
                BusinessId = employeeBody.BusinessId,
                AdLine1 = employeeBody.AdLine1,
                AdLine2 = employeeBody.AdLine2,
                City = employeeBody.City,
                Country = employeeBody.Country,
                PostCode = employeeBody.PostCode,
                ObjectId = repositoryWrapper.GetCurrentUser()
            };

            if (!TryValidateModel(employee))
                return BadRequest(ModelState);

            await Repository.Create(employee);
            repositoryWrapper.SetSyncState(IsSync);
            await repositoryWrapper.SaveAsync();
            //return item;
            return CreatedAtAction("FindById", new { id = employee.Id }, employee);
        }*/
    }
}
