using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    // A view over the same local board, with no separate store or save timer.
    // The owner records changes in one history so transfers undo on both sides.
    internal sealed class InboxWindow : Window
    {
        internal static readonly IReadOnlyList<string> GroupNames = Array.AsReadOnly(
            new[] { "Work 1", "Work 2", "Life" });
        private readonly BoardState state;
        private readonly Func<DateTime> selectedWeek;
        private readonly Action<TaskUndoEntry> remember;
        private readonly Action<bool> changed;
        private readonly Action undo;
        private readonly ColumnPanel[] groups = new ColumnPanel[3];
        private readonly ScrollViewer[] scrollers = new ScrollViewer[3];
        private TaskCard editing;
        private TaskUndoEntry editSnapshot;
        private int activeGroup;
        internal bool HasActiveEdit { get { return editing != null; } }

        internal InboxWindow(BoardState state, Func<DateTime> selectedWeek,
            Action<TaskUndoEntry> remember, Action<bool> changed, Action undo)
        {
            this.state = state ?? throw new ArgumentNullException("state");
            this.selectedWeek = selectedWeek ?? throw new ArgumentNullException("selectedWeek");
            this.remember = remember ?? throw new ArgumentNullException("remember");
            this.changed = changed ?? throw new ArgumentNullException("changed");
            this.undo = undo ?? throw new ArgumentNullException("undo");
            Title = "Inbox";
            Language = XmlLanguage.GetLanguage("en-US");
            FontFamily = Typography.TaskFont;
            FontSize = 14;
            Background = Brushes.White;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var work = SystemParameters.WorkArea;
            MinWidth = Math.Min(560, work.Width);
            MinHeight = Math.Min(360, work.Height);
            Width = Math.Min(840, Math.Max(MinWidth, work.Width * .55));
            Height = Math.Min(600, Math.Max(MinHeight, work.Height * .6));
            UseLayoutRounding = SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = BuildLayout();
            RefreshTasks();
            PreviewKeyDown += HandleKey;
            Closing += (s, e) => CommitPendingEdit();
        }

        private FrameworkElement BuildLayout()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            grid.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < groups.Length; i++)
            {
                int group = i;
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var header = new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(i == 0 ? 0 : 1.4, 0, 0, 1.4),
                    Child = new TextBlock
                    {
                        Text = GroupNames[i], FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Grid.SetColumn(header, i);
                grid.Children.Add(header);
                groups[i] = new ColumnPanel { Background = Brushes.Transparent };
                scrollers[i] = new ScrollViewer
                {
                    Content = groups[i], VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    CanContentScroll = false, PanningMode = PanningMode.VerticalOnly, Focusable = false
                };
                var cell = new Border
                {
                    BorderBrush = Brushes.Black, Background = Brushes.White,
                    BorderThickness = new Thickness(i == 0 ? 0 : 1.4, 0, 0, 0),
                    Child = scrollers[i]
                };
                AutomationProperties.SetName(cell, GroupNames[i]);
                cell.MouseLeftButtonDown += (s, e) =>
                {
                    var origin = e.OriginalSource as DependencyObject;
                    if (VisualTree.FindAncestor<TaskCard>(origin) != null ||
                        VisualTree.FindAncestor<ScrollBar>(origin) != null) return;
                    AddTask(group);
                    e.Handled = true;
                };
                Grid.SetColumn(cell, i);
                Grid.SetRow(cell, 1);
                grid.Children.Add(cell);
            }
            return new Border
            {
                Margin = new Thickness(20), BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1.6), Child = grid
            };
        }

        internal void CommitPendingEdit()
        {
            if (editing != null) editing.CommitEdit();
        }

        // The planner makes the single owned save copy. Expose the committed
        // source without cloning a second, immediately discarded inbox snapshot.
        internal IReadOnlyList<InboxItem> CommittedItems
        {
            get { return editing != null && editSnapshot != null ? editSnapshot.InboxBefore : state.Inbox; }
        }

        internal void RefreshTasks()
        {
            foreach (var group in groups) group.Children.Clear();
            foreach (var item in state.Inbox)
                groups[item.Group - 1].Children.Add(CreateCard(item));
            Renumber();
        }

        private TaskUndoEntry Snapshot(bool includeWeek = false)
        {
            return new TaskUndoEntry(includeWeek ? BoardOperations.CloneTasks(state.Tasks) : null,
                inboxBefore: InboxOperations.CloneItems(state.Inbox));
        }

        private void AddTask(int group)
        {
            CommitPendingEdit();
            activeGroup = group;
            editSnapshot = Snapshot();
            var item = InboxOperations.Add(state, group + 1);
            item.Task.Color = "#202020";
            var card = CreateCard(item);
            groups[group].Children.Add(card);
            Renumber();
            scrollers[group].ScrollToEnd();
            card.BeginEdit(true);
            Dispatcher.BeginInvoke(new Action(() => card.BringIntoView()), DispatcherPriority.Loaded);
        }

        private TaskCard CreateCard(InboxItem item)
        {
            var card = new TaskCard(item.Task, TaskCardMode.Inbox);
            card.EditStarted += (s, e) =>
            {
                if (editing != null && editing != card) editing.CommitEdit();
                activeGroup = item.Group - 1;
                editing = card;
                if (editSnapshot == null) editSnapshot = Snapshot();
            };
            card.Committed += (s, e) =>
            {
                if (editSnapshot != null) remember(editSnapshot);
                editSnapshot = null;
                if (!card.IsEditing && editing == card) editing = null;
                changed(false);
            };
            card.Cancelled += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(item.Task.Text))
                {
                    InboxOperations.Remove(state, item);
                    groups[item.Group - 1].Children.Remove(card);
                }
                editing = null;
                editSnapshot = null;
                Renumber();
                changed(false);
            };
            card.DeleteRequested += (s, e) =>
            {
                if (editing != null && editing != card) editing.CommitEdit();
                if (!state.Inbox.Contains(item)) return;
                remember(editing == card && editSnapshot != null ? editSnapshot : Snapshot());
                InboxOperations.Remove(state, item);
                groups[item.Group - 1].Children.Remove(card);
                editing = null;
                editSnapshot = null;
                Renumber();
                changed(false);
            };
            var schedule = card.AddDestinationMenu("Schedule for", BoardLabels.DayNames, day => ScheduleTask(item, day + 1));
            RefreshDates(schedule);
            schedule.SubmenuOpened += (s, e) => RefreshDates(schedule);
            card.AddDestinationMenu("Move to group", GroupNames, group => MoveToGroup(item, group + 1));
            return card;
        }

        private void RefreshDates(MenuItem schedule)
        {
            DateTime monday = selectedWeek();
            for (int day = 0; day < 7; day++)
            {
                var option = (MenuItem)schedule.Items[day];
                option.IsEnabled = monday <= DateTime.MaxValue.Date.AddDays(-day);
                option.Header = option.IsEnabled ? monday.AddDays(day)
                    .ToString("ddd · yyyy.MM.dd", CultureInfo.GetCultureInfo("en-US")) : BoardLabels.DayNames[day] + " · —";
            }
        }

        private void ScheduleTask(InboxItem item, int day)
        {
            CommitPendingEdit();
            if (!state.Inbox.Contains(item) || string.IsNullOrWhiteSpace(item.Task.Text) ||
                selectedWeek() > DateTime.MaxValue.Date.AddDays(1 - day)) return;
            var before = Snapshot(true);
            if (!InboxOperations.Schedule(state, item, day)) return;
            remember(before);
            RefreshTasks();
            changed(true);
        }

        private void MoveToGroup(InboxItem item, int group)
        {
            CommitPendingEdit();
            if (!state.Inbox.Contains(item) || item.Group == group) return;
            var before = Snapshot();
            if (!InboxOperations.ChangeGroup(state, item, group)) return;
            remember(before);
            RefreshTasks();
            changed(false);
        }

        private void Renumber()
        {
            foreach (var group in groups)
            {
                int ordinal = 1;
                foreach (var card in group.Children.OfType<TaskCard>()) card.Ordinal = ordinal++;
            }
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                AddTask(activeGroup);
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && editing == null &&
                VisualTree.FindAncestor<TextBox>(e.OriginalSource as DependencyObject) == null)
            {
                undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && editing == null &&
                !groups.SelectMany(group => group.Children.OfType<TaskCard>()).Any(card => card.IsPaletteOpen || card.ContextMenu.IsOpen))
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
