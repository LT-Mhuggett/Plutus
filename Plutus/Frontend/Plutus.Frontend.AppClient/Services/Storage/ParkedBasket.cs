using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Database.Models;
using Plutus.Frontend.AppClient.Models;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// One record of a parked basket, on the wire.
    ///
    /// ⚠ <see cref="Kind"/> IS THE POINT. The legacy park used Newtonsoft's
    /// `TypeNameHandling.Auto`, which writes .NET type names (`NatApp.Plutus.Models.BasketItem,
    /// NatApp.Plutus`) into the blob. Those stop resolving the moment a namespace, assembly or
    /// class name changes — and this codebase has renamed all three. A basket parked by one build
    /// and recalled by the next threw on deserialisation, which is how discounted parked baskets
    /// came to crash the app. A plain string discriminator cannot rot that way.
    /// </summary>
    internal sealed class ParkedRecord
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = KindItem;
        [JsonPropertyName("idOne")] public string IdOne { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("incPence")] public long IncPence { get; set; }
        [JsonPropertyName("exPence")] public long ExPence { get; set; }
        [JsonPropertyName("qty")] public int Qty { get; set; } = 1;
        [JsonPropertyName("vatBand")] public string VatBand { get; set; }
        /// <summary>Returns only — why, and which sale it came off.</summary>
        [JsonPropertyName("reason")] public string Reason { get; set; }
        [JsonPropertyName("originSaleId")] public string OriginSaleId { get; set; }

        internal const string KindItem = "item";
        internal const string KindReturn = "return";
        internal const string KindNote = "note";
    }

    /// <summary>
    /// Turning the till's basket into something that survives being written down, and back
    /// (cutover step 18).
    ///
    /// ⚠ THE PARKED PRICE IS KEPT, NOT RE-RESOLVED. A basket is a quote: the customer was told a
    /// figure, walked away, and came back. Re-pricing it on recall would change what they were
    /// quoted without anybody saying so — and the till would be the only place that ever knew.
    /// (The consequence is that a basket parked across an overnight price change recalls at
    /// yesterday's price. That is the deliberate side of the trade, not an oversight.)
    /// </summary>
    internal static class ParkedBasket
    {
        internal static string ToJson(IEnumerable<IBasketRecord> basket)
        {
            var records = new List<ParkedRecord>();

            foreach (var record in basket ?? Enumerable.Empty<IBasketRecord>())
            {
                switch (record)
                {
                    // ⚠⚠ ONE CASE FOR BOTH, SPLIT ON `IsReturn` (step 11b, 2026-08-22). This was two
                    // cases with a comment insisting the RETURN one came first, because the return
                    // type derived from the item type and testing the base first would have parked
                    // every refund as an ordinary sale line — recalled as money owed TO the shop
                    // rather than by it. With the subclass collapsed there is no order to get wrong.
                    case BasketItem r:
                        records.Add(new ParkedRecord
                        {
                            Kind = r.IsReturn ? ParkedRecord.KindReturn : ParkedRecord.KindItem,
                            IdOne = r.Item?.Id, Name = r.Item?.Name,
                            IncPence = Pence.FromDecimal(r.Price), ExPence = Pence.FromDecimal(r.PriceExTax),
                            Qty = r.Quantity, VatBand = r.Item?.VatName,
                            Reason = r.Reason, OriginSaleId = r.ReturnSaleId,
                        });
                        break;

                    // ⚠ A note carrying MONEY is parked with it. The card surcharge is a real line
                    // now, but a legacy basket recalled from an older park may still hold one, and
                    // dropping its price silently would recall a basket that totals less than the
                    // customer was quoted.
                    case BasketNote n:
                        records.Add(new ParkedRecord
                        {
                            Kind = ParkedRecord.KindNote,
                            Name = n.Name,
                            IncPence = Pence.FromDecimal(n.Price), ExPence = Pence.FromDecimal(n.PriceExTax),
                            Qty = n.Quantity,
                        });
                        break;
                }
            }

            return JsonSerializer.Serialize(records, Options);
        }

        /// <summary>Rebuild the basket. Returns an empty list rather than throwing on a blob this
        /// build cannot read — a parked basket that will not open must not take the till with it.</summary>
        internal static IReadOnlyList<IBasketRecord> FromJson(string json)
        {
            var basket = new List<IBasketRecord>();
            if (string.IsNullOrWhiteSpace(json)) return basket;

            List<ParkedRecord> records;
            try
            {
                records = JsonSerializer.Deserialize<List<ParkedRecord>>(json, Options) ?? new();
            }
            catch (JsonException ex)
            {
                Analytics.CrashLog.Write("ParkedBasket.FromJson", ex);
                return basket;
            }

            foreach (var r in records)
            {
                if (string.Equals(r.Kind, ParkedRecord.KindNote, StringComparison.OrdinalIgnoreCase))
                {
                    basket.Add(new BasketNote(
                        new NoteModel { Note = r.Name ?? "" }, r.IncPence / 100m, r.ExPence / 100m));
                    continue;
                }

                // ⚠⚠ THE PARKED WIRE FORMAT IS UNCHANGED BY L5/L6, AND THAT IS THE POINT. A
                // `ParkedRecord` is flat primitives — idOne, name, incPence, exPence, qty, vatBand —
                // never a serialised item entity. So a basket parked by an older build restores here
                // exactly as it always did; only the shape it is rehydrated INTO has changed.
                var item = new Models.TillItem
                {
                    Id = r.IdOne,
                    Name = r.Name,
                    Price = r.IncPence / 100m,
                    ExPrice = r.ExPence / 100m,
                    VatName = r.VatBand ?? string.Empty,
                };

                if (string.Equals(r.Kind, ParkedRecord.KindReturn, StringComparison.OrdinalIgnoreCase))
                {
                    // ⚠⚠ ONE OBJECT, MARKED — not a subclass constructed and then told what it is
                    // (step 11b, 2026-08-22). The old pair was `new BasketReturnItem(...)` followed by
                    // `SetItemReturn(...)`: two steps, with a window in between where the line was a
                    // return carrying no reason and no origin sale. `MarkAsReturn` takes both at once
                    // because a refund with neither is money leaving the drawer with nothing to net
                    // it off.
                    var ret = new BasketItem(item, Math.Max(1, r.Qty));
                    ret.MarkAsReturn(r.Reason, r.OriginSaleId);
                    basket.Add(ret);
                }
                else
                {
                    basket.Add(new BasketItem(item, Math.Max(1, r.Qty)));
                }
            }

            return basket;
        }

        private static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
