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
using TaskBoard.Core;
using TaskBoard.Persistence;
using TaskBoard.Rendering;
using TaskBoard.UI;

namespace TaskBoard.Tests
{
    // Synthetic data and detached WPF trees only; no desktop input or user files.
    internal static class InboxInteractionTests
    {
        private static int checks;

        [STAThread]
        private static int Main()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "test-data-inbox-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                AssertChromeAndEditing(Path.Combine(directory, "editing"));
                AssertScheduleFromEditor(Path.Combine(directory, "schedule"));
                AssertBoardTransfers(Path.Combine(directory, "transfers"));
                AssertWeekIndependence(Path.Combine(directory, "weeks"));
                AssertChronologicalUndo(Path.Combine(directory, "undo"));
                AssertSaveAndClose(Path.Combine(directory, "saving"));
                AssertRollover(Path.Combine(directory, "rollover"));
                Console.WriteLine("Inbox interaction checks passed: " + checks);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void AssertChromeAndEditing(string directory)
        {
            using (var f = new Fixture(directory))
            {
                f.Window.Width = f.Window.MinWidth;
                f.Window.Height = f.Window.MinHeight;
                Layout(f.Window);
                var button = Descendants<Button>((DependencyObject)f.Window.Content)
                    .Single(b => AutomationProperties.GetName(b) == "Inbox");
                Assert(button.Content is System.Windows.Shapes.Path && button.Width <= 28 && button.Height <= 30,
                    "The inbox uses one small icon in the existing header.");
                var metrics = Field<TextBlock>(f.Window, "remaining");
                var root = (FrameworkElement)f.Window.Content;
                Assert(Bounds(metrics, root).Right <= Bounds(button, root).Left + 1,
                    "The inbox icon does not overlap remaining-time metrics at minimum window width.");
                var current = Descendants<Button>(root).Single(b => AutomationProperties.GetName(b) == "This week");
                Assert(Bounds(current, root).Right <= Bounds(metrics, root).Left + 1,
                    "Week navigation and remaining-time metrics stay separate at minimum width.");
                Assert(Field<InboxWindow>(f.Window, "inboxWindow") == null,
                    "The main board does not create or display an inbox until requested.");
                var inbox = f.OpenInbox();
                Layout(inbox);
                Assert(!inbox.ShowInTaskbar && inbox.WindowStartupLocation == WindowStartupLocation.CenterOwner,
                    "The inbox centers on its owner without adding a second taskbar entry.");
                Assert(inbox.FontFamily.Source == f.Window.FontFamily.Source && inbox.FontSize == f.Window.FontSize,
                    "Inbox typography matches the main board.");
                Assert(InboxWindow.GroupNames.SequenceEqual(new[] { "Work 1", "Work 2", "Life" }) &&
                    f.Groups.Length == 3, "The inbox has precisely the three requested groups.");
                foreach (var panel in f.Groups)
                {
                    var card = panel.Children.OfType<TaskCard>().Single();
                    Assert(Math.Abs(card.ActualWidth - panel.ActualWidth) < 1 && card.CornerRadius.TopLeft == 0,
                        "Collected tasks fill each group with the board's square card style.");
                    Assert(Field<FrameworkElement>(card, "grip").Visibility == Visibility.Collapsed &&
                        Field<Button>(card, "completionButton").Visibility == Visibility.Collapsed,
                        "Inbox cards hide board-only drag and completion controls.");
                }
                int history = f.Undo.Count;
                ClickBlank(inbox, "Work 1");
                var draft = f.Groups[0].Children.OfType<TaskCard>().Last();
                Assert(draft.IsEditing && draft.Ordinal == 2 && inbox.HasActiveEdit,
                    "Clicking group whitespace starts a draft at that group's bottom.");
                Editor(draft).Text = "Collected from whitespace";
                ClickBlank(inbox, "Work 1");
                Assert(f.State.Inbox.Count == 5 && f.State.Inbox.Any(i => i.Task.Text == "Collected from whitespace"),
                    "A second blank click commits the previous draft before appending another.");
                Assert(f.Groups[0].Children.OfType<TaskCard>().Select(c => c.Ordinal).SequenceEqual(new[] { 1, 2, 3 }),
                    "Inbox numbering follows append order within its group.");
                f.Groups[0].Children.OfType<TaskCard>().Last().CancelEdit();
                Assert(f.State.Inbox.Count == 4 && f.Undo.Count == history + 1 && !inbox.HasActiveEdit,
                    "Cancelling a blank draft leaves only the committed addition and its undo step.");
                var existing = f.InboxCard("inbox-a");
                existing.BeginEdit();
                Editor(existing).Text = "Cancelled edit";
                Invoke(existing, "ApplyColor", "#FFF4CC", true);
                existing.CancelEdit();
                Assert(existing.Item.Text == "Inbox alpha" && existing.Item.BackgroundColor == null &&
                    f.Undo.Count == history + 1, "Cancelling an existing inbox edit restores text and background without adding undo.");
            }
        }

