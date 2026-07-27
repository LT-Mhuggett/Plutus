using System.Globalization;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    /// <summary>
    /// Pins CultureInfo.CurrentCulture for the lifetime of a test, restoring it on dispose. Needed
    /// wherever behavior depends on the ambient culture (e.g. currency/date parsing) so tests don't
    /// depend on whatever locale the host OS happens to be set to.
    /// </summary>
    internal sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _original;

        public CultureScope(string name)
        {
            _original = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(name);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _original;
    }
}
