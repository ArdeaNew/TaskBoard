using System;
using System.Windows;
using System.Windows.Media;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    // Both preview and fixed-page printing draw the same vector page.
    internal sealed class PageVisual : FrameworkElement
    {
        private BoardPage page;
        private double zoomScale = 1;

        internal PageVisual(BoardPage page) { SetPage(page); }
        internal BoardPage Page { get { return page; } }
        internal double ZoomScale
        {
            get { return zoomScale; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new ArgumentOutOfRangeException("value");
                zoomScale = value;
                UpdateSize();
            }
        }

        internal void SetPage(BoardPage value)
        {
            if (value == null) throw new ArgumentNullException("value");
            page = value;
            UpdateSize();
        }

        private void UpdateSize()
        {
            Width = page.PageSize.Width * zoomScale;
            Height = page.PageSize.Height * zoomScale;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            drawing.PushTransform(new ScaleTransform(zoomScale, zoomScale));
            drawing.DrawDrawing(page.Drawing);
            drawing.Pop();
        }
    }
}