        private static void AssertScheduleFromEditor(string directory)
        {
            using (var f = new Fixture(directory))
            {
                f.OpenInbox();
                var card = f.InboxCard("inbox-a");
                string[] headers = card.ContextMenu.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToArray();
                Assert(headers.Contains("Schedule for") && headers.Contains("Move to group") &&
                    !headers.Contains("Apply to every day") && !headers.Contains("Move to next week"),
                    "Inbox menus expose scheduling and groups without board-only daily or next-week actions.");
                card.BeginEdit();
                Editor(card).Text = "Edited before scheduling";
                Invoke(card, "ApplyColor", "#FFF4CC", true);
                Assert(ReferenceEquals(Editor(card).ContextMenu, card.ContextMenu),
                    "The editor exposes the same scheduling menu as its card.");
                SelectDestination(card, "Schedule for", 2);
                var scheduled = f.State.Tasks.Single(t => t.Id == "inbox-a");
                Assert(!f.State.Inbox.Any(i => i.Task.Id == scheduled.Id) && scheduled.Column == 3 &&
                    scheduled.Text == "Edited before scheduling" && scheduled.Color == "#426787" &&
                    scheduled.BackgroundColor == "#FFF4CC", "Scheduling commits the edit and moves identity, text and colors to the chosen weekday.");
                Assert(f.BoardCards(2).Last().Item.Id == scheduled.Id && f.BoardCards(2).Last().Ordinal == 2,
                    "A scheduled pending task appears last in the destination day with its next ordinal.");
                Assert(f.Undo.Count == 2, "An edited schedule records the edit and the transfer chronologically.");
                int boardCount = f.State.Tasks.Count;
                var blanked = f.InboxCard("inbox-b");
                blanked.BeginEdit();
                Editor(blanked).Text = "   ";
                SelectDestination(blanked, "Schedule for", 4);
                Assert(f.State.Tasks.Count == boardCount && !f.State.Inbox.Any(i => i.Task.Id == "inbox-b"),
                    "Scheduling an emptied existing task deletes it without adding a blank board task.");
                Invoke(f.Inbox, "AddTask", 2);
                var draft = f.Groups[2].Children.OfType<TaskCard>().Last();
                SelectDestination(draft, "Schedule for", 0);
                Assert(!f.Inbox.HasActiveEdit && f.State.Tasks.Count == boardCount &&
                    f.State.Inbox.All(i => !string.IsNullOrWhiteSpace(i.Task.Text)),
                    "Scheduling a blank new draft cancels it without changing the board.");
                Invoke(f.Window, "SaveNow");
                var saved = f.Store.Load();
                Assert(saved.Tasks.Any(t => t.Id == scheduled.Id && t.Column == 3 && t.BackgroundColor == "#FFF4CC") &&
                    !saved.Inbox.Any(i => i.Task.Id == scheduled.Id), "Scheduling persists both sides of the transfer together.");
            }
        }

