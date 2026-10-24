using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBoard.Core;
using TaskBoard.Persistence;
using TaskBoard.Rendering;
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
                AssertBottomAppending(Path.Combine(directory, "bottom-append"));
                AssertBottomAppending(Path.Combine(directory, "bottom-append-minimum"), true);
                AssertMoveToNextWeek(Path.Combine(directory, "move-next-week"));
                AssertRolloverWhileOpen(Path.Combine(directory, "live-rollover"));
                Layout(window);
                var state = Field<BoardState>(window, "state");
                var panels = Field<ColumnPanel[]>(window, "columns");
                Assert(panels.Length == 7, "Seven day columns.");
                AssertEnglishChrome(window);
                AssertTaskOnlyBoard(window, panels, state);
                Assert(!Descendants<Button>((DependencyObject)window.Content).Any(b => AutomationProperties.GetName(b).Contains("Add task")), "Weekday headers have no add buttons.");
                var card = panels[0].Children.OfType<TaskCard>().Single();
                Assert(card.Margin.Left == 0 && card.Margin.Right == 0 && card.CornerRadius.TopLeft == 0, "Task box has no horizontal inset or rounded corners.");
                Assert(Math.Abs(card.ActualWidth - panels[0].ActualWidth) < 1, "Task box fills the column width.");
                card.BeginEdit();
                var activeEditor = Field<TextBox>(card, "editor");
                activeEditor.Text = "Design 编辑中的任务";
                Assert(ReferenceEquals(activeEditor.ContextMenu, card.ContextMenu), "Active text editor exposes the task context menu instead of its default text menu.");
                var apply = activeEditor.ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Apply to every day");
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
                Assert(panels[0].Children.OfType<TaskCard>().Last() == normal && panels[0].Children.OfType<TaskCard>().First().Item.IsDaily, "New ordinary task appears below the existing daily task.");
                Assert(panels[0].Children.OfType<TaskCard>().Select(t => t.Ordinal).SequenceEqual(new[] { 1, 2 }) && panels[1].Children.OfType<TaskCard>().Single().Ordinal == 1, "Numbering follows order and resets each day.");

                var routine = panels[0].Children.OfType<TaskCard>().Single(t => t.Item.IsDaily);
                string completedId = routine.Item.Id;
                Descendants<Button>(routine).Single(t => AutomationProperties.GetName(t) == "Complete")
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
                var completionBackground = completed.Background as SolidColorBrush;
                Assert(completionBackground != null && completionBackground.Color == Color.FromRgb(230, 230, 230),
                    "Completed task block uses a medium-light gray background.");
                Assert(Field<TextBlock>(completed, "display").TextDecorations == null ||
                    Field<TextBlock>(completed, "display").TextDecorations.Count == 0,
                    "Completed task text has no strike-through.");

                string normalId = normal.Item.Id;
                normal = panels[0].Children.OfType<TaskCard>().Single(t => !t.Item.IsDaily);
                normal.BeginEdit();
                Field<TextBox>(normal, "editor").Text = "Review saved before apply";
                var routineMenu = panels[1].Children.OfType<TaskCard>().Single(t => t.Item.IsDaily).ContextMenu;
                routineMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Apply to every day")
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
                routineMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Apply to every day")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(!state.Tasks.Any(t => string.IsNullOrWhiteSpace(t.Text)), "Applying another task removes the pending blank new task.");
                Assert(!panels.SelectMany(p => p.Children.OfType<TaskCard>()).Any(t => string.IsNullOrWhiteSpace(t.Item.Text)),
                    "No stale blank card remains after the board rebuild.");

                Invoke(window, "SaveNow");
                var loaded = store.Load();
                Assert(loaded.Tasks.Count == 8 && loaded.Tasks.Count(t => t.IsDaily) == 7, "Daily tasks survive UI save.");
                Assert(loaded.Tasks.Single(t => t.Id == completedId).IsCompleted, "Independent completion state survives UI save.");
                AssertTaskBackgroundColors(window, store, state, panels, normalId, completedId);
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

        private static void AssertBottomAppending(string directory, bool minimumSize = false)
        {
            var initial = new BoardState
            {
                Tasks = new List<TaskItem>
                {
                    new TaskItem { Id = "done-normal", Text = "Completed ordinary task", Column = 1, IsCompleted = true },
                    new TaskItem { Id = "first-normal", Text = "First pending task", Column = 1 },
                    new TaskItem { Id = "pending-daily", Text = "Pending daily task", Column = 1, DailyGroupId = "routine-a" },
                    new TaskItem { Id = "last-normal", Text = "Last existing pending task", Column = 1 },
                    new TaskItem { Id = "done-daily", Text = "Completed daily task", Column = 1, DailyGroupId = "routine-b", IsCompleted = true },
                    new TaskItem { Id = "other-day", Text = "Another day", Column = 2 }
                }
            };
            var store = new BoardStore(directory);
            store.Save(initial);
            MainWindow window = null;
            try
            {
                window = new MainWindow(store);
                if (minimumSize) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
                Layout(window);
                var state = Field<BoardState>(window, "state");
                var panels = Field<ColumnPanel[]>(window, "columns");
                foreach (var panel in panels) panel.Animate = false;
                var expected = new List<string> { "done-normal", "done-daily", "first-normal", "pending-daily", "last-normal" };
                AssertDayOrder(state, panels[0], expected, "Opening a mixed day preserves pending order and keeps completed tasks first.");
                for (int index = 0; index < 3; index++)
                {
                    ClickBlankDay(window, 0);
                    var draft = panels[0].Children.OfType<TaskCard>().Last();
                    Assert(draft.IsEditing && string.IsNullOrEmpty(draft.Item.Text) && draft.Ordinal == expected.Count + 1,
                        "Every blank-cell click immediately opens the new editor at the absolute bottom.");
                    Assert(state.Tasks.Last(t => t.Column == 1) == draft.Item,
                        "The model and visible column agree on the draft's bottom position.");
                    Field<TextBox>(draft, "editor").Text = "Synthetic appended task " + (index + 1);
                    draft.CommitEdit();
                    expected.Add(draft.Item.Id);
                    AssertDayOrder(state, panels[0], expected, "Committing an appended task retains the mixed order.");
                    Invoke(window, "RenderTasks");
                    AssertDayOrder(state, panels[0], expected, "Rebuilding the board never groups daily tasks below new additions.");
                    Invoke(window, "SaveNow");
                    Assert(store.Load().Tasks.Where(t => t.Column == 1).Select(t => t.Id).SequenceEqual(expected),
                        "Saving and reloading preserve each repeated bottom addition.");
                }

                ClickBlankDay(window, 0);
                var blank = panels[0].Children.OfType<TaskCard>().Last();
                Invoke(window, "SaveNow");
                Assert(store.Load().Tasks.Where(t => t.Column == 1).Select(t => t.Id).SequenceEqual(expected),
                    "Autosave excludes an empty bottom draft without disturbing the committed order.");
                blank.CancelEdit();
                AssertDayOrder(state, panels[0], expected, "Cancelling a bottom draft leaves every committed task in place.");

                var undo = Field<Stack<TaskUndoEntry>>(window, "undo");
                undo.Pop().Restore(state);
                expected.RemoveAt(expected.Count - 1);
                Invoke(window, "RenderTasks");
                Invoke(window, "QueueSave");
                Invoke(window, "SaveNow");
                AssertDayOrder(state, panels[0], expected, "Undoing the latest addition restores the exact preceding mixed order.");

                window.Close();
                window = new MainWindow(store);
                if (minimumSize) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
                Layout(window);
                state = Field<BoardState>(window, "state");
                panels = Field<ColumnPanel[]>(window, "columns");
                foreach (var panel in panels) panel.Animate = false;
                AssertDayOrder(state, panels[0], expected, "Reopening the application preserves the undone bottom-add order.");

                var daily = panels[0].Children.OfType<TaskCard>().Single(card => card.Item.Id == "pending-daily");
                daily.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Apply to every day")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                expected.Remove("pending-daily");
                expected.Add("pending-daily");
                AssertDayOrder(state, panels[0], expected, "Applying a pending daily task appends it at that moment without reordering its peers.");

                Layout(window);
                daily = panels[0].Children.OfType<TaskCard>().Single(card => card.Item.Id == "pending-daily");
                SetPointer(window, daily.TranslatePoint(new Point(12, 12), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "StartDragVisuals", daily);
                SetPointer(window, panels[0].TranslatePoint(new Point(12, 2), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "UpdateDropTarget");
                Assert(((TaskCard)Field<Border>(Drag(window), "ghost").Child).Ordinal == 3,
                    "A pending daily task can move before ordinary pending tasks while completed tasks remain first.");
                Invoke(Drag(window), "FinishDrag", true);
                Invoke(Drag(window), "CompleteDropAnimation");
                expected.Remove("pending-daily");
                expected.Insert(2, "pending-daily");
                AssertDayOrder(state, panels[0], expected, "A daily drag retains its chosen place among ordinary pending tasks.");

                Layout(window);
                var normal = panels[0].Children.OfType<TaskCard>().Single(card => card.Item.Id == "first-normal");
                SetPointer(window, normal.TranslatePoint(new Point(12, 12), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "StartDragVisuals", normal);
                var scroller = Field<ScrollViewer[]>(window, "scrollers")[1];
                // On small desktops the final task can extend below the
                // viewport. Scroll to its actual end before targeting its tail.
                scroller.ScrollToEnd();
                Layout(window);
                SetPointer(window, scroller.TranslatePoint(new Point(12, scroller.ActualHeight - 4), Field<Grid>(window, "surface")));
                Invoke(Drag(window), "UpdateDropTarget");
                Assert(Field<int>(Drag(window), "targetColumn") == 1 &&
                    ((TaskCard)Field<Border>(Drag(window), "ghost").Child).Ordinal == 3,
                    "An ordinary cross-day drop can target the bottom below a daily task. Target=" +
                    Field<int>(Drag(window), "targetColumn") + ", ordinal=" +
                    ((TaskCard)Field<Border>(Drag(window), "ghost").Child).Ordinal + ", viewport=" +
                    scroller.ViewportHeight + ", extent=" + scroller.ExtentHeight + ", offset=" + scroller.VerticalOffset);
                Invoke(Drag(window), "FinishDrag", true);
                Invoke(Drag(window), "CompleteDropAnimation");
                Assert(panels[1].Children.OfType<TaskCard>().Last().Item.Id == "first-normal" &&
                    state.Tasks.Last(t => t.Column == 2).Id == "first-normal",
                    "Dropping below a daily task preserves the same bottom position in the view and model.");
            }
            finally { if (window != null) window.Close(); }
        }

        private static void AssertMoveToNextWeek(string directory)
        {
            DateTime today = new DateTime(2026, 10, 15);
            DateTime monday = WeeklyPlanner.MondayOf(today);
            string sourceKey = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string targetKey = monday.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var source = new WeekPlan { WeekStart = sourceKey };
            source.Tasks.Add(new TaskItem { Id = "move-normal", Text = "Synthetic task", Column = 3, Color = "#426787", BackgroundColor = "#E4EFFA" });
            source.Tasks.Add(new TaskItem { Id = "move-complete", Text = "Completed synthetic task", Column = 2, IsCompleted = true });
            for (int day = 1; day <= 7; day++) source.Tasks.Add(new TaskItem
            { Id = "move-daily-" + day, Text = "Synthetic daily task", Column = day, DailyGroupId = "synthetic-group" });
            var target = new WeekPlan { WeekStart = targetKey };
            target.Tasks.Add(new TaskItem { Id = "existing-sunday", Text = "Existing Sunday plan", Column = 7, BackgroundColor = "#DCF0E7" });
            var store = new BoardStore(directory);
            store.Save(new BoardState { ActiveWeekStart = sourceKey, Tasks = source.Tasks, Weeks = new List<WeekPlan> { source, target } });
            MainWindow window = null;
            try
            {
                window = new MainWindow(store, () => today);
                Layout(window);
                var state = Field<BoardState>(window, "state");
                var panels = Field<ColumnPanel[]>(window, "columns");
                var undo = Field<Stack<TaskUndoEntry>>(window, "undo");
                var card = FindCard(panels, "move-normal");
                var move = MoveMenu(card);
                Assert(move.IsEnabled && card.CanMoveToNextWeek && (string)move.ToolTip == "Move this task to Sunday of the following week",
                    "Right-click offers a clearly labelled English next-week action with an explicit Sunday destination.");
                card.BeginEdit();
                Field<TextBox>(card, "editor").Text = "Edited before moving";
                Assert(ReferenceEquals(Field<TextBox>(card, "editor").ContextMenu, card.ContextMenu),
                    "The next-week action is also available from the active task editor.");
                MoveMenu(card).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var destination = state.Weeks.Single(plan => plan.WeekStart == targetKey);
                var moved = destination.Tasks.Single(task => task.Id == "move-normal");
                Assert(state.ActiveWeekStart == sourceKey && !state.Tasks.Any(task => task.Id == moved.Id) &&
                    moved.Column == 7 && moved.Text == "Edited before moving" && moved.Color == "#426787" && moved.BackgroundColor == "#E4EFFA" && !moved.IsCompleted,
                    "The menu commits active text and moves the same task, colors and state to the following Sunday without switching the view.");
                Assert(destination.Tasks.Where(task => task.Column == 7).Select(task => task.Id).SequenceEqual(new[] { "existing-sunday", "move-normal" }),
                    "Moving to an existing target appends after its pending Sunday tasks.");
                Assert(undo.Count == 2 && undo.Peek().DestinationWeekStart == targetKey && undo.Peek().DestinationBefore.Tasks.Single().Id == "existing-sunday",
                    "The move captures both weeks in one undo entry after committing the text edit.");
                Invoke(window, "SaveNow");
                var saved = store.Load();
                Assert(!saved.Tasks.Any(task => task.Id == moved.Id) && saved.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.Single(task => task.Id == moved.Id).Text == moved.Text,
                    "Saving persists the cross-week move without duplicating it in the source board.");
                Invoke(window, "UndoTasks");
                Assert(state.Tasks.Single(task => task.Id == moved.Id).Text == "Edited before moving" &&
                    state.Tasks.Single(task => task.Id == moved.Id).Column == 3 &&
                    state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.Select(task => task.Id).SequenceEqual(new[] { "existing-sunday" }),
                    "The Ctrl+Z command restores the source and the destination together, retaining the committed text edit.");

                card = FindCard(panels, "move-daily-3");
                MoveMenu(card).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                destination = state.Weeks.Single(plan => plan.WeekStart == targetKey);
                Assert(destination.Tasks.Single(task => task.Id == "move-daily-3").DailyGroupId == null &&
                    state.Tasks.Count(task => task.DailyGroupId == "synthetic-group") == 6 &&
                    state.Tasks.Where(task => task.DailyGroupId == "synthetic-group").All(task => task.Column != 3),
                    "Moving one daily occurrence detaches and moves only that selected copy; its six peers remain in place.");
                Assert(destination.Tasks.Single(task => task.Id == "existing-sunday").BackgroundColor == "#DCF0E7",
                    "A single daily move leaves existing destination tasks and their fill unchanged.");
                Invoke(window, "UndoTasks");
                Assert(state.Tasks.Count(task => task.DailyGroupId == "synthetic-group") == 7 &&
                    !state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.Any(task => task.Id == "move-daily-3"),
                    "Undo restores the daily link and removes the moved occurrence from the destination.");

                MoveMenu(FindCard(panels, "move-complete")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.First().Id == "move-complete" &&
                    state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.First().IsCompleted,
                    "Explicitly moving a completed task preserves completion and the destination's completed-first ordering.");
                Invoke(window, "UndoTasks");
                card = FindCard(panels, "move-normal");
                card.BeginEdit();
                Field<TextBox>(card, "editor").Text = "   ";
                MoveMenu(card).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(!state.Tasks.Any(task => task.Id == "move-normal") &&
                    !state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.Any(task => task.Id == "move-normal"),
                    "Moving a cleared existing editor applies its deletion without moving the stale original text.");
                Invoke(window, "UndoTasks");

                int undoBeforeDraft = undo.Count;
                Invoke(window, "AddTask", 0);
                card = panels[0].Children.OfType<TaskCard>().Single(task => string.IsNullOrWhiteSpace(task.Item.Text));
                string draftId = card.Item.Id;
                MoveMenu(card).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(!state.Tasks.Any(task => task.Id == draftId) &&
                    !state.Weeks.SelectMany(plan => plan.Tasks).Any(task => task.Id == draftId) && undo.Count == undoBeforeDraft,
                    "The next-week action discards a blank new draft without creating work or an undo entry.");
                Invoke(window, "AddTask", 0);
                card = panels[0].Children.OfType<TaskCard>().Single(task => string.IsNullOrWhiteSpace(task.Item.Text));
                Field<TextBox>(card, "editor").Text = "Synthetic new task moved directly";
                draftId = card.Item.Id;
                MoveMenu(card).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.Any(task => task.Id == draftId && task.Column == 7) &&
                    !state.Tasks.Any(task => task.Id == draftId), "A nonblank new editor can commit and move directly through the context menu.");
                Invoke(window, "SaveNow");
                window.Close();
                window = new MainWindow(store, () => today);
                state = Field<BoardState>(window, "state");
                Assert(!state.Tasks.Any(task => task.Id == draftId) &&
                    state.Weeks.Single(plan => plan.WeekStart == targetKey).Tasks.Count(task => task.Id == draftId) == 1,
                    "Reopening preserves a manual move exactly once in the planned week.");
            }
            finally { if (window != null) window.Close(); }

            var freshStore = new BoardStore(Path.Combine(directory, "new-target"));
            freshStore.Save(new BoardState { Tasks = new List<TaskItem> { new TaskItem { Id = "new-target-task", Text = "Synthetic task for an empty future", Column = 1 } } });
            window = null;
            try
            {
                window = new MainWindow(freshStore, () => today);
                var state = Field<BoardState>(window, "state");
                MoveMenu(FindCard(Field<ColumnPanel[]>(window, "columns"), "new-target-task")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(state.Weeks.Count == 2 && state.Tasks.Count == 0, "A manual move creates its missing destination week.");
                Invoke(window, "UndoTasks");
                Assert(state.Weeks.Count == 1 && state.Tasks.Single().Id == "new-target-task",
                    "Undo removes a destination created solely for the move and restores the source task.");
            }
            finally { if (window != null) window.Close(); }

            var disabled = new TaskCard(new TaskItem { Text = "Synthetic boundary task" }) { CanMoveToNextWeek = false };
            int disabledRequests = 0;
            disabled.MoveToNextWeekRequested += (s, e) => disabledRequests++;
            MoveMenu(disabled).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(!MoveMenu(disabled).IsEnabled && disabledRequests == 0,
                "A disabled next-week menu remains discoverable but cannot move beyond the supported date range.");
        }

        private static void AssertRolloverWhileOpen(string directory)
        {
            // Test the date timer's real handler with a local clock and isolated
            // stores; no machine date changes or desktop input are required.
            foreach (int selectedOffset in new[] { -7, 0, 7 })
            {
                DateTime now = new DateTime(2026, 10, 18, 23, 59, 0);
                DateTime oldMonday = WeeklyPlanner.MondayOf(now);
                string oldKey = oldMonday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string newKey = oldMonday.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var store = new BoardStore(Path.Combine(directory, "selected-" + selectedOffset));
                store.Save(new BoardState { Tasks = new List<TaskItem>
                {
                    new TaskItem { Id = "roll-pending", Text = "Synthetic unfinished work", Column = 2, Color = "#426787", BackgroundColor = "#E4EFFA" },
                    new TaskItem { Id = "roll-done", Text = "Synthetic completed work", Column = 1, IsCompleted = true }
                } });
                MainWindow window = null;
                try
                {
                    window = new MainWindow(store, () => now);
                    Layout(window);
                    var state = Field<BoardState>(window, "state");
                    if (selectedOffset != 0) Invoke(window, "NavigateWeek", oldMonday.AddDays(selectedOffset));
                    Invoke(window, "AddTask", 4);
                    var draft = Field<ColumnPanel[]>(window, "columns")[4].Children.OfType<TaskCard>().Last();
                    Field<TextBox>(draft, "editor").Text = "Synthetic edit at week boundary";
                    string editedId = draft.Item.Id;
                    now = now.AddMinutes(2);
                    Invoke(window, "UpdateDate");
                    string expectedKey = (selectedOffset == 0 ? oldMonday.AddDays(7) : oldMonday.AddDays(selectedOffset))
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Assert(state.ActiveWeekStart == expectedKey,
                        "A week boundary follows the current view while preserving an explicitly selected historical or future week.");
                    var oldWeek = state.Weeks.Single(plan => plan.WeekStart == oldKey);
                    var newWeek = state.Weeks.Single(plan => plan.WeekStart == newKey);
                    Assert(oldWeek.Tasks.Single().Id == "roll-done" && newWeek.Tasks.Count(task => task.Id == "roll-pending") == 1 &&
                        newWeek.Tasks.Single(task => task.Id == "roll-pending").Column == 7 &&
                        newWeek.Tasks.Single(task => task.Id == "roll-pending").BackgroundColor == "#E4EFFA",
                        "The live date handler carries unfinished work once to the new Sunday and retains completed history and task colors.");
                    var edited = state.Weeks.SelectMany(plan => plan.Tasks).Single(task => task.Id == editedId);
                    Assert(edited.Text == "Synthetic edit at week boundary" && edited.Column == (selectedOffset <= 0 ? 7 : 5),
                        "The date transition commits an active draft before carrying expired work; future plans keep their chosen day.");
                    Assert(Field<Stack<TaskUndoEntry>>(window, "undo").Count == 0 && Field<TaskCard>(window, "editing") == null,
                        "A week transition clears stale undo history and finishes the active editor.");
                    Invoke(window, "UpdateDate");
                    Assert(state.Weeks.SelectMany(plan => plan.Tasks).Count(task => task.Id == "roll-pending") == 1,
                        "Repeated date timer ticks never duplicate carried work.");
                    Invoke(window, "SaveNow");
                    var saved = store.Load();
                    Assert(saved.Weeks.Single(plan => plan.WeekStart == oldKey).Tasks.Single().IsCompleted &&
                        saved.Weeks.Single(plan => plan.WeekStart == newKey).Tasks.Any(task => task.Id == "roll-pending"),
                        "The automatic carry and completed historical tasks persist together.");
                }
                finally { if (window != null) window.Close(); }
            }
        }

        private static TaskCard FindCard(ColumnPanel[] panels, string id)
        { return panels.SelectMany(panel => panel.Children.OfType<TaskCard>()).Single(card => card.Item.Id == id); }

        private static MenuItem MoveMenu(TaskCard card)
        { return card.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Move to next week"); }

        private static void ClickBlankDay(MainWindow window, int column)
        {
            var cell = Field<Grid>(window, "board").Children.OfType<Border>().Single(element =>
                Grid.GetRow(element) == 1 && Grid.GetColumn(element) == column);
            cell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        }

        private static void AssertDayOrder(BoardState state, ColumnPanel panel, IEnumerable<string> expected, string message)
        {
            string[] ids = expected.ToArray();
            Assert(panel.Children.OfType<TaskCard>().Select(card => card.Item.Id).SequenceEqual(ids) &&
                state.Tasks.Where(task => task.Column == 1).Select(task => task.Id).SequenceEqual(ids) &&
                panel.Children.OfType<TaskCard>().Select(card => card.Ordinal).SequenceEqual(Enumerable.Range(1, ids.Length)), message);
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
            Assert(clearButtons.All(button => AutomationProperties.GetName(button) == "Clear tasks for this day") &&
                clearButtons.All(button => Descendants<TextBlock>(button).Any(text => text.Text == "Clear day")),
                "Each clear region uses a short English label and a descriptive English accessible name.");
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

        private static void AssertEnglishChrome(MainWindow window)
        {
            Assert(string.Equals(window.Language.IetfLanguageTag, "en-US", StringComparison.OrdinalIgnoreCase),
                "The application chrome uses English independently of the Windows language.");
            var board = Field<Grid>(window, "board");
            var headers = board.Children.OfType<Border>().Where(element => Grid.GetRow(element) == 0)
                .OrderBy(element => Grid.GetColumn(element)).ToArray();
            string[] names = headers.Select(element => Descendants<TextBlock>(element).First().Text).ToArray();
            Assert(names.SequenceEqual(BoardLabels.DayNames), "All seven weekday headers use the shared English labels.");
            DateTime selectedMonday = WeeklyPlanner.CurrentWeekStart(Field<BoardState>(window, "state"));
            for (int column = 0; column < headers.Length; column++)
            {
                string date = Descendants<TextBlock>(headers[column]).Skip(2).Single().Text;
                Assert(date == BoardLabels.Date(selectedMonday.AddDays(column)), "Each weekday header shows its calendar date.");
            }
            var cells = Field<Border[]>(window, "dayCells");
            int todayColumn = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            for (int column = 0; column < cells.Length; column++)
            {
                var fill = cells[column].Background as SolidColorBrush;
                bool highlighted = fill != null && fill.Color == Color.FromRgb(240, 251, 224);
                Assert(highlighted == (selectedMonday == WeeklyPlanner.MondayOf(DateTime.Today) && column == todayColumn),
                    "Only today's column uses the pale green highlight.");
                string todayLabel = Descendants<TextBlock>(headers[column]).Skip(1).First().Text;
                Assert(todayLabel == ((selectedMonday == WeeklyPlanner.MondayOf(DateTime.Today) && column == todayColumn) ? "Today" : string.Empty),
                    "Today appears above the current weekday only.");
            }
            var current = Descendants<Button>((DependencyObject)window.Content)
                .Single(button => AutomationProperties.GetName(button) == "This week");
            Assert((string)current.Content == "This week", "The current-week action clearly says This week instead of using a home symbol.");
            Assert(Field<TextBlock>(window, "week").Text == BoardLabels.Week(WeeklyPlanner.CurrentWeekStart(Field<BoardState>(window, "state"))),
                "The selected ISO week remains visible in the toolbar.");
            string remaining = Field<TextBlock>(window, "remaining").Text;
            Assert(remaining.StartsWith("Month left ", StringComparison.Ordinal) && remaining.Contains("%   /   Year left ") && remaining.EndsWith("%", StringComparison.Ordinal),
                "The month and year remaining percentages stay visible with explicit English labels.");

            double width = window.Width, height = window.Height;
            try
            {
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                Layout(window);
                var root = (UIElement)window.Content;
                var metrics = Field<TextBlock>(window, "remaining");
                Assert(current.TranslatePoint(new Point(current.ActualWidth, 0), root).X <=
                    metrics.TranslatePoint(new Point(), root).X + 1,
                    "The This week control and remaining percentages do not overlap at the minimum window width.");
                var footer = Field<DayClearButton[]>(window, "clearButtons")[0];
                var label = Descendants<TextBlock>(footer).Single(text => text.Text == "Clear day");
                Assert(label.DesiredSize.Width + 12 <= footer.ActualWidth + 1,
                    "The English clear label fits the narrowest day column.");
                Invoke(footer, "SetRevealed", true, false);
                SavePreview(window, "minimum-board-preview.png");
                Invoke(footer, "SetRevealed", false, false);
            }
            finally
            {
                window.Width = width;
                window.Height = height;
                Layout(window);
            }

            var confirmation = BoardDialog.Create(null, "Clear synthetic tasks?", "Clear day", "Clear");
            try
            {
                var actions = Descendants<Button>((DependencyObject)confirmation.Content).ToArray();
                Assert(actions.Length == 2 && actions.Any(button => (string)button.Content == "Clear") &&
                    actions.Any(button => (string)button.Content == "Cancel" && button.IsDefault && button.IsCancel),
                    "The English clear confirmation defaults to Cancel.");
            }
            finally { confirmation.Close(); }
        }

        private static void AssertDragPreviewAndDrop(MainWindow window, BoardState state,
            ColumnPanel[] panels, string ordinaryId, string completedId)
        {
            var originalTasks = BoardOperations.CloneTasks(state.Tasks);
            var undo = Field<Stack<TaskUndoEntry>>(window, "undo");
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
                Assert(card.Item.BackgroundColor == "#FFF4CC" &&
                    ((SolidColorBrush)preview.Background).Color == ((SolidColorBrush)card.Background).Color,
                    "The live drag preview preserves the full task background fill.");
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
                Assert(landed.Item.BackgroundColor == "#FFF4CC" &&
                    ((SolidColorBrush)landed.Background).Color == Color.FromRgb(255, 244, 204),
                    "Dropping a filled task preserves its background color.");
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

        private static void AssertTaskBackgroundColors(MainWindow window, BoardStore store, BoardState state,
            ColumnPanel[] panels, string ordinaryId, string dailyId)
        {
            var undo = Field<Stack<TaskUndoEntry>>(window, "undo");
            var card = panels.SelectMany(p => p.Children.OfType<TaskCard>()).Single(c => c.Item.Id == ordinaryId);
            string textColor = card.Item.Color;
            int beforeColor = undo.Count;
            var palette = OpenTaskPalette(card, true);
            Assert(card.IsPaletteOpen && card.ContextMenu.Items.OfType<MenuItem>().Any(i => (string)i.Header == "Background color"),
                "The task context menu opens the background-color palette.");
            ChooseSwatch(palette, "#FFF4CC");
            Assert(card.Item.BackgroundColor == "#FFF4CC" && card.Item.Color == textColor &&
                ((SolidColorBrush)card.Background).Color == Color.FromRgb(255, 244, 204),
                "A preset fills the task border and padding without changing its text color.");
            Assert(undo.Count == beforeColor + 1 && undo.Peek().Tasks.Single(t => t.Id == ordinaryId).BackgroundColor == null,
                "Changing a background records its original unfilled state for undo.");
            Invoke(window, "SaveNow");
            Assert(store.Load().Tasks.Single(t => t.Id == ordinaryId).BackgroundColor == "#FFF4CC",
                "The selected task fill survives a UI save.");

            undo.Pop().Restore(state);
            Invoke(window, "RenderTasks");
            Invoke(window, "QueueSave");
            card = panels.SelectMany(p => p.Children.OfType<TaskCard>()).Single(c => c.Item.Id == ordinaryId);
            Assert(card.Item.BackgroundColor == null && ((SolidColorBrush)card.Background).Color == Colors.Transparent,
                "Applying the undo snapshot restores the task's unfilled appearance.");
            palette = OpenTaskPalette(card, true);
            var hex = Descendants<TextBox>(palette.Child).Single();
            hex.Text = "#ZZZZZZ";
            PressEnter(hex);
            Assert(card.Item.BackgroundColor == null && card.IsPaletteOpen,
                "Invalid custom fill leaves the task unchanged and the palette open.");
            hex.Text = "#e4effa";
            PressEnter(hex);
            Assert(card.Item.BackgroundColor == "#E4EFFA" && !card.IsPaletteOpen,
                "Custom hex fill is validated, normalized and applied through the palette editor.");
            ChooseSwatch(OpenTaskPalette(card, true), "No fill");
            Assert(card.Item.BackgroundColor == null, "The palette can clear a task's background fill.");
            Invoke(window, "SaveNow");
            Assert(store.Load().Tasks.Single(t => t.Id == ordinaryId).BackgroundColor == null,
                "Clearing a fill saves the unfilled state.");
            ChooseSwatch(OpenTaskPalette(card, true), "#FFF4CC");

            int beforeCancel = undo.Count;
            string originalText = card.Item.Text;
            card.BeginEdit();
            var editor = Field<TextBox>(card, "editor");
            editor.Text = originalText + " pending";
            Assert(((SolidColorBrush)card.Background).Color == Color.FromRgb(255, 244, 204),
                "Editing/focus styling retains the chosen background fill.");
            Assert(ReferenceEquals(editor.ContextMenu, card.ContextMenu),
                "An active editor exposes the same background-color action.");
            ChooseSwatch(OpenTaskPalette(card, true), "#DCF0E7");
            Assert(card.IsEditing && editor.Text == originalText + " pending" && card.Item.BackgroundColor == "#DCF0E7",
                "Choosing a fill during editing preserves the pending text and edit session.");
            card.CancelEdit();
            Assert(card.Item.Text == originalText && card.Item.BackgroundColor == "#FFF4CC" && undo.Count == beforeCancel,
                "Cancelling an edit restores the original text and fill without adding undo entries.");

            Invoke(window, "AddTask", 3);
            var draft = panels[3].Children.OfType<TaskCard>().Single(c => string.IsNullOrWhiteSpace(c.Item.Text));
            string draftId = draft.Item.Id;
            ChooseSwatch(OpenTaskPalette(draft, true), "#FCE4E4");
            Assert(draft.IsEditing && state.Tasks.Contains(draft.Item) && draft.Item.BackgroundColor == "#FCE4E4",
                "Choosing a fill for a blank new draft does not discard it.");
            Invoke(window, "SaveNow");
            Assert(!store.Load().Tasks.Any(t => t.Id == draftId),
                "A draft's pending fill does not cause an empty task to be autosaved.");
            draft.CancelEdit();
            Assert(!state.Tasks.Any(t => t.Id == draftId), "Cancelling a filled blank draft removes it cleanly.");

            var daily = panels.SelectMany(p => p.Children.OfType<TaskCard>()).Single(c => c.Item.Id == dailyId);
            ChooseSwatch(OpenTaskPalette(daily, true), "#E8F0DD");
            Assert(state.Tasks.Where(t => t.IsDaily && t.Id != dailyId).All(t => t.BackgroundColor == null),
                "A daily task's background remains independent from its copies on other days.");
            var peer = panels.SelectMany(p => p.Children.OfType<TaskCard>()).First(c => c.Item.IsDaily && c.Item.Id != dailyId);
            ChooseSwatch(OpenTaskPalette(peer, false), "#785C93");
            Assert(state.Tasks.Where(t => t.IsDaily).All(t => t.Color == "#785C93") &&
                daily.Item.BackgroundColor == "#E8F0DD" &&
                state.Tasks.Where(t => t.IsDaily && t.Id != dailyId).All(t => t.BackgroundColor == null),
                "Shared daily text-color changes preserve every day's independent fill.");
            Invoke(window, "SaveNow");
            var saved = store.Load();
            Assert(saved.Tasks.Single(t => t.Id == dailyId).BackgroundColor == "#E8F0DD" &&
                saved.Tasks.Where(t => t.IsDaily && t.Id != dailyId).All(t => t.BackgroundColor == null),
                "Independent daily background colors persist without spreading to peer days.");
        }

        private static ColorPalette OpenTaskPalette(TaskCard card, bool background)
        {
            card.ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == (background ? "Background color" : "Text color"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            return Field<ColorPalette>(card, background ? "backgroundPalette" : "palette");
        }

        private static void ChooseSwatch(ColorPalette palette, string name)
        {
            Descendants<Button>(palette.Child).Single(b => AutomationProperties.GetName(b) == name)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private static void PressEnter(TextBox editor)
        {
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(editor) ?? new DetachedPresentationSource(), 0, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }

        private sealed class DetachedPresentationSource : PresentationSource
        {
            public override Visual RootVisual { get; set; }
            public override bool IsDisposed { get { return false; } }
            protected override CompositionTarget GetCompositionTargetCore() { return null; }
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
            var undo = Field<Stack<TaskUndoEntry>>(window, "undo");
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
            Assert(undo.Count == beforeUndo + 1 && undo.Peek().Tasks.Select(task => task.Id).SequenceEqual(allIds),
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

            undo.Pop().Restore(state);
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
            var previous = navigation.Single(b => AutomationProperties.GetName(b) == "Previous week");
            var next = navigation.Single(b => AutomationProperties.GetName(b) == "Next week");
            var current = navigation.Single(b => AutomationProperties.GetName(b) == "This week");
            int undoCount = Field<Stack<TaskUndoEntry>>(window, "undo").Count;
            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(Field<Stack<TaskUndoEntry>>(window, "undo").Count == undoCount, "Returning to an already selected current week preserves undo.");
            Invoke(window, "NavigateWeek", baseWeek.AddDays(3));
            Assert(Field<Stack<TaskUndoEntry>>(window, "undo").Count == undoCount, "Selecting another date in the same week preserves undo.");
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
