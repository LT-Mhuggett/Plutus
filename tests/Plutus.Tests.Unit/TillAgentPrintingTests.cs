using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.TillAgent.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The MAUI till's route to the receipt printer — the same route the web till has always used.
///
/// ⚠ WHY THIS EXISTS. Matt, 2026-08-10: *"I still cannot see a printer, it says wifi is turned off
/// … The webtill can see the receipt printer fine."* The two tills were on different hardware
/// routes: the web till posts a rendered document to the Plutus Till Agent, which prints through
/// the ordinary Windows queue; the MAUI till opened a `PointOfService` DevicePicker, which
/// enumerates only devices with a Windows POS driver profile and, finding none, offered the generic
/// device chrome's complaint about RADIOS. These tests pin the shared route.
/// </summary>
public class TillAgentClientTests
{
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => new(new StubHandler(respond));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(ct);
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task No_agent_on_this_pc_is_null_not_an_exception()
    {
        // A till PC without the agent refuses the connection outright. ⚠ That must look like
        // "no agent", not like a crash on the checkout path.
        var api = new TillAgentClient(Client(_ => throw new HttpRequestException("refused")));

        Assert.Null(await api.StatusAsync());
    }

    [Fact]
    public async Task A_healthy_agent_reports_its_printer()
    {
        var api = new TillAgentClient(Client(_ => Json("""
            {"agentVersion":"1.2.0","printer":{"name":"TSP100","online":true},
             "drawerSupported":true,"paired":true,"columns":42,"emulation":"star-raster"}
            """)));

        var status = await api.StatusAsync();

        Assert.NotNull(status);
        Assert.Equal("1.2.0", status!.AgentVersion);
        Assert.Equal("TSP100", status.PrinterName);
        Assert.True(status.PrinterOnline);
        Assert.True(status.Paired);
        Assert.Equal("star-raster", status.Emulation);
    }

    [Fact]
    public async Task A_body_with_no_version_is_not_an_agent()
    {
        // Something else answered on 9123. ⚠ Treating it as an agent would send print jobs — and a
        // cash-drawer kick — to an unknown process.
        var api = new TillAgentClient(Client(_ => Json("""{"hello":"world"}""")));

        Assert.Null(await api.StatusAsync());
    }

    [Fact]
    public async Task Zero_columns_falls_back_to_eighty_millimetre_paper()
    {
        // ⚠ Zero columns makes every rule and every right-aligned total zero characters wide — a
        // receipt with no prices on it.
        var api = new TillAgentClient(Client(_ => Json("""{"agentVersion":"1.0.0","columns":0}""")));

        Assert.Equal(42, (await api.StatusAsync())!.Columns);
    }

    [Fact]
    public async Task Printing_carries_the_pairing_token()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var api = new TillAgentClient(new HttpClient(handler));

        Assert.True(await api.PrintAsync(new PrintDocument(), "ABCD-1234"));
        Assert.Equal("ABCD-1234", handler.Last!.Headers.GetValues("X-Agent-Token").Single());
        Assert.Equal("http://127.0.0.1:9123/print", handler.Last.RequestUri!.ToString());
    }

    [Fact]
    public async Task Op_kinds_go_on_the_wire_as_NUMBERS()
    {
        // ⚠ THE ONE THAT WOULD LOOK LIKE A BROKEN PRINTER. The agent is a minimal API, which
        // installs no string-enum converter, and the web till hard-codes the numbers (OP_TEXT = 0,
        // OP_BARCODE = 2). Serialising `Kind` as "Text" is a 400 the operator would read as "the
        // printer isn't working".
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var api = new TillAgentClient(new HttpClient(handler));

        var doc = new PrintDocument { Columns = 42 };
        doc.Ops.Add(PrintOp.Line("hello", PrintAlign.Centre, bold: true));
        doc.Ops.Add(PrintOp.Barcode("ABC123"));

        await api.PrintAsync(doc, "tok");

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var ops = sent.RootElement.GetProperty("ops");
        Assert.Equal(JsonValueKind.Number, ops[0].GetProperty("kind").ValueKind);
        Assert.Equal((int)PrintOpKind.Text, ops[0].GetProperty("kind").GetInt32());
        Assert.Equal((int)PrintAlign.Centre, ops[0].GetProperty("align").GetInt32());
        Assert.Equal((int)PrintOpKind.Barcode, ops[1].GetProperty("kind").GetInt32());
    }

    [Fact]
    public async Task An_unpaired_agent_refuses_and_the_till_carries_on()
    {
        // ⚠ 401 means "pair me", not "unauthorised operator" — and either way a sale must complete.
        var api = new TillAgentClient(Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        Assert.False(await api.PrintAsync(new PrintDocument(), ""));
        Assert.False(await api.OpenDrawerAsync(""));
        Assert.False(await api.TestPrintAsync(""));
    }

    [Fact]
    public async Task A_wedged_agent_does_not_throw_into_a_sale()
    {
        var api = new TillAgentClient(Client(_ => throw new IOException("socket died mid-write")));

        Assert.False(await api.PrintAsync(new PrintDocument(), "tok"));
    }

    [Fact]
    public async Task The_drawer_has_its_own_endpoint()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var api = new TillAgentClient(new HttpClient(handler));

        Assert.True(await api.OpenDrawerAsync("tok"));
        Assert.Equal("http://127.0.0.1:9123/drawer/open", handler.Last!.RequestUri!.ToString());
    }
}

