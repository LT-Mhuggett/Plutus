using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;

namespace Plutus.TenantRestore
{
    /// <summary>
    /// WP15.3 per-tenant restore. NOT a new backup system — it operates on an existing nightly dump
    /// (WP12.3) that has already been loaded into a scratch schema, and reconstructs one tenant's
    /// rows in the live schema.
    ///
    /// Tenant-owned tables are discovered from information_schema (any column named TenantId) — the
    /// SAME schema-as-source-of-truth the WP10.3 exporter uses, never a hand-kept list, so the tool
    /// tracks the EF model automatically. Global tables (no TenantId) are ignored.
    ///
    /// Two modes:
    ///   --verify (default): read-only. Per-table row counts source vs target + SalesV2 penny totals.
    ///   --apply           : INSERT rows present in the dump but missing in live, matched by primary
    ///                       key. INSERT-only-missing — existing rows are NEVER updated, which honours
    ///                       the immutability of event tables (decision D5). Every write is scoped
    ///                       WHERE TenantId = @tenant, so other tenants are structurally untouched; the
    ///                       tool additionally fingerprints every table's other-tenant rows (XOR of
    ///                       per-row CRC32) before and after and aborts if any fingerprint moves.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] rawArgs)
        {
            Dictionary<string, string> args;
            try { args = ParseArgs(rawArgs); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Usage(); return 2; }

            if (args.ContainsKey("help") || rawArgs.Length == 0) { Usage(); return rawArgs.Length == 0 ? 2 : 0; }

            if (!args.TryGetValue("tenant", out var tenantStr) || !Guid.TryParse(tenantStr, out var tenant))
            {
                Console.Error.WriteLine("--tenant <guid> is required."); return 2;
            }
            var source = args.GetValueOrDefault("source", "plutus_restore");
            var target = args.GetValueOrDefault("target", "plutus");
            var apply = args.ContainsKey("apply");
            var conn = BuildConnString(args);
            if (conn == null) { Console.Error.WriteLine("Provide --conn \"<connstr>\" or --server/--user/--password."); return 2; }

            Console.WriteLine($"Plutus tenant restore — tenant {tenant}");
            Console.WriteLine($"  source schema : {source}   (loaded dump)");
            Console.WriteLine($"  target schema : {target}   (live)");
            Console.WriteLine($"  mode          : {(apply ? "APPLY (writes)" : "VERIFY (read-only)")}");
            Console.WriteLine();

            try
            {
                await using var db = new MySqlConnection(conn);
                await db.OpenAsync();
                return apply
                    ? await ApplyAsync(db, source, target, tenant)
                    : await VerifyAsync(db, source, target, tenant);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED: " + ex.Message);
                return 1;
            }
        }

        // ---- verify -----------------------------------------------------------------------------

        private static async Task<int> VerifyAsync(MySqlConnection db, string source, string target, Guid tenant)
        {
            var tables = await TenantTablesAsync(db, source);
            Console.WriteLine($"{tables.Count} tenant-owned tables discovered in `{source}`.\n");
            Console.WriteLine($"{"table",-40} {"dump",10} {"live",10}  {"delta",10}");
            Console.WriteLine(new string('-', 74));

            long totalDump = 0, totalLive = 0, mismatched = 0;
            foreach (var t in tables)
            {
                var d = await CountAsync(db, source, t, tenant);
                var l = await CountAsync(db, target, t, tenant);
                totalDump += d; totalLive += l;
                var delta = d - l;
                if (delta != 0) mismatched++;
                Console.WriteLine($"{t,-40} {d,10} {l,10}  {delta,+10}");
            }
            Console.WriteLine(new string('-', 74));
            Console.WriteLine($"{"TOTAL",-40} {totalDump,10} {totalLive,10}  {totalDump - totalLive,+10}");

            var salesTable = tables.FirstOrDefault(t => t.Equals("salesv2", StringComparison.OrdinalIgnoreCase));
            if (salesTable != null)
            {
                var pd = await SumPenceAsync(db, source, salesTable, tenant);
                var pl = await SumPenceAsync(db, target, salesTable, tenant);
                Console.WriteLine();
                Console.WriteLine($"SalesV2 GrossPence  dump={pd}  live={pl}  delta={pd - pl}");
            }

            Console.WriteLine();
            if (mismatched == 0 && totalDump == totalLive)
            {
                Console.WriteLine("MATCH — live already holds every row the dump has for this tenant.");
                return 0;
            }
            Console.WriteLine($"DIFFERENCE — {mismatched} table(s) differ; live is missing {totalDump - totalLive} row(s). "
                            + "Re-run with --apply to insert the missing rows.");
            return 0; // a difference is not a tool error; it's the finding
        }

