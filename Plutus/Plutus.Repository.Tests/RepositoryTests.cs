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
            //RepositoryWrapper.EmotionDefinition.Create(emotionDefinition);

            Assert.AreEqual(bussiness, DbContext.Bussiness.Find(bussiness.Id));
            //Assert.AreEqual(1, DbContext.Bussiness.Count());
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

            var emotionDefinition = new Bussiness()
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
            await RepositoryWrapper.BussinessRepository.Create(emotionDefinition);
            RepositoryWrapper.Save();
            RepositoryWrapper.SetSyncState();

            Assert.AreEqual(emotionDefinition.CreatedBy, systemName);
            Assert.AreEqual(emotionDefinition.ModifiedBy, systemName);
            Assert.AreEqual(emotionDefinition.CreatedAt, currentTime);
            Assert.AreEqual(emotionDefinition.ModifiedAt, currentTime);
        }

        #endregion

    }
}