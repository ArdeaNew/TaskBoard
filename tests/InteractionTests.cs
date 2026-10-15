using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBoard.Core;
using TaskBoard.Persistence;
using TaskBoard.UI;

namespace TaskBoard.Tests
{
    // Tests real WPF controls without injecting desktop input or showing windows.
    internal static class InteractionTests
    {
        private static int checks;
        [STAThread]
        private static int Main()
        {
            string workspace = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string directory = Path.Combine(workspace, "test-data-ui-" + Guid.NewGuid().ToString("N"));
            MainWindow window = null;
            try
            {
                var initial = new BoardState();
                initial.Tasks.Add(new TaskItem { Text = "Design 设计稿", Color = "#315FA3", Column = 1 });
                var store = new BoardStore(directory);
                store.Save(initial);
                window = new MainWindow(store);
                Assert(window.WindowState == WindowState.Normal, "Window opens restored.");
                Assert(Math.Abs(window.Width - Math.Max(window.MinWidth, SystemParameters.WorkArea.Width * .7)) < 1, "Width uses 70% of work area.");
                Assert(Math.Abs(window.Height - Math.Max(window.MinHeight, SystemParameters.WorkArea.Height * .7)) < 1, "Height uses 70% of work area.");
                Layout(window);
                var state = Field<BoardState>(window, "state");
                var panels = Field<ColumnPanel[]>(window, "columns");
                Assert(panels.Length == 7, "Seven day columns.");
                AssertTaskOnlyBoard(window, panels, state);
                Assert(!Descendants<Button>((DependencyObject)window.Content).Any(b => AutomationProperties.GetName(b).Contains("添加任务")), "Weekday headers have no add buttons.");
                var card = panels[0].Children.OfType<TaskCard>().Single();
                Assert(card.Margin.Left == 0 && card.Margin.Right == 0 && card.CornerRadius.TopLeft == 0, "Task box has no horizontal inset or rounded corners.");
                Assert(Math.Abs(card.ActualWidth - panels[0].ActualWidth) < 1, "Task box fills the column width.");
                card.BeginEdit();
                var activeEditor = Field<TextBox>(card, "editor");
                activeEditor.Text = "Design 编辑中的任务";
                Assert(ReferenceEquals(activeEditor.ContextMenu, card.ContextMenu), "Active text editor exposes the task context menu instead of its default text menu.");
                var apply = activeEditor.ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "应用到每天");
                apply.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(state.Tasks.Count == 7 && state.Tasks.All(t => t.IsDaily), "Context menu creates seven daily tasks.");
                Assert(state.Tasks.All(t => t.Text == "Design 编辑中的任务"), "Applying directly from the editor commits and copies the edited text to all days.");
                foreach (var panel in panels) Assert(panel.Children.OfType<TaskCard>().Count() == 1, "Daily copy rendered in each day.");
                card = panels[0].Children.OfType<TaskCard>().Single();
                card.BeginEdit();
                Field<TextBox>(card, "editor").Text = "Read 阅读";
                card.CommitEdit();
                Assert(state.Tasks.All(t => t.Text == "Read 阅读"), "Editing a routine synchronizes all days.");
                Invoke(window, "AddTask", 0);
                var normal = panels[0].Children.OfType<TaskCard>().Single(t => !t.Item.IsDaily);
                Field<TextBox>(normal, "editor").Text = "Review 复核";
                normal.CommitEdit();
                Assert(panels[0].Children.OfType<TaskCard>().First() == normal && panels[0].Children.OfType<TaskCard>().Last().Item.IsDaily, "New ordinary task stays above daily tasks.");
                Assert(panels[0].Children.OfType<TaskCard>().Select(t => t.Ordinal).SequenceEqual(new[] { 1, 2 }) && panels[1].Children.OfType<TaskCard>().Single().Ordinal == 1, "Numbering follows order and resets each day.");

                var routine = panels[0].Children.OfType<TaskCard>().Single(t => t.Item.IsDaily);
                string completedId = routine.Item.Id;
                Descendants<Button>(routine).Single(t => AutomationProperties.GetName(t) == "完成")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var completed = panels[0].Children.OfType<TaskCard>().First();
                Assert(completed.Item.Id == completedId && completed.Item.IsCompleted,
                    "Completing a routine moves it above the ordinary task.");
                Assert(completed.Ordinal == 1 && panels[0].Children.OfType<TaskCard>().Last().Ordinal == 2, "Completion promotion renumbers the day's tasks.");
                Assert(state.Tasks.Where(t => t.IsDaily && t.Id != completedId).All(t => !t.IsCompleted),
                    "Routine completion remains independent for other days.");
                var completionColor = Field<Border>(completed, "completionSquare").Background as SolidColorBrush;
                Assert(completionColor != null && completionColor.Color == Color.FromRgb(0, 47, 167),
                    "Completed task square uses Klein blue.");
                Assert(Field<TextBlock>(completed, "display").TextDecorations == null ||
                    Field<TextBlock>(completed, "display").TextDecorations.Count == 0,
                    "Completed task text has no strike-through.");

                string normalId = normal.Item.Id;
                normal = panels[0].Children.OfType<TaskCard>().Single(t => !t.Item.IsDaily);
                normal.BeginEdit();
                Field<TextBox>(normal, "editor").Text = "Review saved before apply";
                var routineMenu = panels[1].Children.OfType<TaskCard>().Single(t => t.Item.IsDaily).ContextMenu;
                routineMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "应用到每天")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var updatedNormal = panels[0].Children.OfType<TaskCard>().Single(t => t.Item.Id == normalId);
                Assert(updatedNormal.Item.Text == "Review saved before apply", "Applying another task commits the pending ordinary edit.");
                Assert(Field<TextBlock>(updatedNormal, "display").Text == updatedNormal.Item.Text,
                    "Rebuilt ordinary card displays the edit committed before routine application.");
                Invoke(window, "AddTask", 1);
                Invoke(window, "SaveNow");
                Assert(!store.Load().Tasks.Any(t => string.IsNullOrWhiteSpace(t.Text)),
                    "Autosave while a new blank task is being edited writes only committed tasks.");
                routineMenu = panels[2].Children.OfType<TaskCard>().Single(t => t.Item.IsDaily).ContextMenu;
                routineMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "应用到每天")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(!state.Tasks.Any(t => string.IsNullOrWhiteSpace(t.Text)), "Applying another task removes the pending blank new task.");
                Assert(!panels.SelectMany(p => p.Children.OfType<TaskCard>()).Any(t => string.IsNullOrWhiteSpace(t.Item.Text)),
                    "No stale blank card remains after the board rebuild.");

