using Plutus.Entities.Models;
using System;
using System.Threading.Tasks;

namespace Plutus.Repository.Tests
{
    /// <summary>
    /// Builders for the entity graphs RepositoryTests exercises repeatedly. Every method fills in
    /// every field that is required at the database level - including ones that carry no [Required]
    /// C# attribute (Business.VatIN, Employee.NIN, Sale.TillId, Role.BusinessId, Item.IdOne) and so
    /// only ever failed at the MySQL layer. Centralising them here means a newly-discovered required
    /// field only needs to be added in one place instead of in every test that builds that entity.
    /// Callers pass null/override individual fields to exercise a specific "field is required" case.
    /// </summary>
    internal static class TestFixtures
    {
        public static Business NewBusiness(
            string name = "Test Name",
            string nameAbbr = "TN",
            string vatIN = "GB123456789") => new Business
            {
                Name = name,
                NameAbbr = nameAbbr,
                VatIN = vatIN
            };

        public static Store NewStore(
            Guid businessId,
            string contactNumber = "+449672513556",
            string adLine1 = "Address Line 1",
            string adLine2 = "Address Line 2",
            string city = "Test City",
            string postCode = "201304",
            string country = "England") => new Store
            {
                ContactNumber = contactNumber,
                AdLine1 = adLine1,
                AdLine2 = adLine2,
                City = city,
                PostCode = postCode,
                Country = country,
                BusinessId = businessId
            };

        public static Till NewTill(
            int storeId,
            decimal cashFloat = 100) => new Till
            {
                Id = Guid.NewGuid(),
                StoreId = storeId,
                CashFloat = cashFloat,
                LastOnline = DateTime.Now
            };

        public static Role NewRole(
            Guid businessId,
            string name = "Employee Role") => new Role
            {
                Name = name,
                BusinessId = businessId
            };

        public static Employee NewEmployee(
            int storeId,
            Guid businessId,
            int roleId,
            string nin = "AB123456C",
            string fName = "John",
            string lName = "Doe",
            string mobile = "+449672513556",
            string email = null) => new Employee
            {
                NIN = nin,
                Wage = 1000,
                ContractedHours = 40,
                Active = true,
                StoreId = storeId,
                AdLine1 = "Address Line 1",
                AdLine2 = "Address Line 2",
                City = "London",
                PostCode = "203440",
                Country = "England",
                FName = fName,
                LName = lName,
                Mobile = mobile,
                Email = email ?? (Guid.NewGuid() + "@test.com"),
                BusinessId = businessId,
                RoleId = roleId
            };

        public static Sale NewSale(
            int storeId,
            Guid employeeId,
            Guid tillId) => new Sale
            {
                Total = 2000,
                TotalExTax = 2000,
                DateOfSale = DateTime.Now,
                StoreId = storeId,
                EmployeeId = employeeId,
                TillId = tillId
            };

        public static Tax NewTax(
            Guid businessId,
            string name = "Tax Name",
            double rate = 4.8) => new Tax
            {
                Name = name,
                Rate = rate,
                IdTwo = businessId
            };

        public static Category NewCategory(
            Guid businessId,
            string name = "Test Name",
            string description = "Test Description") => new Category
            {
                Name = name,
                Description = description,
                IdTwo = businessId
            };

        public static Item NewItem(
            string idOne,
            Guid businessId,
            int taxId,
            Guid catId,
            string name = "Item Name",
            string brand = "Item Brand Name") => new Item
            {
                Name = name,
                Brand = brand,
                Desc = "Y",
                Cost = 9999,
                ExPrice = 9999,
                Price = 9999,
                TaxId = taxId,
                CatId = catId,
                IdOne = idOne,
                IdTwo = businessId
            };
    }

    /// <summary>
    /// Collapses the repeated Create-then-SetCurrentUser-then-Save triplet into one call per entity,
    /// and gives the compiler overload resolution on entity type instead of a per-repository name.
    /// </summary>
    internal static class RepositoryWrapperPersistExtensions
    {
        public static async Task<Business> Persist(this RepositoryWrapper wrapper, Business business, string user = "Test")
        {
            await wrapper.BusinessRepository.Create(business);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return business;
        }

        public static async Task<Store> Persist(this RepositoryWrapper wrapper, Store store, string user = "Test")
        {
            await wrapper.StoreRepository.Create(store);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return store;
        }

        public static async Task<Till> Persist(this RepositoryWrapper wrapper, Till till, string user = "Test")
        {
            await wrapper.TillRepository.Create(till);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return till;
        }

        public static async Task<Role> Persist(this RepositoryWrapper wrapper, Role role, string user = "Test")
        {
            await wrapper.RoleRepository.Create(role);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return role;
        }

        public static async Task<Employee> Persist(this RepositoryWrapper wrapper, Employee employee, string user = "Test")
        {
            await wrapper.EmployeeRepository.Create(employee);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return employee;
        }

        public static async Task<Sale> Persist(this RepositoryWrapper wrapper, Sale sale, string user = "Test")
        {
            await wrapper.SaleRepository.Create(sale);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return sale;
        }

        public static async Task<Tax> Persist(this RepositoryWrapper wrapper, Tax tax, string user = "Test")
        {
            await wrapper.TaxRepository.Create(tax);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return tax;
        }

        public static async Task<Category> Persist(this RepositoryWrapper wrapper, Category category, string user = "Test")
        {
            await wrapper.CategoryRepository.Create(category);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return category;
        }

        public static async Task<Item> Persist(this RepositoryWrapper wrapper, Item item, string user = "Test")
        {
            await wrapper.ItemRepository.Create(item);
            wrapper.SetCurrentUser(user);
            wrapper.Save();
            return item;
        }
    }
}