        private static void AssertBoardTransfers(string directory)
        {
            using (var f = new Fixture(directory))
            {
                var normal = f.BoardCard("board-a");
                normal.BeginEdit();
                Editor(normal).Text = "Moved from active editor";
                SelectDestination(normal, "Move to inbox", 1);
                var moved = f.State.Inbox.Single(i => i.Task.Id == "board-a");
                Assert(moved.Group == 2 && moved.Task.Text == "Moved from active editor" &&
                    moved.Task.Color == "#426787" && moved.Task.BackgroundColor == "#E4EFFA" &&
                    !f.State.Tasks.Any(t => t.Id == "board-a"), "Board editor transfers commit text and preserve colors in the chosen inbox group.");
                SelectDestination(f.BoardCard("board-done"), "Move to inbox", 2);
                Assert(f.State.Inbox.Single(i => i.Task.Id == "board-done").Task.IsCompleted,
                    "Moving a completed task into the inbox retains its completion state.");
                SelectDestination(f.BoardCard("daily-1"), "Move to inbox", 0);
                Assert(f.State.Inbox.Single(i => i.Task.Id == "daily-1").Task.DailyGroupId == null &&
                    f.State.Tasks.Count(t => t.DailyGroupId == "routine") == 6,
                    "Moving one daily occurrence detaches only that occurrence and preserves its six peers.");
                f.OpenInbox();
                SelectDestination(f.InboxCard("board-done"), "Schedule for", 0);
                Assert(f.BoardCards(0).First().Item.Id == "board-done" &&
                    f.BoardCards(0).First().Item.IsCompleted, "A completed inbox task returns to the completed section of its chosen day.");
                SelectDestination(f.InboxCard("daily-1"), "Schedule for", 6);
                Assert(f.BoardCards(6).Last().Item.Id == "daily-1" &&
                    f.State.Tasks.Single(t => t.Id == "daily-1").DailyGroupId == null,
                    "Rescheduling a collected daily occurrence appends it as an independent task.");
                SelectDestination(f.InboxCard("inbox-a"), "Move to group", 2);
                Assert(f.State.Inbox.Single(i => i.Task.Id == "inbox-a").Group == 3 &&
                    f.Groups[2].Children.OfType<TaskCard>().Last().Item.Id == "inbox-a",
                    "Changing an inbox group appends at the bottom of the selected group.");
            }
        }

        private static void AssertWeekIndependence(string directory)
        {
            using (var f = new Fixture(directory))
            {
                DateTime future = WeeklyPlanner.CurrentWeekStart(f.State).AddDays(14);
                Invoke(f.Window, "NavigateWeek", future);
                string[] originalInbox = f.State.Inbox.Select(i => i.Task.Id).ToArray();
                f.OpenInbox();
                var schedule = Menu(f.InboxCard("inbox-c"), "Schedule for");
                Assert(schedule.Items.Count == 7, "The schedule submenu provides one destination for every weekday.");
                for (int day = 0; day < 7; day++)
                    Assert((string)((MenuItem)schedule.Items[day]).Header == future.AddDays(day)
                        .ToString("ddd · yyyy.MM.dd", CultureInfo.GetCultureInfo("en-US")),
                        "Schedule destinations use actual dates of the currently displayed future week.");
                SelectDestination(f.InboxCard("inbox-c"), "Schedule for", 3);
                Assert(f.State.Tasks.Any(t => t.Id == "inbox-c" && t.Column == 4),
                    "Scheduling while viewing a future week targets that displayed week.");
                f.CloseInbox();
                Invoke(f.Window, "GoToCurrentWeek");
                Assert(!f.State.Tasks.Any(t => t.Id == "inbox-c") &&
                    f.State.Inbox.Select(i => i.Task.Id).SequenceEqual(originalInbox.Where(id => id != "inbox-c")),
                    "Global inbox contents persist independently while scheduled tasks belong to their selected week.");
                f.OpenInbox();
                Assert(f.Groups.Sum(g => g.Children.OfType<TaskCard>().Count()) == 2,
                    "Reopening the inbox in another week displays the same remaining collected tasks.");
            }
        }

