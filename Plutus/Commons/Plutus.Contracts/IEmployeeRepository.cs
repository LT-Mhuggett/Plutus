using Microsoft.EntityFrameworkCore;
using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Plutus.Contracts
{
    public interface IEmployeeRepository
    {

        /// <summary>
        /// Async CREATE employee to table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="employee">Employee to add to table</param>
        /// <returns>Success of addition</returns>
        Task<bool> Create(Employee employee);

        /// <summary>
        /// Async DELETE employee from table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="employee">Employee to delete from table</param>
        /// <returns>Success of deletion</returns>
        Task<bool> Delete(Employee employee);

        /// <summary>
        /// Async does employee EXIST in table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="employeeId">Employee ID to check</param>
        /// <param name="businessId">Business ID to check</param>
        /// <returns>Employee exists or not</returns>
        Task<bool> Exists(Guid employeeId, Guid businessId);

        /// <summary>
        /// Async FIND all that satisfies predicate expression from table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="expression">Condition</param>
        /// <returns>Enumerable List of <see cref="Employee"/></returns>
        Task<IEnumerable<Employee>> FindAllByCondition(Expression<Func<Employee, bool>> expression);

        /// <summary>
        /// Async FIND all that satisfies predicate expression from table, denoted by <see cref="Employee"/>, as IQueryable
        /// </summary>
        /// <param name="expression">Condition</param>
        /// <returns>Queryable List of <see cref="Employee"/></returns>
        IQueryable<Employee> FindAllByConditionQueryable(Expression<Func<Employee, bool>> expression);

        /// <summary>
        /// Async FIND by ID from table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="employeeId">Employee ID of the record</param>
        /// <param name="businessId">Business ID of the record</param>
        /// <returns>Single <see cref="Employee"/></returns>
        Task<Employee> FindById(Guid employeeId, Guid businessId);

        /// <summary>
        /// Async FIND by ID from table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="employeeId">Employee ID of the record</param>
        /// <param name="businessId">Business ID of the record</param>
        /// <param name="query">Query to use</param>
        /// <returns>Single <see cref="Employee"/></returns>
        Task<Employee> FindById(IQueryable<Employee> query, Guid employeeId, Guid businessId);

        /// <summary>
        /// Async FIND first record that satisfies predicate expression from table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <param name="expression">Condition</param>
        /// <returns>Single <see cref="Employee"/></returns>
        Task<Employee> FindFirstByCondition(Expression<Func<Employee, bool>> expression);

        /// <summary>
        /// Async GET all records from table, denoted by <see cref="Employee"/>
        /// </summary>
        /// <returns>Enumerable List of <see cref="Employee"/></returns>
        Task<IEnumerable<Employee>> GetAll();

        /// <summary>
        /// Async GET all records from table, denoted by <see cref="Employee"/>, as IQueryable
        /// </summary>
        /// <returns>Queryable List of <see cref="Employee"/></returns>
        IQueryable<Employee> GetAllQueryable();

        /// <summary>
        /// Manually set the state of an Employee
        /// </summary>
        /// <param name="employee">Employee to change state of</param>
        /// <param name="entityState">State to change to (defaults to <see cref="EntityState.Modified"/></param>
        void SetState(Employee employee, EntityState entityState = EntityState.Modified);

        /// <summary>
        /// Async UPDATE employee in table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="employee">Employee to update in table</param>
        /// <returns>Success of update</returns>
        Task<bool> Update(Employee employee);
    }
}
