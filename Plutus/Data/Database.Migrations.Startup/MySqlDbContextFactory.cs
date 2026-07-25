using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MySqlConnector;
using Plutus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Database.Migrations.Startup
{
    public class MySqlDbContextFactory : IDesignTimeDbContextFactory<MySqlDbContext>
    {
        public MySqlDbContext CreateDbContext(string[] args)
        {
            // Overridable via env var so CI can supply its own credential instead of this local-dev
            // default (a throwaway password for a local MySQL instance, not meant to protect anything).
            var password = Environment.GetEnvironmentVariable("MYSQL_GLOBALADMIN_PASSWORD") ?? "U!NpSPO7Vx7N";
            var connectionString = $"server=localhost;User=GlobalAdmin;Password={password};Database=plutus;Port=3306;Persist Security Info=false; Connect Timeout=300";

            var options = new DbContextOptionsBuilder<MySqlDbContext>()
                .UseMySql(connectionString, MySqlServerVersion.LatestSupportedServerVersion)
                .Options;

            return new MySqlDbContext(options);
        }
    }
}
