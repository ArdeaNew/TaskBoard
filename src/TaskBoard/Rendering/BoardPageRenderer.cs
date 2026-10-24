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
        private static readonly IReadOnlyList<string> Headers = BoardLabels.DayNames;
        private static readonly Brush CompletedBrush = MakeBrush("#002FA7");
        private static readonly Brush CompletedBackgroundBrush = MakeBrush("#E6E6E6");
        private static readonly Brush TodayColumnBrush = MakeBrush("#F0FBE0");
        private static readonly Pen BoxOutline = MakePen("#DDDDDD", 0.6);

        /// <summary>Creates an A4 landscape page without opening a printer dialog.</summary>
        public static BoardPage BuildPage(BoardState state)
        {
            return BuildPage(state, DateTime.Now);
        }

        public static BoardPage BuildPage(BoardState state, DateTime localNow)
        {
            var size = new Size(297 * UnitsPerMillimetre, 210 * UnitsPerMillimetre);
            return BuildPage(state, size, new Rect(new Point(), size), localNow);
        }

        /// <summary>Uses physical paper coordinates and the printer's actual imageable rectangle.</summary>
        public static BoardPage BuildPage(BoardState state, Size pageSize, Rect printableArea)
        {
            return BuildPage(state, pageSize, printableArea, DateTime.Now);
        }

        public static BoardPage BuildPage(BoardState state, Size pageSize, Rect printableArea, DateTime localNow)
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
                throw new InvalidOperationException("The printable area is too small.");

            var columns = new List<TaskItem>[Headers.Count];
            for (int column = 0; column < columns.Length; column++) columns[column] = new List<TaskItem>();
            if (state.Tasks != null)
                foreach (TaskItem task in state.Tasks)
                    if (task != null && task.Column >= 0 && task.Column <= columns.Length)
                        columns[Math.Max(1, task.Column) - 1].Add(task);
            const double metricHeight = 25;
            const double headerHeight = 48;
            var board = new Rect(content.Left, content.Top + metricHeight,
                content.Width, content.Height - metricHeight);
            double columnWidth = board.Width / Headers.Count;
            double fontSize = FindFontSize(columns, columnWidth, board.Height - headerHeight);
            double padding = HorizontalPadding(fontSize);

            var visual = new BoardPage(pageSize);
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, paper);
                DrawMetrics(drawing, visual, content, state, localNow);
                var border = new Pen(Brushes.Black, 1.1);
                border.Freeze();
                double dividerY = board.Top + headerHeight;
                DateTime selectedMonday = SelectedMonday(state, localNow);
                int todayColumn = selectedMonday == WeeklyPlanner.MondayOf(localNow)
                    ? ((int)localNow.DayOfWeek + 6) % 7 : -1;
                if (todayColumn >= 0)
                {
                    double todayX = board.Left + todayColumn * columnWidth;
                    drawing.DrawRectangle(TodayColumnBrush, null,
                        new Rect(todayX, board.Top, columnWidth, board.Height));
                }

                for (int column = 0; column < Headers.Count; column++)
                {
                    double x = board.Left + column * columnWidth;
                    var header = CreateText(Headers[column], 12, Brushes.Black, columnWidth - 8);
                    DateTime headerDate = selectedMonday <= DateTime.MaxValue.Date.AddDays(-column)
                        ? selectedMonday.AddDays(column) : DateTime.MaxValue.Date;
                    bool isToday = todayColumn == column;
                    if (isToday)
                    {
                        var today = CreateText("Today", 9, MakeBrush("#70804F"), columnWidth - 8);
                        DrawTextOutline(drawing, visual, "Today", today, MakeBrush("#70804F"),
                            new Point(x + (columnWidth - today.Width) / 2, board.Top + 2));
                    }
                    DrawTextOutline(drawing, visual, Headers[column], header, Brushes.Black,
                        new Point(x + (columnWidth - header.Width) / 2,
                            board.Top + (isToday ? 14 : 8)));
                    var date = CreateText(BoardLabels.Date(headerDate), 9, Brushes.Black, columnWidth - 8);
                    DrawTextOutline(drawing, visual, BoardLabels.Date(headerDate), date, Brushes.Black,
                        new Point(x + (columnWidth - date.Width) / 2,
                            board.Top + (isToday ? 29 : 26)));

                    double y = dividerY;
                    for (int taskIndex = 0; taskIndex < columns[column].Count; taskIndex++)
                    {
                        TaskItem task = columns[column][taskIndex];
                        string label = TaskLabel(task, taskIndex + 1);
                        Brush foreground = GetBrush(task.Color, Brushes.Black);
                        var text = CreateText(label, fontSize, foreground,
                            TaskTextWidth(task, columnWidth, fontSize));
                        double boxHeight = text.Height + fontSize * 0.92;
                        drawing.DrawRectangle(GetTaskBackground(task), null,
                            new Rect(x, y, columnWidth, boxHeight));
                        drawing.DrawLine(BoxOutline, new Point(x, y + boxHeight),
                            new Point(x + columnWidth, y + boxHeight));
                        DrawTextOutline(drawing, visual, label, text, foreground,
                            new Point(x + padding, y + fontSize * 0.46));
                        if (task.IsCompleted)
                        {
                            double side = fontSize * 0.43;
                            drawing.DrawRectangle(CompletedBrush, null, new Rect(
                                x + columnWidth - padding - side,
                                y + (boxHeight - side) / 2, side, side));
                        }
                        y += boxHeight;
                    }
                }
                // Draw the grid last so a full-width task fill cannot obscure a column edge.
                drawing.DrawRectangle(null, border, board);
                drawing.DrawLine(border, new Point(board.Left, dividerY), new Point(board.Right, dividerY));
                for (int column = 1; column < Headers.Count; column++)
                {
                    double x = board.Left + column * columnWidth;
                    drawing.DrawLine(border, new Point(x, board.Top), new Point(x, board.Bottom));
                }
            }
            return visual;
        }

        private static void DrawMetrics(DrawingContext drawing, BoardPage visual, Rect content, BoardState state, DateTime localNow)
        {
            DateTime selectedWeek;
            if (!DateTime.TryParseExact(state.ActiveWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out selectedWeek)) selectedWeek = localNow;
            string week = BoardLabels.Week(selectedWeek);
            string remaining = BoardLabels.Remaining(localNow);
            const double preferredSize = 10;
            double measuringWidth = Math.Max(1000, content.Width);
            var left = CreateText(week, preferredSize, Brushes.Black, measuringWidth);
            var right = CreateText(remaining, preferredSize, Brushes.Black, measuringWidth);
            double fontSize = preferredSize * Math.Min(1, (content.Width - 12) / (left.Width + right.Width));
            if (fontSize < preferredSize)
            {
                left = CreateText(week, fontSize, Brushes.Black, measuringWidth);
                right = CreateText(remaining, fontSize, Brushes.Black, measuringWidth);
            }
            // Font advance widths can be slightly narrower than the final glyph's ink.
            Rect leftInk = left.BuildGeometry(new Point()).Bounds;
            Rect rightInk = right.BuildGeometry(new Point()).Bounds;
            DrawTextOutline(drawing, visual, week, left, Brushes.Black,
                new Point(content.Left + 2 - Math.Min(0, leftInk.Left), content.Top));
            DrawTextOutline(drawing, visual, remaining, right, Brushes.Black,
                new Point(content.Right - 2 - Math.Max(right.Width, rightInk.Right), content.Top));
        }

        private static void DrawTextOutline(DrawingContext drawing, BoardPage visual, string value,
            FormattedText text, Brush brush, Point origin)
        {
            // Resolve the composite font here, before the page enters XPS or a printer driver.
            // Only paths are serialized; no runtime-only composite font needs to be embedded.
            Geometry geometry = text.BuildGeometry(origin);
            geometry.Freeze();
            drawing.DrawGeometry(brush, null, geometry);
            visual.AddTextRun(value, geometry, brush);
        }

        private static double FindFontSize(List<TaskItem>[] columns, double columnWidth, double availableHeight)
        {
            if (Fits(columns, PreferredFontSize, columnWidth, availableHeight)) return PreferredFontSize;

            double lower = PreferredFontSize / 2;
            while (!Fits(columns, lower, columnWidth, availableHeight))
            {
                lower /= 2;
                if (lower < 0.001)
                    throw new InvalidOperationException("There are too many tasks to fit clearly on one page.");
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
                double usedHeight = BoxOutline.Thickness;
                for (int index = 0; index < tasks.Count; index++)
                {
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
            return Math.Max(1, columnWidth - 2 * HorizontalPadding(fontSize));
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
                CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
                new Typeface(Typography.TaskFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                fontSize, brush, 1.0);
            text.MaxTextWidth = width;
            text.Trimming = TextTrimming.None;
            return text;
        }

        private static Brush GetBrush(string value, Brush fallback)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static Brush GetTaskBackground(TaskItem task)
        {
            if (task == null) return Brushes.White;
            // Explicit task fills remain authoritative. The completion fill is
            // the default only for completed tasks without a custom fill.
            return task.IsCompleted ? CompletedBackgroundBrush : GetBrush(task.BackgroundColor, Brushes.White);
        }

        private static DateTime SelectedMonday(BoardState state, DateTime localNow)
        {
            DateTime selected;
            if (!DateTime.TryParseExact(state.ActiveWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out selected)) selected = localNow;
            return WeeklyPlanner.MondayOf(selected);
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
