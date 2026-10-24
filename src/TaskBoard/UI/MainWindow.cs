using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
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
using TaskBoard.Persistence;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    public sealed partial class MainWindow : Window
    {
        private readonly BoardStore store;
        private readonly BoardState state;
        private readonly Func<DateTime> localNow;
        private readonly Grid surface = new Grid { Background = Brushes.White };
        private readonly Grid board = new Grid();
        private readonly Canvas overlay = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        private readonly ColumnPanel[] columns = new ColumnPanel[7];
        private readonly ScrollViewer[] scrollers = new ScrollViewer[7];
        private readonly DayClearButton[] clearButtons = new DayClearButton[7];
        private readonly Border[] dayHeaders = new Border[7];
        private readonly Border[] dayCells = new Border[7];
        private readonly TextBlock[] dayTodayLabels = new TextBlock[7];
        private readonly TextBlock[] dayDates = new TextBlock[7];
        private static readonly Brush todayColumnBrush = FrozenBrush("#F0FBE0");
        private readonly TextBlock week = new TextBlock();
        private readonly TextBlock remaining = new TextBlock();
        private Button previousWeekButton;
        private Button nextWeekButton;
        private Button chooseWeekButton;
        private Popup weekPicker;
        private InboxWindow inboxWindow;
        private DateTime lastSystemWeek;
        private readonly DispatcherTimer dateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly TaskDragController dragController;
        private readonly Stack<TaskUndoEntry> undo = new Stack<TaskUndoEntry>();
        private List<TaskItem> editSnapshot;
        private TaskCard editing;
        private bool dirty;
        private bool closed;

        public MainWindow(BoardStore boardStore) : this(boardStore, () => DateTime.Now) { }

        internal MainWindow(BoardStore boardStore, Func<DateTime> localNow)
        {
            if (boardStore == null) throw new ArgumentNullException("boardStore");
            if (localNow == null) throw new ArgumentNullException("localNow");
            this.localNow = localNow;
            store = boardStore;
            state = store.Load();
            DateTime today = localNow().Date;
            WeeklyPlanner.Initialize(state, today);
            lastSystemWeek = WeeklyPlanner.MondayOf(today);
            Title = "TaskBoard";
            Language = XmlLanguage.GetLanguage("en-US");
            Background = Brushes.White;
            FontFamily = Typography.TaskFont;
            FontSize = 14;
            var workArea = SystemParameters.WorkArea;
            MinWidth = Math.Min(760, workArea.Width);
            MinHeight = Math.Min(440, workArea.Height);
            Width = Math.Max(MinWidth, workArea.Width * 0.7);
            Height = Math.Max(MinHeight, workArea.Height * 0.7);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Normal;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = surface;
            BuildLayout();
            dragController = new TaskDragController(surface, overlay, columns, scrollers,
                card => { if (editing != null && editing != card) editing.CommitEdit(); },
                (card, column, orderedCards) =>
                {
                    PushUndo(Snapshot());
                    card.Item.Column = column + 1;
                    state.Tasks.Clear();
                    state.Tasks.AddRange(orderedCards.Select(task => task.Item));
                    QueueSave();
                });
            dragController.DragStateChanged += (s, e) => UpdateClearButtons();
            RenderTasks();
            UpdateDate();
            dateTimer.Tick += (s, e) => UpdateDate();
            dateTimer.Start();
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };
            PreviewMouseLeftButtonDown += dragController.PointerDown;
            PreviewMouseMove += dragController.PointerMove;
            PreviewMouseLeftButtonUp += dragController.PointerUp;
            PreviewKeyDown += HandleKey;
            Deactivated += (s, e) => dragController.Cancel();
            Closing += OnClosing;
            Loaded += (s, e) =>
            {
                if (!string.IsNullOrEmpty(store.LastWarning))
                    BoardDialog.Show(this, store.LastWarning, "TaskBoard");
            };
            // Startup can roll overdue tasks across archive boundaries even if
            // the user makes no further edits during this session.
            QueueSave();
        }

        private void BuildLayout()
        {
            var layout = new Grid { Margin = new Thickness(24, 10, 24, 22) };
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            layout.RowDefinitions.Add(new RowDefinition());
            surface.Children.Add(layout);
            surface.Children.Add(overlay);
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            week.VerticalAlignment = remaining.VerticalAlignment = VerticalAlignment.Center;
            week.FontSize = 13;
            remaining.FontSize = 12;
            remaining.Foreground = new SolidColorBrush(Color.FromRgb(105, 105, 105));
            var navigation = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            previousWeekButton = IconButton("‹", "Previous week");
            previousWeekButton.FontSize = 22;
            previousWeekButton.Click += (s, e) => NavigateWeek(WeeklyPlanner.CurrentWeekStart(state).AddDays(-7));
            navigation.Children.Add(previousWeekButton);
            chooseWeekButton = IconButton("", "Choose week");
            chooseWeekButton.Width = 112;
            chooseWeekButton.Content = week;
            chooseWeekButton.Click += (s, e) => OpenWeekPicker();
            navigation.Children.Add(chooseWeekButton);
            nextWeekButton = IconButton("›", "Next week");
            nextWeekButton.FontSize = 22;
            nextWeekButton.Click += (s, e) => NavigateWeek(WeeklyPlanner.CurrentWeekStart(state).AddDays(7));
            navigation.Children.Add(nextWeekButton);
            var currentWeek = IconButton("This week", "This week");
            currentWeek.Width = 84;
            currentWeek.FontSize = 13;
            currentWeek.Margin = new Thickness(6, 0, 0, 0);
            currentWeek.Click += (s, e) => GoToCurrentWeek();
            navigation.Children.Add(currentWeek);
            bar.Children.Add(navigation);
            Grid.SetColumn(remaining, 1);
            bar.Children.Add(remaining);
            var inbox = CreateInboxButton();
            Grid.SetColumn(inbox, 2);
            bar.Children.Add(inbox);
            var export = IconButton("\uE91B", "Export image · Ctrl+Shift+S");
            export.FontFamily = new FontFamily("Segoe MDL2 Assets");
            export.FontSize = 15;
            export.Margin = new Thickness(12, 0, 0, 0);
            export.Click += (s, e) => ExportImage();
            Grid.SetColumn(export, 3);
            bar.Children.Add(export);
            var print = IconButton("\uE749", "Print · Ctrl+P");
            print.FontFamily = new FontFamily("Segoe MDL2 Assets");
            print.FontSize = 15;
            print.Margin = new Thickness(12, 0, 0, 0);
            print.Click += (s, e) => Print();
            Grid.SetColumn(print, 4);
            bar.Children.Add(print);
            layout.Children.Add(bar);

            var frame = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(1.6), Child = board };
            Grid.SetRow(frame, 1);
            layout.Children.Add(frame);
            board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            board.RowDefinitions.Add(new RowDefinition());
            var labels = BoardLabels.DayNames;
            for (int i = 0; i < columns.Length; i++)
            {
                int column = i;
                board.ColumnDefinitions.Add(new ColumnDefinition());
                var head = new Grid { Background = Brushes.Transparent };
                head.RowDefinitions.Add(new RowDefinition());
                head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var todayLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = FrozenBrush("#70804F"), Margin = new Thickness(0, 3, 0, 0) };
                var label = new TextBlock { Text = labels[i], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 14 };
                var date = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, FontSize = 11, Foreground = FrozenBrush("#6A6A6A"), Margin = new Thickness(0, 1, 0, 6) };
                Grid.SetRow(todayLabel, 0);
                Grid.SetRow(label, 1);
                Grid.SetRow(date, 2);
                dayTodayLabels[i] = todayLabel;
                head.Children.Add(label);
                head.Children.Add(todayLabel);
                head.Children.Add(date);
                var header = new Border { Child = head, BorderBrush = Brushes.Black, Background = Brushes.White, BorderThickness = new Thickness(i == 0 ? 0 : 1.4, 0, 0, 1.4) };
                dayHeaders[i] = header;
                dayDates[i] = date;
                Grid.SetColumn(header, i);
                board.Children.Add(header);
                columns[i] = new ColumnPanel { Background = Brushes.Transparent };
                scrollers[i] = new ScrollViewer
                {
                    Content = columns[i], VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false,
                    PanningMode = PanningMode.VerticalOnly, Focusable = false
                };
                var body = new Grid();
                body.RowDefinitions.Add(new RowDefinition());
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.Children.Add(scrollers[i]);
                clearButtons[i] = new DayClearButton();
                clearButtons[i].ClearRequested += (s, e) => RequestClearDay(column);
                Grid.SetRow(clearButtons[i], 1);
                body.Children.Add(clearButtons[i]);
                var cell = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(i == 0 ? 0 : 1.4, 0, 0, 0), Child = body, Background = Brushes.White };
                dayCells[i] = cell;
                AutomationProperties.SetName(cell, labels[i]);
                cell.MouseLeftButtonDown += (s, e) =>
                {
                    var origin = e.OriginalSource as DependencyObject;
                    if (VisualTree.FindAncestor<TaskCard>(origin) == null && VisualTree.FindAncestor<DayClearButton>(origin) == null && VisualTree.FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(origin) == null)
                    { AddTask(column); e.Handled = true; }
                };
                Grid.SetColumn(cell, i);
                Grid.SetRow(cell, 1);
                board.Children.Add(cell);
            }
        }

        private static Brush FrozenBrush(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }

        private void UpdateDayVisuals(DateTime selectedMonday)
        {
            DateTime today = localNow().Date;
            bool currentWeek = selectedMonday == WeeklyPlanner.MondayOf(today);
            for (int column = 0; column < dayDates.Length; column++)
            {
                DateTime date = selectedMonday <= DateTime.MaxValue.Date.AddDays(-column)
                    ? selectedMonday.AddDays(column) : DateTime.MaxValue.Date;
                dayDates[column].Text = BoardLabels.Date(date);
                bool isToday = currentWeek && date == today;
                dayTodayLabels[column].Text = isToday ? "Today" : string.Empty;
                Brush fill = isToday ? todayColumnBrush : Brushes.White;
                dayHeaders[column].Background = fill;
                dayCells[column].Background = fill;
            }
        }

        private static Button IconButton(string text, string name)
        {
            var button = MinimalButton.Create(text, name);
            button.Width = 26;
            button.Height = 28;
            button.VerticalAlignment = VerticalAlignment.Center;
            return button;
        }
        private void RenderTasks()
        {
            dragController.CompleteDropAnimation();
            BoardOperations.ArrangeTasks(state);
            foreach (var column in columns) column.Children.Clear();
            foreach (var item in state.Tasks) columns[Math.Max(0, Math.Min(columns.Length - 1, item.Column - 1))].Children.Add(CreateCard(item));
            RenumberTasks();
        }

        private void RenumberTasks()
        {
            dragController.RenumberCards();
            UpdateClearButtons();
        }

        private void UpdateClearButtons()
        {
            for (int i = 0; i < clearButtons.Length; i++)
                clearButtons[i].IsAvailable = !dragController.IsDragging && state.Tasks.Any(t => t.Column == i + 1 && !string.IsNullOrWhiteSpace(t.Text));
        }

        private void RequestClearDay(int column)
        {
            FinishInteraction();
            int count = state.Tasks.Count(t => t.Column == column + 1);
            if (count == 0) return;
            DateTime date = WeeklyPlanner.CurrentWeekStart(state).AddDays(column);
            string message = date.ToString("yyyy.MM.dd dddd", CultureInfo.GetCultureInfo("en-US")) +
                "\n\nClear all " + count.ToString(CultureInfo.InvariantCulture) + " tasks for this day?";
            bool confirmed = BoardDialog.Confirm(this, message, "Clear day", "Clear");
            Focus();
            if (confirmed)
                ClearDay(column);
        }

        private void ClearDay(int column)
        {
            if (column < 0 || column >= columns.Length) throw new ArgumentOutOfRangeException("column");
            FinishInteraction();
            if (!state.Tasks.Any(t => t.Column == column + 1)) return;
            PushUndo(Snapshot());
            BoardOperations.ClearDay(state, column + 1);
            RenderTasks();
            QueueSave();
        }

        private TaskCard CreateCard(TaskItem item)
        {
            var card = new TaskCard(item) { CanMoveToNextWeek = WeeklyPlanner.CanMoveToNextWeek(state) };
            card.EditStarted += (s, e) =>
            {
                if (editing != null && editing != card) editing.CommitEdit();
                editing = card;
                if (editSnapshot == null) editSnapshot = Snapshot();
            };
            card.Committed += (s, e) =>
            {
                if (editSnapshot != null) { PushUndo(editSnapshot); editSnapshot = null; }
                if (!card.IsEditing && editing == card) editing = null;
                if (item.IsDaily)
                {
                    BoardOperations.SyncDailyTask(state, item);
                    foreach (var peer in columns.SelectMany(p => p.Children.OfType<TaskCard>()))
                        if (peer.Item.DailyGroupId == item.DailyGroupId) peer.RefreshAppearance();
                }
                QueueSave();
                UpdateClearButtons();
            };
            card.Cancelled += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(card.Item.Text))
                { state.Tasks.Remove(card.Item); columns[card.Item.Column - 1].Children.Remove(card); }
                RenumberTasks();
                editing = null;
                editSnapshot = null;
                QueueSave();
            };
            card.DeleteRequested += (s, e) =>
            {
                if (editing != null && editing != card) editing.CommitEdit();
                PushUndo(editing == card && editSnapshot != null ? editSnapshot : Snapshot());
                state.Tasks.Remove(item);
                columns[item.Column - 1].Children.Remove(card);
                RenumberTasks();
                if (editing == card) editing = null;
                editSnapshot = null;
                QueueSave();
            };
            card.ApplyToEveryDayRequested += (s, e) =>
            {
                if (editing != null) editing.CommitEdit();
                if (!state.Tasks.Contains(item) || string.IsNullOrWhiteSpace(item.Text)) return;
                PushUndo(Snapshot());
                BoardOperations.ApplyToEveryDay(state, item);
                RenderTasks();
                QueueSave();
            };
            card.MoveToNextWeekRequested += (s, e) => MoveTaskToNextWeek(item);
            card.AddDestinationMenu("Move to inbox", InboxWindow.GroupNames, group => MoveTaskToInbox(item, group + 1));
            card.CompletionRequested += (s, e) =>
            {
                if (editing != null) editing.CommitEdit();
                if (!state.Tasks.Contains(item)) return;
                PushUndo(Snapshot());
                item.IsCompleted = !item.IsCompleted;
                if (item.IsCompleted)
                {
                    state.Tasks.Remove(item);
                    state.Tasks.Insert(0, item);
                }
                RenderTasks();
                QueueSave();
            };
            return card;
        }

        private void MoveTaskToNextWeek(TaskItem item)
        {
            FinishInteraction();
            if (!state.Tasks.Contains(item) || string.IsNullOrWhiteSpace(item.Text) ||
                !WeeklyPlanner.CanMoveToNextWeek(state)) return;

            string destination = WeeklyPlanner.CurrentWeekStart(state).AddDays(7)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            WeekPlan existing = state.Weeks.Find(plan => plan.WeekStart == destination);
            var before = existing == null ? null : new WeekPlan
            {
                WeekStart = existing.WeekStart, Tasks = BoardOperations.CloneTasks(existing.Tasks)
            };
            var entry = new TaskUndoEntry(Snapshot(), destination, before);
            if (!WeeklyPlanner.MoveToNextWeek(state, item, localNow().Date)) return;
            PushUndo(entry);
            RenderTasks();
            QueueSave();
        }

        private void AddTask(int column)
        {
            if (dragController.IsDragging) return;
            dragController.CompleteDropAnimation();
            if (editing != null) editing.CommitEdit();
            editSnapshot = Snapshot();
            var item = new TaskItem { Id = Guid.NewGuid().ToString("N"), Text = "", Color = "#202020", Column = column + 1 };
            state.Tasks.Add(item);
            var card = CreateCard(item);
            columns[column].Children.Add(card);
            RenumberTasks();
            scrollers[column].ScrollToEnd();
            card.BeginEdit(true);
            Dispatcher.BeginInvoke(new Action(() => card.BringIntoView()), DispatcherPriority.Loaded);
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && dragController.IsDragging) { dragController.Cancel(); e.Handled = true; return; }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            { Print(); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            { ExportImage(); e.Handled = true; }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            { AddTask(((int)localNow().DayOfWeek + 6) % 7); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && editing == null && !dragController.IsDragging && VisualTree.FindAncestor<TextBox>(e.OriginalSource as DependencyObject) == null && undo.Count > 0)
            {
                UndoTasks();
                e.Handled = true;
            }
        }

        private void UndoTasks()
        {
            if (editing != null || dragController.IsDragging || undo.Count == 0 ||
                (inboxWindow != null && inboxWindow.HasActiveEdit)) return;
            undo.Pop().Restore(state);
            RenderTasks();
            if (inboxWindow != null) inboxWindow.RefreshTasks();
            QueueSave();
        }

        private void Print()
        {
            if (PrepareOutput()) BoardPrinter.Print(this, state);
        }

        private void ExportImage()
        {
            if (PrepareOutput()) BoardImageExporter.Export(this, state);
        }

        private bool PrepareOutput()
        {
            FinishInteraction();
            return SaveNow();
        }

        private void FinishInteraction()
        {
            dragController.Cancel();
            dragController.CompleteDropAnimation();
            if (editing != null) editing.CommitEdit();
            if (inboxWindow != null) inboxWindow.CommitPendingEdit();
        }

        private void UpdateDate()
        {
            DateTime now = localNow();
            var systemWeek = WeeklyPlanner.MondayOf(now);
            if (systemWeek != lastSystemWeek)
            {
                bool followingCurrentWeek = WeeklyPlanner.CurrentWeekStart(state) == lastSystemWeek;
                FinishInteraction();
                if (!SaveNow()) return;
                WeeklyPlanner.RollOverUnfinished(state, now.Date);
                if (followingCurrentWeek) WeeklyPlanner.SelectWeek(state, systemWeek, now.Date);
                undo.Clear();
                editSnapshot = null;
                editing = null;
                RenderTasks();
                if (inboxWindow != null) inboxWindow.RefreshTasks();
                if (followingCurrentWeek)
                    foreach (var scroll in scrollers) scroll.ScrollToTop();
                lastSystemWeek = systemWeek;
                QueueSave();
            }
            var selected = WeeklyPlanner.CurrentWeekStart(state);
            UpdateDayVisuals(selected);
            week.Text = BoardLabels.Week(selected);
            var end = selected <= DateTime.MaxValue.Date.AddDays(-6) ? selected.AddDays(6) : DateTime.MaxValue.Date;
            chooseWeekButton.ToolTip = selected.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + " — " + end.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            previousWeekButton.IsEnabled = selected >= DateTime.MinValue.Date.AddDays(7);
            nextWeekButton.IsEnabled = selected <= DateTime.MaxValue.Date.AddDays(-7);
            remaining.Text = BoardLabels.Remaining(now);
        }

        private void NavigateWeek(DateTime anyDay)
        {
            FinishInteraction();
            if (!SaveNow()) return;
            if (WeeklyPlanner.MondayOf(anyDay) == WeeklyPlanner.CurrentWeekStart(state)) return;
            WeeklyPlanner.SelectWeek(state, anyDay, localNow().Date);
            undo.Clear();
            editSnapshot = null;
            editing = null;
            RenderTasks();
            foreach (var scroll in scrollers) scroll.ScrollToTop();
            UpdateDate();
            QueueSave();
        }

        private void GoToCurrentWeek() { NavigateWeek(localNow().Date); }

        private void OpenWeekPicker()
        {
            FinishInteraction();
            if (weekPicker != null && weekPicker.IsOpen) { weekPicker.IsOpen = false; return; }
            var selected = WeeklyPlanner.CurrentWeekStart(state);
            var calendar = new System.Windows.Controls.Calendar
            {
                SelectedDate = selected, DisplayDate = selected, SelectionMode = CalendarSelectionMode.SingleDate,
                FirstDayOfWeek = DayOfWeek.Monday, Language = XmlLanguage.GetLanguage("en-US"),
                FontFamily = Typography.TaskFont, FontSize = 13
            };
            var popup = new Popup
            {
                PlacementTarget = chooseWeekButton, Placement = PlacementMode.Bottom,
                StaysOpen = false, AllowsTransparency = true,
                Child = new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = calendar }
            };
            calendar.SelectedDatesChanged += (s, e) =>
            {
                if (!calendar.SelectedDate.HasValue) return;
                popup.IsOpen = false;
                NavigateWeek(calendar.SelectedDate.Value);
            };
            weekPicker = popup;
            popup.IsOpen = true;
        }

        private List<TaskItem> Snapshot()
        {
            return BoardOperations.CloneTasks(state.Tasks);
        }

        private void PushUndo(List<TaskItem> snapshot)
        {
            PushUndo(new TaskUndoEntry(snapshot));
        }

        private void PushUndo(TaskUndoEntry entry)
        {
            undo.Push(entry);
            if (undo.Count > 30)
            {
                var recent = undo.Take(30).Reverse().ToArray();
                undo.Clear();
                foreach (var item in recent) undo.Push(item);
            }
        }

        private void QueueSave()
        {
            dirty = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        private bool SaveNow()
        {
            saveTimer.Stop();
            if (!dirty) return true;
            try
            {
                var committed = WeeklyPlanner.SnapshotForSave(state,
                    editing != null && editSnapshot != null ? editSnapshot : state.Tasks,
                    inboxWindow == null ? null : inboxWindow.CommittedItems);
                store.Save(committed);
                dirty = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { BoardDialog.Show(this, "Could not save: " + ex.Message, "TaskBoard"); return false; }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (closed) return;
            FinishInteraction();
            dirty = true;
            if (!SaveNow()) { e.Cancel = true; return; }
            closed = true;
            dateTimer.Stop();
            dragController.Dispose();
        }

    }
}
