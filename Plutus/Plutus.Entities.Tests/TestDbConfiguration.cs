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

        [Test]
        [Category("TableAreCreated")]
        public void BusinessTableIsCreated()
        {
            Assert.False(DbContext.Bussiness.Any());
        }

        #endregion Tables are created tests
    }
}