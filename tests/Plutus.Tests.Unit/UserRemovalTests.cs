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
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// FE9.2 "delete" a user (DoD): removal revokes the login and every role but KEEPS the person row,
/// so sales and audit history still resolve to a real name; you cannot remove yourself, nor the
/// tenant's last Owner; restore brings them back WITHOUT silently restoring their access.
/// Also FE9.3: every permission in the catalogue has a description.
/// </summary>
public class UserRemovalTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "removal-test" };

    private static AdminUsersController Controller(MySqlDbContext db, Guid actor)
    {
        var c = new AdminUsersController(db, new FixedTenantContext(Tenant), new PasswordResetService(db), new NullMessageSender())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, actor.ToString()) }, "test")),
                },
            },
        };
        return c;
    }

    private static SqliteConnection Open(out Guid businessId)
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        var b = new Business { Id = Uuid7.New(), Name = "Test Co", NameAbbr = "TC", VatIN = "-" };
        ctx.Business.Add(b);
        ctx.SaveChanges();
        businessId = b.Id;
        return conn;
    }

    private static Guid AddUser(SqliteConnection conn, Guid businessId, string name, bool withLogin = true)
    {
        using var db = Ctx(conn);
        var u = new Employee
        {
            Id = Uuid7.New(), FName = name, LName = "Test", Email = $"{name.ToLower()}@example.com",
            Mobile = "-", NIN = "-", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
            Active = true, BusinessId = businessId, StoreId = 1,
        };
        db.Employees.Add(u);
        if (withLogin)
        {
            var (hash, salt) = Pbkdf2.Hash("a-password-here");
            db.WebCredentials.Add(new WebCredential
            {
                Email = u.Email, EmployeeId = u.Id,
                HashedPassword = Convert.ToBase64String(hash), Salt = Convert.ToBase64String(salt),
            });
        }
        db.SaveChanges();
        return u.Id;
    }

    private static async Task GrantRoleAsync(SqliteConnection conn, Guid userId, string roleName)
    {
        using var db = Ctx(conn);
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Tenant);
        var role = await db.RbacRoles.FirstAsync(r => r.Name == roleName);
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Tenant, UserId = userId, RoleId = role.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Removal_revokes_login_and_roles_but_keeps_the_person()
    {
        using var conn = Open(out var businessId);
        var admin = AddUser(conn, businessId, "Admin");
        var victim = AddUser(conn, businessId, "Leaver");
        await GrantRoleAsync(conn, victim, "Cashier");

        using (var db = Ctx(conn))
        {
            var res = await Controller(db, admin).RemoveUser(victim);
            Assert.IsType<OkObjectResult>(res);
        }

        using (var db = Ctx(conn))
        {
            var user = await db.Employees.IgnoreQueryFilters().FirstAsync(e => e.Id == victim);
            Assert.False(user.Active);                                              // deactivated
            Assert.Equal("Leaver", user.FName);                                     // history intact
            Assert.Empty(db.WebCredentials.Where(c => c.EmployeeId == victim));     // login revoked
            Assert.Empty(db.RbacRoleAssignments.Where(a => a.UserId == victim));    // roles gone
            Assert.Contains(await db.AuditLogs.ToListAsync(), a => a.Action == "user.remove");
        }
    }

    [Fact]
    public async Task You_cannot_remove_yourself()
    {
        using var conn = Open(out var businessId);
        var me = AddUser(conn, businessId, "Me");
        using var db = Ctx(conn);
        var res = await Controller(db, me).RemoveUser(me);
        Assert.IsType<BadRequestObjectResult>(res);
        Assert.True((await db.Employees.FirstAsync(e => e.Id == me)).Active);
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed()
    {
        using var conn = Open(out var businessId);
        var admin = AddUser(conn, businessId, "Admin");
        var owner = AddUser(conn, businessId, "Owner");
        await GrantRoleAsync(conn, owner, "Owner");

        using (var db = Ctx(conn))
        {
            var res = await Controller(db, admin).RemoveUser(owner);
            Assert.IsType<ConflictObjectResult>(res);
        }
        using (var db = Ctx(conn))
        {
            Assert.True((await db.Employees.FirstAsync(e => e.Id == owner)).Active);
            Assert.NotEmpty(db.RbacRoleAssignments.Where(a => a.UserId == owner)); // role untouched
        }
    }

    [Fact]
    public async Task An_owner_can_be_removed_once_a_second_owner_exists()
    {
        using var conn = Open(out var businessId);
        var admin = AddUser(conn, businessId, "Admin");
        var owner1 = AddUser(conn, businessId, "OwnerOne");
        var owner2 = AddUser(conn, businessId, "OwnerTwo");
        await GrantRoleAsync(conn, owner1, "Owner");
        await GrantRoleAsync(conn, owner2, "Owner");

        using (var db = Ctx(conn))
            Assert.IsType<OkObjectResult>(await Controller(db, admin).RemoveUser(owner1));

        // ...and now owner2 is the last one, so they are protected in turn
        using (var db = Ctx(conn))
            Assert.IsType<ConflictObjectResult>(await Controller(db, admin).RemoveUser(owner2));
    }

    [Fact]
    public async Task Restore_reactivates_without_restoring_access()
    {
        using var conn = Open(out var businessId);
        var admin = AddUser(conn, businessId, "Admin");
        var victim = AddUser(conn, businessId, "Leaver");
        await GrantRoleAsync(conn, victim, "Cashier");

        using (var db = Ctx(conn)) await Controller(db, admin).RemoveUser(victim);
        using (var db = Ctx(conn)) Assert.IsType<NoContentResult>(await Controller(db, admin).RestoreUser(victim));

        using (var db = Ctx(conn))
        {
            Assert.True((await db.Employees.FirstAsync(e => e.Id == victim)).Active);
            // deliberately still no login and no roles — those are re-granted on purpose
            Assert.Empty(db.WebCredentials.Where(c => c.EmployeeId == victim));
            Assert.Empty(db.RbacRoleAssignments.Where(a => a.UserId == victim));
        }
    }

    [Fact]
    public async Task Removed_users_are_hidden_from_the_list_unless_asked_for()
    {
        using var conn = Open(out var businessId);
        var admin = AddUser(conn, businessId, "Admin");
        var victim = AddUser(conn, businessId, "Leaver");
        using (var db = Ctx(conn)) await Controller(db, admin).RemoveUser(victim);

        using (var db = Ctx(conn))
        {
            var visible = ((System.Collections.IEnumerable)Assert.IsType<OkObjectResult>(
                await Controller(db, admin).ListUsers()).Value!).Cast<object>().Count();
            var all = ((System.Collections.IEnumerable)Assert.IsType<OkObjectResult>(
                await Controller(db, admin).ListUsers(includeRemoved: true)).Value!).Cast<object>().Count();
            Assert.Equal(1, visible);   // just the admin
            Assert.Equal(2, all);
        }
    }

    // ── FE9.3 ──

    [Fact]
    public void Every_catalogue_permission_has_a_description_and_a_group()
    {
        Assert.All(PermissionCatalogue.All, code =>
        {
            var described = PermissionCatalogue.DescribeOf(code);
            Assert.False(string.IsNullOrWhiteSpace(described));
            Assert.NotEqual(code, described);   // a real sentence, not the key echoed back
            Assert.NotEqual("Other", PermissionCatalogue.GroupOf(code));
        });
    }

    /// <summary>The roles reference is only trustworthy if its grants ARE the seeder's grants.</summary>
    [Fact]
    public async Task Seeded_role_grants_are_all_known_described_permissions()
    {
        using var conn = Open(out _);
        using var db = Ctx(conn);
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Tenant);

        var roles = await db.RbacRoles.Include(r => r.Grants).ToListAsync();
        Assert.NotEmpty(roles);
        Assert.All(roles, r => Assert.All(r.Grants, g =>
        {
            Assert.True(PermissionCatalogue.IsKnown(g.PermissionCode), $"{r.Name} grants unknown '{g.PermissionCode}'");
            Assert.False(string.IsNullOrWhiteSpace(PermissionCatalogue.DescribeOf(g.PermissionCode)));
        }));
        // Owner must be the superset — the roles table is where an admin checks that
        var owner = roles.First(r => r.Name == "Owner");
        Assert.Contains(owner.Grants, g => g.PermissionCode == PermissionCatalogue.PortalUsersManage);
        Assert.Contains(owner.Grants, g => g.PermissionCode == PermissionCatalogue.CustomersManage);
    }
}
