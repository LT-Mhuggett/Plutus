using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Controls
{
    public class IdentifiableEntry : Entry
    {
        public uint UserDefinedId { get; }

        public IdentifiableEntry(uint id) : base()
        {
            this.UserDefinedId = id;
        }
    }
}
