using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;

namespace NatApp.Plutus.Helpers.Extensions
{
    public static class IEnumerableExtensions
    {
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
                action(item);
        }

        public static IEnumerable<T> ForEachLazy<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
            {
                action(item);
                yield return item;
            }

        }

        /// <summary>
        /// Create DataTable from IEnumerable
        /// </summary>
        /// <typeparam name="T">Object type to convert to row in DataTable</typeparam>
        /// <param name="enumerable">IEnumberable to be converted to DataTable</param>
        /// <returns>All of the IEnumerable to DataTable</returns>
        public static DataTable ToDataTable<T>(this IEnumerable<T> enumerable)
        {
            DataTable dataTable = new DataTable(typeof(T).Name);

            var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                dataTable.Columns.Add(prop.Name);
            }

            foreach (var item in enumerable)
            {
                var values = new object[props.Length];
                for (var i = 0; i < props.Count(); i++)
                {
                    values[i] = props[i].GetValue(item, null);
                }
                dataTable.Rows.Add(values);
            }

            return dataTable;
        }

        /// <summary>
        /// Create DataTable from IEnumerable
        /// </summary>
        /// <typeparam name="T">Object type to convert to row in DataTable</typeparam>
        /// <param name="enumerable">IEnumberable to be converted to DataTable</param>
        /// <param name="tableName">Name of Table</param>
        /// <returns>All of the IEnumerable to DataTable</returns>
        public static DataTable ToDataTable<T>(this IEnumerable<T> enumerable, string tableName)
        {
            DataTable dataTable = new DataTable(tableName);

            var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                dataTable.Columns.Add(prop.Name);
            }

            foreach (var item in enumerable)
            {
                var values = new object[props.Length];
                for (var i = 0; i < props.Count(); i++)
                {
                    values[i] = props[i].GetValue(item, null);
                }
                dataTable.Rows.Add(values);
            }

            return dataTable;
        }

        /// <summary>
        /// Create DataTable from IEnumerable<IDictionary<string, Object>>
        /// </summary>
        /// <param name="enumerable">IEnumberable to be converted to DataTable</param>
        /// <param name="tableName">Name of Table</param>
        /// <returns>All of the IEnumerable to DataTable</returns>
        public static DataTable ToDataTable(this IEnumerable<IDictionary<string, object>> enumerable, string tableName)
        {
            DataTable dataTable = new DataTable(tableName);

            var propNames = enumerable.First().Keys;

            foreach (var prop in propNames)
            {
                dataTable.Columns.Add(prop);
            }

            foreach (var item in enumerable)
            {
                var values = new object[propNames.Count()];

                for (var i = 0; i < propNames.Count(); i++)
                {
                    values[i] = item.Values.ElementAt(i);
                }
                dataTable.Rows.Add(values);
            }

            return dataTable;
        }
    }
}
