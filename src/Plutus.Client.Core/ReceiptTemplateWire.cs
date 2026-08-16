using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>
/// Turning the portal's `receiptTemplateJson` blob into the shared
/// <see cref="SharedKernel.ReceiptTemplate"/>.
///
/// ⚠ THE BLOB IS OWNED BY THE FRONTENDS, not the server — `ReceiptTemplateResult` carries it as an
/// opaque string precisely so the layout can gain fields without a server change. This is the one
/// place that reads it, so a new field is added here and nowhere else.
///
/// ⚠ ITS SHAPE IS THE WEB TILL'S `ReceiptTemplate` INTERFACE (`api.ts:720`), camelCase on the wire.
/// The two must stay readable by each other: a store whose template was saved from the portal has
/// to print the same on both tills, and till-design C2 records the pair.
/// </summary>
public static class ReceiptTemplateWire
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Read a saved template, or null when there is not one.
    ///
    /// ⚠ NEVER THROWS. A malformed blob must not stop a sale printing: the caller falls back to the
    /// store's own details, which is the same receipt a shop that never touched its template gets.
    /// A till that refused to print because a portal field was mis-saved would stop trading over a
    /// cosmetic setting.
    /// </summary>
    public static ReceiptTemplate? Parse(string? receiptTemplateJson)
    {
        if (string.IsNullOrWhiteSpace(receiptTemplateJson)) return null;

        try
        {
            var dto = JsonSerializer.Deserialize<TemplateDto>(receiptTemplateJson, Options);
            if (dto is null) return null;

            return new ReceiptTemplate(
                dto.StoreName,
                dto.AddressLines,
                dto.Phone,
                dto.VatNumber,
                dto.HeaderLines,
                dto.FooterLines,
                // ⚠ ABSENT MEANS ON. The web till's toggles are optional booleans and its renderer
                // treats a missing one as true — a template saved before a toggle existed must not
                // silently stop printing the VAT number.
                dto.ShowVatNumber ?? true,
                dto.ShowOperator ?? true,
                dto.ShowBarcode ?? true);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TemplateDto
    {
        [JsonPropertyName("storeName")] public string? StoreName { get; set; }
        [JsonPropertyName("addressLines")] public List<string>? AddressLines { get; set; }
        [JsonPropertyName("phone")] public string? Phone { get; set; }
        [JsonPropertyName("vatNumber")] public string? VatNumber { get; set; }
        [JsonPropertyName("headerLines")] public List<string>? HeaderLines { get; set; }
        [JsonPropertyName("footerLines")] public List<string>? FooterLines { get; set; }
        [JsonPropertyName("showVatNumber")] public bool? ShowVatNumber { get; set; }
        [JsonPropertyName("showOperator")] public bool? ShowOperator { get; set; }
        [JsonPropertyName("showBarcode")] public bool? ShowBarcode { get; set; }
    }
}
