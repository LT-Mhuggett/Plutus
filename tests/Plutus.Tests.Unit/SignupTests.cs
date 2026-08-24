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
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **WP-SIGNUP — the definition of done, as tests.**
///
/// ⚠⚠ THE PREMISE UNDER TEST IS THAT A SIGNUP CREATES NOTHING. An unauthenticated endpoint that
/// creates a tenant is an open door: it writes rows, sends mail and consumes a name in a shared
/// namespace before anyone has proved they are a person. Every assertion here exists to keep the
/// application and the tenant separate until an operator decides otherwise.
/// </summary>
public class SignupTests
{
    private static MySqlDbContext Ctx(SqliteConnection conn, string user = "test")
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Guid.Empty)) { CurrentUser = user };

    private static SqliteConnection NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static TenantApplicationService Svc(MySqlDbContext db, string disposable = null)
        => new(db, new DisposableEmailDomains(disposable));

    private static ApplyRequest Req(string name = "Test Shop", string email = "owner@testshop.co.uk")
        => new(name, "A Person", email, "01234 567890", "UK");

    // ── stage 1: an application is not a tenant ──────────────────────────────────────────────────

    /// <summary>⚠⚠ THE HEADLINE. One row, and nothing a live tenant touches.</summary>
    [Fact]
    public async Task Applying_creates_an_application_and_absolutely_nothing_else()
    {
        var conn = NewDb();
        using (var db = Ctx(conn))
        {
            var res = await Svc(db).ApplyAsync(Req(), "203.0.113.7");
            Assert.True(res.IsNew);
            Assert.NotNull(res.VerifyToken);
        }

        using (var db = Ctx(conn))
        {
            Assert.Equal(1, await db.TenantApplications.CountAsync());
            Assert.Equal(0, await db.Tenants.CountAsync());
            Assert.Equal(0, await db.Business.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.Stores.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.WebCredentials.CountAsync());
            Assert.Equal(0, await db.RbacRoleAssignments.IgnoreQueryFilters().CountAsync());
        }
        conn.Dispose();
    }

    /// <summary>⚠⚠ THE TOKEN IS NEVER STORED. A leaked applications table must not be a set of
    /// working verification links.</summary>
    [Fact]
    public async Task The_verification_token_is_stored_only_as_a_hash()
    {
        var conn = NewDb();
        string token;
        using (var db = Ctx(conn)) token = (await Svc(db).ApplyAsync(Req(), "1.2.3.4")).VerifyToken;

        using (var db = Ctx(conn))
        {
            var app = await db.TenantApplications.FirstAsync();
            Assert.NotNull(app.EmailVerifyTokenHash);
            Assert.Equal(CompactToken.Sha256(Crockford32.Normalise(token)), app.EmailVerifyTokenHash);

            // and the plaintext appears nowhere on the row
            var everyString = string.Join("|", app.BusinessName, app.ContactName, app.ContactEmail,
                                          app.Phone, app.CreatedFromIp, app.BusinessNameFolded);
            Assert.DoesNotContain(token, everyString, StringComparison.OrdinalIgnoreCase);
        }
        conn.Dispose();
    }

    // ── stage 3: abuse controls ──────────────────────────────────────────────────────────────────

    /// <summary>⚠ The business-name reservation, and it is a unique index rather than a check —
    /// two applicants racing for one name must not both win.</summary>
    [Theory]
    [InlineData("Kapow Comics", "kapow-comics")]
    [InlineData("Kapow Comics", "KAPOW  COMICS!")]
    [InlineData("Café Noir", "Cafe Noir")]
    public async Task A_business_name_can_only_be_claimed_once(string first, string second)
    {
        var conn = NewDb();
        using (var db = Ctx(conn)) await Svc(db).ApplyAsync(Req(first, "a@one.co.uk"), "1.1.1.1");
        using (var db = Ctx(conn))
        {
            var res = await Svc(db).ApplyAsync(Req(second, "b@two.co.uk"), "2.2.2.2");
            Assert.False(res.IsNew);
            // ⚠⚠ AND NO TOKEN — a stranger must not be able to verify their way into somebody
            // else's application, and must not learn that the name is taken either.
            Assert.Null(res.VerifyToken);
        }
        using (var db = Ctx(conn)) Assert.Equal(1, await db.TenantApplications.CountAsync());
        conn.Dispose();
    }

    /// <summary>⚠ The SAME person re-applying gets their row back and a fresh link — that is a
    /// forgotten-email retry, not an attack.</summary>
    [Fact]
    public async Task Re_applying_with_the_same_email_re_issues_a_link()
    {
        var conn = NewDb();
        using (var db = Ctx(conn)) await Svc(db).ApplyAsync(Req(), "1.1.1.1");
        using (var db = Ctx(conn))
        {
            var again = await Svc(db).ApplyAsync(Req(), "1.1.1.1");
            Assert.False(again.IsNew);
            Assert.NotNull(again.VerifyToken);
        }
        conn.Dispose();
    }

    [Theory]
    [InlineData("someone@mailinator.com")]
    [InlineData("someone@mail.mailinator.com")]     // ⚠ subdomains count
    [InlineData("someone@YOPMAIL.COM")]             // ⚠ case-insensitive
    public async Task Disposable_addresses_are_refused(string email)
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        var ex = await Assert.ThrowsAsync<EnrolmentException>(() => Svc(db).ApplyAsync(Req("Shop", email), "1.1.1.1"));
        Assert.Equal(400, ex.StatusCode);
        conn.Dispose();
    }

    /// <summary>⚠ The list is CONFIG, not code — it is wrong the day after it ships.</summary>
    [Fact]
    public async Task The_disposable_list_can_be_replaced_from_configuration()
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        // mailinator is no longer blocked; a bespoke domain is
        var svc = new TenantApplicationService(db, new DisposableEmailDomains("burner.example"));
        await svc.ApplyAsync(Req("Shop A", "a@mailinator.com"), "1.1.1.1");
        await Assert.ThrowsAsync<EnrolmentException>(() => svc.ApplyAsync(Req("Shop B", "b@burner.example"), "1.1.1.1"));
        conn.Dispose();
    }

    // ── stage 2: proving the email ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verification_marks_the_application_and_burns_the_link()
    {
        var conn = NewDb();
        string token;
        using (var db = Ctx(conn)) token = (await Svc(db).ApplyAsync(Req(), "1.1.1.1")).VerifyToken;

        using (var db = Ctx(conn))
        {
            var (outcome, app) = await Svc(db).VerifyAsync(token);
            Assert.Equal(TenantApplicationService.VerifyOutcome.Ok, outcome);
            Assert.Equal(TenantApplicationStatus.EmailVerified, app.Status);
        }
        // ⚠ SINGLE USE — the same link a second time is not "already verified by you", it is dead.
        using (var db = Ctx(conn))
            Assert.Equal(TenantApplicationService.VerifyOutcome.UnknownToken, (await Svc(db).VerifyAsync(token)).Outcome);
        conn.Dispose();
    }

    [Fact]
    public async Task An_expired_link_is_refused()
    {
        var conn = NewDb();
        string token;
        using (var db = Ctx(conn)) token = (await Svc(db).ApplyAsync(Req(), "1.1.1.1")).VerifyToken;
        using (var db = Ctx(conn))
        {
            var app = await db.TenantApplications.FirstAsync();
            app.EmailVerifyExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        using (var db = Ctx(conn))
            Assert.Equal(TenantApplicationService.VerifyOutcome.Expired, (await Svc(db).VerifyAsync(token)).Outcome);
        conn.Dispose();
    }

    /// <summary>⚠ Per-application send cap: the per-IP limiter cannot see one address being
    /// mail-bombed from many IPs.</summary>
    [Fact]
    public async Task Verification_resends_are_capped()
    {
        var conn = NewDb();
        Guid id;
        using (var db = Ctx(conn)) id = (await Svc(db).ApplyAsync(Req(), "1.1.1.1")).ApplicationId;

        using var db2 = Ctx(conn);
        var svc = Svc(db2);
        for (var i = 1; i < TenantApplicationService.MaxVerifySends; i++) await svc.ResendVerifyAsync(id);
        var ex = await Assert.ThrowsAsync<EnrolmentException>(() => svc.ResendVerifyAsync(id));
        Assert.Equal(429, ex.StatusCode);
        conn.Dispose();
    }

    // ── stage 5: approval ────────────────────────────────────────────────────────────────────────

    private static (ProvisioningService prov, DpaService dpa) Services(MySqlDbContext db)
        => (new ProvisioningService(db, new TenantRoleProvisioner(db)), new DpaService(db));

    private static async Task<Guid> VerifiedApplicationAsync(SqliteConnection conn, string name = "Test Shop")
    {
        using var db = Ctx(conn);
        var svc = Svc(db);
        var res = await svc.ApplyAsync(Req(name, $"owner@{TenantApplicationService.FoldName(name)}.co.uk"), "1.1.1.1");
        await svc.VerifyAsync(res.VerifyToken);
        return res.ApplicationId;
    }

    private static async Task PublishDpaAsync(SqliteConnection conn, string version = "2026-01")
    {
        using var db = Ctx(conn);
        var dpa = new DpaService(db);
        await dpa.SaveDraftAsync(version, "DPA", "The agreement text.", null, "op");
        await dpa.PublishAsync(version, "op");
    }

    /// <summary>⚠⚠ AN UNVERIFIED APPLICATION CANNOT BE APPROVED BY ANY ROUTE — a DoD line.</summary>
    [Fact]
    public async Task An_unverified_application_cannot_be_approved()
    {
        var conn = NewDb();
        Guid id;
        using (var db = Ctx(conn)) id = (await Svc(db).ApplyAsync(Req(), "1.1.1.1")).ApplicationId;
        await PublishDpaAsync(conn);

        using (var db = Ctx(conn))
        {
            var (prov, dpa) = Services(db);
            var ex = await Assert.ThrowsAsync<EnrolmentException>(
                () => Svc(db).ApproveAsync(id, prov, dpa, "op", "password1"));
            Assert.Equal(409, ex.StatusCode);
        }
        using (var db = Ctx(conn)) Assert.Equal(0, await db.Tenants.CountAsync());
        conn.Dispose();
    }

    /// <summary>⚠⚠ NOR WITHOUT THE DPA. A tenant that can process personal data before accepting
    /// the terms under which it may is a tenant whose first act is a compliance gap.</summary>
    [Fact]
    public async Task An_application_without_a_dpa_acceptance_cannot_be_approved()
    {
        var conn = NewDb();
        var id = await VerifiedApplicationAsync(conn);
        await PublishDpaAsync(conn);

        using (var db = Ctx(conn))
        {
            var (prov, dpa) = Services(db);
            var ex = await Assert.ThrowsAsync<EnrolmentException>(
                () => Svc(db).ApproveAsync(id, prov, dpa, "op", "password1"));
            Assert.Equal(409, ex.StatusCode);
            Assert.Contains("data processing agreement", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        conn.Dispose();
    }

    /// <summary>⚠⚠ SANDBOX-FIRST, ALWAYS. A self-serve tenant that starts live is one taking real
    /// money before anyone has looked at it.</summary>
    [Fact]
    public async Task Approving_provisions_one_sandbox_tenant_with_the_dpa_carried_across()
    {
        var conn = NewDb();
        var id = await VerifiedApplicationAsync(conn);
        await PublishDpaAsync(conn);
        using (var db = Ctx(conn)) await Svc(db).AcceptDpaAsync(id, "2026-01", "198.51.100.9", "a-browser");

        Guid tenantId;
        using (var db = Ctx(conn))
        {
            var (prov, dpa) = Services(db);
            var (t, created) = await Svc(db).ApproveAsync(id, prov, dpa, "matt", "password1");
            tenantId = t;
            Assert.True(created);
        }

        using (var db = Ctx(conn))
        {
            var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
            Assert.True(tenant.IsSandbox, "A self-serve tenant must start in sandbox.");
            Assert.Equal("2026-01", tenant.DpaRef);
            Assert.NotNull(tenant.DpaSignedAtUtc);

            // the evidence row followed the application onto the tenant
            var acc = await db.DpaAcceptances.FirstAsync(a => a.TenantId == tenantId);
            Assert.Equal("2026-01", acc.Version);
            Assert.Equal("198.51.100.9", acc.AcceptedIp);
            Assert.Equal(id, acc.SourceApplicationId);
            Assert.Null(acc.RecordedByOperator);          // ⚠ the CLIENT accepted this one
        }
        conn.Dispose();
    }

    /// <summary>⚠⚠ IDEMPOTENT ON THE APPLICATION ID — the operator queue is exactly where a double
    /// click happens, and two tenants for one application is not something anybody unpicks.</summary>
    [Fact]
    public async Task Approving_twice_provisions_exactly_one_tenant()
    {
        var conn = NewDb();
        var id = await VerifiedApplicationAsync(conn);
        await PublishDpaAsync(conn);
        using (var db = Ctx(conn)) await Svc(db).AcceptDpaAsync(id, "2026-01", "1.1.1.1", "ua");

        Guid first, second;
        bool createdAgain;
        using (var db = Ctx(conn)) { var (p, d) = Services(db); (first, _) = await Svc(db).ApproveAsync(id, p, d, "op", "password1"); }
        using (var db = Ctx(conn)) { var (p, d) = Services(db); (second, createdAgain) = await Svc(db).ApproveAsync(id, p, d, "op", "password1"); }

        Assert.Equal(first, second);
        Assert.False(createdAgain);
        using (var db = Ctx(conn)) Assert.Equal(1, await db.Tenants.CountAsync());
        conn.Dispose();
    }

    /// <summary>⚠ Rejecting keeps the row (the reason must be explainable) but RELEASES the name —
    /// a rejected applicant must not hold "Kapow Comics" against the real one for ever.</summary>
    [Fact]
    public async Task Rejecting_keeps_the_row_and_frees_the_business_name()
    {
        var conn = NewDb();
        var id = await VerifiedApplicationAsync(conn, "Kapow Comics");
        using (var db = Ctx(conn)) await Svc(db).RejectAsync(id, "Not a real shop.", "op");

        using (var db = Ctx(conn))
        {
            var app = await db.TenantApplications.FirstAsync(a => a.Id == id);
            Assert.Equal(TenantApplicationStatus.Rejected, app.Status);
            Assert.Equal("Not a real shop.", app.RejectedReason);
        }
        // the name is claimable again
        using (var db = Ctx(conn))
            Assert.True((await Svc(db).ApplyAsync(Req("Kapow Comics", "real@kapow.co.uk"), "9.9.9.9")).IsNew);
        conn.Dispose();
    }

    // ── the DoD line about mail failing ──────────────────────────────────────────────────────────

    /// <summary>A sender that blows up, which is what a misconfigured SMTP host does.</summary>
    private sealed class ExplodingMailSender : IMessageSender
    {
        public Task<MessageSendResult> SendAsync(OutboundMessage message, System.Threading.CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP is not configured.");
    }

    /// <summary>A sender that politely refuses — the more common failure.</summary>
    private sealed class RefusingMailSender : IMessageSender
    {
        public Task<MessageSendResult> SendAsync(OutboundMessage message, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new MessageSendResult(false, null, "Mailbox unavailable."));
    }

    /// <summary>
    /// ⚠⚠ A DoD LINE, VERBATIM: *"the signup path is tested with the mail sender FAILING — a bounced
    /// verification email must leave a recoverable application, not a dead row."*
    ///
    /// The application is committed BEFORE the mail is attempted, and the send is best-effort. So a
    /// dead mailer costs the applicant a resend, not their application — and the operator queue can
    /// re-issue the link.
    /// </summary>
    [Fact]
    public async Task A_failing_mail_sender_leaves_a_recoverable_application()
    {
        var conn = NewDb();
        Guid id;
        string token;

        using (var db = Ctx(conn))
        {
            var res = await Svc(db).ApplyAsync(Req(), "1.1.1.1");
            id = res.ApplicationId;
            token = res.VerifyToken;

            // Both failure shapes must be swallowed, not thrown.
            Assert.False(await SignupMail.SendVerifyAsync(new ExplodingMailSender(), null, "a@b.co.uk", "A", token, null));
            Assert.False(await SignupMail.SendVerifyAsync(new RefusingMailSender(), null, "a@b.co.uk", "A", token, null));
        }

        // ⚠ The row survived, and the token still works — this is the "recoverable" in the DoD.
        using (var db = Ctx(conn))
        {
            Assert.Equal(1, await db.TenantApplications.CountAsync());
            var (outcome, app) = await Svc(db).VerifyAsync(token);
            Assert.Equal(TenantApplicationService.VerifyOutcome.Ok, outcome);
            Assert.Equal(id, app.Id);
        }
        conn.Dispose();
    }

    // ── the DPA gate ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE RULE THAT MADE MATT'S OWN DOCUMENT SAFE TO SHIP. A draft is never served and never
    /// acceptable. WP-signup §4.3: recording acceptance of unpublished wording *"manufactures
    /// evidence that a client agreed to something nobody wrote"*.
    /// </summary>
    [Fact]
    public async Task An_unpublished_draft_is_neither_served_nor_acceptable()
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        var dpa = new DpaService(db);
        await dpa.SaveDraftAsync("DRAFT-2026-08", "Draft", "Not agreed wording.", "flagged", "seeder");

        Assert.Null(await dpa.GetCurrentAsync());

        // ⚠⚠ AND NOW THE CASE THAT ACTUALLY PINS THE GUARD. The assertion above passes even with the
        // published-check removed, because a draft is not current either — it was passing for a
        // reason unrelated to what it claimed to test, and a mutation run is what caught that.
        //
        // Force the dangerous state directly: CURRENT but UNPUBLISHED. That is what a hand-edited
        // row, a bad restore, or a future code path setting one flag without the other would leave,
        // and it is the only state in which the published-check is the thing standing between a
        // client and an agreement nobody finished writing.
        var draft = await db.DpaDocuments.FirstAsync();
        draft.IsCurrent = true;
        await db.SaveChangesAsync();

        Assert.Null(await dpa.GetCurrentAsync());

        var ex = await Assert.ThrowsAsync<EnrolmentException>(
            () => dpa.AcceptAsync(Guid.NewGuid(), null, "a@b.co.uk", "1.1.1.1", "ua"));
        Assert.Equal(409, ex.StatusCode);
        conn.Dispose();
    }

    /// <summary>⚠ A published version is immutable — editing text somebody accepted rewrites the
    /// evidence.</summary>
    [Fact]
    public async Task A_published_version_cannot_be_edited()
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        var dpa = new DpaService(db);
        await dpa.SaveDraftAsync("2026-01", "DPA", "Version one.", null, "op");
        await dpa.PublishAsync("2026-01", "op");

        var ex = await Assert.ThrowsAsync<EnrolmentException>(
            () => dpa.SaveDraftAsync("2026-01", "DPA", "Quietly different.", null, "op"));
        Assert.Equal(409, ex.StatusCode);
        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ ACCEPTING "2026-01" IS NOT ACCEPTING "2027-04". Publishing a new current version must
    /// leave a tenant that accepted the old one showing as NOT current — otherwise a re-issued DPA
    /// is silently treated as already agreed, which is worse than never having asked.
    /// </summary>
    [Fact]
    public async Task Publishing_a_new_version_leaves_an_earlier_acceptance_behind()
    {
        var conn = NewDb();
        var tenant = Guid.NewGuid();
        using var db = Ctx(conn);
        var dpa = new DpaService(db);

        await dpa.SaveDraftAsync("2026-01", "DPA", "One.", null, "op");
        await dpa.PublishAsync("2026-01", "op");
        await dpa.AcceptAsync(tenant, null, "owner@shop.co.uk", "1.1.1.1", "ua");

        var before = await dpa.StatusAsync(tenant);
        Assert.True(before.CurrentAccepted);

        await dpa.SaveDraftAsync("2027-04", "DPA", "Two.", null, "op");
        await dpa.PublishAsync("2027-04", "op");

        var after = await dpa.StatusAsync(tenant);
        Assert.True(after.Accepted, "The old acceptance is still on record.");
        Assert.Equal("2026-01", after.Version);
        Assert.Equal("2027-04", after.CurrentVersion);
        Assert.False(after.CurrentAccepted, "They have NOT accepted the new version.");
        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE OPERATOR ROUTE MUST NEVER LOOK LIKE THE CLIENT'S. This is Matt's objection in one
    /// assertion — a manual record carries the operator's name and NO client email, so no surface
    /// can render it as "accepted by the client".
    /// </summary>
    [Fact]
    public async Task An_operator_recorded_dpa_is_distinguishable_from_a_client_accepted_one()
    {
        var conn = NewDb();
        var clientTenant = Guid.NewGuid();
        var paperTenant = Guid.NewGuid();
        using var db = Ctx(conn);
        var dpa = new DpaService(db);
        await dpa.SaveDraftAsync("2026-01", "DPA", "Text.", null, "op");
        await dpa.PublishAsync("2026-01", "op");

        await dpa.AcceptAsync(clientTenant, null, "owner@shop.co.uk", "203.0.113.5", "a-browser");
        await dpa.RecordManuallyAsync(paperTenant, "2026-01", "matt", "Signed copy in the folder.");

        var client = await dpa.StatusAsync(clientTenant);
        Assert.Equal("owner@shop.co.uk", client.AcceptedByEmail);
        Assert.Null(client.RecordedByOperator);

        var paper = await dpa.StatusAsync(paperTenant);
        Assert.Null(paper.AcceptedByEmail);
        Assert.Equal("matt", paper.RecordedByOperator);
        conn.Dispose();
    }

    /// <summary>⚠ Idempotent: a double-clicked Accept is one act and must leave one evidence row.
    /// "How many times did they agree?" is not a question a compliance record should answer twice.</summary>
    [Fact]
    public async Task Accepting_twice_records_one_acceptance()
    {
        var conn = NewDb();
        var tenant = Guid.NewGuid();
        using var db = Ctx(conn);
        var dpa = new DpaService(db);
        await dpa.SaveDraftAsync("2026-01", "DPA", "Text.", null, "op");
        await dpa.PublishAsync("2026-01", "op");

        await dpa.AcceptAsync(tenant, null, "a@b.co.uk", "1.1.1.1", "ua");
        await dpa.AcceptAsync(tenant, null, "a@b.co.uk", "1.1.1.1", "ua");

        Assert.Equal(1, await db.DpaAcceptances.CountAsync(a => a.TenantId == tenant));
        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE TEST THAT WOULD HAVE CAUGHT THE LIVE 500, AND DID NOT EXIST.
    ///
    /// Every other test here builds the context through a helper that sets `CurrentUser = "test"`.
    /// Production does not: an anonymous signup has nobody signed in, `RepositoryContext.SaveMethods`
    /// refuses to write without an audit user, and the endpoint threw
    /// `ObjectIdMissingException("CurrentUser not defined!")` the first time it was called on the
    /// live server.
    ///
    /// ⚠ The lesson is the shape, not the field: a test double configured better than reality
    /// proves the double works. This one deliberately builds the context the way the request does.
    /// </summary>
    [Fact]
    public async Task Signup_works_with_no_signed_in_user_which_is_what_anonymous_means()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var seed = Ctx(conn)) seed.Database.EnsureCreated();

        // ⚠ NO CurrentUser — exactly what the DI container hands an anonymous request.
        using var db = new MySqlDbContext(
            new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
            new FixedTenantContext(Guid.Empty));
        Assert.True(string.IsNullOrEmpty(db.CurrentUser), "The premise: no audit user is set.");

        var svc = new TenantApplicationService(db, new DisposableEmailDomains(null));
        var res = await svc.ApplyAsync(Req(), "203.0.113.7");
        Assert.True(res.IsNew);

        // and the follow-on anonymous step saves too
        var (outcome, _) = await svc.VerifyAsync(res.VerifyToken);
        Assert.Equal(TenantApplicationService.VerifyOutcome.Ok, outcome);

        conn.Dispose();
    }

    // ── the front door's switch, and the audit trail ─────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ CLOSED IS THE DEFAULT, AND IT IS THE OPPOSITE OF EVERY OTHER PlatformFlag.
    ///
    /// The rest are kill switches — absent means the feature works. A front door read that way is
    /// OPEN until somebody remembers to close it, which is exactly the state this was written to
    /// end: the signup API was live and public the moment it shipped, with no landing page anywhere,
    /// and a POST from outside created an application on 2026-08-23.
    /// </summary>
    [Fact]
    public async Task The_front_door_is_closed_when_no_flag_exists()
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        Assert.False(await new SignupGate(db).IsOpenAsync(),
            "With no flag row at all the door must be CLOSED. Absent must not mean open — that is "
            + "how an unauthenticated endpoint ends up live before anybody decided it should be.");
        conn.Dispose();
    }

    [Theory]
    [InlineData(false, false)]   // explicitly disabled
    [InlineData(true, true)]     // deliberately opened
    public async Task The_front_door_follows_the_flag(bool enabled, bool expectedOpen)
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        db.PlatformFlags.Add(new PlatformFlag
        {
            FlagName = SignupGate.FlagName, Enabled = enabled, UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Equal(expectedOpen, await new SignupGate(db).IsOpenAsync());
        conn.Dispose();
    }

    /// <summary>⚠ A different flag name must not open this one. Pinned because "signup" and
    /// "signup.public" are one typo apart and the failure would be silent and open.</summary>
    [Fact]
    public async Task Another_flag_being_on_does_not_open_signup()
    {
        var conn = NewDb();
        using var db = Ctx(conn);
        db.PlatformFlags.Add(new PlatformFlag { FlagName = "signup", Enabled = true, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.False(await new SignupGate(db).IsOpenAsync());
        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE DoD LINE "every state change is audited", which was NOT done when WP-signup first
    /// shipped — all three controllers had zero audit calls while Platform → Quarantine, the screen
    /// §6 says to model on, uses the helper.
    ///
    /// ⚠ And the audit row must land in the SAME SaveChanges as the mutation. `AuditExtensions.Audit`
    /// only ADDS to the change tracker on purpose — *"so the trail can never disagree with the
    /// data"* — so auditing from a controller after the service returned would leave the row unsaved.
    /// That is why these live in the services, and why this test reads the database rather than a mock.
    /// </summary>
    [Fact]
    public async Task Approving_and_rejecting_are_audited()
    {
        var conn = NewDb();
        var approved = await VerifiedApplicationAsync(conn, "Audit Shop");
        var refused = await VerifiedApplicationAsync(conn, "Refused Shop");
        await PublishDpaAsync(conn);
        using (var db = Ctx(conn)) await Svc(db).AcceptDpaAsync(approved, "2026-01", "198.51.100.1", "ua");

        using (var db = Ctx(conn))
        {
            var (prov, dpa) = Services(db);
            await Svc(db).ApproveAsync(approved, prov, dpa, "matt", "password1");
        }
        using (var db = Ctx(conn)) await Svc(db).RejectAsync(refused, "Not a real shop.", "matt");

        using var check = Ctx(conn);
        var actions = await check.AuditLogs.AsNoTracking().Select(a => a.Action).ToListAsync();

        Assert.Contains("signup.application.approved", actions);
        Assert.Contains("signup.application.rejected", actions);
        Assert.Contains("signup.dpa.accepted", actions);
        Assert.Contains("dpa.published", actions);
        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE TWO DPA ROUTES MUST NOT FLATTEN INTO ONE ACTION NAME. An audit trail that records
    /// "the operator wrote this down" and "the client agreed" identically has destroyed the
    /// distinction the whole work package exists to make.
    /// </summary>
    [Fact]
    public async Task The_client_route_and_the_operator_route_audit_differently()
    {
        var conn = NewDb();
        var clientTenant = Guid.NewGuid();
        var paperTenant = Guid.NewGuid();
        using var db = Ctx(conn);
        var dpa = new DpaService(db);
        await dpa.SaveDraftAsync("2026-01", "DPA", "Text.", null, "op");
        await dpa.PublishAsync("2026-01", "op");

        await dpa.AcceptAsync(clientTenant, null, "owner@shop.co.uk", "203.0.113.9", "a-browser");
        await dpa.RecordManuallyAsync(paperTenant, "2026-01", "matt", "Signed copy in the folder.");

        var actions = await db.AuditLogs.AsNoTracking().Select(a => a.Action).ToListAsync();
        Assert.Contains("dpa.accepted", actions);
        Assert.Contains("dpa.recorded-manually", actions);
        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE SWEEP MUST RAISE dpa-missing FOR A TENANT THAT ACCEPTED AN OLDER VERSION. The DoD line
    /// says *"publishing a new version re-raises dpa-missing for tenants that have not accepted it"*,
    /// and until this test existed only the STATUS was proven — which is not the same as proving the
    /// signal fires. A re-issued DPA silently treated as already agreed is worse than never asking.
    ///
    /// ⚠ The tenant must be non-sandbox and not Closed, or the sweep skips it by design.
    /// </summary>
    [Fact]
    public async Task Publishing_a_new_version_re_raises_dpa_missing_at_sweep_level()
    {
        var conn = NewDb();
        var tenantId = Guid.NewGuid();

        using (var db = Ctx(conn))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "Behind Shop", Status = 1, Plan = "standard",
                Entitlements = "[]", ConnectionRef = "", CreatedAtUtc = DateTime.UtcNow, IsSandbox = false,
            });
            await db.SaveChangesAsync();

            var dpa = new DpaService(db);
            await dpa.SaveDraftAsync("2026-01", "DPA", "One.", null, "op");
            await dpa.PublishAsync("2026-01", "op");
            await dpa.AcceptAsync(tenantId, null, "owner@behind.co.uk", "1.1.1.1", "ua");
        }

        // Up to date on 2026-01 → the signal must be CLEAR.
        using (var db = Ctx(conn))
        {
            await ComplianceSweep.EvaluateAsync(db, new NullOperatorAlerter(), DateTime.UtcNow);
            Assert.False(await db.TenantSignals.AsNoTracking()
                .AnyAsync(s => s.TenantId == tenantId && s.Signal == TenantSignals.DpaMissing && s.ClearedAtUtc == null),
                "A tenant that has accepted the current version must not be flagged.");
        }

        // A newer version is published; they have not accepted THAT one.
        using (var db = Ctx(conn))
        {
            var dpa = new DpaService(db);
            await dpa.SaveDraftAsync("2027-04", "DPA", "Two.", null, "op");
            await dpa.PublishAsync("2027-04", "op");
        }

        using (var db = Ctx(conn))
        {
            await ComplianceSweep.EvaluateAsync(db, new NullOperatorAlerter(), DateTime.UtcNow);
            Assert.True(await db.TenantSignals.AsNoTracking()
                .AnyAsync(s => s.TenantId == tenantId && s.Signal == TenantSignals.DpaMissing && s.ClearedAtUtc == null),
                "Publishing a new version must re-raise dpa-missing for a tenant that has only "
                + "accepted an earlier one. Otherwise a re-issued DPA is silently treated as agreed.");
        }
        conn.Dispose();
    }

    /// <summary>An alerter that does nothing — the sweep's keyed alerts are not what is under test.</summary>
    private sealed class NullOperatorAlerter : IOperatorAlerter
    {
        public Task RaiseAsync(string alertKey, string jobName, Guid? tenantId, string kind, string message,
                               System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(string alertKey, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
    }
}