/// <summary>
/// The receipt layout, which now exists TWICE — here and in the web till's `receiptDoc.ts`.
///
/// ⚠ That duplication is recorded in `till-design.md` C2 and these tests are what pins it. The
/// browser cannot run C# and MAUI cannot run the TypeScript, so the layout genuinely cannot be
/// shared; what CAN be shared is a test suite that describes the paper, so a change made to one
/// side and not the other fails here instead of printing two different receipts in two shops.
/// </summary>
public class ReceiptDocumentTests
{
    private static ReceiptDocInput Sale(long gross = 1000, long ex = 833) => new()
    {
        StoreName = "Kapow Comics",
        WhenLocal = new DateTime(2026, 8, 10, 14, 30, 0),
        Lines = new[] { new ReceiptDocLine("Batman #1", 2, 500, 1000) },
        ExPence = ex,
        GrossPence = gross,
        Tenders = new[] { new ReceiptDocTender("Cash", 2000, 1000) },
        SaleId = "abc123",
        Columns = 42,
    };

    private static IEnumerable<string> TextOf(PrintDocument doc)
        => doc.Ops.Where(o => o.Kind == PrintOpKind.Text).Select(o => o.Text ?? "");

    [Fact]
    public void A_refund_says_so_in_words_not_just_a_minus_sign()
    {
        // ⚠ Reading a receipt must not depend on spotting a minus sign — on the shop's copy least
        // of all, since that is what gets pulled at a query months later.
        var doc = ReceiptDocumentBuilder.Build(Sale(gross: -1399, ex: -1166));

        Assert.Contains("** REFUND **", TextOf(doc));
    }

    [Fact]
    public void A_sale_does_not_claim_to_be_a_refund()
    {
        Assert.DoesNotContain("** REFUND **", TextOf(ReceiptDocumentBuilder.Build(Sale())));
    }

    [Fact]
    public void The_barcode_is_the_sale_id_in_upper_case()
    {
        // ⚠ Code 39 has no lower case, and this barcode is the ONLY way a customer's receipt ever
        // finds its sale again — for a reprint or a receipt-led refund.
        var doc = ReceiptDocumentBuilder.Build(Sale());

        var barcode = Assert.Single(doc.Ops.Where(o => o.Kind == PrintOpKind.Barcode));
        Assert.Equal("ABC123", barcode.Text);
    }

    [Fact]
    public void A_store_that_turns_the_barcode_off_still_gets_the_id_in_text()
    {
        var doc = ReceiptDocumentBuilder.Build(Sale() with { ShowBarcode = false });

        Assert.Empty(doc.Ops.Where(o => o.Kind == PrintOpKind.Barcode));
        Assert.Contains("abc123", TextOf(doc));
    }

    [Fact]
    public void Change_is_printed_only_when_there_is_change()
    {
        Assert.Contains(TextOf(ReceiptDocumentBuilder.Build(Sale())), t => t.StartsWith("Change"));

        var exact = Sale() with { Tenders = new[] { new ReceiptDocTender("Card", 1000) } };
        Assert.DoesNotContain(TextOf(ReceiptDocumentBuilder.Build(exact)), t => t.StartsWith("Change"));
    }

    [Fact]
    public void Tax_is_the_difference_between_gross_and_ex_never_a_re_computation()
    {
        // ⚠ The committed payload's figures, not a second opinion. The customer's paper and the
        // platform's record must not be able to disagree by a penny.
        var doc = ReceiptDocumentBuilder.Build(Sale(gross: 1000, ex: 833));

        Assert.Contains(TextOf(doc), t => t.StartsWith("Tax") && t.EndsWith("£1.67"));
        Assert.Contains(TextOf(doc), t => t.StartsWith("TOTAL") && t.EndsWith("£10.00"));
    }

    [Fact]
    public void The_name_is_truncated_and_the_money_never_is()
    {
        var padded = ReceiptDocumentBuilder.TwoColumn(new string('x', 80), "£1234.56", 42);

        Assert.Equal(42, padded.Length);
        Assert.EndsWith("£1234.56", padded);
    }

    [Fact]
    public void Money_is_pounds_sterling_whatever_windows_thinks()
    {
        // ⚠ A till whose Windows is set to de-DE must not print "4,99 €" on a UK VAT receipt.
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("£4.99", ReceiptDocumentBuilder.Gbp(499));
            Assert.Equal("-£4.99", ReceiptDocumentBuilder.Gbp(-499));
        }
        finally { CultureInfo.CurrentCulture = was; }
    }

    [Fact]
    public void An_offline_sale_says_so_on_the_paper()
    {
        // Until the outbox drains, the customer's copy is the only evidence the sale happened.
        var doc = ReceiptDocumentBuilder.Build(Sale() with { Queued = true });

        Assert.Contains(TextOf(doc), t => t.Contains("taken offline"));
    }

    [Fact]
    public void A_return_line_is_labelled_on_the_paper()
    {
        var doc = ReceiptDocumentBuilder.Build(Sale() with
        {
            Lines = new[] { new ReceiptDocLine("Batman #1", 1, 500, -500, IsReturn: true) },
        });

        Assert.Contains(TextOf(doc), t => t.StartsWith("RETURN —"));
    }

    [Fact]
    public void The_paper_is_always_cut_and_the_drawer_choice_is_carried()
    {
        // ⚠ The drawer rides WITH the print job: one round trip, and it opens as the receipt starts
        // — which is what the native till does and what the operator expects.
        var doc = ReceiptDocumentBuilder.Build(Sale() with { OpenDrawer = true });

        Assert.Equal(PrintOpKind.Cut, doc.Ops[^1].Kind);
        Assert.True(doc.OpenDrawer);
    }
}
