using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskBoard.Core;

namespace TaskBoard.UI
{
    public sealed partial class MainWindow
    {
        private Button CreateInboxButton()
        {
            var button = IconButton("", "Inbox");
            button.Content = new System.Windows.Shapes.Path
            {
                Width = 16, Height = 16, Stretch = Stretch.Uniform,
                Data = Geometry.Parse("M1.5,7.5 L4,2.5 H12 L14.5,7.5 V13.5 H1.5 Z M1.5,7.5 H5.5 L6.5,9.5 H9.5 L10.5,7.5 H14.5"),
                Stroke = Brushes.Black, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round
            };
            button.Margin = new Thickness(8, 0, 0, 0);
            button.Click += (s, e) => OpenInbox();
            return button;
        }

        private void OpenInbox()
        {
            FinishInteraction();
            if (weekPicker != null) weekPicker.IsOpen = false;
            if (inboxWindow != null) { inboxWindow.Activate(); return; }
            var window = CreateInboxWindow();
            window.Owner = this;
            window.ShowDialog();
        }

        // Creation is separate from showing so controls and data lifecycles can
        // also be verified off-screen using an isolated store.
        private InboxWindow CreateInboxWindow()
        {
            inboxWindow = new InboxWindow(state, () => WeeklyPlanner.CurrentWeekStart(state), PushUndo,
                boardChanged => { if (boardChanged) RenderTasks(); QueueSave(); }, UndoTasks);
            inboxWindow.Closing += (s, e) => { if (!SaveNow()) e.Cancel = true; };
            inboxWindow.Closed += (s, e) => inboxWindow = null;
            return inboxWindow;
        }

        private void MoveTaskToInbox(TaskItem item, int group)
        {
            FinishInteraction();
            if (!state.Tasks.Contains(item) || string.IsNullOrWhiteSpace(item.Text)) return;
            var before = new TaskUndoEntry(Snapshot(), inboxBefore: InboxOperations.CloneItems(state.Inbox));
            if (!InboxOperations.MoveFromBoard(state, item, group)) return;
            PushUndo(before);
            RenderTasks();
            QueueSave();
        }
    }
}
