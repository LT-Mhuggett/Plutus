using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Plutus.Migration.Kapow
{
    /// <summary>
    /// Reads the Kapow SQLite backup into <see cref="KapowSaleInput"/> graphs (F4 schema:
    /// Trans.Amount = qty, Trans.ItemCostPrice = inc-VAT unit price; the line VAT multiplier comes
    /// from Items.VatId → Vats.Rate). Discounts are not folded in this pass — discounted sales
    /// whose lines don't sum to Sales.Total are quarantined by the mapper (a later pass can add
    /// Transaction_Discounts handling).
    /// </summary>
    public sealed class KapowSalesReader
    {
        private readonly SqliteConnection _conn;
        public KapowSalesReader(SqliteConnection conn) => _conn = conn;

        private IEnumerable<IDataRecord> Rows(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read()) yield return r;
        }

        private static string? S(object v) => v == DBNull.Value ? null : Convert.ToString(v);

        public IReadOnlyList<KapowSaleInput> Read(Guid tenantId, Guid tillId, Guid deviceId)
        {
            // item id → VAT multiplier (via VatId → Vats.Rate)
            var vatRate = new Dictionary<int, double>();
            foreach (var r in Rows("SELECT Id, Rate FROM Vats"))
                vatRate[Convert.ToInt32(r["Id"])] = Convert.ToDouble(r["Rate"]);
            var itemMult = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Rows("SELECT Id, VatId FROM Items"))
            {
                var id = S(r["Id"]);
                if (id == null) continue;
                itemMult[id] = r["VatId"] != DBNull.Value && vatRate.TryGetValue(Convert.ToInt32(r["VatId"]), out var m) ? m : 1.0;
            }

            // PayId → tender type (best-effort from PayMethods.Name)
            var payType = new Dictionary<int, byte>();
            foreach (var r in Rows("SELECT Id, Name FROM PayMethods"))
                payType[Convert.ToInt32(r["Id"])] = (S(r["Name"]) ?? "").ToLowerInvariant() switch
                {
                    var n when n.Contains("card") => 1,
                    var n when n.Contains("online") => 2,
                    var n when n.Contains("credit") => 3,
                    _ => 0, // cash / unknown
                };

            var sales = new Dictionary<string, KapowSaleInput>();
            foreach (var r in Rows("SELECT * FROM Sales"))
            {
                var id = S(r["Id"]);
                if (id == null) continue;
                sales[id] = new KapowSaleInput
                {
                    LegacyId = id,
                    TotalText = S(r["Total"]) ?? "0",
                    DateOfSaleLocal = S(r["DateOfSale"]),
                    CreatedLocal = ColumnExists(r, "Created") ? (S(r["Created"]) ?? S(r["DateOfSale"]) ?? "") : (S(r["DateOfSale"]) ?? ""),
                    TenantId = tenantId, TillId = tillId, DeviceId = deviceId, DeviceSeq = 0,
                };
            }

            long seq = 0;
            foreach (var kv in sales) kv.Value.DeviceSeq = ++seq; // synthetic monotonic seq for migrated sales

            foreach (var r in Rows("SELECT * FROM Trans"))
            {
                var saleId = S(r["SaleId"]);
                var itemId = S(r["ItemId"]);
                if (saleId == null || itemId == null || !sales.TryGetValue(saleId, out var sale)) continue;
                sale.Lines.Add(new KapowLineInput
                {
                    OldItemId = itemId,
                    Qty = r["Amount"] == DBNull.Value ? 0 : Convert.ToInt32(r["Amount"]),
                    UnitPriceText = S(r["ItemCostPrice"]) ?? "0",
                    VatMultiplier = itemMult.TryGetValue(itemId, out var m) ? m : 1.0,
                });
            }

            foreach (var r in Rows("SELECT * FROM PaySales"))
            {
                var saleId = S(r["SaleId"]);
                if (saleId == null || !sales.TryGetValue(saleId, out var sale)) continue;
                var payId = r["PayId"] == DBNull.Value ? 0 : Convert.ToInt32(r["PayId"]);
                sale.Tenders.Add(new KapowTenderInput
                {
                    TenderType = payType.TryGetValue(payId, out var t) ? t : (byte)0,
                    AmountText = S(r["Amount"]) ?? "0",
                    ChangeText = S(r["Change"]),
                });
            }

            return new List<KapowSaleInput>(sales.Values);
        }

        private static bool ColumnExists(IDataRecord r, string name)
        {
            for (var i = 0; i < r.FieldCount; i++)
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
