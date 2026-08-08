using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// How long a till may trust a cached credential.
///
/// The numbers here are a JUDGEMENT, and these tests exist to make sure the judgement stays the one
/// that was actually made: selling survives a long outage because a shop that cannot trade falls
/// back to a cash tin and produces no attributable records at all; money-out expires quickly
/// because expiry is the ONLY thing that ever revokes a dismissed employee on a till nothing can be
/// pushed to. Change a number here and you are changing an erasure SLA — do it deliberately.
/// </summary>
public class OfflineCredentialsTests
{
    private static readonly DateTime Now = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
    private static OfflineAssessment At(TimeSpan age) => OfflineCredentials.Assess(Now - age, Now);

    [Fact]
    public void A_fresh_credential_is_trusted_silently()
    {
        var a = At(TimeSpan.FromHours(6));

        Assert.Equal(OfflineTrust.Full, a.Trust);
        Assert.False(a.ShouldWarn);
        Assert.Equal("", a.Message);
    }

    [Fact]
    public void The_warning_arrives_days_before_anything_is_taken_away()
    {
        // A warning that first appears an hour before the cliff is decoration. Its whole job is to
        // get someone to plug the cable in while that is still enough.
        var a = At(TimeSpan.FromDays(4));

        Assert.Equal(OfflineTrust.Full, a.Trust);
        Assert.True(a.ShouldWarn);
        Assert.Contains("Reconnect", a.Message);
    }

    [Fact]
    public void Past_a_week_money_out_stops_but_the_shop_keeps_selling()
    {
        var a = At(TimeSpan.FromDays(8));

        Assert.Equal(OfflineTrust.SellOnly, a.Trust);
        Assert.True(a.MaySignIn);
        Assert.Contains("Sales work normally", a.Message);
    }

    [Fact]
    public void The_spare_till_from_the_cupboard_still_sells_three_weeks_later()
    {
        // The single most likely encounter with any of these boundaries: the backup till is powered
        // on the morning the main one dies, at the moment of maximum stress. It must trade.
        var a = At(TimeSpan.FromDays(21));

        Assert.Equal(OfflineTrust.SellOnly, a.Trust);
        Assert.True(a.MaySignIn);
    }

    [Fact]
    public void Past_a_month_offline_sign_in_is_refused()
    {
        var a = At(TimeSpan.FromDays(31));

        Assert.Equal(OfflineTrust.Refused, a.Trust);
        Assert.False(a.MaySignIn);
    }

    [Theory]
    [InlineData(6.9, OfflineTrust.Full)]
    [InlineData(7.1, OfflineTrust.SellOnly)]
    [InlineData(29.9, OfflineTrust.SellOnly)]
    [InlineData(30.1, OfflineTrust.Refused)]
    public void The_boundaries_sit_exactly_where_the_policy_says(double days, OfflineTrust expected)
    {
        Assert.Equal(expected, At(TimeSpan.FromDays(days)).Trust);
    }

    [Fact]
    public void A_clock_that_has_gone_backwards_does_not_lock_the_shop_out()
    {
        // A record stamped in the future must read as current, not as an error. Failing the other
        // way means a wrong clock stops a shop trading — the one outcome worse than a stale roster.
        var a = OfflineCredentials.Assess(Now.AddDays(3), Now);

        Assert.Equal(OfflineTrust.Full, a.Trust);
        Assert.Equal(TimeSpan.Zero, a.Age);
    }

    // ── the permission tiering ──

    [Fact]
    public void Selling_and_shouting_for_help_survive_staleness()
    {
        Assert.True(OfflineCredentials.SurvivesStaleness(PermissionCatalogue.PosSell));
        // A lone cashier on a broken till must be able to raise a ticket — doubly so when the thing
        // that is broken is the connection.
        Assert.True(OfflineCredentials.SurvivesStaleness(PermissionCatalogue.SupportTickets));
    }

    [Theory]
    [InlineData(PermissionCatalogue.PosRefund)]
    [InlineData(PermissionCatalogue.PosVoid)]
    [InlineData(PermissionCatalogue.PosPriceOverride)]
    [InlineData(PermissionCatalogue.PosNoSale)]
    [InlineData(PermissionCatalogue.PosDiscount)]
    [InlineData(PermissionCatalogue.PortalUsersManage)]
    [InlineData(PermissionCatalogue.CustomersManage)]
    [InlineData(PermissionCatalogue.GiftCardsManage)]
    public void Every_route_from_stock_to_cash_is_withdrawn_when_stale(string code)
    {
        // These are what make a stolen till worth stealing. Ringing up sales is not — that money
        // lands in the ledger.
        Assert.False(OfflineCredentials.SurvivesStaleness(code));
    }

    [Fact]
    public void The_floor_is_an_allow_list_so_a_NEW_permission_defaults_to_withdrawn()
    {
        // ⚠ The regression guard. If this ever becomes a deny-list, a permission added to the
        // catalogue next year silently survives staleness — which is the exact bug the tiering
        // exists to prevent.
        Assert.False(OfflineCredentials.SurvivesStaleness("some.permission.invented.later"));
        Assert.False(OfflineCredentials.SurvivesStaleness(""));
        Assert.False(OfflineCredentials.SurvivesStaleness(null!));
    }

    // ── sessions ──

    [Fact]
    public void A_session_never_crosses_the_business_day_rollover()
    {
        // The compliance-load-bearing half. A session spanning two days attributes the incoming
        // shift's sales to the outgoing operator — silently, in the records HMRC would ask about.
        var signedIn = new DateTime(2026, 8, 8, 20, 0, 0, DateTimeKind.Utc);
        var rollover = new DateTime(2026, 8, 8, 23, 0, 0, DateTimeKind.Utc);

        Assert.Equal(rollover, OfflineCredentials.SessionExpiresAtUtc(signedIn, rollover));
    }

    [Fact]
    public void A_long_quiet_day_still_hits_the_absolute_cap()
    {
        var signedIn = new DateTime(2026, 8, 8, 6, 0, 0, DateTimeKind.Utc);
        var rollover = new DateTime(2026, 8, 9, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(signedIn.AddHours(12), OfflineCredentials.SessionExpiresAtUtc(signedIn, rollover));
    }

    [Fact]
    public void The_lifetimes_stay_in_the_order_that_makes_them_coherent()
    {
        // idle ≤ session ≤ money-out ≤ sell, and the warning lands before the first loss. If an
        // edit ever inverts one of these the policy stops meaning anything, and nothing else would
        // catch it.
        var p = OfflineCredentialPolicy.Default;

        Assert.True(p.IdleLock < p.MaxSession);
        Assert.True(p.MaxSession < p.MoneyOutMaxAge);
        Assert.True(p.WarnAfter < p.MoneyOutMaxAge);
        Assert.True(p.MoneyOutMaxAge < p.SellMaxAge);
    }
}
