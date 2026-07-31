using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.TillAgent.Core;

namespace Plutus.TillAgent
{
    /// <summary>
    /// FE3 "Plutus Till Agent": a loopback HTTP bridge from the browser till to the receipt printer
    /// and cash drawer, plus a tray icon to configure and test it.
    ///
    /// Security posture — a local HTTP server on a shop PC deserves the paragraph:
    ///  • Bound to 127.0.0.1 ONLY, so nothing on the network can reach it.
    ///  • Every hardware action requires the X-Agent-Token shared secret, paired once by hand.
    ///    Loopback alone is NOT enough: any web page the cashier opens can also fetch localhost,
    ///    and without the token that page could kick the drawer or waste a roll of paper.
    ///  • CORS is limited to the configured till origin.
    ///  • /status is deliberately unauthenticated and tells you nothing sensitive — the till needs
    ///    to answer "is there an agent here?" before it has been paired, and that is how the portal
    ///    learns a till PC has an agent at all (FE3.0).
    /// </summary>
    public static class Program
    {
        public static readonly string AgentVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        public const int Port = 9123;

        [STAThread]
        public static void Main()
        {
            // One agent per machine: a second instance would fail to bind the port and leave a
            // confusing tray icon that does nothing.
            using var single = new Mutex(true, "Global\\PlutusTillAgent", out var isFirst);
            if (!isFirst)
            {
                MessageBox.Show("The Plutus Till Agent is already running — look for its icon in the notification area.",
                    "Plutus Till Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = AgentConfig.Load();
            var transport = new RawSpoolerTransport();
            var state = new AgentState(config, transport);

            var web = BuildWebHost(state);
            _ = web.RunAsync();   // Kestrel on the background; the message loop owns the foreground

            ApplicationConfiguration.Initialize();
            using var tray = new TrayApp(state, web);
            Application.Run();
        }

        private static WebApplication BuildWebHost(AgentState state)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();                       // no console; the tray is the UI
            builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
            builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
                .SetIsOriginAllowed(origin =>
                    string.IsNullOrWhiteSpace(state.Config.AllowedOrigin) ||
                    string.Equals(origin, state.Config.AllowedOrigin, StringComparison.OrdinalIgnoreCase))
                .AllowAnyHeader()
                .AllowAnyMethod()));

            var app = builder.Build();
            app.UseCors();

            // ── unauthenticated: "is an agent here, and is it well?" ──
            app.MapGet("/status", () => Results.Ok(new
            {
                agentVersion = AgentVersion,
                printer = new
                {
                    name = state.Config.PrinterName,
                    online = state.Transport.IsOnline(state.Config.PrinterName),
                },
                drawerSupported = !string.IsNullOrWhiteSpace(state.Config.PrinterName),
                paired = !string.IsNullOrWhiteSpace(state.Config.Token),
                columns = state.Config.Columns,
            }));

            // ── everything that touches hardware needs the pairing token ──
            IResult Guard(HttpContext ctx)
            {
                var supplied = ctx.Request.Headers["X-Agent-Token"].ToString();
                if (string.IsNullOrWhiteSpace(supplied) || supplied != state.Config.Token)
                    return Results.Json(new { detail = "Pair this till with the agent first (Settings → Hardware)." },
                        statusCode: StatusCodes.Status401Unauthorized);
                return Results.Empty;
            }

            app.MapPost("/print", async (HttpContext ctx, PrintDocument doc) =>
            {
                var denied = Guard(ctx);
                if (denied != Results.Empty) return denied;
                return await state.PrintAsync(doc);
            });

            app.MapPost("/drawer/open", async (HttpContext ctx) =>
            {
                var denied = Guard(ctx);
                if (denied != Results.Empty) return denied;
                return await state.KickDrawerAsync();
            });

            app.MapPost("/print/test", async (HttpContext ctx) =>
            {
                var denied = Guard(ctx);
                if (denied != Results.Empty) return denied;
                return await state.PrintAsync(TestReceipt(state.Config.Columns));
            });

            return app;
        }

        /// <summary>The settings window's "Test print" — proves paper, alignment, bold, the £ sign
        /// (the classic encoding failure) and the barcode in one strip.</summary>
        public static PrintDocument TestReceipt(int columns) => new()
        {
            Columns = columns,
            Ops =
            {
                PrintOp.Line("PLUTUS TILL AGENT", PrintAlign.Centre, bold: true, large: true),
                PrintOp.Line($"v{AgentVersion}", PrintAlign.Centre),
                PrintOp.Line(),
                PrintOp.Line("Test print — not a sale", PrintAlign.Centre),
                PrintOp.Rule(),
                PrintOp.Line(EscPos.TwoColumn("Left", "Right", columns)),
                PrintOp.Line(EscPos.TwoColumn("2 x Sample item", "£12.34", columns)),
                PrintOp.Line(EscPos.TwoColumn("TOTAL", "£12.34", columns), bold: true),
                PrintOp.Rule(),
                PrintOp.Line("If the £ signs and the barcode look right,", PrintAlign.Centre),
                PrintOp.Line("this printer is good to go.", PrintAlign.Centre),
                PrintOp.Line(),
                PrintOp.Barcode("PLUTUSTEST"),
                PrintOp.Line("PLUTUSTEST", PrintAlign.Centre),
                PrintOp.Cut(),
            },
        };
    }

    /// <summary>Shared mutable state between the web host and the tray UI.</summary>
    public sealed class AgentState
    {
        public AgentConfig Config { get; }
        public IReceiptTransport Transport { get; }
        /// <summary>Last error surfaced in the tray tooltip, so a failing printer is visible without
        /// opening a log.</summary>
        public string? LastError { get; private set; }
        public DateTime? LastPrintUtc { get; private set; }
        public event Action? Changed;

        public AgentState(AgentConfig config, IReceiptTransport transport)
        {
            Config = config;
            Transport = transport;
        }

        public Task<IResult> PrintAsync(PrintDocument doc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Config.PrinterName))
                    return Task.FromResult(Fail("No printer is selected in the agent's settings."));
                doc.Columns = doc.Columns > 0 ? doc.Columns : Config.Columns;
                Transport.Send(Config.PrinterName, EscPos.Render(doc));
                LastError = null;
                LastPrintUtc = DateTime.UtcNow;
                Changed?.Invoke();
                return Task.FromResult(Results.Ok(new { printed = true }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(ex.Message));
            }
        }

        public Task<IResult> KickDrawerAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Config.PrinterName))
                    return Task.FromResult(Fail("No printer is selected in the agent's settings."));
                Transport.Send(Config.PrinterName, EscPos.DrawerKick());
                LastError = null;
                Changed?.Invoke();
                return Task.FromResult(Results.Ok(new { opened = true }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(ex.Message));
            }
        }

        private IResult Fail(string message)
        {
            LastError = message;
            Changed?.Invoke();
            // 503, not 500: this is "the hardware isn't available", which is precisely the case the
            // till must fall back from rather than treat as a bug.
            return Results.Json(new { detail = message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
