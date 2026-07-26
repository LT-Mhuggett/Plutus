using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Plutus.Webstore
{
    // DTOs mirroring the WooCommerce REST v3 order/product shapes we consume (only the fields the
    // connector needs). Money fields arrive as decimal-text strings ("17.33"); quantities and tax
    // rates as JSON numbers. See tests/Fixtures/Woo for captured, PII-scrubbed examples.

    public sealed class WooOrder
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("number")] public string? Number { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("prices_include_tax")] public bool PricesIncludeTax { get; set; }
        [JsonPropertyName("total")] public string? Total { get; set; }
        [JsonPropertyName("total_tax")] public string? TotalTax { get; set; }
        [JsonPropertyName("shipping_total")] public string? ShippingTotal { get; set; }
        [JsonPropertyName("shipping_tax")] public string? ShippingTax { get; set; }
        [JsonPropertyName("customer_id")] public long CustomerId { get; set; }
        [JsonPropertyName("payment_method")] public string? PaymentMethod { get; set; }
        [JsonPropertyName("payment_method_title")] public string? PaymentMethodTitle { get; set; }
        [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
        [JsonPropertyName("date_created_gmt")] public string? DateCreatedGmt { get; set; }
        [JsonPropertyName("date_modified_gmt")] public string? DateModifiedGmt { get; set; }
        [JsonPropertyName("date_paid_gmt")] public string? DatePaidGmt { get; set; }
        [JsonPropertyName("line_items")] public List<WooLineItem> LineItems { get; set; } = new();
        [JsonPropertyName("refunds")] public List<WooOrderRefundSummary> Refunds { get; set; } = new();
        [JsonPropertyName("tax_lines")] public List<WooTaxLine> TaxLines { get; set; } = new();
        [JsonPropertyName("shipping_lines")] public List<WooShippingLine> ShippingLines { get; set; } = new();
        [JsonPropertyName("fee_lines")] public List<WooFeeLine> FeeLines { get; set; } = new();
    }

    public sealed class WooLineItem
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("product_id")] public long ProductId { get; set; }
        [JsonPropertyName("variation_id")] public long VariationId { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("sku")] public string? Sku { get; set; }
        [JsonPropertyName("subtotal")] public string? Subtotal { get; set; }       // net, pre line-discount
        [JsonPropertyName("subtotal_tax")] public string? SubtotalTax { get; set; }
        [JsonPropertyName("total")] public string? Total { get; set; }             // net, post line-discount
        [JsonPropertyName("total_tax")] public string? TotalTax { get; set; }
        [JsonPropertyName("taxes")] public List<WooLineTax> Taxes { get; set; } = new();
    }

    public sealed class WooLineTax
    {
        [JsonPropertyName("id")] public long Id { get; set; }              // matches WooTaxLine.RateId
        [JsonPropertyName("total")] public string? Total { get; set; }
    }

    public sealed class WooTaxLine
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("rate_id")] public long RateId { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("rate_percent")] public double RatePercent { get; set; }   // 20 = 20%
    }

    public sealed class WooShippingLine
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("method_title")] public string? MethodTitle { get; set; }
        [JsonPropertyName("total")] public string? Total { get; set; }
        [JsonPropertyName("total_tax")] public string? TotalTax { get; set; }
    }

    public sealed class WooFeeLine
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("total")] public string? Total { get; set; }
        [JsonPropertyName("total_tax")] public string? TotalTax { get; set; }
    }

    /// <summary>A WooCommerce product (the WP6.4 product-sweep shape — only the fields the
    /// catalogue cache needs).</summary>
    public sealed class WooProduct
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("sku")] public string? Sku { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("regular_price")] public string? RegularPrice { get; set; }
        [JsonPropertyName("stock_quantity")] public int? StockQuantity { get; set; }
        [JsonPropertyName("stock_status")] public string? StockStatus { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("permalink")] public string? Permalink { get; set; }
        [JsonPropertyName("date_modified_gmt")] public string? DateModifiedGmt { get; set; }
    }

    /// <summary>The refund summary embedded in an ORDER body's <c>refunds</c> array —
    /// <c>total</c> is a NEGATIVE decimal string (e.g. "-61.49").</summary>
    public sealed class WooOrderRefundSummary
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("total")] public string? Total { get; set; }
    }

    /// <summary>A WooCommerce refund (from <c>GET orders/{id}/refunds</c> or the order's
    /// <c>refunds</c> array). Kapow's refunds are whole-order amount refunds (empty line_items).</summary>
    public sealed class WooRefund
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("amount")] public string? Amount { get; set; }             // positive magnitude
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("date_created_gmt")] public string? DateCreatedGmt { get; set; }
    }
}
