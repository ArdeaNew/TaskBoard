using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TaskBoard.Core;

namespace TaskBoard.Rendering
{
    /// <summary>Builds a complete vector page without printer or file-system access.</summary>
    public static class BoardPageRenderer
    {
        private const double UnitsPerMillimetre = 96.0 / 25.4;
        private const double RequestedMargin = 8.0 * UnitsPerMillimetre;
        private const double PreferredFontSize = 14.0;
        private const double BoxInset = 0.7;
        private static readonly string[] Headers = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        private static readonly Brush CompletedBrush = MakeBrush("#002FA7");
        private static readonly Pen BoxOutline = MakePen("#DDDDDD", 0.6);

        /// <summary>Creates an A4 landscape page without opening a printer dialog.</summary>
        public static DrawingVisual BuildPage(BoardState state)
        {
            var size = new Size(297 * UnitsPerMillimetre, 210 * UnitsPerMillimetre);
            return BuildPage(state, size, new Rect(new Point(), size));
        }

        /// <summary>Uses physical paper coordinates and the printer's actual imageable rectangle.</summary>
        public static DrawingVisual BuildPage(BoardState state, Size pageSize, Rect printableArea)
        {
            return BuildPage(state, pageSize, printableArea, DateTime.Now);
        }

        public static DrawingVisual BuildPage(BoardState state, Size pageSize, Rect printableArea, DateTime localNow)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (!IsPositiveFinite(pageSize.Width) || !IsPositiveFinite(pageSize.Height))
                throw new ArgumentOutOfRangeException("pageSize");

            var paper = new Rect(new Point(), pageSize);
            printableArea.Intersect(paper);
            // Keep the centred border stroke inside a device's hard printable boundary.
            if (!printableArea.IsEmpty) printableArea.Inflate(-0.75, -0.75);
            var content = new Rect(RequestedMargin, RequestedMargin,
                Math.Max(1, pageSize.Width - 2 * RequestedMargin),
                Math.Max(1, pageSize.Height - 2 * RequestedMargin));
            content.Intersect(printableArea);
            if (content.IsEmpty || content.Width < 80 || content.Height < 100)
                throw new InvalidOperationException("打印区域过小。");

            var columns = new List<TaskItem>[Headers.Length];
            for (int column = 0; column < columns.Length; column++) columns[column] = new List<TaskItem>();
            if (state.Tasks != null)
                foreach (TaskItem task in state.Tasks)
                    if (task != null && task.Column >= 0 && task.Column <= columns.Length)
                        columns[Math.Max(1, task.Column) - 1].Add(task);
            const double metricHeight = 25;
            const double headerHeight = 48;
            var board = new Rect(content.Left, content.Top + metricHeight,
                content.Width, content.Height - metricHeight);
            double columnWidth = board.Width / Headers.Length;
            double fontSize = FindFontSize(columns, columnWidth, board.Height - headerHeight);
            double padding = HorizontalPadding(fontSize);

            var visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, paper);
                DrawMetrics(drawing, content, state, localNow);
                var border = new Pen(Brushes.Black, 1.1);
                border.Freeze();
                drawing.DrawRectangle(null, border, board);
                double dividerY = board.Top + headerHeight;
                drawing.DrawLine(border, new Point(board.Left, dividerY), new Point(board.Right, dividerY));

