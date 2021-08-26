using Xamarin.Forms;

namespace CustomViews.Control
{
    public class IdentifiableEntry : Entry
    {
        public uint UserDefinedId { get; set; }

        public IdentifiableEntry() : base()
        {

        }
    }
}
