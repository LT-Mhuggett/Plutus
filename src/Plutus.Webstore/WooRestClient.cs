using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Webstore
{
    /// <summary>
    /// Minimal WooCommerce REST v3 read client for the reconciliation poll. Deliberately tiny and
    /// gentle (plan rule 3 — the VPS is low-resource): small pages, the caller bounds pages per
    /// cycle, and every request is counted so the budget is observable. Basic auth (consumer
    /// key/secret) over HTTPS — the standard Woo REST scheme.
    /// </summary>
    public sealed class WooRestClient
    {
        private readonly HttpClient _http;
        private readonly string _base;
        private readonly AuthenticationHeaderValue _auth;

        /// <summary>Requests issued by this instance (budget observability).</summary>
        public int RequestCount { get; private set; }

        public WooRestClient(HttpClient http, string siteBaseUrl, WebstoreRestCredentials creds)
        {
            _http = http;
            _base = siteBaseUrl.TrimEnd('/');
            _auth = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{creds.ConsumerKey}:{creds.ConsumerSecret}")));
        }

        /// <summary>One page of orders modified at/after <paramref name="sinceUtc"/> (GMT), plus
        /// the total page count from Woo's X-WP-TotalPages header.</summary>
        public async Task<(List<WooOrder> Orders, int TotalPages)> GetOrdersModifiedSinceAsync(
            DateTime sinceUtc, int page, int perPage, CancellationToken ct = default)
        {
            var url = $"{_base}/wp-json/wc/v3/orders" +
                      $"?modified_after={sinceUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}" +
                      $"&dates_are_gmt=true&per_page={perPage}&page={page}&order=asc&orderby=date";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = _auth;
            RequestCount++;

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var totalPages = 1;
            if (resp.Headers.TryGetValues("X-WP-TotalPages", out var v))
                _ = int.TryParse(System.Linq.Enumerable.FirstOrDefault(v), out totalPages);

            var json = await resp.Content.ReadAsStringAsync(ct);
            var orders = JsonSerializer.Deserialize<List<WooOrder>>(json, WooJson.Options) ?? new List<WooOrder>();
            return (orders, Math.Max(1, totalPages));
        }

        /// <summary>One page of products — either the incremental shape (<paramref name="sinceUtc"/>
        /// set → `modified_after`) or a full-sweep page (null). `_fields` keeps the payload tiny.</summary>
        public async Task<(List<WooProduct> Products, int TotalPages)> GetProductsAsync(
            DateTime? sinceUtc, int page, int perPage, CancellationToken ct = default)
        {
            var url = $"{_base}/wp-json/wc/v3/products?per_page={perPage}&page={page}&order=asc&orderby=id" +
                      "&status=any&_fields=id,sku,name,price,regular_price,stock_quantity,stock_status,status,permalink,date_modified_gmt";
            if (sinceUtc is { } s)
                url += $"&modified_after={s.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}&dates_are_gmt=true";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = _auth;
            RequestCount++;

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var totalPages = 1;
            if (resp.Headers.TryGetValues("X-WP-TotalPages", out var v))
                _ = int.TryParse(System.Linq.Enumerable.FirstOrDefault(v), out totalPages);

            var json = await resp.Content.ReadAsStringAsync(ct);
            var products = JsonSerializer.Deserialize<List<WooProduct>>(json, WooJson.Options) ?? new List<WooProduct>();
            return (products, Math.Max(1, totalPages));
        }

        // ---- WP6.3 WRITES — called ONLY in live outbound mode (dry-run never reaches here).
        //      With the read-only key configured these 401 and the journal records the failure
        //      (fails safe, plan rule 5). ----

        /// <summary>Set a product's stock quantity (also flips Woo's in-stock status).</summary>
        public async Task UpdateProductStockAsync(long wooProductId, int quantity, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{_base}/wp-json/wc/v3/products/{wooProductId}");
            req.Headers.Authorization = _auth;
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { manage_stock = true, stock_quantity = quantity }),
                Encoding.UTF8, "application/json");
            RequestCount++;
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }

        /// <summary>WP6.5: create a DRAFT product (never published by Plutus — a human adds
        /// images/description in wp-admin and publishes there). Returns the new Woo product id.</summary>
        public async Task<long> CreateDraftProductAsync(string sku, string name, long pricePence, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_base}/wp-json/wc/v3/products");
            req.Headers.Authorization = _auth;
            req.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    status = "draft", sku, name,
                    regular_price = (pricePence / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                    manage_stock = true, stock_quantity = 0,
                }),
                Encoding.UTF8, "application/json");
            RequestCount++;
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var body = JsonSerializer.Deserialize<WooProduct>(await resp.Content.ReadAsStringAsync(ct), WooJson.Options);
            return body?.Id ?? 0;
        }
    }
}
