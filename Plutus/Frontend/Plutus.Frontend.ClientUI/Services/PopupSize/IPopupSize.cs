using Microsoft.Maui.Graphics;
namespace Plutus.Frontend.ClientUI.Services.PopupSize
{
    public interface IPopupSize
    {
        public Size Tiny { get; }
        public Size Small { get; }
        public Size Medium { get; }
        public Size Large { get; }
    }
}
