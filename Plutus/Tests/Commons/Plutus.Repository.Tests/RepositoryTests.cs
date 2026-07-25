using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Plutus.Entities;
using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.Repository.Tests
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
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
        public async Task BusinessPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task NotePersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));
            var employee = await RepositoryWrapper.Persist(TestFixtures.NewEmployee(store.Id, business.Id, role.Id));
            var till = await RepositoryWrapper.Persist(TestFixtures.NewTill(store.Id));
            var sale = await RepositoryWrapper.Persist(TestFixtures.NewSale(store.Id, employee.Id, till.Id));

            var note = new Note()
            {
                Text = "Note Text",
                IdTwo = sale.Id
            };

            await RepositoryWrapper.NoteRepository.Create(note);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(note, DbContext.Notes.Find(note.IdOne, note.IdTwo));
        }

        [Test]
        [Category("RecordPersistance")]
        public void SavedTransactionsPersists()
        {
            var savedTransaction = new SavedTransaction()
            {
                Name = "Transaction Name",
                Data = "Transaction Data"
            };

            RepositoryWrapper.SavedTransactionRepository.Create(savedTransaction);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(savedTransaction, DbContext.SavedTransactions.Find(savedTransaction.Id));
            //Assert.AreEqual(1, DbContext.Business.Count());
        }

        [Test]
        [Category("RecordPersistance")]
        public void PaymentMethodsPersists()
        {
            var paymentMethod = new PaymentMethod()
            {
                Name = "Payment Method Name",
                Charge = 1999,
                MinimumCharge = 1999,
                IsChangeable = false,
                IsCashBackable = false
            };

            RepositoryWrapper.PaymentMethodRepository.Create(paymentMethod);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(paymentMethod, DbContext.PayMethods.Find(paymentMethod.Id));
            //Assert.AreEqual(1, DbContext.Business.Count());
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task CategoryPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));

            Assert.AreEqual(category, DbContext.Category.Find(category.IdOne, category.IdTwo));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task RolePersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id, "Test Role"));

            Assert.AreEqual(role, DbContext.Role.Find(role.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task StorePersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task TillPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var till = await RepositoryWrapper.Persist(TestFixtures.NewTill(store.Id));

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
            Assert.AreEqual(till, DbContext.Till.Find(till.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task EmployeePersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));
            var employee = await RepositoryWrapper.Persist(TestFixtures.NewEmployee(store.Id, business.Id, role.Id));

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
            Assert.AreEqual(role, DbContext.Role.Find(role.Id));
            Assert.AreEqual(employee, DbContext.Employees.Find(employee.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task DiscountPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());

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
                BusinessId = business.Id
            };

            await RepositoryWrapper.DiscountRepository.Create(discount);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(discount, DbContext.Discounts.Find(discount.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task TaxPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, business.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task ItemPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));
            var item = await RepositoryWrapper.Persist(TestFixtures.NewItem("ITEMPERSISTS01", business.Id, tax.IdOne, category.IdOne));

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, business.Id));
            Assert.AreEqual(category, DbContext.Category.Find(category.IdOne, business.Id));
            Assert.AreEqual(item, DbContext.Items.Find(item.IdOne, business.Id));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task TransactionPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));
            var item = await RepositoryWrapper.Persist(TestFixtures.NewItem("ITEMTRANSPST01", business.Id, tax.IdOne, category.IdOne));
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var till = await RepositoryWrapper.Persist(TestFixtures.NewTill(store.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));
            var employee = await RepositoryWrapper.Persist(TestFixtures.NewEmployee(store.Id, business.Id, role.Id));
            var sale = await RepositoryWrapper.Persist(TestFixtures.NewSale(store.Id, employee.Id, till.Id));

            var transaction = new Transaction()
            {
                Amount = 20000,
                ItemCostExPrice = 20000,
                ItemCostPrice = 20000,
                TillId = till.Id,
                IdTwo = sale.Id,
                ItemIdOne = item.IdOne,
                ItemIdTwo = item.IdTwo,
            };

            await RepositoryWrapper.TransactionRepository.Create(transaction);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, business.Id));
            Assert.AreEqual(category, DbContext.Category.Find(category.IdOne, business.Id));
            Assert.AreEqual(item, DbContext.Items.Find(item.IdOne, business.Id));
            Assert.AreEqual(transaction, DbContext.Trans.Find(transaction.IdOne, transaction.IdTwo));
        }

        [Test]
        [Category("RecordPersistance")]
        public async Task StockPersists()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));
            var item = await RepositoryWrapper.Persist(TestFixtures.NewItem("ITEMSTOCKPST01", business.Id, tax.IdOne, category.IdOne));
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));

            var stock = new Stock()
            {
                IdOne = item.IdOne,
                IdTwo = item.IdTwo,
                IdThree = store.Id,
                Quantity = 12
            };

            await RepositoryWrapper.StockRepository.Create(stock);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            Assert.AreEqual(business, DbContext.Business.Find(business.Id));
            Assert.AreEqual(tax, DbContext.Taxes.Find(tax.IdOne, business.Id));
            Assert.AreEqual(category, DbContext.Category.Find(category.IdOne, business.Id));
            Assert.AreEqual(item, DbContext.Items.Find(item.IdOne, business.Id));
            Assert.AreEqual(store, DbContext.Stores.Find(store.Id));
            Assert.AreEqual(stock, DbContext.Stocks.Find(stock.IdOne, stock.IdTwo, stock.IdThree));
        }


        #endregion

        #region Record Property Constraint Tests

        #region Business

        private static IEnumerable<TestCaseData> BusinessRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Business>)(b => b.Name = null)).SetName("BusinessRecordFieldIsRequired(Name)");
            yield return new TestCaseData((Action<Business>)(b => b.NameAbbr = null)).SetName("BusinessRecordFieldIsRequired(NameAbbr)");
        }

        [TestCaseSource(nameof(BusinessRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public void BusinessRecordFieldIsRequired(Action<Business> omitField)
        {
            var business = TestFixtures.NewBusiness();
            omitField(business);

            RepositoryWrapper.BusinessRepository.Create(business);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Business

        #region Category

        private static IEnumerable<TestCaseData> CategoryRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Category>)(c => c.Name = null)).SetName("CategoryRecordFieldIsRequired(Name)");
            yield return new TestCaseData((Action<Category>)(c => c.Description = null)).SetName("CategoryRecordFieldIsRequired(Description)");
        }

        [TestCaseSource(nameof(CategoryRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public void CategoryRecordFieldIsRequired(Action<Category> omitField)
        {
            var category = new Category { Name = "Test Name", Description = "Test Description" };
            omitField(category);

            RepositoryWrapper.CategoryRepository.Create(category);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Category

        #region Store
        private static IEnumerable<TestCaseData> StoreRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Store>)(s => s.ContactNumber = null)).SetName("StoreRecordFieldIsRequired(ContactNumber)");
            yield return new TestCaseData((Action<Store>)(s => s.PostCode = null)).SetName("StoreRecordFieldIsRequired(PostCode)");
            yield return new TestCaseData((Action<Store>)(s => s.BusinessId = default)).SetName("StoreRecordFieldIsRequired(BusinessId)");
        }

        [TestCaseSource(nameof(StoreRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public async Task StoreRecordFieldIsRequired(Action<Store> omitField)
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = TestFixtures.NewStore(business.Id);
            omitField(store);

            await RepositoryWrapper.StoreRepository.Create(store);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Store

        #region Till
        [Test]
        [Category("RecordPersistance")]
        public void TillRecordStoreIdIsRequired()
        {
            var till = new Till()
            {
                Id = Guid.NewGuid(),
                CashFloat = 100,
                LastOnline = DateTime.Now
            };

            RepositoryWrapper.TillRepository.Create(till);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Till

        #region Item
        private static IEnumerable<TestCaseData> ItemRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Item>)(i => i.Name = null)).SetName("ItemRecordFieldIsRequired(Name)");
            yield return new TestCaseData((Action<Item>)(i => i.Brand = null)).SetName("ItemRecordFieldIsRequired(Brand)");
            yield return new TestCaseData((Action<Item>)(i => i.TaxId = default)).SetName("ItemRecordFieldIsRequired(TaxId)");
            yield return new TestCaseData((Action<Item>)(i => i.CatId = default)).SetName("ItemRecordFieldIsRequired(CatId)");
        }

        [TestCaseSource(nameof(ItemRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public async Task ItemRecordFieldIsRequired(Action<Item> omitField)
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));

            var item = TestFixtures.NewItem("ITEMREQFIELD01", business.Id, tax.IdOne, category.IdOne);
            omitField(item);

            await RepositoryWrapper.ItemRepository.Create(item);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Item

        #region Tax
        [Test]
        [Category("RecordPropertyConstraint")]
        public async Task TaxRecordNameIsRequired()
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = TestFixtures.NewTax(business.Id);
            tax.Name = null;

            await RepositoryWrapper.TaxRepository.Create(tax);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Tax

        #region Discount
        private static IEnumerable<TestCaseData> DiscountRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Discount>)(d => d.Name = null)).SetName("DiscountRecordFieldIsRequired(Name)");
            yield return new TestCaseData((Action<Discount>)(d => d.BusinessId = default)).SetName("DiscountRecordFieldIsRequired(BusinessId)");
        }

        [TestCaseSource(nameof(DiscountRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public async Task DiscountRecordFieldIsRequired(Action<Discount> omitField)
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var discount = new Discount
            {
                Name = "Test Name",
                AllApplicable = true,
                CanUseWithOtherDiscounts = false,
                AutoApply = true,
                Type = 1,
                Amount = 13556,
                UsesPerTransaction = 1,
                RequiredNumOfItems = 1,
                BusinessId = business.Id
            };
            omitField(discount);

            await RepositoryWrapper.DiscountRepository.Create(discount);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Discount

        #region Employee
        private static IEnumerable<TestCaseData> EmployeeRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Employee>)(e => e.FName = null)).SetName("EmployeeRecordFieldIsRequired(FName)");
            yield return new TestCaseData((Action<Employee>)(e => e.LName = null)).SetName("EmployeeRecordFieldIsRequired(LName)");
            yield return new TestCaseData((Action<Employee>)(e => e.StoreId = default)).SetName("EmployeeRecordFieldIsRequired(StoreId)");
            yield return new TestCaseData((Action<Employee>)(e => e.Mobile = null)).SetName("EmployeeRecordFieldIsRequired(Mobile)");
            yield return new TestCaseData((Action<Employee>)(e => e.Email = null)).SetName("EmployeeRecordFieldIsRequired(Email)");
        }

        [TestCaseSource(nameof(EmployeeRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public async Task EmployeeRecordFieldIsRequired(Action<Employee> omitField)
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));

            var employee = TestFixtures.NewEmployee(store.Id, business.Id, role.Id);
            omitField(employee);

            await RepositoryWrapper.EmployeeRepository.Create(employee);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion Employee

        #region Transaction

        private static IEnumerable<TestCaseData> TransactionRequiredFieldCases()
        {
            yield return new TestCaseData((Action<Transaction>)(t => t.TillId = default)).SetName("TransactionRecordFieldIsRequired(TillId)");
            yield return new TestCaseData((Action<Transaction>)(t => t.IdTwo = default)).SetName("TransactionRecordFieldIsRequired(SaleId)");
        }

        [TestCaseSource(nameof(TransactionRequiredFieldCases))]
        [Category("RecordPropertyConstraint")]
        public async Task TransactionRecordFieldIsRequired(Action<Transaction> omitField)
        {
            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));
            var item = await RepositoryWrapper.Persist(TestFixtures.NewItem("ITEMTRANREQ001", business.Id, tax.IdOne, category.IdOne));
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));
            var employee = await RepositoryWrapper.Persist(TestFixtures.NewEmployee(store.Id, business.Id, role.Id));
            var till = await RepositoryWrapper.Persist(TestFixtures.NewTill(store.Id));
            var sale = await RepositoryWrapper.Persist(TestFixtures.NewSale(store.Id, employee.Id, till.Id));

            var transaction = new Transaction
            {
                Amount = 20000,
                ItemCostExPrice = 20000,
                ItemCostPrice = 20000,
                TillId = till.Id,
                IdTwo = sale.Id,
                ItemIdOne = item.IdOne,
                ItemIdTwo = item.IdTwo,
            };
            omitField(transaction);

            await RepositoryWrapper.TransactionRepository.Create(transaction);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion Transaction

        #region PaymentMethod
        
        [Test]
        [Category("RecordPropertyConstraint")]
        public void PaymentMethodRecordNameIsRequired()
        {
            var paymentMethod = new PaymentMethod()
            {
                Charge = 1999,
                MinimumCharge = 1999,
                IsChangeable = false,
                IsCashBackable = false
            };

            RepositoryWrapper.PaymentMethodRepository.Create(paymentMethod);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }

        #endregion PaymentMethod




        #region SavedTransaction

        [Test]
        [Category("RecordPropertyConstraint")]
        public void SavedTransactionRecordNameIsRequired()
        {
            var savedTransaction = new SavedTransaction()
            {
                Data = "Transaction Data"
            };

            RepositoryWrapper.SavedTransactionRepository.Create(savedTransaction);
            RepositoryWrapper.SetCurrentUser("Test");
            Assert.Throws<DbUpdateException>(() => RepositoryWrapper.Save());
        }
        #endregion SavedTransaction

        /*#region Stock
        [Test]
        [Category("RecordPropertyConstraint")]
        public void StockRecordStoreIdIsRequired()
        {
            var Business = new Business()
            {
                Name = "Test Name",
                NameAbbr = "TN",
                VatIN = "GB123456789"
            };

            RepositoryWrapper.BusinessRepository.Create(Business);
            RepositoryWrapper.SetCurrentUser("Test");
            RepositoryWrapper.Save();

            var tax = new Tax()
            {
                Name = "Tax Name",
                Rate = 4.8,
                IdTwo = Business.Id
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
                IdTwo = Business.Id
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
                BusinessId = Business.Id
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

            var business = TestFixtures.NewBusiness();
            business.CreatedAt = currentTime;
            business.CreatedBy = systemName;
            business.ModifiedAt = currentTime;
            business.ModifiedBy = systemName;

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.BusinessRepository.Create(business);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(business.CreatedBy, systemName);
            Assert.AreEqual(business.ModifiedBy, systemName);
            Assert.AreEqual(business.CreatedAt, currentTime);
            Assert.AreEqual(business.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task NoteSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));
            var employee = await RepositoryWrapper.Persist(TestFixtures.NewEmployee(store.Id, business.Id, role.Id));
            var till = await RepositoryWrapper.Persist(TestFixtures.NewTill(store.Id));
            var sale = await RepositoryWrapper.Persist(TestFixtures.NewSale(store.Id, employee.Id, till.Id));

            var note = new Note()
            {
                Text = "Note Text",
                IdTwo = sale.Id,
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
        public async Task PaymentMethodsSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";

            var paymentMethod = new PaymentMethod()
            {
                Name = "Payment Method Name",
                Charge = 1999,
                MinimumCharge = 1999,
                IsChangeable = false,
                IsCashBackable = false,
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.PaymentMethodRepository.Create(paymentMethod);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(paymentMethod.CreatedBy, systemName);
            Assert.AreEqual(paymentMethod.ModifiedBy, systemName);
            Assert.AreEqual(paymentMethod.CreatedAt, currentTime);
            Assert.AreEqual(paymentMethod.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task SavedTransactionSyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";
            var savedTransaction = new SavedTransaction()
            {
                Name = "Transaction Name",
                Data = "Transaction Data",
                CreatedAt = currentTime,
                CreatedBy = systemName,
                ModifiedAt = currentTime,
                ModifiedBy = systemName
            };

            RepositoryWrapper.SetSyncState(true);
            await Task.Delay(10);
            RepositoryWrapper.SetCurrentUser(systemName);
            await RepositoryWrapper.SavedTransactionRepository.Create(savedTransaction);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(savedTransaction.CreatedBy, systemName);
            Assert.AreEqual(savedTransaction.ModifiedBy, systemName);
            Assert.AreEqual(savedTransaction.CreatedAt, currentTime);
            Assert.AreEqual(savedTransaction.ModifiedAt, currentTime);
        }

        [Test]
        [Category("SyncHandling")]
        public async Task CategorySyncOnly()
        {
            var currentTime = DateTime.UtcNow;
            var systemName = "Plutus.Repository.Tests";

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());

            var category = TestFixtures.NewCategory(business.Id);
            category.CreatedAt = currentTime;
            category.CreatedBy = systemName;
            category.ModifiedAt = currentTime;
            category.ModifiedBy = systemName;

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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());

            var store = TestFixtures.NewStore(business.Id);
            store.CreatedAt = currentTime;
            store.CreatedBy = systemName;
            store.ModifiedAt = currentTime;
            store.ModifiedBy = systemName;

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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));

            var till = TestFixtures.NewTill(store.Id);
            till.CreatedAt = currentTime;
            till.CreatedBy = systemName;
            till.ModifiedAt = currentTime;
            till.ModifiedBy = systemName;

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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id, "Test Role"));

            var employee = TestFixtures.NewEmployee(store.Id, business.Id, role.Id);
            employee.CreatedAt = currentTime;
            employee.CreatedBy = systemName;
            employee.ModifiedAt = currentTime;
            employee.ModifiedBy = systemName;

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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());

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
                BusinessId = business.Id,
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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());

            var tax = TestFixtures.NewTax(business.Id);
            tax.CreatedAt = currentTime;
            tax.CreatedBy = systemName;
            tax.ModifiedAt = currentTime;
            tax.ModifiedBy = systemName;

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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));

            var item = TestFixtures.NewItem("ITEMSYNCONLY01", business.Id, tax.IdOne, category.IdOne);
            item.CreatedAt = currentTime;
            item.CreatedBy = systemName;
            item.ModifiedAt = currentTime;
            item.ModifiedBy = systemName;

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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));
            var item = await RepositoryWrapper.Persist(TestFixtures.NewItem("ITEMTRANSYNC01", business.Id, tax.IdOne, category.IdOne));
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));
            var till = await RepositoryWrapper.Persist(TestFixtures.NewTill(store.Id));
            var role = await RepositoryWrapper.Persist(TestFixtures.NewRole(business.Id));
            var employee = await RepositoryWrapper.Persist(TestFixtures.NewEmployee(store.Id, business.Id, role.Id));
            var sale = await RepositoryWrapper.Persist(TestFixtures.NewSale(store.Id, employee.Id, till.Id));

            var transaction = new Transaction()
            {
                Amount = 20000,
                ItemCostExPrice = 20000,
                ItemCostPrice = 20000,
                TillId = till.Id,
                IdTwo = sale.Id,
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

            var business = await RepositoryWrapper.Persist(TestFixtures.NewBusiness());
            var tax = await RepositoryWrapper.Persist(TestFixtures.NewTax(business.Id));
            var category = await RepositoryWrapper.Persist(TestFixtures.NewCategory(business.Id));
            var item = await RepositoryWrapper.Persist(TestFixtures.NewItem("ITEMSTOCKSYNC1", business.Id, tax.IdOne, category.IdOne));
            var store = await RepositoryWrapper.Persist(TestFixtures.NewStore(business.Id));

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
            DbContext.Business.RemoveRange(DbContext.Business);

            DbContext.SaveChanges();
        }
    }
}