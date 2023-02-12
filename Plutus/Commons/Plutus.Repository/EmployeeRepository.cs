using Microsoft.EntityFrameworkCore;
using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Plutus.Repository
{
    public class EmployeeRepository : IEmployeeRepository
    {
        protected RepositoryContext RepositoryContext { get; init; }
        public EmployeeRepository(RepositoryContext repositoryContext)
        {
            RepositoryContext = repositoryContext;
        }

        public async virtual Task<bool> Create(Employee employee)
        {
            try
            {
                await RepositoryContext.Set<Employee>().AddAsync(employee);
                return true;
            }
            catch
            {
                return false;
            }
        }


#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async virtual Task<bool> Delete(Employee employee)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                RepositoryContext.Set<Employee>().Remove(employee);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async virtual Task<bool> Exists(Guid employeeId, Guid businessId) => await RepositoryContext.Set<Employee>().AnyAsync(e => e.Id.Equals(employeeId) && e.BusinessId.Equals(businessId));

        public async virtual Task<IEnumerable<Employee>> FindAllByCondition(Expression<Func<Employee, bool>> expression) => await RepositoryContext.Set<Employee>().Where(expression).AsNoTracking().ToListAsync();
        public virtual IQueryable<Employee> FindAllByConditionQueryable(Expression<Func<Employee, bool>> expression) => RepositoryContext.Set<Employee>().Where(expression).AsNoTracking();
        public async virtual Task<Employee> FindById(Guid employeeId, Guid businessId) => await RepositoryContext.Set<Employee>().FindAsync(employeeId, businessId);
        public async virtual Task<Employee> FindById(IQueryable<Employee> query, Guid employeeId, Guid businessId) => await query.FirstOrDefaultAsync(e => e.Id.Equals(employeeId) && e.BusinessId.Equals(businessId));
        public async virtual Task<Employee> FindFirstByCondition(Expression<Func<Employee, bool>> expression) => await RepositoryContext.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(expression);
        public async virtual Task<IEnumerable<Employee>> GetAll() => await RepositoryContext.Set<Employee>().AsNoTracking().ToListAsync();
        public virtual IQueryable<Employee> GetAllQueryable() => RepositoryContext.Set<Employee>().AsNoTracking();
        public virtual void SetState(Employee employee, EntityState entityState = EntityState.Modified) => RepositoryContext.Entry(employee).State = entityState;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async virtual Task<bool> Update(Employee employee)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                RepositoryContext.Set<Employee>().Update(employee);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