        // ---- apply ------------------------------------------------------------------------------

        private static async Task<int> ApplyAsync(MySqlConnection db, string source, string target, Guid tenant)
        {
            var tables = await TenantTablesAsync(db, source);
            Console.WriteLine($"{tables.Count} tenant-owned tables. Fingerprinting other-tenant rows before apply…\n");

            // Safety net: capture a fingerprint of every table's OTHER-tenant rows up front. Since we
            // only ever INSERT rows for @tenant, these must be identical afterwards.
            var before = new Dictionary<string, string>();
            foreach (var t in tables) before[t] = await OtherTenantFingerprintAsync(db, target, t, tenant);

            // Restore a self-consistent set of rows that existed together in the dump. Insert order
            // across FK-linked tables is arbitrary (alphabetical), so relax FK checks for this
            // session only — exactly what mysqldump does around a restore — and re-enable after.
            long inserted = 0;
            await ExecAsync(db, "SET SESSION FOREIGN_KEY_CHECKS = 0");
            try
            {
                foreach (var t in tables)
                {
                    var n = await InsertMissingAsync(db, source, target, t, tenant);
                    inserted += n;
                    Console.WriteLine($"  {t,-40} +{n} row(s)");
                }
            }
            finally { await ExecAsync(db, "SET SESSION FOREIGN_KEY_CHECKS = 1"); }
            Console.WriteLine($"\nInserted {inserted} row(s). Re-checking other-tenant fingerprints…");

            var drift = new List<string>();
            foreach (var t in tables)
            {
                var after = await OtherTenantFingerprintAsync(db, target, t, tenant);
                if (after != before[t]) drift.Add(t);
            }
            if (drift.Count > 0)
            {
                Console.Error.WriteLine("ABORT-CHECK FAILED — other-tenant rows changed in: " + string.Join(", ", drift));
                Console.Error.WriteLine("This should be impossible (all writes are TenantId-scoped). Investigate before trusting live.");
                return 1;
            }

            Console.WriteLine("OK — other tenants byte-untouched. Running a verify pass:\n");
            return await VerifyAsync(db, source, target, tenant);
        }

        /// <summary>Cross-schema INSERT … SELECT of rows present in source but missing (by PK) in
        /// target. Never updates an existing row — immutability-safe for event tables (D5).</summary>
        private static async Task<long> InsertMissingAsync(MySqlConnection db, string source, string target, string table, Guid tenant)
        {
            var cols = await ColumnsAsync(db, table, source);
            var pk = await PrimaryKeyAsync(db, table, source);
            if (pk.Count == 0)
            {
                Console.Error.WriteLine($"  {table}: no primary key — skipped (cannot match rows safely).");
                return 0;
            }
            var colList = string.Join(", ", cols.Select(c => $"`{c}`"));
            var selList = string.Join(", ", cols.Select(c => $"s.`{c}`"));
            var join = string.Join(" AND ", pk.Select(c => $"t.`{c}` = s.`{c}`"));

            var sql = $"INSERT INTO `{target}`.`{table}` ({colList}) " +
                      $"SELECT {selList} FROM `{source}`.`{table}` s " +
                      $"WHERE s.`TenantId` = @tid " +
                      $"AND NOT EXISTS (SELECT 1 FROM `{target}`.`{table}` t WHERE {join})";
            await using var cmd = new MySqlCommand(sql, db);
            cmd.Parameters.AddWithValue("@tid", tenant.ToString());
            return await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ExecAsync(MySqlConnection db, string sql)
        {
            await using var cmd = new MySqlCommand(sql, db);
            await cmd.ExecuteNonQueryAsync();
        }

        // ---- schema introspection + aggregates --------------------------------------------------

        private static async Task<List<string>> TenantTablesAsync(MySqlConnection db, string schema)
        {
            const string sql = "SELECT DISTINCT table_name FROM information_schema.columns " +
                               "WHERE table_schema = @s AND column_name = 'TenantId' ORDER BY table_name";
            await using var cmd = new MySqlCommand(sql, db);
            cmd.Parameters.AddWithValue("@s", schema);
            var list = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetString(0));
            return list;
        }

