using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **The till's half of step 28's server half — it has to actually SEND the list.**
///
/// ⚠⚠ WITHOUT THIS TEST THE WHOLE CHANGE CAN SILENTLY BECOME A NO-OP. The server withholds a
/// credential hash only for operators the till NAMES. If the till stops naming them — a refactor, a
/// dropped argument, a call site that constructs `OperatorSync` without the verifier store — the
/// roster simply arrives with every hash, exactly as before. Nothing errors, nothing logs, and the
/// hardening is gone. `RosterHashWithholding`'s server tests keep passing throughout, because they
/// send the header themselves.
///
/// ⚠ That was not hypothetical: with the send deleted, the twelve existing step-28 tests all still
/// passed. This suite is the one that fails.
///
/// ⚠ AND THE SAFE DIRECTIONS ARE PINNED TOO. No verifier store, an empty store, an unreadable store —
/// all must send NO header, so the server ships hashes and offline sign-in keeps working. The failure
/// this feature must never cause is a shop that cannot sign in.
/// </summary>
public class RosterVerifierHeaderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Sent { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"tillId":"00000000-0000-0000-0000-000000000001","asOfUtc":"2026-08-23T10:00:00Z","operators":[]}""",
                    System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class NullOperatorStore : IOperatorStore
    {
        public Task<TillOperatorsResult> LoadAsync(CancellationToken ct = default) => Task.FromResult<TillOperatorsResult>(null);
        public Task SaveAsync(TillOperatorsResult roster, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Verifiers : IDeviceVerifierStore
    {
        public List<Guid> Usable = new();
        public bool Throws;

        public Task<DeviceVerifier.Record> GetAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<DeviceVerifier.Record>(null);
        public Task SaveAsync(DeviceVerifier.Record record, CancellationToken ct = default) => Task.CompletedTask;
        public Task ForgetAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> UsableVerifierUserIdsAsync(CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("unreadable verifier store");
            return Task.FromResult<IReadOnlyList<Guid>>(Usable);
        }
    }

    private static readonly Guid Till = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static async Task<HttpRequestMessage> RefreshAsync(IDeviceVerifierStore verifiers)
    {
        var handler = new StubHandler();
        var api = new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://plutus.test") });

        await new OperatorSync(api, new NullOperatorStore(), verifiers).RefreshRosterAsync(Till);

        return Assert.Single(handler.Sent);
    }

    private static string HeaderOn(HttpRequestMessage req) =>
        req.Headers.TryGetValues(PlutusApiClient.HaveVerifiersHeader, out var v) ? string.Join(",", v) : null;

    /// <summary>⚠ THE ONE THAT MAKES THE FEATURE REAL.</summary>
    [Fact]
    public async Task The_roster_fetch_names_the_operators_this_till_can_verify()
    {
        var a = Guid.Parse("0192b8a0-0000-7000-8000-00000000000a");
        var b = Guid.Parse("0192b8a0-0000-7000-8000-00000000000b");

        var sent = await RefreshAsync(new Verifiers { Usable = { a, b } });

        var header = HeaderOn(sent);
        Assert.NotNull(header);
        Assert.Contains(a.ToString("D"), header);
        Assert.Contains(b.ToString("D"), header);
    }

    /// <summary>
    /// ⚠⚠ THE SAFE DIRECTIONS. Every one of these must send NOTHING, so the server ships every hash
    /// and a till that has just been set up — or whose store is damaged — can still sign people in.
    /// </summary>
    [Fact]
    public async Task A_till_holding_no_verifiers_sends_no_header()
        => Assert.Null(HeaderOn(await RefreshAsync(new Verifiers())));

    [Fact]
    public async Task A_till_with_no_verifier_store_at_all_sends_no_header()
        => Assert.Null(HeaderOn(await RefreshAsync(null)));

    /// <summary>⚠ An unreadable store must not fail the ROSTER. The roster is how a shop signs in;
    /// it cannot become conditional on a hardening feature being readable.</summary>
    [Fact]
    public async Task An_unreadable_verifier_store_still_refreshes_the_roster_without_a_header()
        => Assert.Null(HeaderOn(await RefreshAsync(new Verifiers { Throws = true })));
}