        private static void AssertChronologicalUndo(string directory)
        {
            using (var f = new Fixture(directory))
            {
                var board = f.BoardCard("board-a");
                board.BeginEdit();
                Editor(board).Text = "Board edit before collecting";
                board.CommitEdit();
                SelectDestination(board, "Move to inbox", 0);
                f.OpenInbox();
                var collected = f.InboxCard("board-a");
                collected.BeginEdit();
                Editor(collected).Text = "Inbox edit after collecting";
                collected.CommitEdit();
                SelectDestination(collected, "Schedule for", 5);
                Assert(f.Undo.Count == 4 && f.State.Tasks.Any(t => t.Id == "board-a" && t.Column == 6),
                    "Board edits, collection, inbox edits and scheduling share one chronological history.");
                Invoke(f.Window, "UndoTasks");
                Assert(!f.State.Tasks.Any(t => t.Id == "board-a") &&
                    f.InboxCard("board-a").Item.Text == "Inbox edit after collecting",
                    "Undoing scheduling restores the edited task to its inbox group and refreshes both views.");
                Invoke(f.Window, "UndoTasks");
                Assert(f.InboxCard("board-a").Item.Text == "Board edit before collecting",
                    "The next undo restores the inbox edit without losing its earlier board edit.");
                f.CloseInbox();
                Invoke(f.Window, "UndoTasks");
                Assert(f.BoardCard("board-a").Item.Text == "Board edit before collecting" &&
                    !f.State.Inbox.Any(i => i.Task.Id == "board-a"),
                    "Undo after closing the inbox reverses collection on both sides.");
                Invoke(f.Window, "UndoTasks");
                Assert(f.BoardCard("board-a").Item.Text == "Board alpha" && f.Undo.Count == 0,
                    "Closing the inbox preserves the earlier board edit's undo entry.");
            }
        }

        private static void AssertSaveAndClose(string directory)
        {
            using (var f = new Fixture(directory))
            {
                f.OpenInbox();
                var card = f.InboxCard("inbox-a");
                card.BeginEdit();
                Editor(card).Text = "Uncommitted new text";
                Invoke(card, "ApplyColor", "#FFF4CC", true);
                Invoke(f.Window, "QueueSave");
                Invoke(f.Window, "SaveNow");
                var saved = f.Store.Load().Inbox.Single(i => i.Task.Id == "inbox-a").Task;
                Assert(saved.Text == "Inbox alpha" && saved.BackgroundColor == null && f.Inbox.HasActiveEdit,
                    "Autosave uses the committed inbox snapshot during active text or background edits.");
                f.CloseInbox();
                saved = f.Store.Load().Inbox.Single(i => i.Task.Id == "inbox-a").Task;
                Assert(saved.Text == "Uncommitted new text" && saved.BackgroundColor == "#FFF4CC",
                    "Closing the inbox commits and saves the active editor.");
                f.OpenInbox();
                Invoke(f.Inbox, "AddTask", 2);
                var draft = f.Groups[2].Children.OfType<TaskCard>().Last();
                Editor(draft).Text = "Draft not yet committed";
                Invoke(f.Window, "QueueSave");
                Invoke(f.Window, "SaveNow");
                Assert(f.Store.Load().Inbox.Count == 3 && f.Inbox.CommittedItems.Count == 3,
                    "Autosave excludes a new inbox draft even if text has been typed into its editor.");
                draft.CancelEdit();
                var editing = f.InboxCard("inbox-a");
                editing.BeginEdit();
                Editor(editing).Text = "Retained while undo is unavailable";
                int history = f.Undo.Count;
                Invoke(f.Window, "UndoTasks");
                Assert(f.Undo.Count == history && editing.IsEditing,
                    "Board-level undo cannot consume history while the inbox editor owns text input.");
                editing.CancelEdit();
            }
        }

