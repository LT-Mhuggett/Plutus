using System;
using System.Linq;
using System.Threading.Tasks;
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
/// FE9.1 password reset / invite (DoD): tokens are single-use, expire, are stored ONLY as a hash,
/// and issuing a new one kills the old; completing sets a working password and can GRANT a login to
/// a user who never had one.
/// </summary>
public class PasswordResetTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "reset-test" };

    private static (SqliteConnection Conn, Guid UserId) OpenWithUser(bool withLogin = true, bool active = true)
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        var business = new Business { Id = Uuid7.New(), Name = "Test Co", NameAbbr = "TC", VatIN = "-" };
        ctx.Business.Add(business);
        var user = new Employee
        {
            Id = Uuid7.New(), FName = "Ada", LName = "Lovelace", Email = "ada@example.com",
            Mobile = "-", NIN = "-", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
            Active = active, BusinessId = business.Id, StoreId = 1,
        };
        ctx.Employees.Add(user);
        if (withLogin)
        {
            var (hash, salt) = Pbkdf2.Hash("original-password");
            ctx.WebCredentials.Add(new WebCredential
            {
                Email = user.Email, EmployeeId = user.Id,
                HashedPassword = Convert.ToBase64String(hash), Salt = Convert.ToBase64String(salt),
            });
        }
        ctx.SaveChanges();
        return (conn, user.Id);
    }

    private static bool PasswordWorks(MySqlDbContext db, Guid userId, string password)
    {
        var cred = db.WebCredentials.AsNoTracking().First(c => c.EmployeeId == userId);
        return Pbkdf2.Verify(password, Convert.FromBase64String(cred.Salt), Convert.FromBase64String(cred.HashedPassword));
    }

    private static string MintAndSave(SqliteConnection conn, Guid userId, bool isInvite = false)
    {
        using var db = Ctx(conn);
        var svc = new PasswordResetService(db);
        var (token, row) = svc.Mint(Tenant, userId, "ada@example.com", isInvite, requestedBy: null);
        db.PasswordResetTokens.Add(row);
        db.SaveChanges();
        return token;
    }

    [Fact]
    public void The_plaintext_token_is_never_stored()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        var token = MintAndSave(conn, userId);

        using var db = Ctx(conn);
        var row = db.PasswordResetTokens.AsNoTracking().Single();
        Assert.Equal(32, row.TokenHash.Length);                       // SHA-256
        Assert.Equal(CompactToken.Sha256(token), row.TokenHash);      // hash matches
        // and no column anywhere holds the token itself
        Assert.DoesNotContain(token, row.Email);
    }

    [Fact]
    public async Task Completing_sets_the_new_password_and_burns_the_token()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        var token = MintAndSave(conn, userId);

        using (var db = Ctx(conn))
        {
            var outcome = await new PasswordResetService(db).CompleteAsync(token, "brand-new-password");
            Assert.Equal(PasswordResetService.CompleteOutcome.Ok, outcome);
        }

        using (var db = Ctx(conn))
        {
            Assert.True(PasswordWorks(db, userId, "brand-new-password"));
            Assert.False(PasswordWorks(db, userId, "original-password"));   // old one is dead
            Assert.NotNull(db.PasswordResetTokens.AsNoTracking().Single().UsedAtUtc);
        }
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        var token = MintAndSave(conn, userId);

        using (var db = Ctx(conn))
            Assert.Equal(PasswordResetService.CompleteOutcome.Ok, await new PasswordResetService(db).CompleteAsync(token, "first-password"));

        using (var db = Ctx(conn))
        {
            var second = await new PasswordResetService(db).CompleteAsync(token, "second-password");
            Assert.Equal(PasswordResetService.CompleteOutcome.AlreadyUsed, second);
            Assert.True(PasswordWorks(db, userId, "first-password"));  // unchanged by the replay
        }
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        string token;
        using (var db = Ctx(conn))
        {
            var svc = new PasswordResetService(db);
            var (t, row) = svc.Mint(Tenant, userId, "ada@example.com", false, null);
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);   // just lapsed
            db.PasswordResetTokens.Add(row);
            db.SaveChanges();
            token = t;
        }
        using (var db = Ctx(conn))
        {
            Assert.Equal(PasswordResetService.CompleteOutcome.Expired,
                await new PasswordResetService(db).CompleteAsync(token, "new-password"));
            Assert.True(PasswordWorks(db, userId, "original-password")); // untouched
        }
    }

    [Fact]
    public async Task An_unknown_token_is_refused()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        MintAndSave(conn, userId);
        using var db = Ctx(conn);
        Assert.Equal(PasswordResetService.CompleteOutcome.UnknownToken,
            await new PasswordResetService(db).CompleteAsync("NOTAREALTOKEN0000000000000000000", "new-password"));
    }

    [Fact]
    public async Task Issuing_a_new_token_invalidates_the_previous_link()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        var first = MintAndSave(conn, userId);

        using (var db = Ctx(conn))
        {
            var svc = new PasswordResetService(db);
            await svc.InvalidateOutstandingAsync(userId);
            var (_, row) = svc.Mint(Tenant, userId, "ada@example.com", false, null);
            db.PasswordResetTokens.Add(row);
            db.SaveChanges();
        }

        using (var db = Ctx(conn))
            Assert.Equal(PasswordResetService.CompleteOutcome.AlreadyUsed,
                await new PasswordResetService(db).CompleteAsync(first, "new-password"));
    }

    [Fact]
    public async Task A_short_password_is_refused()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        var token = MintAndSave(conn, userId);
        using var db = Ctx(conn);
        Assert.Equal(PasswordResetService.CompleteOutcome.WeakPassword,
            await new PasswordResetService(db).CompleteAsync(token, "short"));
        // and the token survives, so the user can try again with a better one
        Assert.Null(db.PasswordResetTokens.AsNoTracking().Single().UsedAtUtc);
    }

    /// <summary>The invite case: a staff user with no web login gets one by completing the token.</summary>
    [Fact]
    public async Task An_invite_grants_a_login_to_a_user_who_had_none()
    {
        var (conn, userId) = OpenWithUser(withLogin: false);
        using var _ = conn;
        using (var db = Ctx(conn))
            Assert.Empty(db.WebCredentials.AsNoTracking().Where(c => c.EmployeeId == userId));

        var token = MintAndSave(conn, userId, isInvite: true);
        using (var db = Ctx(conn))
            Assert.Equal(PasswordResetService.CompleteOutcome.Ok, await new PasswordResetService(db).CompleteAsync(token, "my-first-password"));

        using (var db = Ctx(conn))
        {
            Assert.Single(db.WebCredentials.AsNoTracking().Where(c => c.EmployeeId == userId));
            Assert.True(PasswordWorks(db, userId, "my-first-password"));
        }
    }

    [Fact]
    public async Task A_removed_user_cannot_complete_a_reset()
    {
        var (conn, userId) = OpenWithUser(active: false);
        using var _ = conn;
        var token = MintAndSave(conn, userId);
        using var db = Ctx(conn);
        Assert.Equal(PasswordResetService.CompleteOutcome.NoAccount,
            await new PasswordResetService(db).CompleteAsync(token, "new-password"));
    }

    [Fact]
    public async Task Admin_set_password_replaces_the_existing_one()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        using (var db = Ctx(conn))
        {
            await new PasswordResetService(db).SetPasswordAsync(userId, "ada@example.com", "admin-chosen-pw");
            await db.SaveChangesAsync();
        }
        using (var db = Ctx(conn))
        {
            Assert.True(PasswordWorks(db, userId, "admin-chosen-pw"));
            Assert.False(PasswordWorks(db, userId, "original-password"));
        }
    }

    [Fact]
    public void Tokens_are_unique_and_long_enough_to_be_unguessable()
    {
        var (conn, userId) = OpenWithUser();
        using var _ = conn;
        using var db = Ctx(conn);
        var svc = new PasswordResetService(db);
        var tokens = Enumerable.Range(0, 200)
            .Select(_ => svc.Mint(Tenant, userId, "ada@example.com", false, null).Token).ToList();
        Assert.Equal(200, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Equal(PasswordResetService.TokenChars, t.Length));
        // Crockford alphabet only — safe in a URL and readable aloud
        Assert.All(tokens, t => Assert.All(t, c => Assert.Contains(c, Crockford32.Alphabet)));
    }
}
