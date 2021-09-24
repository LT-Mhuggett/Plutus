using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Entities;

namespace Plutus.Repository.Tests
{
    public class RepositoryWrapper : Repository.RepositoryWrapper
    {
        public RepositoryWrapper(MySqlDbContext context) : base(context)
        {

        }
    }
}
