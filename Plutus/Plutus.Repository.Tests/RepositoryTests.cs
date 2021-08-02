using NUnit.Framework;
using Plutus.Entities.Models;

namespace Plutus.Repository.Tests
{
    public class RepositoryTests : TestWithMySql
    {
        /*[SetUp]
        public void Setup()
        {
        }*/

        protected RepositoryWrapper RepositoryWrapper { get; set; }

        public RepositoryTests() : base()
        {
            RepositoryWrapper = new RepositoryWrapper(DbContext);
        }

        [Test]
        public void Test1()
        {
            Assert.Pass();
        }

        #region Data Persistance
        [Test]
        [Category("RecordPersistance")]
        public void BussinessPersists()
        {
            var bussiness = new Bussiness()
            {
                Id = "2",
                Name = "Test Name",
                NameAbbr = "TN"
            };

            RepositoryWrapper.BussinessRepository.Create(bussiness);
            RepositoryWrapper.Save();

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            //Assert.AreEqual(1, DbContext.Bussiness.Count());
        }

        #endregion

    }
}