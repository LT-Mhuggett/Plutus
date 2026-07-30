using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP5.3 cross-channel identity (link-only, by email). A recorded Woo order whose billing email
/// matches an existing loyalty customer records ONE CustomerExternalRef; a non-match records
/// nothing (never auto-creates); re-processing is idempotent (refreshes LastSeen, no duplicate).
/// </summary>
public class WebstoreCustomerLinkE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public WebstoreCustomerLinkE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private static WooOrder Order(long id, long customerId, string? email) => new()
    {
        Id = id, Status = "processing", CustomerId = customerId,
        Billing = email is null ? null : new WooBilling { Email = email },
    };

    private static WebstoreConnectionContext Ctx() => new()
    {
        WebStoreId = Guid.NewGuid(), TenantId = Kapow, TillId = Guid.NewGuid(), DeviceId = Guid.NewGuid(),
    };

    [Fact]
    public async Task Order_email_links_to_existing_customer_only_and_is_idempotent()
    {
        var customerId = Guid.NewGuid();
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "weblink-e2e-seed";
            db.Customers.Add(new Customer
            {
                Id = customerId, TenantId = Kapow, Name = "Ada Buyer",
                Email = "buyer@example.com", Active = true, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // 1. matching email (case-insensitive), registered buyer → one ref keyed "c55"
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            await WebstoreCustomerLink.ApplyAsync(db, Ctx(), Order(1001, 55, "Buyer@Example.com"));
            var refs = await db.CustomerExternalRefs.AsNoTracking().Where(r => r.CustomerId == customerId).ToListAsync();
            Assert.Single(refs);
            Assert.Equal("woo", refs[0].Provider);
            Assert.Equal("c55", refs[0].ExternalId);
        }

        // 2. a non-matching email creates nothing
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            await WebstoreCustomerLink.ApplyAsync(db, Ctx(), Order(1002, 77, "stranger@nowhere.com"));
            Assert.Equal(1, await db.CustomerExternalRefs.CountAsync(r => r.CustomerId == customerId));
            Assert.Equal(0, await db.CustomerExternalRefs.CountAsync(r => r.ExternalId == "c77"));
        }

        // 3. re-processing the same order is idempotent (still one "c55" ref)
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            await WebstoreCustomerLink.ApplyAsync(db, Ctx(), Order(1001, 55, "buyer@example.com"));
            Assert.Equal(1, await db.CustomerExternalRefs.CountAsync(r => r.ExternalId == "c55"));
        }

        // 4. a guest checkout (customer_id 0) with the same email links under the order key "o2002"
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            await WebstoreCustomerLink.ApplyAsync(db, Ctx(), Order(2002, 0, "buyer@example.com"));
            Assert.Equal(1, await db.CustomerExternalRefs.CountAsync(r => r.ExternalId == "o2002"));
            Assert.Equal(2, await db.CustomerExternalRefs.CountAsync(r => r.CustomerId == customerId));
        }
    }
}
