using System;
using System.IO;
using System.Linq;
using Database;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// ⚠ THE LEGACY LOCAL DATABASE, RUNNING ON EF CORE 9 INSTEAD OF THE 3.1 IT WAS WRITTEN FOR.
    ///
    /// Binding default 9 (2026-08-09) cuts the till over to <c>Plutus.Client.Storage</c>, which
    /// needs <c>Microsoft.EntityFrameworkCore.Sqlite</c> 9.0.18. The app pinned 3.1.17 for
    /// <c>Plutus/Data/Database</c>, and only ONE version of an assembly can load — so referencing
    /// the v2 store forced the entire app onto EF 9, including code compiled against 3.1.
    ///
    /// That is a RUNTIME-BINDING risk, not a compile-time one. It built cleanly at the first
    /// attempt, which proves nothing at all: six major versions separate the two, and the legacy
    /// layer had **no test coverage whatsoever**. Until this file existed, the first thing that
    /// would have reported a break was a till failing to open in a shop.
    ///
    /// ⚠ These drive <see cref="AppDBContext"/> DIRECTLY against a temp file, NOT through
    /// <c>Helpers.Database.Database</c>. That helper resolves its path from
    /// <c>FileSystem.AppDataDirectory</c> and would open — and write to — the real till's database
    /// on whatever machine ran the tests. The first draft of this file did exactly that.
    ///
    /// ⚠ DELETE THIS FILE when <c>Plutus/Data/Database</c> is removed: keeping it would be testing
    /// something the till no longer uses.
    /// </summary>
    public sealed class LegacyDatabaseUnderEf9Tests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dbPath;

        public LegacyDatabaseUnderEf9Tests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "plutus-legacy-ef9-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_dir);
            _dbPath = Path.Combine(_dir, "Database.db");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private AppDBContext Open() => new(_dbPath, DatabaseProvider.Sqlite, null, null);

        /// <summary>⚠ The most load-bearing behaviour there is: migrations authored by EF 3.1,
        /// applied by EF 9. If this fails, every till fails at start.</summary>
        [Fact]
        public void Migrations_authored_on_EF31_still_apply_on_EF9()
        {
            using var db = Open();
            var exception = Record.Exception(() => db.Database.Migrate());
            Assert.Null(exception);
            Assert.True(File.Exists(_dbPath));
        }

        [Fact]
        public void Insert_query_update_and_delete_still_work()
        {
            using (var db = Open())
            {
                db.Database.Migrate();
                db.Set<StoreModel>().Add(new StoreModel
                {
                    Id = Guid.NewGuid().ToString(), StoreName = "EF9 Smoke", StoreAbbr = "EF9", VatIN = "", ContactNumber = "",
                    AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
                });
                db.SaveChanges();
            }

            // Reopened, so this reads from the FILE rather than the change tracker.
            using (var db = Open())
            {
                var read = db.Set<StoreModel>().FirstOrDefault(s => s.StoreName == "EF9 Smoke");
                Assert.NotNull(read);

                read!.StoreAbbr = "EF9b";
                db.SaveChanges();
                Assert.Equal("EF9b", db.Set<StoreModel>().First(s => s.StoreName == "EF9 Smoke").StoreAbbr);

                db.Set<StoreModel>().Remove(read);
                db.SaveChanges();
                Assert.Empty(db.Set<StoreModel>().AsQueryable().Where(s => s.StoreName == "EF9 Smoke"));
            }
        }

        /// <summary>
        /// ⚠ Counting an EMPTY table is the exact call sign-in makes to decide whether this till has
        /// any staff (<c>probe.Get&lt;EmployeeModel&gt;().Count()</c>). If EF 9 threw here nobody
        /// could sign in — and the message would say the till had no staff rather than that the
        /// query had failed, sending whoever read it in precisely the wrong direction.
        /// </summary>
        [Fact]
        public void Counting_an_empty_table_returns_zero_rather_than_throwing()
        {
            using var db = Open();
            db.Database.Migrate();
            Assert.Equal(0, db.Set<EmployeeModel>().Count());
        }

        /// <summary>The other query shape the app relies on: a filtered read over a table with rows
        /// in it, which is what every legacy screen does.</summary>
        [Fact]
        public void A_filtered_query_over_a_populated_table_still_works()
        {
            using var db = Open();
            db.Database.Migrate();
            db.Set<StoreModel>().AddRange(
                new StoreModel { Id = Guid.NewGuid().ToString(), StoreName = "Alpha", StoreAbbr = "A", VatIN = "", ContactNumber = "", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-" },
                new StoreModel { Id = Guid.NewGuid().ToString(), StoreName = "Beta", StoreAbbr = "B", VatIN = "", ContactNumber = "", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-" });
            db.SaveChanges();

            Assert.Single(db.Set<StoreModel>().AsQueryable().Where(s => s.StoreName == "Alpha"));
            Assert.Equal(2, db.Set<StoreModel>().Count());
        }
    }
}
