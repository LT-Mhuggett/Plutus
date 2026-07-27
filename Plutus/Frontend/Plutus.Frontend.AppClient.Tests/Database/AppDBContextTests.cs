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
