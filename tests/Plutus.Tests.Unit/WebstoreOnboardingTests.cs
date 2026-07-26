using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plutus.Entities;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP6.1 one-click onboarding: the file secret store (config-first), CreateConnection
/// (rows + minted secret + wc-auth URL), the one-shot callback (stores keys, creates webhooks),
/// and Disconnect (removes our webhooks, disables the connection).</summary>
public class WebstoreOnboardingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(DbContextOptions<MySqlDbContext> o) =>
        new(o, new FixedTenantContext(Tenant)) { CurrentUser = "onboarding-test" };

    private static (SqliteConnection conn, DbContextOptions<MySqlDbContext> o) Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var o = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        using var db = Ctx(o);
        db.Database.EnsureCreated();
        return (conn, o);
    }

    private static FileWebstoreSecretProvider Store(out string path, Dictionary<string, string?>? config = null)
    {
        path = Path.Combine(Path.GetTempPath(), $"webstore-secrets-{Guid.NewGuid():N}.json");
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config ?? new()).Build();
        return new FileWebstoreSecretProvider(cfg, path);
    }

    private sealed class WooApiStub : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string Body)> Requests = new();
        public string ListWebhooksBody = "[]";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var body = request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/webhooks")
                ? ListWebhooksBody : "{\"id\":31}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public void File_store_round_trips_and_config_wins()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var provider = Store(out var path, new Dictionary<string, string?>
        {
            [$"Webstore:Secrets:{idA:D}"] = "whsec_from_config",
        });
        try
        {
            provider.SetWebhookSecret(idB, "whsec_from_file");
            provider.SetRestCredentials(idB, "ck_x", "cs_y");

            Assert.Equal("whsec_from_config", provider.GetWebhookSecret(idA));   // config first
            Assert.Equal("whsec_from_file", provider.GetWebhookSecret(idB));     // file fallback
            var creds = provider.GetRestCredentials(idB);
            Assert.Equal(("ck_x", "cs_y"), (creds!.ConsumerKey, creds.ConsumerSecret));
            Assert.Null(provider.GetRestCredentials(idA));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Create_connection_builds_rows_secret_and_the_wc_auth_url()
    {
        var (conn, o) = Open();
        using var _ = conn;
        var provider = Store(out var path);
        try
        {
            using var db = Ctx(o);
            var r = await WebstoreOnboarding.CreateConnectionAsync(
                db, provider, Tenant, "Second Shop Web", "https://shop2.example.test/", 1,
                "https://plutus.example.test", "https://admin.example.test/?connected=1");

            Assert.Contains("https://shop2.example.test/wc-auth/v1/authorize?app_name=Plutus&scope=read_write", r.AuthorizeUrl);
            Assert.Contains($"user_id={r.Id:D}", r.AuthorizeUrl);
            Assert.Contains(Uri.EscapeDataString("https://plutus.example.test/api/v1/webstores/wc-auth/callback"), r.AuthorizeUrl);

            var ws = db.WebStores.AsNoTracking().Single(w => w.Id == r.Id);
            Assert.True(ws.Enabled);
            Assert.Equal("off", ws.OutboundMode);                       // outbound never auto-arms
            Assert.Single(db.Till.IgnoreQueryFilters().Where(t => t.Id == ws.TillId).ToList());
            Assert.Single(db.TillDetails.AsNoTracking().Where(t => t.TillId == ws.TillId).ToList());
            Assert.Single(db.Devices.AsNoTracking().Where(d => d.Id == ws.DeviceId).ToList());
            Assert.StartsWith("whsec_", provider.GetWebhookSecret(r.Id));

            await Assert.ThrowsAsync<ArgumentException>(() => WebstoreOnboarding.CreateConnectionAsync(
                db, provider, Tenant, "X", "http://insecure.example.test", 1, "https://p", "https://r"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Callback_is_one_shot_stores_keys_and_creates_both_webhooks()
    {
        var (conn, o) = Open();
        using var _ = conn;
        var provider = Store(out var path);
        try
        {
            using var db = Ctx(o);
            var created = await WebstoreOnboarding.CreateConnectionAsync(
                db, provider, Tenant, "Shop2", "https://shop2.example.test", 1,
                "https://plutus.example.test", "https://admin.example.test/");

            var stub = new WooApiStub();
            var (s1, _) = await WebstoreOnboarding.HandleCallbackAsync(
                db, provider, provider, new HttpClient(stub), created.Id, "ck_new", "cs_new",
                "https://plutus.example.test");
            Assert.Equal(200, s1);
            Assert.Equal("ck_new", provider.GetRestCredentials(created.Id)!.ConsumerKey);
            var hooks = stub.Requests.Where(r => r.Method == "POST" && r.Url.Contains("/webhooks")).ToList();
            Assert.Equal(2, hooks.Count);                                // order.created + order.updated
            Assert.All(hooks, h => Assert.Contains($"/api/v1/webstores/{created.Id:D}/webhook", h.Body));
            Assert.All(hooks, h => Assert.Contains("whsec_", h.Body));   // our webhook secret rides along

            // Replayed/forged callback → refused, keys unchanged.
            var (s2, _) = await WebstoreOnboarding.HandleCallbackAsync(
                db, provider, provider, new HttpClient(stub), created.Id, "ck_evil", "cs_evil",
                "https://plutus.example.test");
            Assert.Equal(409, s2);
            Assert.Equal("ck_new", provider.GetRestCredentials(created.Id)!.ConsumerKey);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Disconnect_removes_our_webhooks_and_disables_the_connection()
    {
        var (conn, o) = Open();
        using var _ = conn;
        var provider = Store(out var path);
        try
        {
            using var db = Ctx(o);
            var created = await WebstoreOnboarding.CreateConnectionAsync(
                db, provider, Tenant, "Shop2", "https://shop2.example.test", 1,
                "https://plutus.example.test", "https://admin.example.test/");
            provider.SetRestCredentials(created.Id, "ck", "cs");

            var stub = new WooApiStub
            {
                // Two of ours + one unrelated webhook that must survive.
                ListWebhooksBody = $"[{{\"id\":31,\"delivery_url\":\"https://plutus.example.test/api/v1/webstores/{created.Id:D}/webhook\"}}," +
                                   $"{{\"id\":32,\"delivery_url\":\"https://plutus.example.test/api/v1/webstores/{created.Id:D}/webhook\"}}," +
                                   "{\"id\":9,\"delivery_url\":\"https://other-app.example.test/hook\"}]",
            };
            var (status, detail) = await WebstoreOnboarding.DisconnectAsync(db, provider, new HttpClient(stub), created.Id);

            Assert.Equal(200, status);
            Assert.Contains("2 webhook(s) removed", detail);
            Assert.Equal(2, stub.Requests.Count(r => r.Method == "DELETE"));
            Assert.DoesNotContain(stub.Requests, r => r.Method == "DELETE" && r.Url.Contains("/webhooks/9"));
            Assert.False(db.WebStores.AsNoTracking().Single(w => w.Id == created.Id).Enabled);
        }
        finally { File.Delete(path); }
    }
}
