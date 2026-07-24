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
            var options = new DbContextOptionsBuilder<MySqlDbContext>()
                .UseMySql("server=localhost;User=GlobalAdmin;Password=U!NpSPO7Vx7N;Database=plutus;Port=3306;Persist Security Info=false; Connect Timeout=300", MySqlServerVersion.LatestSupportedServerVersion)
                .Options;

            return new MySqlDbContext(options);
        }
    }
}
