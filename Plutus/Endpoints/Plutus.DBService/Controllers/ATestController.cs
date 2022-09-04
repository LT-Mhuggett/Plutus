using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using System.Threading.Tasks;
using System;

namespace Plutus.DBService.Controllers
{
    [Route("/api/[controller]")]
    public class ATestController : ControllerBase
    {
        private RepositoryWrapper repository;

        public ATestController(IRepositoryWrapper repository)
        {
            this.repository = (RepositoryWrapper)repository;
            this.repository.SetCurrentUser("CUNT");
        }

        [HttpGet("testTransaction")]
        public async Task<ActionResult> TestTransactions()
        {
            var transaction = repository.GetNewTransaction();
            try
            {
                await Task.Delay(100);

                var business = new Business { Id = Guid.NewGuid(), Name = "Test", NameAbbr = "Tnuc", VatIN = "FuckYouGuys", RecMarkup = 100.10m };

                if (!await repository.BusinessRepository.Create(business))
                    new Exception();

                await Task.Delay(100);
                await repository.SaveAsync();
                await transaction.CommitAsync();
                return Ok("Data saved succesfully you cunt.");
            }
            catch
            {
                transaction.Rollback();
                return BadRequest("Data failed to save sean was a retard.");
            }
        }
    }
}
