using System;
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
/// WP14 — which card flow the operator runs.
///
/// ⚠ The trap this pins: EVERY provider reports Integrated:false today, because terminal
/// integration is greenfield across the platform. A till that switched on the provider NAME would
/// wait for a terminal that never answers, with a customer standing there. Integrated decides the
/// flow; the provider only decides the wording.
/// </summary>
public class PaymentGatewayTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public string? Json = null;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Exception? Throws;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throws != null) throw Throws;
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json ?? "null", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static PlutusApiClient Api(Handler h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://till.example") });

    // ── the rule ──

    /// <summary>⚠ THE ONE THAT MATTERS. A real provider that isn't wired yet is still the manual
    /// flow — but the screen names it and flags the integration as pending, so the cashier reaches
    /// for the terminal instead of waiting for a prompt.</summary>
    [Fact]
    public void A_chosen_provider_with_no_integration_is_still_the_manual_flow()
    {
        var d = PaymentGateway.Resolve(new ActiveGatewayDto("worldpay", "Worldpay", Integrated: false));

        Assert.Equal(CardFlow.Standalone, d.Flow);
        Assert.Equal("Worldpay", d.Label);
        Assert.True(d.PendingIntegration);
    }

    [Fact]
    public void A_wired_integration_drives_the_terminal()
    {
        var d = PaymentGateway.Resolve(new ActiveGatewayDto("worldpay", "Worldpay", Integrated: true));

        Assert.Equal(CardFlow.Integrated, d.Flow);
        Assert.False(d.PendingIntegration);
    }

    /// <summary>Null is a NORMAL input, not an error: never-been-online, failed poll, and
    /// "this tenant chose standalone" are the same instruction to a cashier.</summary>
    [Fact]
    public void No_answer_at_all_resolves_to_standalone_without_pretending_something_is_pending()
    {
        var d = PaymentGateway.Resolve(null);

        Assert.Equal(CardFlow.Standalone, d.Flow);
        Assert.Equal(PaymentGateway.StandaloneLabel, d.Label);
        // ⚠ NOT pending — there is no integration to be waiting for, and saying otherwise would put
        // a permanent "integration pending" notice on a tenant that never chose a provider.
        Assert.False(d.PendingIntegration);
    }

    [Theory]
    [InlineData("standalone")]
    [InlineData("Standalone")]   // the wire is a string; casing must not change the flow
    [InlineData("  standalone ")]
    [InlineData("")]
    [InlineData("   ")]
    public void The_standalone_provider_resolves_to_standalone_however_it_is_written(string provider)
    {
        var d = PaymentGateway.Resolve(new ActiveGatewayDto(provider, "ignored", Integrated: false));

        Assert.Equal(CardFlow.Standalone, d.Flow);
        Assert.Equal(PaymentGateway.StandaloneLabel, d.Label);
        Assert.False(d.PendingIntegration);
    }

    /// <summary>⚠ Even if the server ever marked the standalone "provider" as integrated, there is
    /// nothing to drive — standalone IS the human flow by definition.</summary>
    [Fact]
    public void Standalone_marked_integrated_is_still_the_human_flow()
    {
        var d = PaymentGateway.Resolve(
            new ActiveGatewayDto(PaymentProviderCatalogue.Standalone, "Standalone", Integrated: true));

        Assert.Equal(CardFlow.Standalone, d.Flow);
    }

    /// <summary>A provider with no label falls back to the key rather than showing "Card via ".</summary>
    [Fact]
    public void A_missing_label_falls_back_to_the_provider_key()
    {
        var d = PaymentGateway.Resolve(new ActiveGatewayDto("sumup", "  ", Integrated: false));
        Assert.Equal("sumup", d.Label);
    }

    // ── the fetch ──

    [Fact]
    public async Task Fetch_reads_the_active_gateway()
    {
        var h = new Handler { Json = """{"provider":"worldpay","label":"Worldpay","integrated":false}""" };

        var d = await PaymentGateway.FetchAsync(Api(h));

        Assert.Equal("Worldpay", d.Label);
        Assert.True(d.PendingIntegration);
    }

    /// <summary>⚠ A cosmetic lookup must never stop a card sale. Both a server fault and a dead
    /// socket resolve to standalone — the flow the till would have run anyway.</summary>
    [Fact]
    public async Task A_server_fault_resolves_to_standalone_rather_than_blocking_the_sale()
    {
        var h = new Handler { Status = HttpStatusCode.InternalServerError };

        var d = await PaymentGateway.FetchAsync(Api(h));

        Assert.Equal(CardFlow.Standalone, d.Flow);
        Assert.Equal(PaymentGateway.StandaloneLabel, d.Label);
    }

    [Fact]
    public async Task A_dead_network_resolves_to_standalone_and_never_throws()
    {
        var h = new Handler { Throws = new HttpRequestException("no route to host") };

        var d = await PaymentGateway.FetchAsync(Api(h));

        Assert.Equal(CardFlow.Standalone, d.Flow);
    }
}