                Invoke(window, "SaveNow");
                var loaded = store.Load();
                Assert(loaded.Tasks.Count == 8 && loaded.Tasks.Count(t => t.IsDaily) == 7, "Daily tasks survive UI save.");
                Assert(loaded.Tasks.Single(t => t.Id == completedId).IsCompleted, "Independent completion state survives UI save.");
                string exportPath = Path.Combine(workspace, "exported-board.png");
                BoardImageExporter.Save(state, exportPath);
                Assert(File.ReadAllBytes(exportPath).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                    "Image export writes a valid PNG signature.");
                using (var imageStream = File.OpenRead(exportPath))
                {
                    var exported = BitmapDecoder.Create(imageStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    Assert(exported.PixelWidth > 2000 && exported.PixelHeight > 1400,
                        "PNG export contains the complete high-resolution board page.");
                    Assert(Math.Abs(exported.DpiX - 192) < 1 && Math.Abs(exported.DpiY - 192) < 1,
                        "PNG export retains its 192 dpi resolution.");
                }
                AssertWeeklyPlanning(window, store, state, panels, completedId, normalId);
                AssertDragPreviewAndDrop(window, state, panels, normalId, completedId);
                AssertDayClearing(window, store, state, panels);
                Layout(window);
                AssertTaskOnlyBoard(window, panels, state);
                SavePreview(window);
                Console.WriteLine("UI integration: " + checks + " checks passed.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                if (window != null) window.Close();
                string resolved = Path.GetFullPath(directory);
                string allowedPrefix = workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!resolved.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Test cleanup target is outside its workspace.");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }

        private static void Layout(MainWindow window)
        {
            var root = (FrameworkElement)window.Content;
            var size = new Size(window.Width, Math.Max(1, window.Height - 30));
            root.Measure(size);
            root.Arrange(new Rect(new Point(), size));
            root.UpdateLayout();
        }

        private static void SavePreview(MainWindow window)
        {
            SavePreview(window, "board-preview.png");
        }

        private static void SavePreview(MainWindow window, string fileName)
        {
            var root = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 1.5),
                (int)Math.Ceiling(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName))) encoder.Save(stream);
        }

        private static void AssertTaskOnlyBoard(MainWindow window, ColumnPanel[] panels, BoardState state)
        {
            var root = (DependencyObject)window.Content;
            var taskScrollers = Descendants<ScrollViewer>(root).Where(s => s.Content is ColumnPanel).ToArray();
            Assert(taskScrollers.Length == 7 && panels.All(p => taskScrollers.Any(s => ReferenceEquals(s.Content, p))),
                "Only seven task-scrolling day columns appear in the board.");
            double bodyHeight = Field<Grid>(window, "board").RowDefinitions[1].ActualHeight;
            var clearButtons = Field<DayClearButton[]>(window, "clearButtons");
            Assert(clearButtons.Length == 7 && clearButtons.All(button => Math.Abs(button.ActualHeight - 40) < 1),
                "Every day reserves the same fixed-height clear region.");
            Assert(taskScrollers.All(s => Math.Abs(s.ActualHeight + 40 - bodyHeight) < 2),
                "Task scrollers use the full day body above its fixed clear region.");
            Assert(clearButtons.Select((button, index) => button.IsAvailable == state.Tasks.Any(t =>
                t.Column == index + 1 && !string.IsNullOrWhiteSpace(t.Text))).All(value => value),
                "Clear availability follows each day's committed tasks.");
            string[] recordLabels = { "体重", "今日花销", "锻炼", "咖啡饮用量", "Vitamin D3", "Vitamin D₃", "kg", "￥", "组", "杯", "IU" };
            Assert(!Descendants<TextBlock>(root).Any(t => recordLabels.Contains(t.Text)),
                "Record labels and units are absent from the task board.");
            Assert(!Descendants<TextBox>(root).Any(t => recordLabels.Contains(AutomationProperties.GetName(t)) ||
                AutomationProperties.GetName(t) == "记录项目名称"),
                "Record value and label editors are absent from the task board.");
            Assert(!Descendants<Button>(root).Any(b => AutomationProperties.GetName(b) == "添加记录项目"),
                "The add-record button is absent from the task board.");
        }

        private static void AssertDragPreviewAndDrop(MainWindow window, BoardState state,
            ColumnPanel[] panels, string ordinaryId, string completedId)
        {
            var originalTasks = BoardOperations.CloneTasks(state.Tasks);
            var undo = Field<Stack<List<TaskItem>>>(window, "undo");
            int originalUndoCount = undo.Count;
            double originalTargetWidth = panels[1].Width;
            bool[] animated = panels.Select(panel => panel.Animate).ToArray();
            foreach (var panel in panels) panel.Animate = false;
            try
            {
                var follower = new TaskItem { Column = 1, Text = "Follower after reserved drag slot" };
                state.Tasks.Add(follower);
                Invoke(window, "RenderTasks");
                var card = panels[0].Children.OfType<TaskCard>().Single(task => task.Item.Id == ordinaryId);
                card.Item.Text = "跨列视觉一致 Drag preview wraps smoothly across columns. 跨列视觉一致 Drag preview wraps smoothly.";
                card.RefreshAppearance();
                Layout(window);
                double sourceHeight = card.ActualHeight;
                panels[1].Width = Math.Max(80, card.ActualWidth * 0.72);
                Layout(window);
                SetPointer(window, card.TranslatePoint(new Point(12, 12), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "StartDragVisuals", card);
                var ghost = Field<Border>(Drag(window), "ghost");
                var preview = ghost.Child as TaskCard;
                var gap = Field<Border>(Drag(window), "gap");
                Assert(preview != null && ReferenceEquals(preview.Item, card.Item),
                    "Dragging uses a live task-card preview backed by the same task, rather than a bitmap.");
                Assert(ghost.Opacity == 1 && preview.BorderThickness == card.BorderThickness &&
                    preview.CornerRadius == card.CornerRadius, "Drag preview retains the task's opacity and border geometry.");
                Assert(Field<TextBlock>(preview, "display").FontFamily.Equals(Field<TextBlock>(card, "display").FontFamily) &&
                    Field<TextBlock>(preview, "display").FontSize == Field<TextBlock>(card, "display").FontSize,
                    "Drag preview retains the task's exact font family and size.");
                Assert(((SolidColorBrush)Field<TextBlock>(preview, "display").Foreground).Color ==
                    ((SolidColorBrush)Field<TextBlock>(card, "display").Foreground).Color,
                    "Drag preview preserves the individual text color.");
                Assert(gap.BorderThickness == card.BorderThickness && gap.CornerRadius == card.CornerRadius,
                    "Drop placeholder uses the same border geometry as a task card.");
                Assert(Field<DayClearButton[]>(window, "clearButtons").All(button => !button.IsAvailable),
                    "Clear actions are unavailable while a drag is in progress.");
                Assert(panels[0].Children.OfType<TaskCard>().Single(task => task.Item.Id == follower.Id).Ordinal == 3,
                    "The source placeholder reserves its ordinal so following tasks do not jump backward.");

                Point destination = panels[1].TranslatePoint(new Point(panels[1].ActualWidth / 2, 2), Field<Grid>(window, "surface"));
                SetPointer(window, destination);
                Invoke(Drag(window), "UpdateDropTarget");
                Layout(window);
                Assert(Field<int>(Drag(window), "targetColumn") == 1 && preview.Ordinal == 1,
                    "Cross-column preview adopts the destination's insertion number.");
                Assert(panels[0].Children.OfType<TaskCard>().Single(task => task.Item.Id == follower.Id).Ordinal == 2 &&
                    panels[1].Children.OfType<TaskCard>().Single().Ordinal == 2,
                    "Moving the placeholder renumbers both source and destination peers around the reserved slot.");
                Assert(Math.Abs(ghost.Width - panels[1].ActualWidth) < 1 && Math.Abs(gap.Height - ghost.Height) < 1,
                    "Preview width and placeholder height follow the narrower destination column.");
                Assert(ghost.Height >= sourceHeight && ghost.Height > 48 && Math.Abs(preview.ActualHeight - ghost.Height) < 1,
                    "Long multilingual text reflows live before dropping into a narrower column.");
                SavePreview(window, "drag-preview.png");
                double landingHeight = ghost.Height;
                Invoke(Drag(window), "FinishDrag", true);
                Layout(window);
                Invoke(Drag(window), "CompleteDropAnimation");
                Layout(window);
                var landed = panels[1].Children.OfType<TaskCard>().Single(task => task.Item.Id == ordinaryId);
                Assert(landed.Item.Column == 2 && landed.Ordinal == 1 && !panels[0].Children.Contains(landed),
                    "Cross-column drop commits the target day and ordinal exactly once.");
                Assert(Math.Abs(landed.ActualWidth - panels[1].ActualWidth) < 1 && Math.Abs(landed.ActualHeight - landingHeight) < 1,
                    "The landed card matches the live preview's width and wrapped height.");
                AssertDropClean(window, landed);

                int undoBeforeCancel = undo.Count;
                SetPointer(window, landed.TranslatePoint(new Point(12, 12), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "StartDragVisuals", landed);
                SetPointer(window, panels[0].TranslatePoint(new Point(panels[0].ActualWidth / 2, 2), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "UpdateDropTarget");
                Invoke(Drag(window), "FinishDrag", false);
                Invoke(Drag(window), "CompleteDropAnimation");
                Layout(window);
                Assert(landed.Item.Column == 2 && panels[1].Children.OfType<TaskCard>().First() == landed && undo.Count == undoBeforeCancel,
                    "Cancelling a cross-column preview restores its original day, order and undo history.");
                AssertDropClean(window, landed);

                var completed = panels[0].Children.OfType<TaskCard>().Single(task => task.Item.Id == completedId);
                SetPointer(window, completed.TranslatePoint(new Point(12, 12), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "StartDragVisuals", completed);
                preview = (TaskCard)Field<Border>(Drag(window), "ghost").Child;
                var blue = Field<Border>(preview, "completionSquare").Background as SolidColorBrush;
                Assert(blue != null && blue.Color == Color.FromRgb(0, 47, 167),
                    "Completed task previews preserve their Klein blue completion square.");
                SetPointer(window, destination);
                Invoke(Drag(window), "UpdateDropTarget");
                Assert(Field<int>(Drag(window), "targetColumn") == 0, "Daily task previews remain in their own day.");
                Invoke(Drag(window), "FinishDrag", false);
                Invoke(Drag(window), "CompleteDropAnimation");
                AssertDropClean(window, completed);
            }
            finally
            {
                if (Field<TaskCard>(Drag(window), "dragged") != null) Invoke(Drag(window), "FinishDrag", false);
                Invoke(Drag(window), "CompleteDropAnimation");
                panels[1].Width = originalTargetWidth;
                state.Tasks = originalTasks;
                while (undo.Count > originalUndoCount) undo.Pop();
                Invoke(window, "RenderTasks");
                Invoke(window, "QueueSave");
                for (int index = 0; index < panels.Length; index++) panels[index].Animate = animated[index];
                Layout(window);
            }
        }

        private static void AssertDropClean(MainWindow window, TaskCard card)
        {
            Assert(Field<TaskCard>(Drag(window), "dragged") == null && Field<Border>(Drag(window), "ghost") == null &&
                Field<Border>(Drag(window), "gap") == null && Field<Canvas>(window, "overlay").Children.Count == 0,
                "Finishing a drag removes its preview, placeholder and overlay state.");
            Assert(card.Opacity == 1 && card.IsHitTestVisible && !Field<bool>(card, "isDragging"),
                "Drop cleanup restores the real card's visibility, hit testing and normal appearance.");
        }

        private static void SetPointer(MainWindow window, Point point)
        {
            SetField(Drag(window), "pressed", point);
            SetField(Drag(window), "pointer", point);
            if (Field<Border>(Drag(window), "ghost") != null) Invoke(Drag(window), "MoveGhost");
        }

        private static void AssertDayClearing(MainWindow window, BoardStore store, BoardState state, ColumnPanel[] panels)
        {
            var button = new DayClearButton { IsAvailable = true };
            int requested = 0;
            button.ClearRequested += (s, e) => requested++;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(requested == 0 && !button.IsRevealed, "An invisible clear region cannot activate before dwell.");
            Invoke(button, "StartDwell");
            Assert(button.IsRevealPending, "Pointer dwell waits before revealing the clear action.");
            button.IsAvailable = false;
            Assert(!button.IsRevealPending && !button.IsRevealed && !button.IsEnabled,
                "Making a day unavailable cancels dwell and hides its clear action.");
            button.IsAvailable = true;
            Invoke(button, "SetRevealed", true, false);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(requested == 1, "A revealed available clear action requests confirmation once.");
            button.IsAvailable = false;

            Invoke(window, "SaveNow");
            string activeKey = state.ActiveWeekStart;
            var otherWeeks = state.Weeks.Where(week => week.WeekStart != activeKey).ToDictionary(week => week.WeekStart,
                week => week.Tasks.Select(task => task.Id).ToArray());
            string[] allIds = state.Tasks.Select(task => task.Id).ToArray();
            string[] otherDayIds = state.Tasks.Where(task => task.Column != 1).Select(task => task.Id).ToArray();
            var undo = Field<Stack<List<TaskItem>>>(window, "undo");
            int beforeUndo = undo.Count;
            var clearButtons = Field<DayClearButton[]>(window, "clearButtons");
            Invoke(clearButtons[0], "SetRevealed", true, false);
            SavePreview(window, "footer-preview.png");
            Invoke(clearButtons[0], "SetRevealed", false, false);
            Invoke(window, "ClearDay", 0);
            Layout(window);
            Assert(!state.Tasks.Any(task => task.Column == 1) && !panels[0].Children.OfType<TaskCard>().Any(),
                "Confirmed clear removes every ordinary, daily and completed task from the selected day.");
            Assert(state.Tasks.Select(task => task.Id).SequenceEqual(otherDayIds), "Clearing one day preserves all other days and their order.");
            Assert(!clearButtons[0].IsAvailable && clearButtons.Skip(1).All(clear => clear.IsAvailable) &&
                clearButtons.All(clear => Math.Abs(clear.ActualHeight - 40) < 1),
                "Clearing an empty day disables its action while every fixed footer keeps its height.");
            Assert(undo.Count == beforeUndo + 1 && undo.Peek().Select(task => task.Id).SequenceEqual(allIds),
                "Day clear records one complete undo snapshot with the removed tasks.");
            foreach (var week in state.Weeks.Where(week => week.WeekStart != activeKey))
                Assert(week.Tasks.Select(task => task.Id).SequenceEqual(otherWeeks[week.WeekStart]),
                    "Clearing this week does not alter any archived week.");
            Invoke(window, "SaveNow");
            var saved = store.Load();
            Assert(!saved.Tasks.Any(task => task.Column == 1) &&
                !saved.Weeks.Single(week => week.WeekStart == activeKey).Tasks.Any(task => task.Column == 1),
                "Day clear persists in the active task list and its weekly archive.");
            Invoke(window, "ClearDay", 0);
            Assert(undo.Count == beforeUndo + 1, "Clearing an already empty day creates no extra undo entry.");

            state.Tasks = undo.Pop();
            Invoke(window, "RenderTasks");
            Invoke(window, "QueueSave");
            Invoke(window, "SaveNow");
            Layout(window);
            Assert(state.Tasks.Select(task => task.Id).SequenceEqual(allIds) && clearButtons[0].IsAvailable,
                "The clear undo snapshot restores the original tasks and footer availability.");
        }

        private static void AssertWeeklyPlanning(MainWindow window, BoardStore store, BoardState state,
            ColumnPanel[] panels, string completedId, string normalId)
        {
            DateTime baseWeek = WeeklyPlanner.CurrentWeekStart(state);
            DateTime futureWeek = baseWeek.AddDays(7);
            string baseLabel = Field<TextBlock>(window, "week").Text;
            string[] baseIds = state.Tasks.Select(t => t.Id).OrderBy(id => id).ToArray();
            var navigation = Descendants<Button>((DependencyObject)window.Content).ToArray();
            var previous = navigation.Single(b => AutomationProperties.GetName(b) == "上一周");
            var next = navigation.Single(b => AutomationProperties.GetName(b) == "下一周");
            var current = navigation.Single(b => AutomationProperties.GetName(b) == "回到本周");
            int undoCount = Field<Stack<List<TaskItem>>>(window, "undo").Count;
            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(Field<Stack<List<TaskItem>>>(window, "undo").Count == undoCount, "Returning to an already selected current week preserves undo.");
            Invoke(window, "NavigateWeek", baseWeek.AddDays(3));
            Assert(Field<Stack<List<TaskItem>>>(window, "undo").Count == undoCount, "Selecting another date in the same week preserves undo.");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert(WeeklyPlanner.CurrentWeekStart(state) == futureWeek,
                "Navigating ahead selects the next week's board.");
            Assert(Field<TextBlock>(window, "week").Text != baseLabel,
                "The visible week label follows the selected week.");
            Assert(state.Tasks.Count == 7 && state.Tasks.All(t => t.IsDaily && !t.IsCompleted),
                "A new future week starts with seven pending daily tasks.");
            Assert(!state.Tasks.Any(t => baseIds.Contains(t.Id)),
                "Future daily tasks are independent copies; ordinary tasks are not copied.");
            Assert(panels.All(p => p.Children.OfType<TaskCard>().Count() == 1 &&
                p.Children.OfType<TaskCard>().Single().Ordinal == 1),
                "The future week's daily tasks render and number independently.");
            AssertTaskOnlyBoard(window, panels, state);
            Invoke(window, "AddTask", 4);
            var futureOrdinary = panels[4].Children.OfType<TaskCard>().Single(t => !t.Item.IsDaily);
            Field<TextBox>(futureOrdinary, "editor").Text = "Future planning 下周计划";
            futureOrdinary.CommitEdit();
            string futureId = futureOrdinary.Item.Id;
            string[] futureIds = state.Tasks.Select(t => t.Id).OrderBy(id => id).ToArray();
            Assert(state.Tasks.Count == 8 && state.Tasks.Count(t => t.IsDaily) == 7,
                "An ordinary task can be planned in the future week.");

            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(WeeklyPlanner.CurrentWeekStart(state) == baseWeek &&
                state.Tasks.Select(t => t.Id).OrderBy(id => id).SequenceEqual(baseIds),
                "Returning to this week restores its original eight tasks.");
            Assert(state.Tasks.Single(t => t.Id == completedId).IsCompleted &&
                state.Tasks.Single(t => t.Id == normalId).Text == "Review saved before apply",
                "Weekly history preserves completion and the original ordinary task.");
            Assert(Field<TextBlock>(window, "week").Text == baseLabel && !state.Tasks.Any(t => t.Id == futureId),
                "Returning restores the week label and keeps future plans out of this week.");

            Invoke(window, "NavigateWeek", futureWeek.AddDays(3));
            Assert(state.Tasks.Select(t => t.Id).OrderBy(id => id).SequenceEqual(futureIds),
                "Revisiting a planned week restores its existing tasks without creating copies.");
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(WeeklyPlanner.CurrentWeekStart(state) == baseWeek,
                "The previous-week control returns from the future plan to this week's history.");
            Invoke(window, "NavigateWeek", baseWeek.AddDays(2));
            Assert(WeeklyPlanner.CurrentWeekStart(state) == baseWeek && state.Tasks.Count == 8,
                "A date within a week selects that week's original board.");
            Invoke(window, "SaveNow");
            var archived = store.Load();
            string baseKey = baseWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string futureKey = futureWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert(archived.Weeks.Any(w => w.WeekStart == baseKey && w.Tasks.Count == 8) &&
                archived.Weeks.Any(w => w.WeekStart == futureKey && w.Tasks.Count == 8),
                "Saving persists both the historical board and future plan.");
            WeeklyPlanner.Initialize(archived, DateTime.Today);
            WeeklyPlanner.SelectWeek(archived, futureWeek, DateTime.Today);
            Assert(archived.Tasks.Select(t => t.Id).OrderBy(id => id).SequenceEqual(futureIds) &&
                archived.Tasks.Single(t => t.Id == futureId).Text == "Future planning 下周计划" &&
                archived.Tasks.Where(t => t.IsDaily).All(t => !t.IsCompleted),
                "Reloading restores the future ordinary task and independent pending daily copies.");
            WeeklyPlanner.SelectWeek(archived, baseWeek, DateTime.Today);
            Assert(archived.Tasks.Select(t => t.Id).OrderBy(id => id).SequenceEqual(baseIds) &&
                archived.Tasks.Single(t => t.Id == completedId).IsCompleted,
                "Reloading historical weeks retains their task identities and completion.");
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T) yield return (T)parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }

        private static T Field<T>(object instance, string name)
        { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
        private static TaskDragController Drag(MainWindow window)
        { return Field<TaskDragController>(window, "dragController"); }
        private static void SetField<T>(object instance, string name, T value)
        { instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value); }
        private static object Invoke(object instance, string name, params object[] args)
        { return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args); }
        private static void Assert(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
    }
}
