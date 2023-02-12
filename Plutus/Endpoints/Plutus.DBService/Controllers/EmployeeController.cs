using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Threading.Tasks;
using Microsoft.Identity.Web;
using System.Collections.Generic;
using Plutus.Repository.Extensions;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.JsonPatch;
using System;
using DocumentFormat.OpenXml.Office2010.Excel;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ControllerBase
    {
        protected readonly IHttpContextAccessor HttpContextAccessor;
        protected readonly IRepositoryWrapper RepositoryWrapper;
        public EmployeeController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor)
        {
            HttpContextAccessor = httpContextAccessor;
            RepositoryWrapper = repositoryWrapper;
            RepositoryWrapper.SetCurrentUser(HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value);
        }

        protected IEmployeeRepository Repository => RepositoryWrapper.EmployeeRepository;
        /// <summary>
        /// Find employee by employee ID as <see cref="string"/> and business ID as <see cref="string"/>
        /// </summary>
        /// <param name="employeeId">Employee ID</param>
        /// <param name="businessId">Business ID</param>
        /// <returns>Emplyee record found <see cref="Employee"/>; else <see cref="NotFoundResult"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("{employeeId}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Find))]
        public async Task<ActionResult<Employee>> FindById([FromRoute] Guid employeeId, [FromHeader] Guid businessId)
        {
            if (employeeId.Equals(default) || businessId.Equals(default))
                return BadRequest("Record ID and/or Business ID not provided");

            var employee = await Repository.FindFirstByCondition(e=>e.Id.Equals(employeeId) && e.BusinessId.Equals(businessId));
            if (employee == default)
                return NotFound();

            return Ok(employee);
        }

        /// <summary>
        /// Load a list of records that are of type <see cref="Employee"/>
        /// </summary>
        /// <param name="businessId">Business ID</param>
        /// <param name="employeeParameters">Employee query parameters</param>
        /// <returns>Employee records found <see cref="IEnumerable{Employee}"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("Index")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Index))]
        public ActionResult<IEnumerable<Employee>> Index([FromHeader] Guid businessId, [FromQuery] EmployeeParameters employeeParameters)
        {
            if (businessId.Equals(default))
                return BadRequest("Business ID not provided");

            if (!employeeParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less tan Created Min Date");

            var employees = PagedList<Employee>.ToPagedList(Repository.FindAllByConditionQueryable(employeeParameters.GetExpression()
                                                                                                                    .And(e => e.BusinessId.Equals(businessId))).OrderBy(e => e.CreatedAt),
                                                           employeeParameters.PageNumber,
                                                           employeeParameters.PageSize,
                                                           employeeParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(employees.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { employeeParameters.MinCreatedDate, employeeParameters.MaxCreatedDate }));
            return Ok(employees);
        }

        /// <summary>
        /// PATCH employee to database
        /// </summary>
        /// <param name="employeeId">Employee ID as <see cref="string"/></param>
        /// <param name="businessId">Business ID as <see cref="string"/></param>
        /// <param name="patchDocument">Data to update emlpoyee</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns><see cref="ActionResult"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPatch("employeeId")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Patch))]
        public async Task<ActionResult> Patch([FromRoute] Guid employeeId, [FromHeader] Guid businessId, [FromBody] JsonPatchDocument<Employee> patchDocument, [FromQuery] bool isSync = false)
        {
            if (employeeId.Equals(default) || businessId.Equals(default))
                return BadRequest("Record ID and/or Business ID not provided");

            if (patchDocument == default)
                return BadRequest("Patch Document not provided");

            var employee = await Repository.FindById(employeeId, businessId);
            if (employee == default)
                return NotFound();

            patchDocument.ApplyTo(employee, ModelState);

            if (!TryValidateModel(employee))
                return BadRequest(ModelState);

            try
            {
                RepositoryWrapper.SetSyncState(isSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(employeeId, businessId))
                    return NotFound();
            }

            return NoContent();
        }

        /// <summary>
        /// Add <see cref="Employee"/> to Database and attach to <see/>
        /// </summary>
        /// <param name="employeeBody">Form body post data</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns>Employee FindById in <see cref="CreatedAtActionResult"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPost]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Post))]
        public async Task<ActionResult<Employee>> Post([FromHeader] Guid businessId, [FromBody] EmployeeBody employeeBody, [FromQuery] bool isSync = false)
        {
            if(businessId == Guid.Empty)
                return BadRequest("Business ID not provided");

            if(employeeBody.Id == Guid.Empty)
                employeeBody.Id = Guid.Parse(HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value);
            employeeBody.BusinessId = businessId;
            var employee = employeeBody.GenerateEntity();

            //if (!TryValidateModel(employee))
            //    return BadRequest(ModelState);

            await Repository.Create(employee);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { employeeId = employee.Id, businessId = employee.BusinessId }, employee);
        }
        /// <summary>
        /// PUT entity to database
        /// </summary>
        /// <param name="employeeId">Employee ID as <see cref="string"/></param>
        /// <param name="businessId">Business ID as <see cref="string"/></param>
        /// <param name="employee">Employee to save</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns><see cref="ActionResult"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPut]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Put))]
        public async Task<ActionResult> Put([FromRoute] Guid employeeId, [FromHeader] Guid businessId, [FromBody] Employee employee, [FromQuery] bool isSync = false)
        {
            if (employeeId.Equals(default) || businessId.Equals(default))
                return BadRequest("Record ID and/or Business ID not provided");

            if (!EqualityComparer<Guid>.Default.Equals(employeeId, employee.Id) &&
               !EqualityComparer<Guid>.Default.Equals(businessId, employee.BusinessId))
                return BadRequest("Entity keys do not match provided IDs");

            if(isSync)
            {
                if(!await Repository.Exists(employeeId, businessId))
                    return NotFound();
                var tempEmployee = await Repository.FindById(employeeId, businessId);

                if (tempEmployee.ModifiedAt > employee.ModifiedAt)
                    return NoContent();

                Repository.SetState(tempEmployee, EntityState.Detached);
                tempEmployee = null;
            }

            Repository.SetState(employee);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();

            return NoContent();
        }
    }
}
