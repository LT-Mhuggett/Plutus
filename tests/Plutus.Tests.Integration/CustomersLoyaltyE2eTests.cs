using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Identity;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// Loyalty usability: customer writes are gated on the dedicated `customers.manage` permission
/// (not portal.users.manage). An operator without it is 403'd on create; a manager holding it
/// (via a seeded built-in role) can create AND edit (the new PUT), and the edit round-trips.
/// </summary>
public class CustomersLoyaltyE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CustomersLoyaltyE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;


    /// <summary>
    /// One authenticated call, retrying a 429 — the same helper `GiftCardsE2eTests` and
    /// `GiftCardVatDecisionE2eTests` already carry, for the same reason.
    ///
    /// ⚠⚠ WP13.5 THROTTLES PER TENANT (50 rps, a one-second fixed window) AND EVERY TEST IN THIS
    /// SUITE SHARES KAPOW. So the suite has a latent fragility: adding requests anywhere can push a
    /// **different** test's second over the limit, and it fails with `TooManyRequests` while asserting
    /// something else entirely. That is exactly what happened when the credit-reason test below was
    /// added — `Loyalty_tiers_...` went red on a `BadRequest` assertion, having been rate-limited.
    ///
    /// ⚠ THE 429 IS RETRIED, NEVER ACCEPTED. `RateLimitE2eTests` proves the limiter works; nothing
    /// here weakens it. A fresh `HttpRequestMessage` per attempt — a sent one cannot be re-sent.
    /// </summary>
    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string url, string token, object body = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body);
            var resp = await SendAsync(client, req);
            if (resp.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 4) return resp;
            resp.Dispose();
            await Task.Delay(1100);   // the limiter's window is one second
        }
    }

    /// <summary>
    /// Send a prepared request, retrying a 429 — the drop-in for `SendAsync(client, req)`.
    ///
    /// ⚠⚠ A SENT REQUEST CANNOT BE RE-SENT, which is why this CLONES rather than retrying the
    /// original. That is the whole reason the raw call could not simply be wrapped, and why 24 call
    /// sites in this file were each a separate chance to be rate-limited.
    ///
    /// ⚠ WHY THIS FILE NEEDS IT AT ALL: WP13.5 throttles **50 rps per tenant** and every test in the
    /// suite shares Kapow, so requests added anywhere can push a **different** test's second over the
    /// limit — it then fails with `TooManyRequests` while asserting something else entirely, naming the
    /// wrong fault. This class grew by a dozen requests on 2026-08-18 and tipped a sibling over twice.
    ///
    /// ⚠ THE 429 IS RETRIED, NEVER ACCEPTED — `RateLimitE2eTests` proves the limiter works and must
    /// keep seeing raw 429s, which is why this is per-file rather than a handler on the shared factory.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage req)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var clone = new HttpRequestMessage(req.Method, req.RequestUri);
            foreach (var h in req.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (req.Content != null)
            {
                var bytes = await req.Content.ReadAsByteArrayAsync();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var h in req.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            var resp = await client.SendAsync(clone);
            if (resp.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 4) return resp;
            resp.Dispose();
            await Task.Delay(1100);   // the limiter's window is one second
        }
    }
    /// <summary>Assigns a user a built-in role that carries customers.manage (Store Manager).</summary>
    private Task<Guid> SeedCustomerManagerAsync() => SeedRoleAsync("Store Manager");

    /// <summary>Assigns a user any built-in role, seeding the tenant's roles first.</summary>
    private async Task<Guid> SeedRoleAsync(string roleName)
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "loyalty-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var role = await db.RbacRoles.FirstAsync(r => r.Name == roleName);
        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Plutus.SharedKernel.Uuid7.New(), TenantId = Kapow, UserId = userId,
            RoleId = role.Id, ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant,
            ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    /// <summary>
    /// WP12 / binding default 20 (Matt, 2026-08-13: *"Till operator to add new loyalty members.
    /// Supervisor to change tiers."*) — a **Cashier** may sign a member up and do nothing else to
    /// them.
    ///
    /// ⚠ THIS TEST IS THE CREATE/EDIT LINE, and it is the half that would fail silently. Widening
    /// the create gate is visible the moment a cashier tries it; accidentally widening EDIT is not
    /// — a cashier who can change an email quietly redirects somebody's account, and one who can set
    /// a tier changes every future basket that customer puts through. So the refusals are asserted,
    /// not just the permission.
    /// </summary>
    [Fact]
    public async Task A_cashier_can_ADD_a_member_but_not_edit_one_or_set_a_tier()
    {
        var client = _f.CreateClient();
        var cashier = PlutusAppFactory.OperatorTokenFor(await SeedRoleAsync("Cashier"), "pos.sell");

        // ADD → allowed, and it really is a member (a number was issued)
        Guid customerId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", cashier);
            req.Content = JsonContent.Create(new { name = $"Queue Signup {Guid.NewGuid().ToString()[..8]}" });
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            customerId = body.GetProperty("id").GetGuid();
            Assert.True(Plutus.SharedKernel.MemberNumbers.IsValid(body.GetProperty("memberNo").GetString()));
        }

        // EDIT → refused. Adding a row can be undone by deactivating it; altering one leaves no
        // trace of what it used to be.
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", cashier);
            req.Content = JsonContent.Create(new { name = "Renamed By Cashier", email = "redirected@example.com" });
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, req)).StatusCode);
        }

        // SET A TIER → refused. That is the supervisor's call, and it changes every future basket.
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId}/membership"))
        {
            req.Headers.Authorization = new("Bearer", cashier);
            req.Content = JsonContent.Create(new { tier = "Gold", autoDiscountRate = 0.10m });
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, req)).StatusCode);
        }

        // ⚠ And a Supervisor — who holds BOTH codes — can do the tier half, so the split above is a
        // deliberate line rather than the whole capability being missing.
        var supervisor = PlutusAppFactory.OperatorTokenFor(await SeedRoleAsync("Supervisor"), "pos.sell");
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId}/membership"))
        {
            req.Headers.Authorization = new("Bearer", supervisor);
            req.Content = JsonContent.Create(new { tier = "Gold", autoDiscountRate = 0.10m });
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, req)).StatusCode);   // SetMembership creates
        }
    }

    [Fact]
    public async Task Customer_writes_are_gated_on_customers_manage_and_edit_round_trips()
    {
        var client = _f.CreateClient();

        // authenticated operator (pos.sell) but NO customers.manage assignment → 403 on create
        var outsider = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            req.Content = JsonContent.Create(new { name = "Blocked" });
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, req)).StatusCode);
        }

        // a manager holding customers.manage (Store Manager) → create succeeds
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");

        Guid customerId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = "Ada Lovelace", email = "ada@example.com" });
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            customerId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetGuid();
        }

        // edit (the new PUT) → 200 and the change round-trips on GET
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = "Ada King", email = "ada@example.com", phone = "0700" });
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, req)).StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Ada King", body.GetProperty("name").GetString());
            Assert.Equal("0700", body.GetProperty("phone").GetString());
        }

        // ⚠⚠ THE AUDIT MUST SHOW WHAT IT WAS, NOT ONLY WHAT IT BECAME - Matt, 2026-08-18:
        // *"A customer needs to have a unique ID, because people can change emails over time. Audit
        // please."*
        //
        // ⚠ The audit row used to carry the REQUEST BODY, which is the destination only. An audit
        // that records `ada@example.com` and nothing else cannot tell you an email CHANGED, let alone
        // from what - and an email change is the exact event the ruling is about. The identity is
        // `Customer.Id`, never the email, which is what makes editing one safe at all.
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var row = await db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.Action == "customer.update" && a.EntityId == customerId.ToString())
                .OrderByDescending(a => a.Id).FirstOrDefaultAsync();

            Assert.NotNull(row);
            var payload = row!.DetailJson ?? "";

            // Both halves, and the OLD name is the one that proves the point.
            Assert.Contains("Ada Lovelace", payload);
            Assert.Contains("Ada King", payload);
        }

        // the outsider still cannot edit
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            req.Content = JsonContent.Create(new { name = "Hacked" });
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, req)).StatusCode);
        }
    }

    /// <summary>FE2 (DoD): a new customer is given a unique membership number, and scanning that
    /// card (the "C…" barcode payload) or typing the number resolves to exactly that customer —
    /// which is what makes scan-to-attach work at the till, since scanners are keyboard-wedge into
    /// the ordinary customer search.</summary>
    [Fact]
    public async Task Member_numbers_are_issued_and_a_scanned_card_finds_its_customer()
    {
        var client = _f.CreateClient();
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");
        var name = $"Card Carrier {Guid.NewGuid().ToString()[..8]}";

        Guid custId;
        string memberNo;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name });
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            custId = body.GetProperty("id").GetGuid();
            memberNo = body.GetProperty("memberNo").GetString()!;
        }
        Assert.True(Plutus.SharedKernel.MemberNumbers.IsValid(memberNo), memberNo);

        // the detail read exposes the number and the barcode payload to print on a card
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers/{custId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var body = JsonDocument.Parse(await (await SendAsync(client, req)).Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(memberNo, body.GetProperty("memberNo").GetString());
            Assert.Equal("C" + memberNo, body.GetProperty("memberBarcode").GetString());
        }

        async Task<System.Collections.Generic.List<Guid>> SearchAsync(string term)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers?search={Uri.EscapeDataString(term)}");
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement
                .EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        }

        // scan the card (barcode payload), type the bare number, or type just the sequence
        Assert.Contains(custId, await SearchAsync("C" + memberNo));
        Assert.Contains(custId, await SearchAsync(memberNo));
        Assert.Contains(custId, await SearchAsync(memberNo[..6]));
        // a mis-keyed digit must NOT silently attach the wrong customer
        var mistyped = memberNo[..5] + (memberNo[5] == '7' ? '8' : '7') + memberNo[^1];
        Assert.DoesNotContain(custId, await SearchAsync(mistyped));

        // a second customer gets a different number
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = name + " II" });
            var body = JsonDocument.Parse(await (await SendAsync(client, req)).Content.ReadAsStringAsync()).RootElement;
            Assert.NotEqual(memberNo, body.GetProperty("memberNo").GetString());
        }
    }

    /// <summary>FE1 (DoD): the tier catalogue is gated on customers.manage; names are unique;
    /// assigning a tier derives name/rate/renewal from it; and re-rating the tier moves every
    /// member of it at once (live-follow) without re-assignment.</summary>
    [Fact]
    public async Task Loyalty_tiers_are_gated_unique_and_live_follow_their_members()
    {
        var client = _f.CreateClient();
        var outsider = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");
        var suffix = Guid.NewGuid().ToString()[..8]; // tier names are tenant-unique across cases

        // gated: no customers.manage → 403
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            req.Content = JsonContent.Create(new { name = $"Blocked-{suffix}", autoDiscountRate = 0.1m });
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, req)).StatusCode);
        }

        // create a tier (24-month duration so the renewal date is unmistakably tier-derived)
        var tierName = $"Gold-{suffix}";
        Guid tierId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = tierName, autoDiscountRate = 0.10m, durationMonths = 24 });
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            tierId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        // duplicate name (different case) → 409
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = tierName.ToLower(), autoDiscountRate = 0.2m });
            Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, req)).StatusCode);
        }

        // rate out of range → 400
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Bad-{suffix}", autoDiscountRate = 1.5m });
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, req)).StatusCode);
        }

        // a customer assigned the tier by id — no tier name or rate in the body at all
        Guid custId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Tiered Tina {suffix}" });
            var resp = await SendAsync(client, req);
            custId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{custId}/membership"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { tierId });
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, req)).StatusCode);
        }

        async Task<JsonElement> MembershipAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers/{custId}");
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await SendAsync(client, req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("membership").Clone();
        }

        var m = await MembershipAsync();
        Assert.Equal(tierName, m.GetProperty("tier").GetString());
        Assert.Equal(0.10m, m.GetProperty("autoDiscountRate").GetDecimal());
        Assert.Equal(tierId, m.GetProperty("tierId").GetGuid());
        // renewal came from the tier's 24-month duration, not the legacy +1 year default
        var renewal = DateOnly.Parse(m.GetProperty("renewalDay").GetString()!);
        Assert.True(renewal > DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(18), $"renewal {renewal} should be ~24 months out");

        // re-rate + rename the tier → the member follows both, with no re-assignment
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/loyalty/tiers/{tierId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Gold Plus-{suffix}", autoDiscountRate = 0.15m });
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, req)).StatusCode);
        }

        m = await MembershipAsync();
        Assert.Equal($"Gold Plus-{suffix}", m.GetProperty("tier").GetString());
        Assert.Equal(0.15m, m.GetProperty("autoDiscountRate").GetDecimal());

        // the loyalty list shows the same live values, and the tier reports its member count
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/loyalty"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var body = JsonDocument.Parse(await (await SendAsync(client, req)).Content.ReadAsStringAsync()).RootElement;
            var row = body.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("id").GetGuid() == custId);
            Assert.Equal($"Gold Plus-{suffix}", row.GetProperty("tier").GetString());
            Assert.Equal(0.15m, row.GetProperty("autoDiscountRate").GetDecimal());
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var tiers = JsonDocument.Parse(await (await SendAsync(client, req)).Content.ReadAsStringAsync()).RootElement;
            var tier = tiers.EnumerateArray().First(t => t.GetProperty("id").GetGuid() == tierId);
            Assert.Equal(1, tier.GetProperty("memberCount").GetInt32());
        }

        // an inactive tier can no longer be assigned
        using (var resp = await Send(client, HttpMethod.Put, $"/api/v1/loyalty/tiers/{tierId}", manager,
                   new { name = $"Gold Plus-{suffix}", autoDiscountRate = 0.15m, active = false }))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // ⚠ THROUGH `Send`, WHICH RETRIES A 429. This assertion is the one that went red when the
        // credit-reason test was added to this class: it was rate-limited and reported
        // `TooManyRequests` while asserting `BadRequest`, so the failure named the wrong fault
        // entirely. See `Send`'s header — the suite shares one tenant and the limiter is per second.
        using (var resp = await Send(client, HttpMethod.Post, $"/api/v1/customers/{custId}/membership", manager,
                   new { tierId }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        // ...but its existing member keeps working (deactivation is not deletion)
        m = await MembershipAsync();
        Assert.Equal(0.15m, m.GetProperty("autoDiscountRate").GetDecimal());
    }

    /// <summary>
    /// ⚠⚠ MATT'S RULING, 2026-08-18: *"Store credit needs to be for a KNOWN customer. Adding credit
    /// needs to have a reason and be viewable in the customers history."*
    ///
    /// ⚠ A REASON WAS OPTIONAL AND SUBSTITUTED. The endpoint did `body.Reason?.Trim() ?? "grant"` and
    /// the portal sent `issueReason || "goodwill grant"` — so credit could be put on somebody's account
    /// with **no reason anybody typed**, and the history would then show a plausible-looking word that
    /// means nothing. That is worse than a blank: it reads as an audit trail.
    ///
    /// ⚠ THE KNOWN-CUSTOMER HALF IS STRUCTURAL and asserted here too: the route carries the customer,
    /// so an unknown one is a 404. There is deliberately no anonymous credit — a bearer instrument is
    /// what a GIFT CARD is (WP13), and credit is a liability the shop owes a NAMED person.
    ///
    /// ⚠ AND THE REASON MUST COME BACK OUT. Storing it is only half the ruling; `GET .../credit` is
    /// what makes it *"viewable in the customers history"*, so the round-trip is asserted rather than
    /// assumed.
    ///
    /// ⚠ Every call goes through `Send`, which retries a 429 — see its header. Adding this test is what
    /// pushed a sibling over the per-tenant limiter.
    /// </summary>
    [Fact]
    public async Task Granting_credit_needs_a_known_customer_and_a_real_reason()
    {
        var client = _f.CreateClient();
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");

        Guid customerId;
        using (var resp = await Send(client, HttpMethod.Post, "/api/v1/customers", manager,
                   new { name = "Grace Hopper" }))
        {
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            customerId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetGuid();
        }

        // ⚠ AN UNKNOWN CUSTOMER IS A 404 — there is no anonymous credit to fall back to.
        using (var resp = await Send(client, HttpMethod.Post,
                   $"/api/v1/customers/{Guid.NewGuid()}/credit/issue", manager,
                   new { amountPence = 500, reason = "goodwill" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // ⚠⚠ NO REASON ⇒ REFUSED. Three shapes, because all three reached the old default: absent,
        // empty, and whitespace.
        foreach (var body in new object[]
                 {
                     new { amountPence = 500 },
                     new { amountPence = 500, reason = "" },
                     new { amountPence = 500, reason = "   " },
                 })
        {
            using var resp = await Send(client, HttpMethod.Post,
                $"/api/v1/customers/{customerId}/credit/issue", manager, body);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Contains("reason", await resp.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        // A real reason succeeds.
        using (var resp = await Send(client, HttpMethod.Post,
                   $"/api/v1/customers/{customerId}/credit/issue", manager,
                   new { amountPence = 500, reason = "damaged comic, agreed with Matt" }))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // ⚠ AND IT IS VISIBLE AFTERWARDS — the other half of the ruling.
        using (var resp = await Send(client, HttpMethod.Get,
                   $"/api/v1/customers/{customerId}/credit", manager))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(500, body.GetProperty("balancePence").GetInt64());

            var entries = body.GetProperty("entries");
            Assert.Equal(1, entries.GetArrayLength());
            Assert.Equal("damaged comic, agreed with Matt", entries[0].GetProperty("reason").GetString());
        }
    }

    /// <summary>
    /// ⚠⚠ A JUST-ADDED MEMBER MUST BE FINDABLE ON THE LOYALTY LIST. Matt, first hand-run of G38
    /// (2026-08-18): *"add a new member, did not work ... saying it added, but its not"*. It HAD been
    /// added — the membership number in that confirmation is server-minted — but `GET /api/v1/loyalty`
    /// returned only customers holding a Membership OR a CreditAccount, and somebody who has just
    /// signed up has **neither**.
    ///
    /// ⚠⚠ AND IT WAS A DEAD END, not merely a confusing screen: **Set tier picks from the rows on
    /// screen**, so a new member could never be given a tier from the till. Add, vanish, cannot promote.
    ///
    /// ⚠ THE DEFAULT VIEW MUST STAY NARROW. With no search this is still "members and credit
    /// holders", which is what the tab is for — so both halves are asserted here. Widening only on an
    /// explicit search is the whole design.
    /// </summary>
    [Fact]
    public async Task A_new_member_with_no_tier_and_no_credit_is_findable_by_search()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedCustomerManagerAsync(), "pos.sell");
        var name = $"Susan Testerson {Guid.NewGuid().ToString()[..6]}";

        string memberNo;
        Guid customerId;
        using (var resp = await Send(client, HttpMethod.Post, "/api/v1/customers", manager, new { name }))
        {
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var b = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            customerId = b.GetProperty("id").GetGuid();
            memberNo = b.GetProperty("memberNo").GetString();
        }

        // ⚠ BY NAME - what an operator types.
        using (var resp = await Send(client, HttpMethod.Get, $"/api/v1/loyalty?search={Uri.EscapeDataString(name)}", manager))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var rows = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("rows");
            Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("id").GetGuid() == customerId);
        }

        // ⚠⚠ BY MEMBERSHIP NUMBER - what the till fills the search box with after adding, and what
        // a scanned card produces.
        using (var resp = await Send(client, HttpMethod.Get, $"/api/v1/loyalty?search={memberNo}", manager))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var rows = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("rows");
            Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("id").GetGuid() == customerId);
        }

        // ⚠⚠ AND THE UNSEARCHED LIST SHOWS THEM TOO. This assertion used to be
        // `DoesNotContain` — it pinned the very behaviour that was hiding Susan and Brian, and it
        // passed while Matt was telling me *"I still cannot see them."*
        //
        // ⚠ A TEST CAN ENCODE A DESIGN DECISION AND MAKE IT LOOK LIKE A REQUIREMENT. Mine did: I
        // widened the search, kept the default narrow "because that is what the tab is for", and wrote
        // an assertion that froze it. Every customer gets a membership number on create, so every
        // customer IS a member - the tab was denying what the Add button had just done.
        using (var resp = await Send(client, HttpMethod.Get, "/api/v1/loyalty", manager))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var rows = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("rows");
            Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("id").GetGuid() == customerId);
        }
    }
}
