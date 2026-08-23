using System;
using System.Collections.Generic;

namespace Plutus.Frontend.AppClient.Helpers.Extensions
{
    /// <summary>
    /// ⚠ ONE METHOD LEFT — L14, 2026-08-23. This file also held `ForEachLazy` and three `ToDataTable`
    /// overloads. `ToDataTable`'s only consumer was `ExcelHandling.cs`, deleted 2026-08-21 with the
    /// Syncfusion removal; `ForEachLazy` never had one. Removing them also drops this file's
    /// `System.Data`, `System.Linq` and `System.Reflection` dependencies.
    /// </summary>
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// ⚠⚠ THIS ONE STAYS, AND IT IS NOT REDUNDANT WITH `List&lt;T&gt;.ForEach`. Its live caller —
        /// `FilePlatform.SaveFiles` — holds an `IList&lt;T&gt;`, which has no instance `ForEach`, so this
        /// extension is what resolves there. Deleting it as "the BCL already has one" would break a
        /// build that looks like it should compile.
        /// </summary>
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
                action(item);
        }
    }
}
