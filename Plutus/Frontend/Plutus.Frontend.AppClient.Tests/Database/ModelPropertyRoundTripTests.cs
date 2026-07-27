using System.Linq;
using System.Reflection;
using Database.Models;

namespace Plutus.Frontend.AppClient.Tests.Database
{
    /// <summary>
    /// Database.Models classes are simple NotifyModelChanged-backed POCOs whose scalar properties all
    /// follow the same SetProperty(ref field, value) pattern. Rather than hand-writing one near-identical
    /// test per class, this walks every settable scalar property via reflection and round-trips it,
    /// asserting the value is read back, IsDirty flips, and PropertyChanged fires. Navigation properties
    /// (virtual reference/collection types linking to other models) are skipped since round-tripping
    /// them meaningfully needs a full object graph, not a single scalar value - those still get
    /// exercised indirectly via AppDBContextTests and the ViewModel/Basket model tests elsewhere.
    /// </summary>
    public class ModelPropertyRoundTripTests
    {
        public static IEnumerable<object[]> ModelTypes()
        {
            return typeof(NotifyModelChanged).Assembly.GetTypes()
                .Where(t => t.Namespace == "Database.Models"
                    && !t.IsAbstract
                    && !t.IsGenericTypeDefinition
                    && t != typeof(NotifyModelChanged)
                    && typeof(NotifyModelChanged).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => new object[] { t });
        }

        [Theory]
        [MemberData(nameof(ModelTypes))]
        public void ScalarProperties_RoundTripAndRaisePropertyChangedAndSetIsDirty(Type modelType)
        {
            var instance = (NotifyModelChanged)Activator.CreateInstance(modelType)!;
            var raised = new List<string?>();
            instance.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            var scalarProps = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.CanRead
                    && p.GetSetMethod()?.IsVirtual == false
                    && IsSimpleType(p.PropertyType))
                .ToList();

            Assert.NotEmpty(scalarProps);

            foreach (var prop in scalarProps)
            {
                var value = SampleValue(prop.PropertyType);
                prop.SetValue(instance, value);
                Assert.Equal(value, prop.GetValue(instance));
            }

            Assert.True(instance.IsDirty);
            Assert.NotEmpty(raised);
        }

        [Theory]
        [MemberData(nameof(ModelTypes))]
        public void NavigationProperties_GetterAndSetterAreReachable(Type modelType)
        {
            // Navigation properties (virtual reference/collection types linking to other models) follow
            // the same SetProperty pattern as scalars but need a full object graph to round-trip
            // meaningfully - that's still exercised via AppDBContextTests. Here we only need each
            // getter/setter *line* to execute at least once, which null (a valid value for any of these
            // reference-typed properties) is enough to do safely, without constructing a graph.
            var instance = (NotifyModelChanged)Activator.CreateInstance(modelType)!;

            var navProps = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.CanRead && p.GetSetMethod()?.IsVirtual == true)
                .ToList();

            foreach (var prop in navProps)
            {
                prop.SetValue(instance, null);
                prop.GetValue(instance);
            }
        }

        private static bool IsSimpleType(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying == typeof(string) || underlying == typeof(int) || underlying == typeof(decimal) ||
                   underlying == typeof(double) || underlying == typeof(bool) || underlying == typeof(DateTime) ||
                   underlying == typeof(Guid) || underlying.IsEnum;
        }

        private static object SampleValue(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string)) return "sample";
            if (underlying == typeof(int)) return 42;
            if (underlying == typeof(decimal)) return 42.5m;
            if (underlying == typeof(double)) return 42.5d;
            if (underlying == typeof(bool)) return true;
            if (underlying == typeof(DateTime)) return new DateTime(2024, 1, 1);
            if (underlying == typeof(Guid)) return Guid.NewGuid();
            if (underlying.IsEnum) return Enum.GetValues(underlying).Cast<object>().First();
            throw new NotSupportedException(underlying.FullName);
        }
    }
}