                for (int column = 0; column < Headers.Length; column++)
                {
                    double x = board.Left + column * columnWidth;
                    if (column > 0)
                        drawing.DrawLine(border, new Point(x, board.Top), new Point(x, board.Bottom));
                    var header = CreateText(Headers[column], 12, Brushes.Black, columnWidth - 8);
                    drawing.DrawText(header, new Point(x + (columnWidth - header.Width) / 2,
                        board.Top + (headerHeight - header.Height) / 2));

                    double y = dividerY + fontSize * 0.4;
                    for (int taskIndex = 0; taskIndex < columns[column].Count; taskIndex++)
                    {
                        TaskItem task = columns[column][taskIndex];
                        var text = CreateText(TaskLabel(task, taskIndex + 1), fontSize, GetBrush(task.Color),
                            TaskTextWidth(task, columnWidth, fontSize));
                        double boxHeight = text.Height + fontSize * 0.92;
                        drawing.DrawRectangle(null, BoxOutline,
                            new Rect(x + BoxInset, y, columnWidth - 2 * BoxInset, boxHeight));
                        drawing.DrawText(text, new Point(x + BoxInset + padding, y + fontSize * 0.46));
                        if (task.IsCompleted)
                        {
                            double side = fontSize * 0.43;
                            drawing.DrawRectangle(CompletedBrush, null, new Rect(
                                x + columnWidth - BoxInset - padding - side,
                                y + (boxHeight - side) / 2, side, side));
                        }
                        y += boxHeight + fontSize * 0.28;
                    }
                }
            }
            return visual;
        }

        private static void DrawMetrics(DrawingContext drawing, Rect content, BoardState state, DateTime localNow)
        {
            DateMetrics metrics = DateMetrics.From(localNow);
            DateTime selectedWeek;
            if (!DateTime.TryParseExact(state.ActiveWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out selectedWeek)) selectedWeek = localNow;
            DateMetrics weekMetrics = DateMetrics.From(selectedWeek);
            string week = weekMetrics.WeekYear.ToString(CultureInfo.InvariantCulture) + " / W" +
                weekMetrics.WeekNumber.ToString("00", CultureInfo.InvariantCulture);
            string remaining = "月余 " + metrics.MonthRemainingPercent.ToString("0.0", CultureInfo.InvariantCulture) +
                "%   /   年余 " + metrics.YearRemainingPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            var left = CreateText(week, 10, Brushes.Black, content.Width);
            var right = CreateText(remaining, 10, Brushes.Black, content.Width);
            // Font advance widths can be slightly narrower than the final glyph's ink.
            Rect leftInk = left.BuildGeometry(new Point()).Bounds;
            Rect rightInk = right.BuildGeometry(new Point()).Bounds;
            drawing.DrawText(left, new Point(content.Left + 2 - Math.Min(0, leftInk.Left), content.Top));
            drawing.DrawText(right, new Point(content.Right - 2 - Math.Max(right.Width, rightInk.Right), content.Top));
        }

        private static double FindFontSize(List<TaskItem>[] columns, double columnWidth, double availableHeight)
        {
            if (Fits(columns, PreferredFontSize, columnWidth, availableHeight)) return PreferredFontSize;

            double lower = PreferredFontSize / 2;
            while (!Fits(columns, lower, columnWidth, availableHeight))
            {
                lower /= 2;
                if (lower < 0.001)
                    throw new InvalidOperationException("内容过多，无法在一页内清晰排版。");
            }
            double upper = lower * 2;
            for (int iteration = 0; iteration < 22; iteration++)
            {
                double candidate = (lower + upper) / 2;
                if (Fits(columns, candidate, columnWidth, availableHeight)) lower = candidate;
                else upper = candidate;
            }
            return lower;
        }

        private static bool Fits(List<TaskItem>[] columns, double fontSize, double columnWidth, double availableHeight)
        {
            foreach (List<TaskItem> tasks in columns)
            {
                double usedHeight = fontSize * 0.8;
                for (int index = 0; index < tasks.Count; index++)
                {
                    if (index > 0) usedHeight += fontSize * 0.28;
                    usedHeight += CreateText(TaskLabel(tasks[index], index + 1), fontSize, Brushes.Black,
                        TaskTextWidth(tasks[index], columnWidth, fontSize)).Height + fontSize * 0.92;
                    if (usedHeight > availableHeight) return false;
                }
                if (usedHeight > availableHeight) return false;
            }
            return true;
        }

        private static double HorizontalPadding(double fontSize) { return Math.Max(2, Math.Min(9, fontSize * 0.65)); }

        private static double TextWidth(double columnWidth, double fontSize)
        {
            return Math.Max(1, columnWidth - 2 * BoxInset - 2 * HorizontalPadding(fontSize));
        }

        private static double TaskTextWidth(TaskItem task, double columnWidth, double fontSize)
        {
            return Math.Max(1, TextWidth(columnWidth, fontSize) - (task.IsCompleted ? fontSize : 0));
        }

        private static string TaskLabel(TaskItem task, int ordinal)
        {
            return ordinal.ToString(CultureInfo.InvariantCulture) + ". " + (task.Text ?? string.Empty);
        }

        private static Brush MakeBrush(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }

        private static Pen MakePen(string value, double thickness)
        {
            var pen = new Pen(MakeBrush(value), thickness);
            pen.Freeze();
            return pen;
        }

        private static FormattedText CreateText(string value, double fontSize, Brush brush, double width)
        {
            // A space retains an editable empty task's vertical position in the printed list.
            var text = new FormattedText(string.IsNullOrEmpty(value) ? " " : value,
                CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(Typography.TaskFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                fontSize, brush, 1.0);
            text.MaxTextWidth = width;
            text.Trimming = TextTrimming.None;
            return text;
        }

        private static Brush GetBrush(string value)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                return Brushes.Black;
            }
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
