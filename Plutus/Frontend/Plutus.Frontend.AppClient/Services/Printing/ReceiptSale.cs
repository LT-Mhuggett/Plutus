using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Printing
{
    /// <summary>One line of the payment block on a receipt.</summary>
    public sealed record ReceiptTender(string Name, long AmountPence, long ChangePence);

    /// <summary>
    /// What a receipt says about a sale — built from the payload that was COMMITTED, never from a
    /// parallel legacy total (cutover step 14).
    ///
    /// ⚠ THE BARCODE IS THE POINT. The footer used to print <c>SaleModel.Id</c>, a legacy string,
    /// and since step 11 deleted the legacy <c>db.Save()</c> that assigned it **nothing sets it at
    /// all** — so every receipt has been printing an EMPTY barcode. That is not cosmetic: the
    /// barcode is how a customer's receipt finds its sale again for a reprint or a refund, and step
    /// 15 builds that read path keyed on the platform <c>saleId</c>. A receipt carrying no id, or a
    /// legacy id the platform has never heard of, cannot be looked up by anything ever again.
    ///
    /// ⚠ THE MONEY COMES FROM THE COMMITTED PAYLOAD. Re-summing the basket for the receipt would be
    /// a second opinion about the sale: the customer's paper and the platform's record could differ
    /// by a penny and the receipt would be the only evidence of which was charged.
    /// </summary>
    public sealed record ReceiptSale(
        Guid SaleId,
        DateTime WhenLocal,
        long GrossPence,
        long ExPence,
        long VatPence,
        IReadOnlyList<ReceiptTender> Tenders,
        IReadOnlyList<string> Notes)
    {
        /// <summary>Total change given, across every tender.</summary>
        public long ChangePence => Tenders.Sum(t => t.ChangePence);

        /// <summary>
        /// Build from the assembled request and the tender names the operator chose.
        ///
        /// ⚠ Names come from the till, not the wire: the payload carries tender BYTES, and a
        /// receipt that read "1" where the customer expects "Card" is worse than useless. The byte
        /// is what reconciles; the name is what the customer reads.
        /// </summary>
        public static ReceiptSale From(
            IngestSaleRequest request, IReadOnlyList<string> tenderNames, IReadOnlyList<string> notes = null)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            var tenders = request.Tenders
                .Select((t, i) => new ReceiptTender(
                    i < (tenderNames?.Count ?? 0) ? tenderNames[i] : NameFor(t.TenderType),
                    t.AmountPence, t.ChangePence))
                .ToList();

            return new ReceiptSale(
                request.SaleId,
                // ⚠ The customer's clock, not UTC. A receipt timestamped 23:00 for a sale the shop
                // rang at midnight is the sort of thing that gets queried at a chargeback.
                request.OccurredAtUtc.ToLocalTime(),
                request.GrossPence,
                request.GrossPence - request.VatPence,
                request.VatPence,
                tenders,
                notes ?? Array.Empty<string>());
        }

        // ⚠ Fully qualified: this record has its own `Tenders` property, and the unqualified name
        // binds to it rather than to the shared constants.
        private static string NameFor(byte tenderType) => tenderType switch
        {
            SharedKernel.Tenders.Cash => "Cash",
            SharedKernel.Tenders.Card => "Card",
            SharedKernel.Tenders.Online => "Online",
            SharedKernel.Tenders.Credit => "Store credit",
            SharedKernel.Tenders.GiftCard => "Gift card",
            _ => "Payment",
        };
    }
}
