using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using Newtonsoft.Json;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Z.EntityFramework.Plus;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BusinessController : ApiControllerBaseCRU<Business, BusinessBody, Guid, BusinessParameters>
    {
        private Dictionary<string, List<byte[]>> _fileSignature;
        public BusinessController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
            _fileSignature = new Dictionary<string, List<byte[]>>
            {
                { ".png", new List<byte[]>
                    {
                        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
                    }
                },
                { ".jpg", new List<byte[]>
                    {
                        new byte[]{ 0xFF, 0xD8, 0xFF, 0xE0 },
                        new byte[]{ 0xFF, 0xD8, 0xFF, 0xE1 },
                        new byte[]{ 0xFF, 0xD8, 0xFF, 0xE8 },
                    }
                },
                { ".jpeg", new List<byte[]>
                    {
                        new byte[]{ 0xFF, 0xD8, 0xFF, 0xE0 },
                        new byte[]{ 0xFF, 0xD8, 0xFF, 0xE2 },
                        new byte[]{ 0xFF, 0xD8, 0xFF, 0xE3 },
                    }
                }
            };
        }

        protected override IRepositoryBase<Business, Guid> Repository => RepositoryWrapper.BusinessRepository;
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("{id}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Find))]
        public override async Task<ActionResult<Business>> FindById([FromRoute] Guid id, [FromQuery] BusinessParameters businessParameters)
        {
            var query = Repository.GetAllQueryable();
            if (businessParameters.WithRoles)
                query = query.Include(b => b.Roles);

            var entity = await Repository.FindById(query, id);
            if (entity == default)
                return NotFound();

            return Ok(entity);
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("Index")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Index))]
        public override ActionResult<IEnumerable<Business>> Index([FromQuery] BusinessParameters businessParameters)
        {
            var query = RepositoryWrapper.EmployeeRepository.GetAllQueryable().Where(e=>e.Id.Equals(businessParameters.EmployeeObjectId));
            if (businessParameters.WithRoles)
                query = query.Include(e=>e.Business).ThenInclude(b => b.Roles);
            else
                query = query.Include(e => e.Business);

            var businesses = PagedList<Business>.ToPagedList(query.Select(e=>e.Business).OrderBy(e => e.CreatedAt),
                                                             businessParameters.PageNumber,
                                                             businessParameters.PageSize,
                                                             businessParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(businesses.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { businessParameters.MinCreatedDate, businessParameters.MaxCreatedDate }));
            return Ok(businesses);
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPatch("Image/{id}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Patch))]
        public async Task<ActionResult> UploadBusinessImage([FromRoute]Guid id, [FromBody] IFormFile imageFile)
        {
            if (!await Repository.Exists(id))
                BadRequest("Business does not exist.");

            var ext = "." + imageFile.FileName.Split('.')[imageFile.FileName.Split('.').Length - 1];            
            if(!_fileSignature.ContainsKey(ext))
                BadRequest("File extension is not supported.");

            using var reader = new BinaryReader(imageFile.OpenReadStream());
            var signatures = _fileSignature[ext];
            var headerBytes = reader.ReadBytes(signatures.Max(m => m.Length));
            if (!signatures.Any(signature => headerBytes.Take(signature.Length).SequenceEqual(signature)))
                BadRequest("File is not supported.");

            var business = await Repository.FindById(id);
            business.Logo = reader.ReadBytes(Convert.ToInt32(imageFile.Length));

            if (!await Repository.Update(business))
                BadRequest("Error with saving image to business.");
            await RepositoryWrapper.SaveAsync();
            return Ok();
        }
    }
}
