using NUnit.Framework;
using System.Linq;

namespace Plutus.Entities.Tests
{
    public class TestDbConfiguration
    {
        protected MySqlDbContext DbContext;
        public TestDbConfiguration() : base()
        {
            //string connString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus_test;Port=3306;Persist Security Info=false;Connect Timeout=300";

            //DbContext = new MySqlDbContext(connString);
            DbContext = new MySqlDbContext();

        }

        #region Tables are created tests

        /*[Test]
        [Category("TableAreCreated")]
        public void BusinessTableIsCreated()
        {
            Assert.False(DbContext.Business.Any());
        }*/

        [Test]
        [Category("TableAreCreated")]
        public void StoreTableIsCreated()
        {
            Assert.False(DbContext.Stores.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void RoleTableIsCreated()
        {
            Assert.False(DbContext.Role.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void DiscountTableIsCreated()
        {
            Assert.False(DbContext.Discounts.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void TillTableIsCreated()
        {
            Assert.False(DbContext.Till.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void CategoryTableIsCreated()
        {
            Assert.False(DbContext.Category.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void EmployeesTableIsCreated()
        {
            Assert.False(DbContext.Employees.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void TaxesTableIsCreated()
        {
            Assert.False(DbContext.Taxes.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void NotesTableIsCreated()
        {
            Assert.False(DbContext.Notes.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void AuthActionsTableIsCreated()
        {
            Assert.False(DbContext.AuthActions.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void DiscountCatsTableIsCreated()
        {
            Assert.False(DbContext.DiscountCats.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void DiscountItemsTableIsCreated()
        {
            Assert.False(DbContext.DiscountItems.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void ItemsTableIsCreated()
        {
            Assert.False(DbContext.Items.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void NoteSalesTableIsCreated()
        {
            Assert.False(DbContext.NotesSales.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void PayMethodsTableIsCreated()
        {
            Assert.False(DbContext.PayMethods.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void PaySalesTableIsCreated()
        {
            Assert.False(DbContext.PaySales.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void RefundsTableIsCreated()
        {
            Assert.False(DbContext.Refunds.Any());
        }

        [Test]
        [Category("TableAreCreated")]
        public void StocksTableIsCreated()
        {
            Assert.False(DbContext.Stocks.Any());
        }

        #endregion Tables are created tests
    }
}