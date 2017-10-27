using System;
using QuickLook;

namespace Plutus.iOS
{
    internal class PreviewControllerDS : QLPreviewControllerDataSource
    {
        private QLPreviewItem _previewItem;

        public PreviewControllerDS(QLPreviewItem previewItem)
        {
            _previewItem = previewItem;
        }

        public override IQLPreviewItem GetPreviewItem(QLPreviewController controller, nint index)
        {
            return _previewItem;
        }

        public override nint PreviewItemCount(QLPreviewController controller)
        {
            return 1;
        }
    }
}