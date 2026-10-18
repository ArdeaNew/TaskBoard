using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TaskBoard.Rendering
{
    /// <summary>A physical vector page with semantic text corresponding to its drawn outlines.</summary>
    public sealed class BoardPage : DrawingVisual
    {
        private readonly List<PageTextRun> textRuns = new List<PageTextRun>();

        public Size PageSize { get; private set; }
        public IReadOnlyList<PageTextRun> TextRuns { get; private set; }

        internal BoardPage(Size pageSize)
        {
            PageSize = pageSize;
            TextRuns = textRuns.AsReadOnly();
        }

        internal void AddTextRun(string text, Geometry geometry, Brush brush)
        {
            textRuns.Add(new PageTextRun(text, geometry, brush));
        }
    }

    /// <summary>Text retained alongside its visible, frozen vector geometry.</summary>
    public sealed class PageTextRun
    {
        public string Text { get; private set; }
        public Geometry Geometry { get; private set; }
        public Brush Brush { get; private set; }
        public Rect Bounds { get { return Geometry.Bounds; } }

        internal PageTextRun(string text, Geometry geometry, Brush brush)
        {
            Text = text;
            Geometry = geometry;
            Brush = brush;
        }
    }
}
