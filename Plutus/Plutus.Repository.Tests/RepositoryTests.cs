using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Plutus.Entities;
using Plutus.Entities.Models;
using System;
using System.Threading.Tasks;

namespace Plutus.Repository.Tests
{
    public class RepositoryTests 
    {
        /*[SetUp]
        public void Setup()
        {
        }*/

        protected RepositoryWrapper RepositoryWrapper { get; set; }
        protected MySqlDbContext DbContext;
        public RepositoryTests() : base()
        {
            //string connString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus_test;Port=3306;Persist Security Info=false;Connect Timeout=300";

            //DbContext = new MySqlDbContext(connString);
            DbContext = new MySqlDbContext();
            RepositoryWrapper = new RepositoryWrapper(DbContext);
        }

        #region Data Persistance
        [Test]
        [Category("RecordPersistance")]
        public void BussinessPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            //Assert.AreEqual(1, DbContext.Bussiness.Count());
        }

        [Test]
        [Category("RecordPersistance")]
        public void CategoryPersists()
        {
            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(category, DbContext.Category.Find(category.Id));
            //Assert.AreEqual(1, DbContext.Category.Count());
        }

        [Test]
        [Category("RecordPersistance")]
        public void RolePersists()
        {
            var role = new Role()
            {
                Name = "Test Role"
            };

            RepositoryWrapper.RoleRepository.Create(role);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(role, DbContext.Role.Find(role.Id));
            //Assert.AreEqual(1, DbContext.Category.Count());
        }


        [Test]
        [Category("RecordPersistance")]
        public void StorePersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id
            };

            RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public void TillPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id
            };

            RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var till = new Till()
            {
                MachineId = "1",
                StoreId = store.Id,
                CashFloat = 100,
                LastOnline = DateTime.Now
            };

            RepositoryWrapper.TillRepository.Create(till);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
            Assert.AreEqual(till, DbContext.Till.Find(till.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public void EmployeePersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id
            };

            RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var role = new Role()
            {
                Name = "Employee Role"
            };

            RepositoryWrapper.RoleRepository.Create(role);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var employee = new Employee()
            {
                Wage = 1000,
                ContractedHours = 40,
                Active = true,
                StoreId = store.Id,
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "London",
                PostCode = "203440",
                Country = "England",
                FName = "John",
                LName = "Doe",
                Mobile = "+449672513556",
                Email = store.Id + "@test.com",
                BussinessId = bussiness.Id,
                RoleId = role.Id
            };

            RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
            Assert.AreEqual(role, DbContext.Role.Find(role.Id));
            Assert.AreEqual(employee, DbContext.Employees.Find(employee.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public void DiscountPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var discount = new Discount()
            {
                Name = "Test Name",
                AllApplicable = true,
                CanUseWithOtherDiscounts = false,
                AutoApply = true,
                Type = 1,
                Amount = 13556,
                UsesPerTransaction = 1,
                RequiredNumOfItems = 1,
                BussinessId = bussiness.Id
            };

            RepositoryWrapper.DiscountRepository.Create(discount);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(discount, DbContext.Discounts.Find(discount.Id));
        }

        #endregion

        #region Record Property Constraint Tests

        #region Business
        /* [Test]
         [Category("RecordPropertyConstraint")]
         public void BusinessRecordNameIsRequired()
         {
             var bussiness = new Bussiness()
             {
                 NameAbbr = "TN"
             };
             RepositoryWrapper.BussinessRepository.Create(bussiness);
             RepositoryWrapper.SetCurrentUser("Test");
             Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
         }

         [Test]
         [Category("RecordPropertyConstraint")]
         public void BusinessRecordNameAbbrIsRequired()
         {
             var bussiness = new Bussiness()
             {
                 Name = "Test Name"
             };
             RepositoryWrapper.BussinessRepository.Create(bussiness);
             RepositoryWrapper.SetCurrentUser("Test");
             Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
         }*/

        #endregion Business

        #endregion Record Property Constraint Tests

        #region Sync Only Data Storage
        [Test]
        [Category("SyncHandling")]
        public async Task BusinessSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";

            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(bussiness.CreatedBy, systemName);
            Assert.AreEqual(bussiness.ModifiedBy, systemName);
            Assert.AreEqual(bussiness.CreatedAt, currentTime);
            Assert.AreEqual(bussiness.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task CategorySyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(category.CreatedBy, systemName);
            Assert.AreEqual(category.ModifiedBy, systemName);
            Assert.AreEqual(category.CreatedAt, currentTime);
            Assert.AreEqual(category.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task StoreSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(store.CreatedBy, systemName);
            Assert.AreEqual(store.ModifiedBy, systemName);
            Assert.AreEqual(store.CreatedAt, currentTime);
            Assert.AreEqual(store.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task TillSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var till = new Till()
            {
                MachineId = "1",
                StoreId = store.Id,
                CashFloat = 100,
                LastOnline = DateTime.Now,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.TillRepository.Create(till);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(till.CreatedBy, systemName);
            Assert.AreEqual(till.ModifiedBy, systemName);
            Assert.AreEqual(till.CreatedAt, currentTime);
            Assert.AreEqual(till.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task EmployeeSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var role = new Role()
            {
                Name = "Test Role",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.RoleRepository.Create(role);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var employee = new Employee()
            {
                Wage = 1000,
                ContractedHours = 40,
                Active = true,
                StoreId = store.Id,
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "London",
                PostCode = "203440",
                Country = "England",
                FName = "John",
                LName = "Doe",
                Mobile = "+449672513556",
                Email = store.Id+"@test.com",
                BussinessId = bussiness.Id,
                RoleId = role.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(employee.CreatedBy, systemName);
            Assert.AreEqual(employee.ModifiedBy, systemName);
            Assert.AreEqual(employee.CreatedAt, currentTime);
            Assert.AreEqual(employee.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task DiscountSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var discount = new Discount()
            {
                Name = "Test Name",
                AllApplicable = true,
                CanUseWithOtherDiscounts = false,
                AutoApply = true,
                Type = 1,
                Amount = 13556,
                UsesPerTransaction = 1,
                RequiredNumOfItems = 1,
                BussinessId = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.DiscountRepository.Create(discount);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(discount.CreatedBy, systemName);
            Assert.AreEqual(discount.ModifiedBy, systemName);
            Assert.AreEqual(discount.CreatedAt, currentTime);
            Assert.AreEqual(discount.ModifiedAt, currentTime);
        }

        #endregion

        [TearDown]
        public void DeleteDb()
        {
            foreach (var entry in DbContext.ChangeTracker.Entries())
            {
                switch (entry.State)
                {
                    case EntityState.Modified:
                    case EntityState.Deleted:
                        entry.State = EntityState.Modified;
                        entry.State = EntityState.Unchanged;
                        break;

                    case EntityState.Added:
                        entry.State = EntityState.Detached;
                        break;
                }
            }

            DbContext.Employees.RemoveRange(DbContext.Employees);
            DbContext.Category.RemoveRange(DbContext.Category);
            DbContext.Till.RemoveRange(DbContext.Till);
            DbContext.Stores.RemoveRange(DbContext.Stores);
            DbContext.Role.RemoveRange(DbContext.Role);
            DbContext.Discounts.RemoveRange(DbContext.Discounts);
            DbContext.Bussiness.RemoveRange(DbContext.Bussiness);

            DbContext.SaveChanges();
        }
    }
}