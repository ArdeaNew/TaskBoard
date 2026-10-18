using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskBoard.Core;
using TaskBoard.Persistence;
using TaskBoard.UI;

namespace TaskBoard.Tools
{
    // Documentation fixtures only. No default user directory or desktop capture.
    internal static class DemoImages
    {
        private static readonly DateTime sampleDate = new DateTime(2026, 10, 17);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("Expected image and isolated data directories.");
            string output = Path.GetFullPath(args[0]);
            string data = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(data);
            var store = new BoardStore(data);
            var fixture = CreateFixture();
            store.Save(fixture);
            // The same injected clock used by lifecycle checks keeps the actual
            // header, navigation, and weekly logic on a fixed fictional date.
            var window = new MainWindow(store, () => sampleDate);
            Field<DispatcherTimer>(window, "dateTimer").Stop();
            Field<DispatcherTimer>(window, "saveTimer").Stop();
            var root = (FrameworkElement)window.Content;
            Layout(root, new Size(1440, 780));
            Save(root, null, new Point(), Path.Combine(output, "overview.png"));

            var card = Field<ColumnPanel[]>(window, "columns")[4].Children.OfType<TaskCard>()
                .Single(item => item.Item.Id == "demo-release");
            var menu = card.ContextMenu;
            if (!menu.Items.OfType<MenuItem>().Any(item => (string)item.Header == "Move to next week"))
                throw new InvalidOperationException("The actual task menu must expose Move to next week.");
            menu.ApplyTemplate();
            menu.Measure(new Size(280, 500));
            Size menuSize = menu.DesiredSize;
            if (menuSize.Width < 100 || menuSize.Height < 80)
                throw new InvalidOperationException("The actual task menu did not produce a renderable layout.");
            menu.Arrange(new Rect(new Point(), menuSize));
            menu.UpdateLayout();
            Layout(root, new Size(1440, 780));
            Point cardPoint = card.TranslatePoint(new Point(20, Math.Max(20, card.ActualHeight - 8)), root);
            Save(root, menu, cardPoint, Path.Combine(output, "task-actions.png"), new Rect(820, 0, 620, 430));

            // Render the actual inbox content off-screen using only the same
            // isolated fictional state. No desktop window is shown or captured.
            BoardState inboxState = Field<BoardState>(window, "state");
            var inbox = new InboxWindow(inboxState, () => WeeklyPlanner.CurrentWeekStart(inboxState),
                snapshot => { }, refresh => { }, () => { });
            var inboxRoot = (FrameworkElement)inbox.Content;
            Layout(inboxRoot, new Size(840, 520));
            Save(inboxRoot, null, new Point(), Path.Combine(output, "inbox.png"));
            Field<TaskDragController>(window, "dragController").Dispose();
            Console.WriteLine("Created three documentation images using fictional fixtures only.");
            return 0;
        }

        private static BoardState CreateFixture()
        {
            var state = new BoardState();
            Add(state, "demo-brief", 1, "Sketch the week", "#111111", "#E4EFFA", true);
            Add(state, "demo-inspiration", 1, "整理灵感", "#426787", null);
            Add(state, "demo-read-mon", 1, "Read 20 pages", "#4A7555", "#E8F0DD");
            Add(state, "demo-design", 2, "Explore a new layout", "#111111", "#FFF4CC");
            Add(state, "demo-walk", 2, "Take a long walk", "#4A7555", null);
            Add(state, "demo-review", 3, "Review the prototype", "#426787", "#E4EFFA", true);
            Add(state, "demo-notes", 3, "Collect feedback", "#111111", null);
            Add(state, "demo-write", 4, "Write release notes", "#785C93", "#EAE6F7");
            Add(state, "demo-book", 4, "读一篇短文", "#111111", null);
            Add(state, "demo-release", 5, "Plan next release", "#111111", "#FFF4CC");
            Add(state, "demo-cleanup", 5, "Tidy the workspace", "#6B7280", null);
            Add(state, "demo-gallery", 6, "Visit an exhibition", "#A25B78", "#F4E3F0");
            Add(state, "demo-cook", 6, "Try a new recipe", "#111111", null);
            Add(state, "demo-reflect", 7, "Reflect & reset", "#426787", "#E4EFFA");
            Add(state, "demo-plan", 7, "Make room for next week", "#6B7280", null);
            AddInbox(state, "demo-inbox-idea", 1, "Explore a new idea", "#426787", "#E4EFFA");
            AddInbox(state, "demo-inbox-notes", 1, "整理项目想法", "#111111", null);
            AddInbox(state, "demo-inbox-study", 2, "Outline the next study", "#785C93", "#EAE6F7");
            AddInbox(state, "demo-inbox-reference", 2, "Read reference notes", "#111111", null);
            AddInbox(state, "demo-inbox-walk", 3, "Plan a weekend walk", "#4A7555", "#E8F0DD");
            AddInbox(state, "demo-inbox-library", 3, "Book a library visit", "#111111", null);
            return state;
        }

        private static void Add(BoardState state, string id, int day, string text, string color, string fill, bool completed = false)
        {
            state.Tasks.Add(new TaskItem { Id = id, Column = day, Text = text, Color = color,
                BackgroundColor = fill, IsCompleted = completed });
        }

        private static void AddInbox(BoardState state, string id, int group, string text, string color, string fill)
        {
            state.Inbox.Add(new InboxItem
            {
                Group = group,
                Task = new TaskItem { Id = id, Text = text, Color = color, BackgroundColor = fill }
            });
        }

        private static void Layout(FrameworkElement element, Size size)
        {
            element.Measure(size);
            element.Arrange(new Rect(new Point(), size));
            element.UpdateLayout();
        }

        private static void Save(FrameworkElement board, FrameworkElement menu, Point position, string path, Rect? viewport = null)
        {
            const double scale = 1.25;
            Rect bounds = viewport ?? new Rect(0, 0, board.ActualWidth, board.ActualHeight);
            var drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
            {
                // Render detail views directly from vector controls; no raster crop.
                context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, board.ActualWidth, board.ActualHeight));
                context.DrawRectangle(new VisualBrush(board) { Stretch = Stretch.None, AutoLayoutContent = false }, null,
                    new Rect(0, 0, board.ActualWidth, board.ActualHeight));
                if (menu != null)
                {
                    // Composite the actual ContextMenu control, not a drawn mockup.
                    context.DrawRectangle(new VisualBrush(menu) { Stretch = Stretch.None, AutoLayoutContent = false }, null,
                        new Rect(position, new Size(menu.ActualWidth, menu.ActualHeight)));
                }
                context.Pop();
            }
            var image = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * scale),
                (int)Math.Ceiling(bounds.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }
    }
}