        private static void AssertRollover(string directory)
        {
            using (var f = new Fixture(directory))
            {
                f.OpenInbox();
                var card = f.InboxCard("inbox-a");
                card.BeginEdit();
                Editor(card).Text = "Captured across week boundary";
                f.Now = new DateTime(2026, 10, 19, 0, 1, 0);
                Invoke(f.Window, "UpdateDate");
                Assert(!f.Inbox.HasActiveEdit && f.InboxCard("inbox-a").Item.Text == "Captured across week boundary",
                    "The date timer commits the active inbox edit before moving to a new system week.");
                Assert(WeeklyPlanner.CurrentWeekStart(f.State) == new DateTime(2026, 10, 19) &&
                    f.State.Inbox.Count == 3 && f.Undo.Count == 0,
                    "Weekly rollover leaves global inbox items intact and clears snapshots tied to the prior week.");
                Assert(f.State.Tasks.Any(t => t.Id == "board-a" && t.Column == 7),
                    "Opening an inbox does not prevent the board's normal unfinished-task rollover.");
                var schedule = Menu(f.InboxCard("inbox-a"), "Schedule for");
                Assert((string)((MenuItem)schedule.Items[0]).Header == "Mon · 2026.10.19",
                    "Inbox destination dates refresh after rollover while the inbox remains open.");
                Invoke(f.Window, "SaveNow");
                Assert(f.Store.Load().Inbox.Single(i => i.Task.Id == "inbox-a").Task.Text == "Captured across week boundary",
                    "The inbox edit and rollover remain persisted together.");
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal DateTime Now = new DateTime(2026, 10, 15, 12, 0, 0);
            internal readonly BoardStore Store;
            internal readonly MainWindow Window;
            internal BoardState State { get { return Field<BoardState>(Window, "state"); } }
            internal InboxWindow Inbox { get { return Field<InboxWindow>(Window, "inboxWindow"); } }
            internal ColumnPanel[] Groups { get { return Field<ColumnPanel[]>(Inbox, "groups"); } }
            internal Stack<TaskUndoEntry> Undo { get { return Field<Stack<TaskUndoEntry>>(Window, "undo"); } }

            internal Fixture(string directory)
            {
                var state = new BoardState();
                state.Tasks.Add(new TaskItem { Id = "board-a", Text = "Board alpha", Column = 1,
                    Color = "#426787", BackgroundColor = "#E4EFFA" });
                state.Tasks.Add(new TaskItem { Id = "board-done", Text = "Completed board task", Column = 2, IsCompleted = true });
                for (int day = 1; day <= 7; day++) state.Tasks.Add(new TaskItem
                { Id = "daily-" + day, Text = "Synthetic routine", Column = day, DailyGroupId = "routine" });
                for (int group = 1; group <= 3; group++) state.Inbox.Add(new InboxItem
                {
                    Group = group, Task = new TaskItem { Id = "inbox-" + (char)('a' + group - 1),
                        Text = group == 1 ? "Inbox alpha" : "Synthetic collected task " + group, Color = "#426787" }
                });
                Store = new BoardStore(directory);
                Store.Save(state);
                Window = new MainWindow(Store, () => Now);
                Layout(Window);
                foreach (var panel in Field<ColumnPanel[]>(Window, "columns")) panel.Animate = false;
            }

            internal InboxWindow OpenInbox()
            {
                var inbox = (InboxWindow)Invoke(Window, "CreateInboxWindow");
                Layout(inbox);
                foreach (var panel in Groups) panel.Animate = false;
                return inbox;
            }
            internal void CloseInbox() { if (Inbox != null) Inbox.Close(); }
            internal TaskCard InboxCard(string id)
            { return Groups.SelectMany(p => p.Children.OfType<TaskCard>()).Single(c => c.Item.Id == id); }
            internal TaskCard BoardCard(string id)
            { return Field<ColumnPanel[]>(Window, "columns").SelectMany(p => p.Children.OfType<TaskCard>()).Single(c => c.Item.Id == id); }
            internal IEnumerable<TaskCard> BoardCards(int column)
            { return Field<ColumnPanel[]>(Window, "columns")[column].Children.OfType<TaskCard>(); }
            public void Dispose() { CloseInbox(); Window.Close(); }
        }

        private static TextBox Editor(TaskCard card) { return Field<TextBox>(card, "editor"); }
        private static MenuItem Menu(TaskCard card, string header)
        { return card.ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == header); }
        private static void SelectDestination(TaskCard card, string header, int index)
        { ((MenuItem)Menu(card, header).Items[index]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
        private static void ClickBlank(InboxWindow window, string name)
        {
            Descendants<Border>((DependencyObject)window.Content).Single(b => AutomationProperties.GetName(b) == name)
                .RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        }
        private static void Layout(Window window)
        {
            var root = (FrameworkElement)window.Content;
            var size = new Size(window.Width, Math.Max(1, window.Height - 30));
            root.Measure(size);
            root.Arrange(new Rect(new Point(), size));
            root.UpdateLayout();
        }
        private static Rect Bounds(FrameworkElement element, FrameworkElement root)
        { return new Rect(element.TranslatePoint(new Point(), root), element.RenderSize); }
        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T) yield return (T)parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        private static T Field<T>(object instance, string name)
        { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
        private static object Invoke(object instance, string name, params object[] arguments)
        { return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments); }
        private static void Assert(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
    }
}
