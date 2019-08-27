using SkiaSharp;
using SkiaSharp.Views.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Controls.SVG
{
    class Icon : Frame
    {
        public static readonly BindableProperty IconFilePathProperty = BindableProperty.Create(
            nameof(ResourceId),
            typeof(string),
            typeof(Icon),
            default(string),
            propertyChanged: RedrawCanvas);


        private readonly SKCanvasView _canvasView = new SKCanvasView();

        public string ResourceId
        {
            get => (string)GetValue(IconFilePathProperty);
            set => SetValue(IconFilePathProperty, value);
        }

        public Icon()
        {
            Padding = new Thickness(0);

            HasShadow = false;
            BackgroundColor = Color.Transparent;

            Content = _canvasView;
            _canvasView.PaintSurface += CanvasViewOnPainSurface;
        }

        #region Events
        private static void RedrawCanvas(BindableObject bindable, object oldValue, object newValue)
        {
            Icon sVGIcon = bindable as Icon;
            sVGIcon?._canvasView.InvalidateSurface();
        }

        private void CanvasViewOnPainSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            SKCanvas canvas = e.Surface.Canvas;
            canvas.Clear();

            if (string.IsNullOrEmpty(ResourceId))
                return;

            using(Stream stream = GetType().Assembly.GetManifestResourceStream(ResourceId))
            {
                SkiaSharp.Extended.Svg.SKSvg sVG = new SkiaSharp.Extended.Svg.SKSvg();
                sVG.Load(stream);

                SKImageInfo info = e.Info;
                canvas.Translate(info.Width / 2f, info.Height / 2f);

                SKRect bounds = sVG.ViewBox;
                float xRatio = info.Width / bounds.Width;
                float yRatio = info.Height / bounds.Height;

                float ratio = Math.Min(xRatio, yRatio);

                canvas.Scale(ratio);
                canvas.Translate(-bounds.MidX, -bounds.MidY);

                canvas.DrawPicture(sVG.Picture);
            }
        }
        #endregion
    }
}
