using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Plutus.Entities;
using MySqlConnector;

namespace Plutus.Repository.Tests
{
    public abstract class TestWithMySql : IDisposable
    {
        //private const string InMemoryConnectionString = "DataSource=:memory:";
        //private const string InMemoryConnectionString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus;Port=3306;Persist Security Info=false;Connect Timeout=300";

        //private readonly SqliteConnection _Connection;
        private readonly MySqlConnection _Connection;
        protected readonly MySqlDbContext DbContext;

        protected TestWithMySql()
        {
            //_Connection = new MySqlConnection(InMemoryConnectionString);
            _Connection = new MySqlConnection();
            _Connection.Open();
            var options = new DbContextOptionsBuilder<RepositoryContext>()
                .UseMySql(_Connection, MySqlServerVersion.LatestSupportedServerVersion)
                .Options;
            DbContext = new MySqlDbContext(options);
            DbContext.Database.Migrate();
        }

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

            DbContext.Business.RemoveRange(DbContext.Business);
            /*DbContext.EmotionPlots.RemoveRange(DbContext.EmotionPlots);
            DbContext.FacebookPages.RemoveRange(DbContext.FacebookPages);
            DbContext.FacebookPosts.RemoveRange(DbContext.FacebookPosts);
            DbContext.OnPremisesExtensionAttributes.RemoveRange(DbContext.OnPremisesExtensionAttributes);
            DbContext.PostData.RemoveRange(DbContext.PostData);
            DbContext.StaffPosts.RemoveRange(DbContext.StaffPosts);
            DbContext.Tweets.RemoveRange(DbContext.Tweets);
            DbContext.TwitterAccounts.RemoveRange(DbContext.TwitterAccounts);
            DbContext.Users.RemoveRange(DbContext.Users);*/
            DbContext.SaveChanges();
        }

        public void Dispose() => _Connection.Close();
    }
}