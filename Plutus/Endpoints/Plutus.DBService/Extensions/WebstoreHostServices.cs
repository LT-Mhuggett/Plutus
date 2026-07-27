using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Sales;
using Plutus.Webstore;

namespace Plutus.DBService.Extensions
{
    /// <summary>Host adapter: mapped webstore SaleV2 → ingest DTO → the platform's idempotent
    /// <see cref="SalesIngestService"/> on the delivery's TENANT-FIXED context. Lives host-side so
    /// the connector module never references the Sales module (plan: core↮connector isolation).
    /// Constructed per delivery by the WebstoreWebhookPipelineFactory via the registered func.</summary>
    public sealed class WebstoreIngestSink : IWebstoreSaleSink
    {
        private readonly MySqlDbContext _db;
        private readonly WebstoreConnectionContext _ctx;
        public WebstoreIngestSink(MySqlDbContext db, WebstoreConnectionContext ctx) { _db = db; _ctx = ctx; }

        public async Task<bool> SubmitAsync(SaleV2 sale, CancellationToken ct = default)
        {
            var req = new IngestSaleRequest
            {
                SaleId = sale.Id, DeviceId = sale.DeviceId, DeviceSeq = sale.DeviceSeq,
                Channel = (byte)sale.Channel, BusinessDay = sale.BusinessDay, OccurredAtUtc = sale.OccurredAtUtc,
                GrossPence = sale.GrossPence, VatPence = sale.VatPence, Note = sale.Note, OperatorUserId = sale.OperatorUserId,
                Lines = sale.Lines.Select(l => new IngestLine
                {
                    ItemId = l.ItemId, Qty = l.Qty, UnitPricePence = l.UnitPricePence, DiscountPence = l.DiscountPence,
                    LineGrossPence = l.LineGrossPence, VatRateBp = l.VatRateBp, VatAmountPence = l.VatAmountPence,
                    OverriddenFromPence = l.OverriddenFromPence, DiscountsJson = l.DiscountsJson,
                }).ToList(),
                Tenders = sale.Tenders.Select(t => new IngestTender
                {
                    TenderType = (byte)t.TenderType, AmountPence = t.AmountPence, ChangePence = t.ChangePence, ProviderRef = t.ProviderRef,
                }).ToList(),
            };
            var outcome = await new SalesIngestService(_db).IngestAsync(
                req, _ctx.TenantId, _ctx.DeviceId, $"webstore:{_ctx.WebStoreId:D}");
            return outcome.Status == 201;   // 201 new; 200 = idempotent duplicate
        }
    }

    /// <summary>WP6.2a step 2 — per-connection secrets from server configuration (pm2 env /
    /// user-secrets on the Mac). Never the DB. Webhook HMAC: <c>Webstore:Secrets:{id}</c>;
    /// REST read credentials for the reconciliation poll: <c>Webstore:RestKeys:{id}</c> = "ck|cs".</summary>
    public sealed class ConfigWebstoreSecretProvider : IWebstoreSecretProvider
    {
        private readonly IConfiguration _config;
        public ConfigWebstoreSecretProvider(IConfiguration config) => _config = config;
        public string GetWebhookSecret(Guid webStoreId) => _config[$"Webstore:Secrets:{webStoreId:D}"];

        public WebstoreRestCredentials GetRestCredentials(Guid webStoreId)
        {
            var raw = _config[$"Webstore:RestKeys:{webStoreId:D}"];
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Split('|');
            return parts.Length == 2 ? new WebstoreRestCredentials(parts[0].Trim(), parts[1].Trim()) : null;
        }
    }
}
