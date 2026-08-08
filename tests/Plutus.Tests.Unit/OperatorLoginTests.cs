using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP8 — signing an operator in at a till with the network off.
///
/// This is a money path wearing a login screen. Every test here is a way a till could let the wrong
/// person do the wrong thing, or refuse the right person and stop a shop trading.
/// </summary>
public class OperatorLoginTests
{
    private static readonly DateTime Now = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeStore : IOperatorStore
    {
        public TillOperatorsResult? Roster;
        public Task<TillOperatorsResult?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Roster);
        public Task SaveAsync(TillOperatorsResult roster, CancellationToken ct = default) { Roster = roster; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken ct = default) { Roster = null; return Task.CompletedTask; }
    }

    private static TillOperatorDto Operator(
        string email, string password, params OperatorGrantDto[] grants)
    {
        var (hash, salt) = Pbkdf2.Hash(password);
        return new TillOperatorDto(
            Guid.NewGuid(), "Test Person", email,
            Convert.ToBase64String(hash), Convert.ToBase64String(salt), grants);
    }

    private static OperatorGrantDto Grant(string code, long? max = null,
        byte? days = null, TimeOnly? from = null, TimeOnly? to = null) =>
        new(code, max, null, null, days, from, to);

    private static (OperatorLogin Login, FakeStore Store) Build(DateTime? rosterAsOf = null, params TillOperatorDto[] ops)
    {
        var store = new FakeStore
        {
            Roster = new TillOperatorsResult(Guid.NewGuid(), rosterAsOf ?? Now, ops),
        };
        return (new OperatorLogin(store, utcNow: () => Now), store);
    }

    // ── signing in ──

    [Fact]
    public async Task A_correct_password_signs_in_against_the_CACHED_hash()
    {
        // The whole point: no network, and the same credential that works on the web till.
        var (login, _) = Build(Now, Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell)));

        var result = await login.SignInAsync("sam@kapow.test", "S3cret!");

        Assert.True(result.Succeeded);
        Assert.Equal(OfflineTrust.Full, result.Operator!.Trust);
    }

    [Fact]
    public async Task Sign_in_is_case_insensitive_on_email()
    {
        var (login, _) = Build(Now, Operator("Sam@Kapow.test", "S3cret!"));
        Assert.True((await login.SignInAsync("sam@KAPOW.test", "S3cret!")).Succeeded);
    }

    [Fact]
    public async Task A_wrong_password_fails_as_a_WRONG_PASSWORD()
    {
        var (login, _) = Build(Now, Operator("sam@kapow.test", "S3cret!"));

        var result = await login.SignInAsync("sam@kapow.test", "wrong");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailure.WrongPassword, result.Failure);
    }

    [Theory]
    [InlineData(LoginFailure.NoOperators)]
    [InlineData(LoginFailure.UnknownOperator)]
    [InlineData(LoginFailure.NoCredential)]
    public async Task Each_way_of_failing_is_DISTINGUISHABLE_from_a_wrong_password(LoginFailure expected)
    {
        // ⚠ THE REGRESSION THIS PREVENTS. Every one of these used to surface as "details not
        // correct or user does not exist" — which sent someone hunting for a typo when the real
        // answer was "this till has no staff on it" or "that account has no password yet". A login
        // screen that lies about WHY costs more support time than one that simply fails.
        var result = expected switch
        {
            LoginFailure.NoOperators =>
                await new OperatorLogin(new FakeStore(), utcNow: () => Now).SignInAsync("a@b.test", "x"),
            LoginFailure.UnknownOperator =>
                await Build(Now, Operator("sam@kapow.test", "S3cret!")).Login.SignInAsync("nobody@kapow.test", "x"),
            _ => await Build(Now, new TillOperatorDto(Guid.NewGuid(), "No Login", "staff@kapow.test",
                    null, null, Array.Empty<OperatorGrantDto>())).Login.SignInAsync("staff@kapow.test", "x"),
        };

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Failure);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task A_corrupt_cached_credential_is_not_reported_as_a_wrong_password()
    {
        var op = new TillOperatorDto(Guid.NewGuid(), "Broken", "b@kapow.test", "not-base64!!", "also-not", Array.Empty<OperatorGrantDto>());
        var (login, _) = Build(Now, op);

        var result = await login.SignInAsync("b@kapow.test", "anything");

        Assert.Equal(LoginFailure.NoCredential, result.Failure);
        Assert.Contains("damaged", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── staleness: the horizons doing real work ──

    [Fact]
    public async Task Past_the_money_out_horizon_the_operator_can_STILL_SELL()
    {
        // A shop that cannot trade is worse than a stale roster — it falls back to a cash tin and
        // produces no attributable records at all.
        var (login, _) = Build(Now.AddDays(-10),
            Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell), Grant(PermissionCatalogue.PosRefund)));

        var result = await login.SignInAsync("sam@kapow.test", "S3cret!");

        Assert.True(result.Succeeded);
        Assert.Equal(OfflineTrust.SellOnly, result.Operator!.Trust);
        Assert.True(result.Operator.Can(PermissionCatalogue.PosSell));
        // ⚠ …but refunds are gone. That is the tiering: selling survives a long outage because it
        // is worthless to a thief; turning stock back into cash does not.
        Assert.False(result.Operator.Can(PermissionCatalogue.PosRefund));
    }

    [Fact]
    public async Task Past_the_sell_horizon_sign_in_is_refused_with_a_reason()
    {
        var (login, _) = Build(Now.AddDays(-40), Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell)));

        var result = await login.SignInAsync("sam@kapow.test", "S3cret!");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailure.CredentialsTooStale, result.Failure);
        Assert.Contains("internet", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Staleness_is_judged_AFTER_the_password()
    {
        // ⚠ Order matters for two reasons: telling anyone who walks up that the till is weeks out of
        // date leaks the state of the estate, and the message someone with a bad password needs
        // first is the one about their password.
        var (login, _) = Build(Now.AddDays(-40), Operator("sam@kapow.test", "S3cret!"));

        var result = await login.SignInAsync("sam@kapow.test", "WRONG");

        Assert.Equal(LoginFailure.WrongPassword, result.Failure);
    }

    // ── permissions and their windows ──

    [Fact]
    public async Task A_ceiling_caps_the_amount_but_not_the_action()
    {
        var (login, _) = Build(Now, Operator("sup@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosRefund, max: 2000)));
        var op = (await login.SignInAsync("sup@kapow.test", "S3cret!")).Operator!;

        Assert.True(op.Can(PermissionCatalogue.PosRefund));                 // "may refund at all"
        Assert.True(op.Can(PermissionCatalogue.PosRefund, 2000));           // exactly at the ceiling
        Assert.False(op.Can(PermissionCatalogue.PosRefund, 2001));          // over it
    }

    [Fact]
    public async Task A_Saturday_only_grant_is_refused_on_a_Sunday_and_allowed_on_the_Saturday()
    {
        // ⚠ THE TRAP THIS DESIGN AVOIDS. The server does NOT pre-evaluate the window at sync time.
        // If it did, a Saturday-only supervisor synced on a Wednesday would arrive with no
        // permissions at all and keep none until the next sync — silently. The window ships raw and
        // the till judges it against its own clock at the moment of the action.
        const byte saturdayOnly = 1 << (int)DayOfWeek.Saturday;
        var (login, _) = Build(Now, Operator("sat@kapow.test", "S3cret!",
            Grant(PermissionCatalogue.PosRefund, days: saturdayOnly)));

        var op = (await login.SignInAsync("sat@kapow.test", "S3cret!")).Operator!;

        var sunday = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local);
        var saturday = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);

        Assert.False(op.Can(PermissionCatalogue.PosRefund, nowLocal: sunday));
        Assert.True(op.Can(PermissionCatalogue.PosRefund, nowLocal: saturday));
    }

    [Fact]
    public async Task A_time_window_is_enforced_against_the_local_clock()
    {
        var (login, _) = Build(Now, Operator("day@kapow.test", "S3cret!",
            Grant(PermissionCatalogue.PosVoid, from: new TimeOnly(9, 0), to: new TimeOnly(17, 0))));
        var op = (await login.SignInAsync("day@kapow.test", "S3cret!")).Operator!;

        Assert.True(op.Can(PermissionCatalogue.PosVoid, nowLocal: new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Local)));
        Assert.False(op.Can(PermissionCatalogue.PosVoid, nowLocal: new DateTime(2026, 8, 8, 18, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public async Task Grants_from_several_scopes_UNION_and_the_highest_ceiling_wins()
    {
        // A store-level £20 ceiling and a till-level £50 one must resolve to £50, not to whichever
        // arrived first.
        var (login, _) = Build(Now, Operator("mgr@kapow.test", "S3cret!",
            Grant(PermissionCatalogue.PosRefund, max: 2000),
            Grant(PermissionCatalogue.PosRefund, max: 5000)));

        var op = (await login.SignInAsync("mgr@kapow.test", "S3cret!")).Operator!;

        Assert.True(op.Can(PermissionCatalogue.PosRefund, 5000));
        Assert.False(op.Can(PermissionCatalogue.PosRefund, 5001));
    }

    [Fact]
    public async Task An_unlimited_grant_beats_a_ceiling()
    {
        var (login, _) = Build(Now, Operator("owner@kapow.test", "S3cret!",
            Grant(PermissionCatalogue.PosRefund, max: 2000),
            Grant(PermissionCatalogue.PosRefund)));

        var op = (await login.SignInAsync("owner@kapow.test", "S3cret!")).Operator!;

        Assert.True(op.Can(PermissionCatalogue.PosRefund, 1_000_000));
    }

    // ── supervisor override ──

    private async Task<SignedInOperator> SignedIn(OperatorLogin login, string email, string password) =>
        (await login.SignInAsync(email, password)).Operator!;

    [Fact]
    public async Task A_supervisor_can_authorise_what_a_cashier_cannot()
    {
        var cashier = Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell));
        var supervisor = Operator("priya@kapow.test", "Sup3r!", Grant(PermissionCatalogue.PosRefund, max: 5000));
        var (login, _) = Build(Now, cashier, supervisor);

        var sam = await SignedIn(login, "sam@kapow.test", "S3cret!");
        Assert.False(sam.Can(PermissionCatalogue.PosRefund, 2000));

        var result = await login.AuthoriseOverrideAsync(
            sam, "priya@kapow.test", "Sup3r!", PermissionCatalogue.PosRefund, 2000);

        Assert.True(result.Succeeded);
        // ⚠ BOTH names. "A refund was authorised" is worthless; naming only the supervisor would
        // quietly re-attribute the sale.
        Assert.Equal(sam.UserId, result.Granted!.RequestedByUserId);
        Assert.Equal(supervisor.UserId, result.Granted.AuthorisedByUserId);
        Assert.Equal(2000, result.Granted.AmountPence);
    }

    [Fact]
    public async Task You_cannot_authorise_your_OWN_action()
    {
        // ⚠ Otherwise "override" is just a second password prompt on the way to doing whatever you
        // liked — an escalation path, not a control.
        var op = Operator("mgr@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosRefund));
        var (login, _) = Build(Now, op);
        var mgr = await SignedIn(login, "mgr@kapow.test", "S3cret!");

        var result = await login.AuthoriseOverrideAsync(
            mgr, "mgr@kapow.test", "S3cret!", PermissionCatalogue.PosRefund, 1000);

        Assert.False(result.Succeeded);
        Assert.Equal(OverrideFailure.SamePerson, result.Failure);
    }

    [Fact]
    public async Task An_override_does_NOT_bypass_the_supervisors_own_ceiling()
    {
        // A £20 supervisor cannot wave through a £200 refund. If they could, every ceiling in the
        // system would be advisory.
        var cashier = Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell));
        var supervisor = Operator("priya@kapow.test", "Sup3r!", Grant(PermissionCatalogue.PosRefund, max: 2000));
        var (login, _) = Build(Now, cashier, supervisor);
        var sam = await SignedIn(login, "sam@kapow.test", "S3cret!");

        var result = await login.AuthoriseOverrideAsync(
            sam, "priya@kapow.test", "Sup3r!", PermissionCatalogue.PosRefund, 20_000);

        Assert.False(result.Succeeded);
        Assert.Equal(OverrideFailure.OverTheirCeiling, result.Failure);
    }

    [Fact]
    public async Task Someone_without_the_permission_at_all_is_told_THAT_not_the_ceiling()
    {
        // Two different next steps: "get someone else" vs "get someone more senior".
        var cashier = Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell));
        var other = Operator("alex@kapow.test", "Oth3r!", Grant(PermissionCatalogue.PosSell));
        var (login, _) = Build(Now, cashier, other);
        var sam = await SignedIn(login, "sam@kapow.test", "S3cret!");

        var result = await login.AuthoriseOverrideAsync(
            sam, "alex@kapow.test", "Oth3r!", PermissionCatalogue.PosRefund, 500);

        Assert.Equal(OverrideFailure.NotPermitted, result.Failure);
    }

    [Fact]
    public async Task A_wrong_supervisor_password_is_NotAuthenticated()
    {
        var cashier = Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell));
        var supervisor = Operator("priya@kapow.test", "Sup3r!", Grant(PermissionCatalogue.PosRefund));
        var (login, _) = Build(Now, cashier, supervisor);
        var sam = await SignedIn(login, "sam@kapow.test", "S3cret!");

        var result = await login.AuthoriseOverrideAsync(
            sam, "priya@kapow.test", "WRONG", PermissionCatalogue.PosRefund, 500);

        Assert.Equal(OverrideFailure.NotAuthenticated, result.Failure);
    }

    [Fact]
    public async Task An_override_does_NOT_bypass_STALENESS()
    {
        // ⚠ THE ONE THAT MATTERS MOST. A roster too old to be trusted with refunds is too old to
        // AUTHORISE one — otherwise every staleness restriction has a trivial workaround and the
        // whole tiering is decoration.
        var cashier = Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell));
        var supervisor = Operator("priya@kapow.test", "Sup3r!", Grant(PermissionCatalogue.PosRefund));
        var (login, _) = Build(Now.AddDays(-10), cashier, supervisor);   // past the money-out horizon

        var sam = await SignedIn(login, "sam@kapow.test", "S3cret!");
        Assert.Equal(OfflineTrust.SellOnly, sam.Trust);

        var result = await login.AuthoriseOverrideAsync(
            sam, "priya@kapow.test", "Sup3r!", PermissionCatalogue.PosRefund, 500);

        Assert.False(result.Succeeded);
        Assert.Equal(OverrideFailure.NotPermitted, result.Failure);
    }

    [Fact]
    public async Task An_override_is_ONE_action_not_a_mode()
    {
        // The result is a record of a single decision, carrying the exact permission and amount it
        // authorised. Nothing about it can be reused for the next line.
        var cashier = Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell));
        var supervisor = Operator("priya@kapow.test", "Sup3r!", Grant(PermissionCatalogue.PosRefund));
        var (login, _) = Build(Now, cashier, supervisor);
        var sam = await SignedIn(login, "sam@kapow.test", "S3cret!");

        var result = await login.AuthoriseOverrideAsync(
            sam, "priya@kapow.test", "Sup3r!", PermissionCatalogue.PosRefund, 750);

        Assert.Equal(PermissionCatalogue.PosRefund, result.Granted!.Permission);
        Assert.Equal(750, result.Granted.AmountPence);
        // The cashier is unchanged — no lingering elevation.
        Assert.False(sam.Can(PermissionCatalogue.PosRefund, 750));
    }

    [Fact]
    public async Task A_permission_nobody_granted_is_refused_and_so_is_an_unknown_code()
    {
        // ⚠ Fails closed. "perm:x" and PlutusPolicies.X are different namespaces and a typo between
        // them has already caused one silent outage here (the pick-notes gate) — so an unrecognised
        // code is denied, never waved through.
        var (login, _) = Build(Now, Operator("sam@kapow.test", "S3cret!", Grant(PermissionCatalogue.PosSell)));
        var op = (await login.SignInAsync("sam@kapow.test", "S3cret!")).Operator!;

        Assert.False(op.Can(PermissionCatalogue.PosRefund));
        Assert.False(op.Can("pos.refund.typo"));
        Assert.False(op.Can(""));
    }
}
