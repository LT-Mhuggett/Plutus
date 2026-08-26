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

        /// <summary>The auto-updater, so the tray's "Check for updates now" can force a look.</summary>
        public static AgentUpdater? Updater { get; private set; }

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

            // ⚠⚠ BEFORE ANYTHING ELSE, and before the tray icon exists: if auto-start is on, make
            // sure it points at THIS exe. The registration used to be written once, when the box was
            // ticked, and never revisited — so an agent ticked while running from `Downloads` kept a
            // dead path and launched nothing on every boot, with the checkbox still showing ticked.
            // ⚠ It matters more once the agent ships with the till and is replaced on a schedule.
            TrayApp.ReconcileAutoStart();

            var config = AgentConfig.Load();
            var transport = new RawSpoolerTransport();
            var state = new AgentState(config, transport);

            var web = BuildWebHost(state);
            _ = web.RunAsync();   // Kestrel on the background; the message loop owns the foreground

            // ⚠ This build has started, so the version it replaced is no longer needed as a rollback.
            // Doing it HERE rather than in the updater is deliberate: the old exe survives until the
            // new one has actually run, so a build that cannot start can be renamed back by hand.
            AgentUpdater.CleanUpAfterUpdate();

            // ⚠ Fire and forget, and never awaited: an update check must not be able to delay or
            // prevent the agent serving the till. It waits ten minutes before its first look, then
            // hourly, and only ever acts while the agent is idle — see AgentUpdater.
            Updater = new AgentUpdater(state);
            Updater.Start();

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
                // ⚠ Delegates to AgentConfig so the rule is in one place and can carry a LIST — see
                // the comment there for why a single exact string nearly took printing off the shop
                // counter when the till changed hostname.
                .SetIsOriginAllowed(origin => state.Config.IsOriginAllowed(origin))
                .AllowAnyHeader()
                .AllowAnyMethod()));

            var app = builder.Build();
            app.UseCors();

            // ── unauthenticated: "is an agent here, and is it well?" ──
            app.MapGet("/status", () =>
            {
                var pos = !string.IsNullOrWhiteSpace(state.Config.PosDeviceId);
                return Results.Ok(new
                {
                    agentVersion = AgentVersion,
                    printer = new
                    {
                        name = pos ? state.Config.PosDeviceName : state.Config.PrinterName,
                        // POS device health needs an async claim — the test print is the real
                        // check; report presence rather than lie about a probe we didn't do.
                        online = pos || state.Transport.IsOnline(state.Config.PrinterName),
                    },
                    drawerSupported = pos || !string.IsNullOrWhiteSpace(state.Config.PrinterName),
                    paired = !string.IsNullOrWhiteSpace(state.Config.Token),
                    columns = state.Config.Columns,
                    // FE3.1/3.2: which route this agent will use — the till's Settings → Hardware
                    // shows it, so "why doesn't it print" is answerable at a glance.
                    emulation = pos ? "pointofservice"
                        : EmulationResolver.Resolve(state.Config.Emulation, state.Config.PrinterName),
                });
            });

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

        /// <summary>FE3.1: TSP100-family printers are raster-only — ESC/POS bytes are at best
        /// discarded and at worst mis-parsed into runaway paper feeds. Auto mode picks the right
        /// language per printer, so the same agent drives an Epson and a TSP143 out of the box.</summary>
        private string Emulation => EmulationResolver.Resolve(Config.Emulation, Config.PrinterName);

        /// <summary>FE3.2: a configured PointOfService device always wins — it bypasses the print
        /// queue (whose futurePRNT incarnation text-renders even RAW jobs) and is the route the
        /// NatApp proved against Kapow's TSP143.</summary>
        private bool UsePos => !string.IsNullOrWhiteSpace(Config.PosDeviceId);

        /// <summary>
        /// How many hardware operations are in flight right now.
        ///
        /// ⚠⚠ THIS EXISTS FOR THE AUTO-UPDATER (2026-08-26) AND NOTHING ELSE READS IT. An agent that
        /// replaces its own executable while a receipt is halfway out of the printer is worse than an
        /// agent a version behind: the customer is standing there and the money has already moved.
        /// `AgentUpdater` refuses to hand over while this is non-zero.
        ///
        /// ⚠ `Interlocked`, because the print endpoints are served concurrently by Kestrel and this
        /// is read from a background timer.
        /// </summary>
        public int PrintsInFlight => Volatile.Read(ref _printsInFlight);
        private int _printsInFlight;

        public async Task<IResult> PrintAsync(PrintDocument doc)
        {
            Interlocked.Increment(ref _printsInFlight);
            try
            {
                if (!UsePos && string.IsNullOrWhiteSpace(Config.PrinterName))
                    return Fail("No printer is selected in the agent's settings.");
                doc.Columns = doc.Columns > 0 ? doc.Columns : Config.Columns;

                if (UsePos)
                {
                    await PosPrint.PrintAsync(Config.PosDeviceId, doc);
                }
                else if (Emulation == EmulationResolver.Gdi)
                {
                    // FE3.3: through the vendor driver as a normal job — the TSP100-family default.
                    using var bitmap = ReceiptRasterizer.RasterizeToBitmap(doc, out _, out var drawer);
                    GdiPrint.Print(Config.PrinterName, bitmap);
                    if (drawer)
                    {
                        // drawer via PointOfService (present where Star's OPOS is registered, e.g.
                        // the shop PCs that ran the NatApp); best-effort — never fails the print
                        try { await PosPrint.OpenDrawerAsync(); } catch (Exception) { /* see /drawer/open */ }
                    }
                }
                else if (Emulation == EmulationResolver.StarRasterMode)
                {
                    var rows = ReceiptRasterizer.Rasterize(doc, out var narrow, out var drawer);
                    Transport.Send(Config.PrinterName,
                        StarRaster.RenderJob(rows, new StarRaster.Options { OpenDrawer = drawer, Narrow58mm = narrow }));
                }
                else
                {
                    Transport.Send(Config.PrinterName, EscPos.Render(doc));
                }

                LastError = null;
                LastPrintUtc = DateTime.UtcNow;
                Changed?.Invoke();
                return Results.Ok(new { printed = true });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                // ⚠ In a finally, so a printer that throws still releases the guard — otherwise one
                // failed print would pin the agent as "busy" for ever and auto-update would never run.
                Interlocked.Decrement(ref _printsInFlight);
            }
        }

        public async Task<IResult> KickDrawerAsync()
        {
            // ⚠ The drawer counts as hardware in flight too. Restarting the agent between the print
            // and the kick leaves the cash drawer shut on a completed cash sale.
            Interlocked.Increment(ref _printsInFlight);
            try
            {
                if (UsePos || Emulation == EmulationResolver.Gdi)
                {
                    // GDI mode has no byte path to the DK port — the PointOfService cash drawer
                    // (registered by Star's OPOS, standard on the NatApp-era shop PCs) is the kick.
                    await PosPrint.OpenDrawerAsync();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(Config.PrinterName))
                        return Fail("No printer is selected in the agent's settings.");
                    Transport.Send(Config.PrinterName,
                        Emulation == EmulationResolver.StarRasterMode ? StarRaster.DrawerOnlyJob() : EscPos.DrawerKick());
                }
                LastError = null;
                Changed?.Invoke();
                return Results.Ok(new { opened = true });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _printsInFlight);
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
