using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Identity;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Plutus.Tenancy.Controllers;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP3.2 admin APIs (DoD: isolation + RBAC suites pass over the new endpoints; audit rows
/// written). Tenant isolation is exercised by running the same controller under two tenant
/// contexts; every mutation is asserted to leave an AuditLogs row in the same save.
/// </summary>
public class AdminApiTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn, Guid tenant)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(tenant)) { CurrentUser = "admin-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn, TenantA);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static T WithActor<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()) }, "test")),
            },
        };
        return controller;
    }

    private static Guid SeedBusiness(SqliteConnection conn, Guid tenant, string name)
    {
        using var ctx = Ctx(conn, tenant);
        var b = new Business { Id = Guid.NewGuid(), Name = name, NameAbbr = name[..3].ToUpperInvariant(), VatIN = "GB0" };
        ctx.Business.Add(b);
        ctx.SaveChanges();
        return b.Id;
    }

    [Fact]
    public async Task Companies_are_tenant_isolated_and_updates_are_audited()
    {
        using var conn = Open();
        var aId = SeedBusiness(conn, TenantA, "Alpha");
        SeedBusiness(conn, TenantB, "Bravo");

        // tenant A lists ONLY its own company
        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new CompaniesController(db, new FixedTenantContext(TenantA))).List();
            var list = ((System.Collections.IEnumerable)Assert.IsType<OkObjectResult>(res).Value!).Cast<object>();
            Assert.Single(list);
        }

        // tenant A updating its own company works + writes an audit row
        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new CompaniesController(db, new FixedTenantContext(TenantA)))
                .Update(aId, new CompanyAdminBody("Alpha Ltd", null, null));
            Assert.IsType<NoContentResult>(res);
        }
        using (var check = Ctx(conn, TenantA))
        {
            Assert.Equal("Alpha Ltd", (await check.Business.FirstAsync(b => b.Id == aId)).Name);
            var audit = Assert.Single(await check.AuditLogs.ToListAsync());
            Assert.Equal("company.update", audit.Action);
            Assert.Equal(ActorId, audit.ActorUserId);
        }

        // tenant B cannot see or update tenant A's company → 404, no audit row for B
        using (var db = Ctx(conn, TenantB))
        {
            var res = await WithActor(new CompaniesController(db, new FixedTenantContext(TenantB)))
                .Update(aId, new CompanyAdminBody("Hijack", null, null));
            Assert.IsType<NotFoundResult>(res);
        }
        using (var check = Ctx(conn, TenantA))
            Assert.Equal("Alpha Ltd", (await check.Business.FirstAsync(b => b.Id == aId)).Name);
    }

    [Fact]
    public async Task Store_create_and_update_persist_opening_hours_and_audit()
    {
        using var conn = Open();
        var companyId = SeedBusiness(conn, TenantA, "Alpha");
        const string hours = "{\"mon\":[{\"open\":\"09:00\",\"close\":\"17:30\"}]}";

        int storeId;
        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new StoresController(db, new FixedTenantContext(TenantA)))
                .Create(new StoreAdminBody(companyId, "1 High St", "", "Town", "AB1 2CD", "UK", "0123", hours));
            var created = Assert.IsType<CreatedResult>(res);
            storeId = (int)created.Value!.GetType().GetProperty("id")!.GetValue(created.Value)!;
        }

        using (var check = Ctx(conn, TenantA))
        {
            Assert.Equal(hours, (await check.StoreDetails.FirstAsync(d => d.StoreId == storeId)).OpeningHoursJson);
            Assert.Contains(await check.AuditLogs.ToListAsync(), a => a.Action == "store.create");
        }

        const string newHours = "{\"sat\":[{\"open\":\"10:00\",\"close\":\"16:00\"}]}";
        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new StoresController(db, new FixedTenantContext(TenantA)))
                .Update(storeId, new StoreAdminBody(null, null, null, null, null, null, null, newHours));
            Assert.IsType<NoContentResult>(res);
        }
        using (var check = Ctx(conn, TenantA))
        {
            Assert.Equal(newHours, (await check.StoreDetails.FirstAsync(d => d.StoreId == storeId)).OpeningHoursJson);
            Assert.Contains(await check.AuditLogs.ToListAsync(), a => a.Action == "store.update");
        }
    }

    [Fact]
    public async Task Role_assignment_lifecycle_changes_effective_permissions_and_audits()
    {
        using var conn = Open();
        var companyId = SeedBusiness(conn, TenantA, "Alpha");
        var userId = Guid.NewGuid();

        Guid roleId;
        using (var db = Ctx(conn, TenantA))
        {
            await RbacSeeder.EnsureBuiltInRolesAsync(db, TenantA);
            roleId = (await db.RbacRoles.FirstAsync(r => r.Name == "Cashier")).Id;
        }

        Guid assignmentId;
        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new AdminUsersController(db, new FixedTenantContext(TenantA)))
                .Assign(userId, new AssignRoleBody(roleId, $"company:{companyId}", null, null, null, null, null));
            var created = Assert.IsType<CreatedResult>(res);
            assignmentId = (Guid)created.Value!.GetType().GetProperty("id")!.GetValue(created.Value)!;
        }

        using (var db = Ctx(conn, TenantA))
        {
            var perms = await new EffectivePermissionsService(db)
                .ResolveAsync(userId, ScopeNode.Company(companyId), DateTime.Now);
            Assert.Contains(perms, p => p.Code == PermissionCatalogue.PosSell);
        }

        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new AdminUsersController(db, new FixedTenantContext(TenantA)))
                .Unassign(userId, assignmentId);
            Assert.IsType<NoContentResult>(res);
        }

        using (var check = Ctx(conn, TenantA))
        {
            Assert.Empty(await new EffectivePermissionsService(check)
                .ResolveAsync(userId, ScopeNode.Company(companyId), DateTime.Now));
            var actions = (await check.AuditLogs.ToListAsync()).Select(a => a.Action).ToList();
            Assert.Contains("role.assign", actions);
            Assert.Contains("role.unassign", actions);
        }
    }

    [Fact]
    public async Task User_creation_makes_employee_plus_login_and_audits()
    {
        using var conn = Open();
        var companyId = SeedBusiness(conn, TenantA, "Alpha");
        using (var db = Ctx(conn, TenantA))
        {
            db.Stores.Add(new Store
            {
                Id = 5, BusinessId = companyId, ContactNumber = "-",
                AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
            });
            db.SaveChanges();
        }

        using (var db = Ctx(conn, TenantA))
        {
            var res = await WithActor(new AdminUsersController(db, new FixedTenantContext(TenantA)))
                .CreateUser(new CreateUserBody("Jo", "Bloggs", "jo@example.com", null, 5, "s3cret-pass"));
            Assert.IsType<CreatedResult>(res);
        }

        using (var check = Ctx(conn, TenantA))
        {
            var emp = await check.Employees.FirstAsync(e => e.Email == "jo@example.com");
            Assert.True(emp.Active);
            Assert.Equal(5, emp.StoreId);
            Assert.Single(await check.WebCredentials.Where(w => w.EmployeeId == emp.Id).ToListAsync());
            Assert.Contains(await check.AuditLogs.ToListAsync(), a => a.Action == "user.create");
        }
    }
}