        private static async Task<List<string>> ColumnsAsync(MySqlConnection db, string table, string schema)
        {
            const string sql = "SELECT column_name FROM information_schema.columns " +
                               "WHERE table_schema = @s AND table_name = @t ORDER BY ordinal_position";
            await using var cmd = new MySqlCommand(sql, db);
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@t", table);
            var cols = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) cols.Add(r.GetString(0));
            return cols;
        }

        private static async Task<List<string>> PrimaryKeyAsync(MySqlConnection db, string table, string schema)
        {
            const string sql = "SELECT k.column_name FROM information_schema.key_column_usage k " +
                               "JOIN information_schema.table_constraints c " +
                               "  ON c.constraint_name = k.constraint_name AND c.table_schema = k.table_schema AND c.table_name = k.table_name " +
                               "WHERE c.constraint_type = 'PRIMARY KEY' AND k.table_schema = @s AND k.table_name = @t " +
                               "ORDER BY k.ordinal_position";
            await using var cmd = new MySqlCommand(sql, db);
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@t", table);
            var pk = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) pk.Add(r.GetString(0));
            return pk;
        }

        private static async Task<long> CountAsync(MySqlConnection db, string schema, string table, Guid tenant)
        {
            await using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{schema}`.`{table}` WHERE `TenantId` = @tid", db);
            cmd.Parameters.AddWithValue("@tid", tenant.ToString());
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        private static async Task<long> SumPenceAsync(MySqlConnection db, string schema, string table, Guid tenant)
        {
            await using var cmd = new MySqlCommand($"SELECT COALESCE(SUM(`GrossPence`),0) FROM `{schema}`.`{table}` WHERE `TenantId` = @tid", db);
            cmd.Parameters.AddWithValue("@tid", tenant.ToString());
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        /// <summary>Order-independent checksum of the rows NOT belonging to the tenant: BIT_XOR of a
        /// per-row CRC32 over all columns. Any insert/update/delete among other-tenant rows moves it.</summary>
        private static async Task<string> OtherTenantFingerprintAsync(MySqlConnection db, string schema, string table, Guid tenant)
        {
            var cols = await ColumnsAsync(db, table, schema);
            var concat = "CONCAT_WS('|'," + string.Join(",", cols.Select(c => $"COALESCE(CAST(`{c}` AS CHAR),'\\0')")) + ")";
            var sql = $"SELECT COALESCE(BIT_XOR(CRC32({concat})),0), COUNT(*) FROM `{schema}`.`{table}` WHERE `TenantId` <> @tid";
            await using var cmd = new MySqlCommand(sql, db);
            cmd.Parameters.AddWithValue("@tid", tenant.ToString());
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            return $"{r.GetValue(0)}:{r.GetValue(1)}";
        }

        // ---- args -------------------------------------------------------------------------------

        private static Dictionary<string, string> ParseArgs(string[] a)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < a.Length; i++)
            {
                if (!a[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument: {a[i]}");
                var key = a[i].Substring(2);
                if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) { d[key] = a[++i]; }
                else d[key] = "true"; // flag
            }
            return d;
        }

        private static string? BuildConnString(Dictionary<string, string> a)
        {
            if (a.TryGetValue("conn", out var c) && !string.IsNullOrWhiteSpace(c)) return c;
            if (!a.ContainsKey("server") && !a.ContainsKey("user")) return null;
            var b = new MySqlConnectionStringBuilder
            {
                Server = a.GetValueOrDefault("server", "127.0.0.1"),
                Port = uint.TryParse(a.GetValueOrDefault("port", "3306"), out var p) ? p : 3306,
                UserID = a.GetValueOrDefault("user", "root"),
                Password = a.GetValueOrDefault("password", ""),
                AllowUserVariables = true,
            };
            return b.ConnectionString;
        }

        private static void Usage()
        {
            Console.WriteLine(@"
plutus-tenant-restore — reconstruct one tenant's rows from a loaded nightly dump.

  Prerequisite: load the dump into a scratch schema first, e.g.
      mysql -e ""DROP DATABASE IF EXISTS plutus_restore; CREATE DATABASE plutus_restore;""
      mysql plutus_restore < /path/to/nightly-dump.sql

  Usage:
      plutus-tenant-restore --tenant <guid> [--source plutus_restore] [--target plutus]
                            (--conn ""<connstr>"" | --server H --port 3306 --user U --password P)
                            [--apply]

  Modes:
      (default)  VERIFY — read-only: per-table row counts dump vs live + SalesV2 penny totals.
      --apply    APPLY  — INSERT rows in the dump but missing (by PK) in live. Never updates an
                          existing row (immutability of event tables, D5). Aborts if any other
                          tenant's rows change.
");
        }
    }
}
