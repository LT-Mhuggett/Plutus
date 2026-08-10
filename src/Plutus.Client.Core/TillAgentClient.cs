using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plutus.TillAgent.Core;

namespace Plutus.Client.Core;

/// <summary>What the agent said about itself. Mirrors the web till's <c>AgentStatus</c>.</summary>
public sealed record TillAgentStatus(
    string AgentVersion,
    string? PrinterName,
    bool PrinterOnline,
    bool DrawerSupported,
    bool Paired,
    int Columns,
    string? Emulation);

/// <summary>
/// The till's half of the Plutus Till Agent — the tray app on the till PC that owns the receipt
/// printer and the cash drawer, on <c>http://127.0.0.1:9123</c>.
///
/// ⚠ THIS EXISTS BECAUSE THE MAUI TILL COULD NOT SEE A PRINTER THE WEB TILL PRINTS ON EVERY DAY.
/// Matt, 2026-08-10: *"I still cannot see a printer, it says wifi is turned off, but I do not
/// understand what this means? The webtill can see the receipt printer fine."* Both halves of that
/// are explained by the two tills using completely different hardware routes:
///
///   • The WEB till posts a rendered document to this agent, which drives the printer through the
///     ordinary Windows print queue (ESC/POS or Star raster). Any printer Windows has a driver for
///     is a printer the web till can use — which is why it "sees the receipt printer fine".
///   • The MAUI till opened <c>Windows.Devices.Enumeration.DevicePicker</c> over the
///     <c>PointOfService</c> selector. That enumerates only devices carrying a Windows
///     PointOfService DRIVER PROFILE — a category almost no receipt printer ships — and the picker
///     is the same generic device chrome used for Bluetooth and Wi-Fi Direct, so with no matches it
///     volunteers "Wireless is turned off". ⚠ That message is about RADIOS, not about the printer,
///     and it is why the screen was unreadable: it answers a question nobody asked.
///
/// So the fix is not a better picker. It is to stop having two hardware routes. This client is the
/// shared one, in `Plutus.Client.Core` so every till gets it — see `till-design.md` Part B.
///
/// ⚠ GRACEFUL DEGRADATION IS THE RULE, the same one the web till states in `hardware.ts`: every
/// method here swallows its own failures and returns false/null. No agent, wrong token, printer
/// off, agent wedged — the till carries on selling. A shop must be able to trade with a broken
/// printer, and a receipt is never worth losing a sale over.
/// </summary>
public sealed class TillAgentClient
{
    /// <summary>Loopback only. ⚠ Not configurable by design: the agent binds 127.0.0.1 so nothing
    /// off the machine can drive a shop's cash drawer.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:9123";

    /// <summary>⚠ Short. A till PC with no agent refuses the connection instantly; this only ever
    /// guards a WEDGED agent, and it sits on the checkout path.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <summary>
    /// ⚠ Injected <see cref="HttpClient"/>, like everything else in this project — that is what
    /// makes the agent testable against a stub handler with no tray app, no printer and no Windows.
    /// </summary>
    public TillAgentClient(HttpClient http, string? baseUrl = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
    }

    /// <summary>
    /// ⚠ ASP.NET Core minimal APIs bind camelCase and do NOT install a string-enum converter, so
    /// <c>PrintOpKind</c> must go on the wire as a NUMBER. The web till hard-codes those numbers
    /// (<c>OP_TEXT = 0</c>); serialising the C# enum as a name would produce a 400 that looks
    /// exactly like "the printer is off".
    /// </summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Is an agent here, and is it well? Null means "no usable agent" — absent, refused,
    /// timed out or nonsense — and every one of those means the same thing to the till.</summary>
    public async Task<TillAgentStatus?> StatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            using var res = await _http.GetAsync($"{_baseUrl}/status", cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            var body = await res.Content.ReadFromJsonAsync<StatusBody>(Wire, cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body?.AgentVersion)) return null;

            return new TillAgentStatus(
                body!.AgentVersion!,
                body.Printer?.Name,
                body.Printer?.Online ?? false,
                body.DrawerSupported,
                body.Paired,
                // ⚠ A zero here would make every rule and every right-aligned total zero characters
                // wide. 42 is Font A on 80mm paper, the same default the agent itself carries.
                body.Columns > 0 ? body.Columns : 42,
                body.Emulation);
        }
        catch
        {
            return null; // absent, refused, timed out, garbage — all mean "no agent"
        }
    }

    /// <summary>Print a rendered document. False means it did not print, never an exception.</summary>
    public Task<bool> PrintAsync(PrintDocument doc, string token, CancellationToken ct = default)
        => PostAsync("/print", doc, token, ct);

    /// <summary>Kick the drawer on its own — a no-sale, or a cash event with nothing to print.</summary>
    public Task<bool> OpenDrawerAsync(string token, CancellationToken ct = default)
        => PostAsync<object?>("/drawer/open", null, token, ct);

    /// <summary>The agent's own test strip: paper, alignment, bold, the £ sign and a barcode.</summary>
    public Task<bool> TestPrintAsync(string token, CancellationToken ct = default)
        => PostAsync<object?>("/print/test", null, token, ct);

    private async Task<bool> PostAsync<T>(string path, T? payload, string token, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{path}");
            // ⚠ The PAIRING token, not a Plutus token. It is per till PC, never leaves the machine,
            // and the agent 401s without it — which is the agent saying "pair me", not "unauthorised".
            req.Headers.TryAddWithoutValidation("X-Agent-Token", token ?? string.Empty);
            if (payload is not null) req.Content = JsonContent.Create(payload, options: Wire);

            using var res = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false; // ⚠ never throws into a sale
        }
    }

    // ── the wire shape of GET /status, which is anonymous on the agent's side ──
    private sealed class StatusBody
    {
        public string? AgentVersion { get; set; }
        public PrinterBody? Printer { get; set; }
        public bool DrawerSupported { get; set; }
        public bool Paired { get; set; }
        public int Columns { get; set; }
        public string? Emulation { get; set; }
    }

    private sealed class PrinterBody
    {
        public string? Name { get; set; }
        public bool Online { get; set; }
    }
}
