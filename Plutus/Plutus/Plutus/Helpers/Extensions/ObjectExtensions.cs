using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Helpers.Extensions
{
    public static class ObjectExtensions
    {
        public static T ToModel<T>(this object o) where T : class
        {
            if (o is T)
            {
                return (T)o;
            }
            try
            {
                return (T)Convert.ChangeType(o, typeof(T));
            }
            catch (InvalidCastException)
            {
                return default(T);
            }
        }
    }
}
