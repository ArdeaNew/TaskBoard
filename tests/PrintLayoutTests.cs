using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.Tests
{
    public static class PrintLayoutTests
    {
        private static readonly Size A4 = new Size(297 * 96.0 / 25.4, 210 * 96.0 / 25.4);
        private static readonly DateTime TestDate = new DateTime(2026, 10, 15, 12, 0, 0);
        private static int checks;

        [STAThread]
        public static int Main(string[] arguments)
        {
            try
            {
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                Run();
                string output = arguments.Length > 0 ? Path.GetFullPath(arguments[0])
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print-preview.png");
                SavePreview(output);
                Console.WriteLine("Print layout: " + checks + " checks passed.");
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        public static void Run()
        {
            EmptyPageAndDate();
            SelectedWeekMetadata();
            WeekdayMapping();
            PerDayTaskNumbers();
            LargePrinterMargins();
            TaskBoxesAndFullHeight();
            CompletedTaskMarkers();
            DenseMultilingualTasks();
        }

        private static void EmptyPageAndDate()
        {
            DrawingVisual page = Build(new BoardState(), new Rect(new Point(), A4));
            Rect bounds = page.ContentBounds;
            Assert(Near(bounds.Left, 0) && Near(bounds.Top, 0) &&
                Near(bounds.Width, A4.Width) && Near(bounds.Height, A4.Height), "Physical A4 landscape bounds.");
            string text = ReadText(page.Drawing);
            Assert(text.Contains("2026 / W42"), "Deterministic ISO week and year.");
            Assert(text.Contains("月余 54.8%") && text.Contains("年余 21.4%"), "Current-day-inclusive remaining percentages.");
            foreach (string header in new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" })
                Assert(text.Contains(header), "Printed header " + header + ".");
            Assert(!text.Contains("Task"), "Removed Task header is absent.");
            foreach (string removedLabel in new[] { "体重", "今日花销", "锻炼", "咖啡饮用量", "Vitamin D3", "Vitamin D₃" })
                Assert(!text.Contains(removedLabel), "Task-only printing contains no retired record labels.");
            double[] dividers = Leaves(page.Drawing).OfType<GeometryDrawing>()
                .Select(item => item.Geometry as LineGeometry)
                .Where(line => line != null && Near(line.StartPoint.X, line.EndPoint.X))
                .Select(line => line.StartPoint.X).OrderBy(value => value).ToArray();
            Assert(dividers.Length == 6, "Seven weekday columns have six internal dividers.");
            for (int index = 1; index < dividers.Length - 1; index++)
                Assert(Near(dividers[index] - dividers[index - 1], dividers[index + 1] - dividers[index]),
                    "Weekday columns are evenly spaced.");
        }

        private static void WeekdayMapping()
        {
            var state = new BoardState();
            state.Tasks.Add(new TaskItem { Column = 0, Text = "LEGACY", Color = "#202020" });
            state.Tasks.Add(new TaskItem { Column = 1, Text = "MONDAY", Color = "#202020" });
            state.Tasks.Add(new TaskItem { Column = 7, Text = "SUNDAY", Color = "#202020" });
            DrawingVisual page = Build(state, new Rect(new Point(), A4));
            string text = ReadText(page.Drawing);
            Assert(text.Contains("LEGACY") && text.Contains("MONDAY") && text.Contains("SUNDAY"),
                "Legacy, Monday and Sunday tasks are all printed.");
            Rect legacy = FindGlyphBounds(page.Drawing, "LEGACY");
            Rect monday = FindGlyphBounds(page.Drawing, "MONDAY");
            Rect sunday = FindGlyphBounds(page.Drawing, "SUNDAY");
            Assert(Math.Abs(legacy.Left - monday.Left) < 2, "Legacy column zero maps to Monday.");
            Assert(sunday.Left > A4.Width * 0.8, "Column seven stays in Sunday's final column.");
        }

        private static void SelectedWeekMetadata()
        {
            var state = new BoardState { ActiveWeekStart = "2026-12-28" };
            state.Tasks.Add(new TaskItem { Column = 1, Text = "ACTIVE_WEEK_TASK" });
            state.Weeks.Add(new WeekPlan
            {
                WeekStart = "2026-10-12",
                Tasks = new List<TaskItem> { new TaskItem { Column = 7, Text = "OTHER_WEEK_MARKER" } }
            });
            DrawingVisual page = Build(state, new Rect(new Point(), A4));
            string text = ReadText(page.Drawing);
            Assert(text.Contains("2026 / W53"), "Printed week reflects the selected Monday across the year boundary.");
            Assert(text.Contains("月余 54.8%") && text.Contains("年余 21.4%"),
                "Remaining month and year percentages still reflect the supplied current date.");
            Assert(text.Contains("ACTIVE_WEEK_TASK"), "Selected week's active task is printed.");
            Assert(!text.Contains("OTHER_WEEK_MARKER"), "Archived weeks are excluded from the selected print page.");
            Assert(state.ActiveWeekStart == "2026-12-28", "Rendering leaves the selected week unchanged.");
            Assert(state.Weeks[0].Tasks.Single().Text == "OTHER_WEEK_MARKER", "Rendering leaves archived task data unchanged.");
        }

        private static void LargePrinterMargins()
        {
            var state = new BoardState();
            for (int column = 1; column <= 7; column++)
                state.Tasks.Add(new TaskItem { Column = column, Text = "Margin " + column + " 边界", Color = "#2562A0" });
            var printable = new Rect(76, 65, A4.Width - 152, A4.Height - 130);
            DrawingVisual page = Build(state, printable);
            foreach (var item in PositionedLeaves(page.Drawing, Matrix.Identity))
            {
                Drawing leaf = item.Item1;
                if (IsPaper(leaf)) continue;
                Assert(Inside(item.Item2, printable), "Content stays inside a printer's hard margins: " + leaf.GetType().Name + " " + item.Item2 + ".");
            }
        }

        private static void PerDayTaskNumbers()
        {
            var state = new BoardState();
            state.Tasks.Add(new TaskItem { Column = 1, Text = "MON_FIRST" });
            state.Tasks.Add(new TaskItem { Column = 1, Text = "MON_LAST", DailyGroupId = "routine" });
            state.Tasks.Add(new TaskItem { Column = 7, Text = "SUN_FIRST", IsCompleted = true });
            state.Tasks.Add(new TaskItem { Column = 7, Text = "SUN_LAST" });
            DrawingVisual page = Build(state, new Rect(new Point(), A4));
            string text = ReadText(page.Drawing);
            Assert(text.Contains("1. MON_FIRST") && text.Contains("2. MON_LAST"), "Monday numbering follows its visible task order.");
            Assert(text.Contains("1. SUN_FIRST") && text.Contains("2. SUN_LAST"), "Sunday numbering resets to one.");
            Assert(FindGlyphBounds(page.Drawing, "MON_FIRST").Top < FindGlyphBounds(page.Drawing, "MON_LAST").Top,
                "Printed first and last task positions agree with their numbers.");
            Assert(state.Tasks[0].Text == "MON_FIRST" && state.Tasks[1].Text == "MON_LAST",
                "Print numbering leaves stored task text unchanged.");
        }

        private static void DenseMultilingualTasks()
        {
            var state = new BoardState();
            for (int index = 0; index < 120; index++)
                state.Tasks.Add(new TaskItem
                {
                    Column = 1,
                    Text = "DENSE_" + index.ToString("000", CultureInfo.InvariantCulture) +
                        " 混合中文任务与 English text\n下一行：" + new string('测', 18),
                    Color = index == 119 ? "#C61C22" : "#202020"
                });
            state.Tasks.Add(new TaskItem
            {
                Column = 7,
                Text = "LONG_START " + string.Concat(Enumerable.Repeat("英文 English 与中文无截断，", 40)) +
                    "\nLONG_END",
                Color = "#20704A"
            });
            var printable = new Rect(new Point(), A4);
            DrawingVisual page = Build(state, printable);
            string text = ReadText(page.Drawing);
            for (int index = 0; index < 120; index++)
                Assert(text.Contains("DENSE_" + index.ToString("000", CultureInfo.InvariantCulture)),
                    "Dense task " + index + " is present.");
            Assert(text.Contains("LONG_START") && text.Contains("LONG_END"), "Long multilingual task includes its final line.");
            bool redFound = false;
            foreach (var item in PositionedLeaves(page.Drawing, Matrix.Identity))
            {
                Drawing leaf = item.Item1;
                if (IsPaper(leaf)) continue;
                Assert(Inside(item.Item2, printable), "Dense page ink stays on the single sheet.");
                var glyph = leaf as GlyphRunDrawing;
                var brush = glyph == null ? null : glyph.ForegroundBrush as SolidColorBrush;
                if (brush != null && brush.Color == Color.FromRgb(198, 28, 34)) redFound = true;
            }
            Assert(redFound, "Final dense task retains its individual font color.");
        }

        private static void TaskBoxesAndFullHeight()
        {
            var state = new BoardState();
            for (int column = 1; column <= 7; column++)
            {
                int normalCount = column == 7 ? 18 : 1;
                for (int index = 1; index <= normalCount; index++)
                    state.Tasks.Add(new TaskItem { Column = column, Text = "NORMAL_" + column + "_" + index });
                state.Tasks.Add(new TaskItem { Column = column, Text = "ROUTINE" + column, DailyGroupId = "routine" });
            }
            DrawingVisual page = Build(state, new Rect(new Point(), A4));
            string text = ReadText(page.Drawing);
            GeometryDrawing[] boxes = Leaves(page.Drawing).OfType<GeometryDrawing>()
                .Where(item => item.Geometry is RectangleGeometry && PenColor(item.Pen, "#DDDDDD")).ToArray();
            Assert(boxes.Length == state.Tasks.Count, "Only task boxes are drawn on the page.");
            Assert(boxes.All(item => Near(((RectangleGeometry)item.Geometry).RadiusX, 0)), "Task boxes retain square corners.");
            Assert(boxes.All(item => Near(item.Geometry.Bounds.Width, boxes[0].Geometry.Bounds.Width)),
                "Task boxes keep equal full-column widths.");
            Assert(text.Contains("19. ROUTINE7"), "Sunday's final routine and its number remain visible.");
            Rect finalTask = FindGlyphBounds(page.Drawing, "ROUTINE7");
            Assert(finalTask.Bottom > A4.Height * 0.85 && finalTask.Bottom < A4.Height - 25,
                "Tasks use the full column height without clipping the final task.");
            foreach (var item in PositionedLeaves(page.Drawing, Matrix.Identity))
                Assert(Inside(item.Item2, new Rect(new Point(), A4)), "Task-only page ink remains inside its A4 sheet.");
        }
        private static void CompletedTaskMarkers()
        {
            var state = new BoardState();
            state.Tasks.Add(new TaskItem { Column = 1, Text = "COMPLETED_TEXT", IsCompleted = true });
            state.Tasks.Add(new TaskItem { Column = 1, Text = "PENDING" });
            state.Tasks.Add(new TaskItem { Column = 7, Text = "COMPLETE_7", IsCompleted = true });
            DrawingVisual page = Build(state, new Rect(new Point(), A4));
            GeometryDrawing[] markers = Leaves(page.Drawing).OfType<GeometryDrawing>()
                .Where(item => item.Geometry is RectangleGeometry && BrushColor(item.Brush, "#002FA7")).ToArray();
            Assert(markers.Length == 2, "Only completed tasks receive a blue marker.");
            foreach (GeometryDrawing marker in markers)
            {
                Rect bounds = marker.Geometry.Bounds;
                Assert(Near(bounds.Width, bounds.Height) && bounds.Width > 0 && bounds.Width < 8,
                    "Completion marker is a tiny filled square.");
            }
            Rect text = FindGlyphBounds(page.Drawing, "COMPLETED_TEXT");
            Assert(markers[0].Geometry.Bounds.Left > text.Right, "Reserved right space keeps completion marker clear of task text.");
            Assert(ReadText(page.Drawing).Contains("COMPLETED_TEXT"), "Completed task text stays intact.");
            Assert(!Leaves(page.Drawing).OfType<GeometryDrawing>().Any(item =>
            {
                var line = item.Geometry as LineGeometry;
                return line != null && line.StartPoint.Y > text.Top && line.StartPoint.Y < text.Bottom &&
                    line.StartPoint.X < text.Right && line.EndPoint.X > text.Left;
            }), "Completed text has no strike-through line.");
            Assert(Inside(markers[0].Bounds, new Rect(new Point(), A4)) && Inside(markers[1].Bounds, new Rect(new Point(), A4)),
                "Completion markers stay inside the page.");
        }

        private static void SavePreview(string path)
        {
            string[] examples =
            {
                "整理本周任务\nWeekly priorities", "设计桌面应用\nTask board", "检查交互细节",
                "字体与颜色\n中文宋体 / monospace", "Smooth reorder", "完成本周工作", "整理下周计划"
            };
            string[] colors = { "#202020", "#202020", "#315FA3", "#8F423D", "#202020", "#3D7254", "#202020" };
            var state = new BoardState();
            for (int column = 1; column <= 7; column++)
            {
                state.Tasks.Add(new TaskItem { Column = column, Text = examples[column - 1], Color = colors[column - 1], IsCompleted = column == 1 });
                state.Tasks.Add(new TaskItem { Column = column, Text = "Review " + column + "\n复核与调整", Color = "#202020" });
                state.Tasks.Add(new TaskItem { Column = column, Text = "阅读 · Read", Color = "#426787", DailyGroupId = "read" });
            }
            DrawingVisual page = Build(state, new Rect(new Point(), A4));
            const double dpi = 144;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(A4.Width * dpi / 96),
                (int)Math.Ceiling(A4.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write)) encoder.Save(stream);
        }

        private static DrawingVisual Build(BoardState state, Rect printable)
        {
            return BoardPageRenderer.BuildPage(state, A4, printable, TestDate);
        }

        private static string ReadText(Drawing drawing)
        {
            var text = new StringBuilder();
            foreach (Drawing leaf in Leaves(drawing))
            {
                var glyph = leaf as GlyphRunDrawing;
                if (glyph != null && glyph.GlyphRun.Characters != null)
                    foreach (char character in glyph.GlyphRun.Characters) text.Append(character);
            }
            return text.ToString();
        }

        private static Rect FindGlyphBounds(Drawing drawing, string value)
        {
            var text = new StringBuilder();
            var spans = new List<Tuple<int, int, Rect>>();
            foreach (var item in PositionedLeaves(drawing, Matrix.Identity))
            {
                var glyph = item.Item1 as GlyphRunDrawing;
                if (glyph == null || glyph.GlyphRun.Characters == null) continue;
                string part = new string(glyph.GlyphRun.Characters.ToArray());
                spans.Add(Tuple.Create(text.Length, part.Length, item.Item2));
                text.Append(part);
            }
            int start = text.ToString().IndexOf(value, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("Missing printed text: " + value);
            Rect bounds = Rect.Empty;
            foreach (var span in spans)
                if (span.Item1 < start + value.Length && span.Item1 + span.Item2 > start) bounds.Union(span.Item3);
            return bounds;
        }

        private static IEnumerable<Drawing> Leaves(Drawing drawing)
        {
            var group = drawing as DrawingGroup;
            if (group == null) { yield return drawing; yield break; }
            foreach (Drawing child in group.Children)
                foreach (Drawing leaf in Leaves(child)) yield return leaf;
        }

        private static IEnumerable<Tuple<Drawing, Rect>> PositionedLeaves(Drawing drawing, Matrix parentTransform)
        {
            var group = drawing as DrawingGroup;
            if (group == null)
            {
                Rect bounds = drawing.Bounds;
                if (!bounds.IsEmpty) bounds.Transform(parentTransform);
                yield return Tuple.Create(drawing, bounds);
                yield break;
            }
            Matrix transform = group.Transform == null ? Matrix.Identity : group.Transform.Value;
            transform.Append(parentTransform);
            foreach (Drawing child in group.Children)
                foreach (var leaf in PositionedLeaves(child, transform)) yield return leaf;
        }

        private static bool IsPaper(Drawing drawing)
        {
            var geometry = drawing as GeometryDrawing;
            var brush = geometry == null ? null : geometry.Brush as SolidColorBrush;
            return geometry != null && geometry.Pen == null && brush != null && brush.Color == Colors.White;
        }

        private static bool PenColor(Pen pen, string value)
        {
            return pen != null && BrushColor(pen.Brush, value);
        }

        private static bool BrushColor(Brush valueBrush, string value)
        {
            var brush = valueBrush as SolidColorBrush;
            return brush != null && brush.Color == (Color)ColorConverter.ConvertFromString(value);
        }

        private static bool Inside(Rect inner, Rect outer)
        {
            return inner.IsEmpty || inner.Left >= outer.Left - 0.1 && inner.Top >= outer.Top - 0.1 &&
                inner.Right <= outer.Right + 0.1 && inner.Bottom <= outer.Bottom + 0.1;
        }

        private static bool Near(double first, double second) { return Math.Abs(first - second) < 0.01; }

        private static void Assert(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
