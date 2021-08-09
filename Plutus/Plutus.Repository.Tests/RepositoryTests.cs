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
        public void NotePersists()
        {
            var note = new Note()
            {
                Text = "Note Text"
            };

            RepositoryWrapper.NoteRepository.Create(note);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(note, DbContext.Notes.Find(note.Id));
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

        [Test]
        [Category("RecordPersistance")]
        public void TaxPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, bussiness.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public void ItemPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, bussiness.Id));
            Assert.AreEqual(category, DbContext.Category.Find(category.Id));
            Assert.AreEqual(item, DbContext.Items.Find(item.IdOne, bussiness.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public void TransactionPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
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

            var sale = new Sale()
            {
                Total = 2000,
                TotalExTax = 2000,
                DateOfSale = DateTime.Now,
                StoreId = store.Id,
                EmployeeId = employee.Id
            };

            RepositoryWrapper.SaleRepository.Create(sale);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var transaction = new Transaction()
            {
                Amount = 20000,
                ItemsCostExPrice = 20000,
                ItemsCostPrice = 20000,
                TillId = till.Id,
                SaleId = sale.Id,
                ItemIdOne = item.IdOne,
                ItemIdTwo = item.IdTwo,
            };

            RepositoryWrapper.TransactionRepository.Create(transaction);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, bussiness.Id));
            Assert.AreEqual(category, DbContext.Category.Find(category.Id));
            Assert.AreEqual(item, DbContext.Items.Find(item.IdOne, bussiness.Id));
            Assert.AreEqual(transaction, DbContext.Trans.Find(transaction.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public void StockPersists()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
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

            var stock = new Stock()
            {
                IdOne = item.IdOne,
                IdTwo = item.IdTwo,
                IdThree = store.Id,
                Quantity = 12
            };

            RepositoryWrapper.StockRepository.Create(stock);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, bussiness.Id));
            Assert.AreEqual(category, DbContext.Category.Find(category.Id));
            Assert.AreEqual(item, DbContext.Items.Find(item.IdOne, bussiness.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
            Assert.AreEqual(stock, DbContext.Stocks.Find(stock.IdOne, stock.IdTwo, stock.IdThree));
        }


        #endregion

        #region Record Property Constraint Tests

        #region Business

        [Test]
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
        }

        #endregion Business

        #region Category
        [Test]
        [Category("RecordPersistance")]
        public void CategoryRecordNameIsRequired()
        {
            var category = new Category()
            {
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPersistance")]
        public void CategoryRecordDescriptionIsRequired()
        {
            var category = new Category()
            {
                Name = "Test Name"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Category

        #region Store
        [Test]
        [Category("RecordPersistance")]
        public void StoreRecordContactNumberIsRequired()
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
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
                BussinessId = bussiness.Id
            };

            RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPersistance")]
        public void StoreRecordPostcodeIsRequired()
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
                Country = "England",
                BussinessId = bussiness.Id
            };

            RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPersistance")]
        public void StoreRecordBussinessIdIsRequired()
        {

            var store = new Store()
            {
                ContactNumber = "+449672513556",
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "Test City",
                PostCode = "201304",
                Country = "England",
            };

            RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Store

        #region Till
        [Test]
        [Category("RecordPersistance")]
        public void TillRecordMachineIdIsRequired()
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
                StoreId = store.Id,
                CashFloat = 100,
                LastOnline = DateTime.Now
            };

            RepositoryWrapper.TillRepository.Create(till);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPersistance")]
        public void TillRecordStoreIdIsRequired()
        {
            var till = new Till()
            {
                MachineId = "1",
                CashFloat = 100,
                LastOnline = DateTime.Now
            };

            RepositoryWrapper.TillRepository.Create(till);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Till

        #region Item
        [Test]
        [Category("RecordPropertyConstraint")]
        public void ItemRecordNameIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());

        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void ItemRecordBrandIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());

        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void ItemRecordTaxIdIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());

        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void ItemRecordCatIdIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());

        }
        #endregion Item

        #region Tax
        [Test]
        [Category("RecordPropertyConstraint")]
        public void TaxRecordNameIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Tax

        #region Discount
        [Test]
        [Category("RecordPropertyConstraint")]
        public void DiscountRecordNameIsRequired()
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
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void DiscountRecordBussinessIdIsRequired()
        {
            var discount = new Discount()
            {
                Name = "Test Name",
                AllApplicable = true,
                CanUseWithOtherDiscounts = false,
                AutoApply = true,
                Type = 1,
                Amount = 13556,
                UsesPerTransaction = 1,
                RequiredNumOfItems = 1
            };

            RepositoryWrapper.DiscountRepository.Create(discount);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Discount

        #region Employee
        [Test]
        [Category("RecordPropertyConstraint")]
        public void EmployeeRecordFNameIsRequired()
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
                LName = "Doe",
                Mobile = "+449672513556",
                Email = store.Id + "@test.com",
                BussinessId = bussiness.Id,
                RoleId = role.Id
            };

            RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void EmployeeRecordLNameIsRequired()
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
                Mobile = "+449672513556",
                Email = store.Id + "@test.com",
                BussinessId = bussiness.Id,
                RoleId = role.Id
            };

            RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void EmployeeRecordStoreIdIsRequired()
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
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void EmployeeRecordMobileIsRequired()
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
                Email = store.Id + "@test.com",
                BussinessId = bussiness.Id,
                RoleId = role.Id
            };

            RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void EmployeeRecordEmailIsRequired()
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
                BussinessId = bussiness.Id,
                RoleId = role.Id
            };

            RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Employee

        #region Transaction

        [Test]
        [Category("RecordPropertyConstraint")]
        public void TransactionRecordTillIdIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
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

            var sale = new Sale()
            {
                Total = 2000,
                TotalExTax = 2000,
                DateOfSale = DateTime.Now,
                StoreId = store.Id,
                EmployeeId = employee.Id
            };

            RepositoryWrapper.SaleRepository.Create(sale);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var transaction = new Transaction()
            {
                Amount = 20000,
                ItemsCostExPrice = 20000,
                ItemsCostPrice = 20000,
                SaleId = sale.Id,
                ItemIdOne = item.IdOne,
                ItemIdTwo = item.IdTwo,
            };

            RepositoryWrapper.TransactionRepository.Create(transaction);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        [Test]
        [Category("RecordPropertyConstraint")]
        public void TransactionRecordSaleIdIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
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

            var role = new Role()
            {
                Name = "Employee Role"
            };

            RepositoryWrapper.RoleRepository.Create(role);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var transaction = new Transaction()
            {
                Amount = 20000,
                ItemsCostExPrice = 20000,
                ItemsCostPrice = 20000,
                TillId = till.Id,
                ItemIdOne = item.IdOne,
                ItemIdTwo = item.IdTwo,
            };

            RepositoryWrapper.TransactionRepository.Create(transaction);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Transaction

        /*#region Stock
        [Test]
        [Category("RecordPropertyConstraint")]
        public void StockRecordStoreIdIsRequired()
        {
            var bussiness = new Bussiness()
            {
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description"
            };

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();


            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id
            };

            RepositoryWrapper.ItemRepository.Create(item);
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

            var stock = new Stock()
            {
                IdOne = item.IdOne,
                IdTwo = item.IdTwo,
                Quantity = 12
            };

            RepositoryWrapper.StockRepository.Create(stock);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Stock*/

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
        public async Task NoteSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";

            var note = new Note()
            {
                Text = "Note Text",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.NoteRepository.Create(note);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(note.CreatedBy, systemName);
            Assert.AreEqual(note.ModifiedBy, systemName);
            Assert.AreEqual(note.CreatedAt, currentTime);
            Assert.AreEqual(note.ModifiedAt, currentTime);
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

        [Test]
        [Category("SyncHandling")]
        public async Task TaxesSyncOnly()
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

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(tax.CreatedBy, systemName);
            Assert.AreEqual(tax.ModifiedBy, systemName);
            Assert.AreEqual(tax.CreatedAt, currentTime);
            Assert.AreEqual(tax.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task ItemSyncOnly()
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

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(item.CreatedBy, systemName);
            Assert.AreEqual(item.ModifiedBy, systemName);
            Assert.AreEqual(item.CreatedAt, currentTime);
            Assert.AreEqual(item.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task TransactionSyncOnly()
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

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.ItemRepository.Create(item);
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

            await RepositoryWrapper.TillRepository.Create(till);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var role = new Role()
            {
                Name = "Employee Role",
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
                Email = store.Id + "@test.com",
                BussinessId = bussiness.Id,
                RoleId = role.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var sale = new Sale()
            {
                Total = 2000,
                TotalExTax = 2000,
                DateOfSale = DateTime.Now,
                StoreId = store.Id,
                EmployeeId = employee.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.SaleRepository.Create(sale);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var transaction = new Transaction()
            {
                Amount = 20000,
                ItemsCostExPrice = 20000,
                ItemsCostPrice = 20000,
                TillId = till.Id,
                SaleId = sale.Id,
                ItemIdOne = item.IdOne,
                ItemIdTwo = item.IdTwo,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);

            await RepositoryWrapper.TransactionRepository.Create(transaction);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(transaction.CreatedBy, systemName);
            Assert.AreEqual(transaction.ModifiedBy, systemName);
            Assert.AreEqual(transaction.CreatedAt, currentTime);
            Assert.AreEqual(transaction.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task StockSyncOnly()
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

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var category = new Category()
            {
                Name = "Test Name",
                Description = "Test Description",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var item = new Item()
            {
                Name = "Item Name",
                Brand = "Item Brand Name",
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = tax.IdOne,
                CatId = category.Id,
                IdTwo = bussiness.Id,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            await RepositoryWrapper.ItemRepository.Create(item);
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

            var stock = new Stock()
            {
                IdOne = item.IdOne,
                IdTwo = item.IdTwo,
                IdThree = store.Id,
                Quantity = 12,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.StockRepository.Create(stock);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(stock.CreatedBy, systemName);
            Assert.AreEqual(stock.ModifiedBy, systemName);
            Assert.AreEqual(stock.CreatedAt, currentTime);
            Assert.AreEqual(stock.ModifiedAt, currentTime);
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

            DbContext.Trans.RemoveRange(DbContext.Trans);
            DbContext.Sales.RemoveRange(DbContext.Sales);
            DbContext.Employees.RemoveRange(DbContext.Employees);
            DbContext.Category.RemoveRange(DbContext.Category);
            DbContext.Till.RemoveRange(DbContext.Till);
            DbContext.Stores.RemoveRange(DbContext.Stores);
            DbContext.Role.RemoveRange(DbContext.Role);
            DbContext.Discounts.RemoveRange(DbContext.Discounts);
            DbContext.Notes.RemoveRange(DbContext.Notes);
            DbContext.Bussiness.RemoveRange(DbContext.Bussiness);

            DbContext.SaveChanges();
        }
    }
}