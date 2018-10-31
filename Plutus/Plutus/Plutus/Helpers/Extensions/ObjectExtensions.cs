using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
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

        public static bool TrySetProperty(this object obj, string property, object value)
        {
            var prop = obj.GetType().GetProperty(property);
            if (prop == null || !prop.CanWrite) return false;
            try
            {
                prop.SetValue(obj, Convert.ChangeType(value, prop.PropertyType), null);
            }
            catch(ArgumentException)
            {
                return false;
            }
            return true;
        }

        public static T DeepClone<T>(this T o)
        {
            using(MemoryStream stream = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, o);
                stream.Position = 0;
                return (T)formatter.Deserialize(stream);
            }
        }
    }
}
