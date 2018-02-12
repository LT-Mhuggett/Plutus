using System;
using System.Collections.Generic;
using System.IO;
using Plutus.Data;
using Plutus.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Plutus.Models.Interface;
using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Plutus.Helpers
{
    internal class Database
    {
        private static Context _db;

        internal Database()
        {
            _db = new Context(Path.Combine(FileIO.GetLib(), "Database.db"));
            _db.Database.EnsureCreated();
        }

        internal async void Add<T>(T tmp) where T : class
        {
            await _db.Set<T>().AddAsync(tmp);
        }

        internal bool Save()
        {
            try
            {
                _db.SaveChanges();
                return true;
            }
            catch(Exception e)
            {
                Console.Write(e);
                return false;
            }
        }

        internal void RevertDbContextChanges()
        {
            for (var i = 0; i <= _db.ChangeTracker.Entries().Count() - 1; i++)
            {
                switch (_db.ChangeTracker.Entries().ElementAt(i).State)
                {
                    case EntityState.Modified:
                        _db.ChangeTracker.Entries().ElementAt(i).State = EntityState.Unchanged;
                        break;
                    case EntityState.Added:
                        _db.ChangeTracker.Entries().ElementAt(i).State = EntityState.Detached;
                        break;
                    case EntityState.Deleted:
                        _db.ChangeTracker.Entries().ElementAt(i).Reload();
                        break;
                    default:
                        break;
                }
            }
        }

        internal bool CheckIfChanged<T>(T obj) where T : class
        {
            return _db.Entry<T>(obj).State == EntityState.Modified;
        }

        internal void RevertDbContextChange(EntityEntry model)
        {

        }

        internal IQueryable<T> Get<T>() where T : class => _db.Set<T>();

        internal void Init()
        {
            /*var vat = new TaxModel() { Name = "0%", Rate = 1 };
            Add(vat);
            var vat2 = new TaxModel() {Name = "20%", Rate = 1.2};
            Add(vat2);
            var vat3 = new TaxModel() {Name = "No VAT", Rate = 1};
            Add(vat3);
            */
            //AuthActions Initalization
            var AuthAction = new AuthActions() { Name = "Till" };
            Add(AuthAction);
            var AuthAction1 = new AuthActions() { Name = "Refund20", Amount = 20 };
            Add(AuthAction1);
            var AuthAction2 = new AuthActions() { Name = "Refund100", Amount = 100 };
            Add(AuthAction2);
            var AuthAction3 = new AuthActions() { Name = "Staff" };
            Add(AuthAction3);
            var AuthAction4 = new AuthActions() { Name = "Item" };
            Add(AuthAction4);
            var AuthAction5 = new AuthActions() { Name = "Force Loggout All Users" };
            Add(AuthAction5);
            var AuthAction6 = new AuthActions() { Name = "Force Loggout Single User" };
            Add(AuthAction6);
            var AuthAction7 = new AuthActions() { Name = "Refund Unlimited", Amount = 100000 };
            Add(AuthAction7);
            var AuthAction8 = new AuthActions() { Name = "Report" };
            Add(AuthAction8);
            var AuthAction9 = new AuthActions() { Name = "Admin" };
            Add(AuthAction9);
            var AuthAction10 = new AuthActions() {Name = "Management"};
            Add(AuthAction10);

            var payM = new PaymentMethodModel() { Name = "Card", Charge = 0.0m, MinimumCharge = 0.0m, IsChangeable = false, IsCashBackable = true };
            Add(payM);
            var payM2 = new PaymentMethodModel() { Name = "Cash", Charge = 0.0m, MinimumCharge = 0.0m, IsChangeable = true, IsCashBackable = false };
            Add(payM2);
            //tempData();

            _db.SaveChanges();
        }

        internal async Task<EmployeeModel> Login(string idEmail, string password)
        {
            var emp = Get<EmployeeModel>()
                .Include(e=>e.EmpAuths)
                .SingleOrDefault(e => e.Id.Equals(idEmail) || e.Email.Equals(idEmail));
            if (emp == null)
                return null;
            if (await Task.Run(() => 
                    Password.Verify(password, Convert.FromBase64String(emp.Salt), Convert.FromBase64String(emp.HashedPassword))))
                return emp;
            emp = null;
            return null;
        }

        internal IQueryable GetById<TOne, TTwo>(TTwo id) where TOne : class => Get<TOne>()
            .OfType<IBase<TTwo>>()
            .Where(m => m.Id.Equals(id));

        internal bool IsIdSame<TOne, TTwo>(TTwo id) where TOne : class => GetById<TOne, TTwo>(id)
            .OfType<TOne>().Select(i => i).Any();

        internal IQueryable<ItemModel> Search(string temp) => Get<ItemModel>()
            .Include(a => a.Vat)
            .Where(i => i.Id.Equals(temp) ||
                CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                    i.Name, temp, CompareOptions.IgnoreCase) >= 0);

        internal void UpdateStock(StockModel toUpdateModel)
        {
            var query = from stock in _db.Stocks
                        where stock.ItemId.Equals(toUpdateModel.ItemId) &&
                            stock.StoreId.Equals(toUpdateModel.StoreId)
                        select stock;
            foreach(StockModel stock in query)
            {
                stock.Quantity += toUpdateModel.Quantity;
            }
        }

        internal void UpdateItem(ItemModel item)
        {
            var query = GetById<ItemModel, string>(item.Id)
                .OfType<ItemModel>()
                .Select(i => i);

            foreach(ItemModel fItem in query)
            {
                fItem.Name = item.Name;
                fItem.Image = item.Image;
                fItem.Desc = item.Desc;
                fItem.Brand = item.Brand;
                fItem.CatId = item.CatId;
                fItem.Cost = item.Cost;
                fItem.VatId = item.VatId;
                fItem.Price = item.Price;
            }
        }

        internal IQueryable<PaymentMethodModel> GetPayM(string name) => Get<PaymentMethodModel>()
                .Where(p => p.Name.Equals(name));

        internal IQueryable<TransactionModel> CheckItemExistInSale(string saleId, string itemId) => Get<TransactionModel>()
                .Include(t => t.Sale)
                .Where(t => t.SaleId.Equals(saleId) && t.ItemId.Equals(itemId));

        internal NoteModel GetNote(string noteTemp) => Get<NoteModel>()
            .FirstOrDefault(n => n.Note.Equals(noteTemp));

        internal IQueryable<ItemModel> GetAllItems() => Get<ItemModel>()
            .Include(i => i.Vat)
            .Include(i => i.Cat)
            .Include(i => i.Transactions)
            .Include(i => i.Stock);

        internal IQueryable<SaleModel> GetSales(string condition) => Get<SaleModel>()
            .Include(s => s.Notes)
            .Include(s => s.Refunded)
            .Include(s => s.Refunds)
            .Include(s => s.Transactions)
            .Include(s => s.PaySales)
                .ThenInclude(ps => ps.PayMethod)
            .Where(s => s.DateOfSale.ToString().Contains(condition)
                || s.EmployeeId.Equals(condition));

        internal IQueryable<SaleModel> GetSales() => Get<SaleModel>()
            .Include(s => s.Notes)
            .Include(s => s.Refunded)
            .Include(s => s.Refunds)
            .Include(s => s.Transactions)
                .ThenInclude(t=>t.Item)
            .Include(s => s.PaySales)
                .ThenInclude(ps => ps.PayMethod);

        internal List<DateTime> GetDateOfSales()
        {
            var data = Get<SaleModel>().ToList();
            return data.Select(s => s.DateOfSale).ToList();
        }

        internal IIncludableQueryable<EmployeeModel, StoreModel> GetAllEmps()
        {
            var emps = _db.Employees
                .Include(i => i.EmpAuths)
                .Include(i => i.Store);
            return emps;
        }

        internal EmployeeModel GetEmp(string id)
        {
            var emp = _db.Employees
                .Include(e => e.EmpAuths)
                .Include(e => e.Store)
                .FirstOrDefault(e => e.Id.Equals(id));
            return emp ?? null;
        }

        private void tempData()
        {

            //Will be removed as only applies to UK, User will have to add manually
            var cat = new CategoryModel() { Name = "Customer Care", Description = "Items such as Bags etc." };
            Add(cat);
            var cat2 = new CategoryModel() { Name = "Book", Description = "Readable information" };
            Add(cat2);
            _db.SaveChanges();
            var bag = new ItemModel() { Id = "BAG001", Name = "Bag", Desc = "Item to allow Customers to carry things", CatId = 1, VatId = 2, Price = .05m, Cost = 0.0m };
            Add(bag);

            //There will be a more detailed setup page this temporay
            var payM = new PaymentMethodModel() { Name = "Card", Charge = 0.5m, MinimumCharge = 5.0m, IsChangeable = false, IsCashBackable = true };
            Add(payM);
            var payM2 = new PaymentMethodModel() { Name = "Cash", Charge = 0.0m, MinimumCharge = 0.0m, IsChangeable = true, IsCashBackable = false };
            Add(payM2);

            var item1 = new ItemModel()
            {
                Id = "9781593072995",
                Name = "Sin City book 7",
                Brand = "Sin City",
                CatId = 2,
                VatId = 2,
                Price = 15.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 15.00m,
                ExPrice = 15.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item1);
            var item2 = new ItemModel()
            {
                Id = "69978810954137",
                Name = "Arkham horror board game",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item2);
            var item3 = new ItemModel()
            {
                Id = "699788154309137",
                Name = "Arkham horror board game2",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item3);
            var item4 = new ItemModel()
            {
                Id = "69978813409137",
                Name = "Arkham horror board game3",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item4);
            var item5 = new ItemModel()
            {
                Id = "6997881019137",
                Name = "Arkham horror board game4",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item5);
            var item6 = new ItemModel()
            {
                Id = "69978t8109137",
                Name = "Arkham horror board game5",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item6);
            var item7 = new ItemModel()
            {
                Id = "6997881n09137",
                Name = "Arkham horror board game6",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item7);
            var item8 = new ItemModel()
            {
                Id = "6997881b09137",
                Name = "Arkham horror board game7",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item8);
            var item9 = new ItemModel()
            {
                Id = "699788v109137",
                Name = "Arkham horror board game8",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item9);
            var item10 = new ItemModel()
            {
                Id = "699788vn109137",
                Name = "Arkham horror board game9",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item10);
            var item11 = new ItemModel()
            {
                Id = "699788109137",
                Name = "Arkham horror board game10",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item11);
            var item12 = new ItemModel()
            {
                Id = "699788109k137",
                Name = "Arkham horror board game11",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item12);
            var item13 = new ItemModel()
            {
                Id = "69a9788109137",
                Name = "Arkham horror board game12",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item13);
            var item14 = new ItemModel()
            {
                Id = "69978810k9k137",
                Name = "Arkham horror board game13",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item14);
            var item15 = new ItemModel()
            {
                Id = "69a978j8109137",
                Name = "Arkham horror board game14",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item15);
            var item16 = new ItemModel()
            {
                Id = "699788fg109k137",
                Name = "Arkham horror board game15",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item16);
            var item17 = new ItemModel()
            {
                Id = "69a97881091kj37",
                Name = "Arkham horror board game16",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item17);
            var item18 = new ItemModel()
            {
                Id = "699788109k13hg7",
                Name = "Arkham horror board game17",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item18);
            var item19 = new ItemModel()
            {
                Id = "69a9788109137ui",
                Name = "Arkham horror board game18",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item19);
            var item20 = new ItemModel()
            {
                Id = "699788109k13sdfs7",
                Name = "Arkham horror board game19",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item20);
            var item21 = new ItemModel()
            {
                Id = "69a978810913fdgg7",
                Name = "Arkham horror board game20",
                Brand = "Batman",
                CatId = 2,
                VatId = 2,
                Price = 30.00m * App.Store.RecMarkup.GetValueOrDefault() * 1.2m,
                Cost = 30.00m,
                ExPrice = 30.00m * App.Store.RecMarkup.GetValueOrDefault()
            };
            Add(item21);
            var Dis = new DiscountModel() { Name = "BOGOF", Type = 1, Amount = 1, RequiredNumOfItems = 2, UsesPerTransaction = -1 };
            var Cat = new Discount_Category() { Cat = cat2, StartDateTime = DateTime.ParseExact("2017-05-05", "yyyy-MM-dd", null), EndDateTime = DateTime.ParseExact("2018-12-30", "yyyy-MM-dd", null) };
            Dis.DisCategoryList = new List<Discount_Category>
            {
                Cat
            };
            Add(Cat);
            Add(Dis);
        }
    }
}
