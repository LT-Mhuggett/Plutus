using Database;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Plutus.Frontend.AppClient.Tests.Database
{
    public class AppDBContextTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"plutus-test-{Guid.NewGuid()}.db");

        private AppDBContext CreateContext(string? empId = null)
        {
            var context = new AppDBContext(_dbPath, DatabaseProvider.Sqlite, empId, null);
            context.Database.Migrate();
            return context;
        }

        public void Dispose()
        {
            // ⚠ EF 3.1 -> 9 BEHAVIOUR CHANGE. Microsoft.Data.Sqlite POOLS connections from v6, so
            // disposing the context no longer closes the underlying handle and File.Delete fails
            // with "used by another process". These six tests began failing on exactly this the
            // moment the legacy project was retargeted, and it is teardown, not the schema.
            //
            // ⚠ The same pooling matters in production: anything that COPIES OR MOVES the legacy
            // database file — the cutover archive above all (binding default 3: archive, never
            // delete) — must clear the pool first, or it will fail against a till that has merely
            // opened the file once.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void Migrate_CreatesSchemaSuccessfully()
        {
            using var context = CreateContext();
            Assert.True(context.CurrentVersion());
        }

        [Fact]
        public void SaveChanges_OnAdd_StampsCreatedAndCreatedBy()
        {
            using var context = CreateContext("EMP1");
            var tax = new TaxModel { Name = "Standard", Rate = 0.2 };
            context.Vats.Add(tax);

            context.SaveChanges();

            Assert.NotEqual(default, tax.Created);
            Assert.Equal("EMP1", tax.CreatedBy);
        }

        [Fact]
        public void SaveChanges_WithNoEmpId_StampsCreatedByAsSystem()
        {
            using var context = CreateContext(empId: null);
            var tax = new TaxModel { Name = "Standard", Rate = 0.2 };
            context.Vats.Add(tax);

            context.SaveChanges();

            Assert.Equal("System", tax.CreatedBy);
        }

        [Fact]
        public void SaveChanges_OnModify_StampsModifiedAndModifiedBy()
        {
            using var context = CreateContext("EMP1");
            var tax = new TaxModel { Name = "Standard", Rate = 0.2 };
            context.Vats.Add(tax);
            context.SaveChanges();

            tax.Rate = 0.25;
            using var secondSaveContext = context;
            secondSaveContext.SaveChanges();

            Assert.NotEqual(default, tax.Modified);
            Assert.Equal("EMP1", tax.ModifiedBy);
        }

        [Fact]
        public void EmployeeModel_EmailMustBeUnique()
        {
            using var context = CreateContext();
            context.Employees.Add(new EmployeeModel { Id = "1", Email = "dup@example.com", FName = "A", LName = "A", Salt = "x", HashedPassword = "x" });
            context.SaveChanges();

            context.Employees.Add(new EmployeeModel { Id = "2", Email = "dup@example.com", FName = "B", LName = "B", Salt = "x", HashedPassword = "x" });

            Assert.ThrowsAny<Exception>(() => context.SaveChanges());
        }

        [Fact]
        public void ItemModel_CanBeQueriedBackWithVatIncluded()
        {
            using var context = CreateContext();
            var tax = new TaxModel { Name = "Standard", Rate = 0.2 };
            var category = new CategoryModel { Name = "General" };
            var item = new ItemModel { Id = "ITEM1", Name = "Widget", Vat = tax, Cat = category };
            context.Items.Add(item);
            context.SaveChanges();

            var reloaded = context.Items.Include(i => i.Vat).Single(i => i.Id == "ITEM1");
            Assert.Equal("Standard", reloaded.Vat.Name);
        }
    }
}
