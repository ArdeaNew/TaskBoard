using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace TaskBoard.UI
{
    // Owns only pointer and visual state. The window commits model changes after
    // a successful drop, while the original task data is still available to undo.
    internal sealed class TaskDragController : IDisposable
    {
        private readonly Grid surface;
        private readonly Canvas overlay;
        private readonly ColumnPanel[] columns;
        private readonly ScrollViewer[] scrollers;
        private readonly Action<TaskCard> commitOtherEditor;
        private readonly Action<TaskCard, int, IReadOnlyList<TaskCard>> dropCommitted;
        private readonly DispatcherTimer scrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        private TaskCard candidate;
        private bool candidateFromHandle;
        private TaskCard dragged;
        private Point pressed;
        private Point pointer;
        private Point offset;
        private Border gap;
        private Border ghost;
        private Action completeDropAnimation;
        private int sourceColumn;
        private int sourceIndex;
        private int targetColumn;
        private bool finishing;
        private bool disposed;

        internal event EventHandler DragStateChanged;
        internal bool IsDragging { get { return dragged != null; } }

        internal TaskDragController(Grid surface, Canvas overlay, ColumnPanel[] columns,
            ScrollViewer[] scrollers, Action<TaskCard> commitOtherEditor,
            Action<TaskCard, int, IReadOnlyList<TaskCard>> dropCommitted)
        {
            if (surface == null) throw new ArgumentNullException("surface");
            if (overlay == null) throw new ArgumentNullException("overlay");
            if (columns == null || columns.Length == 0 || columns.Any(c => c == null))
                throw new ArgumentException("Day columns must be initialized.", "columns");
            if (scrollers == null || scrollers.Length != columns.Length || scrollers.Any(s => s == null))
                throw new ArgumentException("Each day column needs a scroller.", "scrollers");
            if (commitOtherEditor == null) throw new ArgumentNullException("commitOtherEditor");
            if (dropCommitted == null) throw new ArgumentNullException("dropCommitted");
            this.surface = surface;
            this.overlay = overlay;
            this.columns = columns;
            this.scrollers = scrollers;
            this.commitOtherEditor = commitOtherEditor;
            this.dropCommitted = dropCommitted;
            scrollTimer.Tick += AutoScroll;
            surface.LostMouseCapture += OnLostMouseCapture;
        }

        internal void PointerDown(object sender, MouseButtonEventArgs e)
        {
            if (disposed || IsDragging) return;
            candidate = null;
            candidateFromHandle = false;
            var origin = e.OriginalSource as DependencyObject;
            var card = VisualTree.FindAncestor<TaskCard>(origin);
            bool handle = card != null && card.IsDragHandle(origin);
            if (VisualTree.FindAncestor<Popup>(origin) == null) commitOtherEditor(card);
            if (card != null && card.IsEditing && handle)
            {
                card.CommitEdit();
                int column, index;
                if (!FindCard(card, out column, out index)) return;
            }
            if (card == null || card.IsEditing || card.IsPaletteOpen || e.ClickCount > 1 ||
                VisualTree.FindAncestor<ButtonBase>(origin) != null || VisualTree.FindAncestor<TextBox>(origin) != null) return;
            int currentColumn, currentIndex;
            if (!FindCard(card, out currentColumn, out currentIndex)) return;
            candidate = card;
            candidateFromHandle = handle;
            pressed = e.GetPosition(surface);
        }

        internal void PointerMove(object sender, MouseEventArgs e)
        {
            if (disposed) return;
            pointer = e.GetPosition(surface);
            if (dragged != null)
            {
                MoveGhost();
                UpdateDropTarget();
                e.Handled = true;
                return;
            }
            if (candidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            if (Math.Abs(pointer.X - pressed.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pointer.Y - pressed.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            StartDrag(candidate);
            candidate = null;
            e.Handled = true;
        }

        internal void PointerUp(object sender, MouseButtonEventArgs e)
        {
            if (disposed) return;
            var clicked = candidate;
            candidate = null;
            if (dragged == null)
            {
                if (clicked != null && !candidateFromHandle && !clicked.IsEditing && !clicked.IsPaletteOpen)
                    clicked.BeginEdit();
                return;
            }
            pointer = e.GetPosition(surface);
            UpdateDropTarget();
            int dropColumn = ColumnAt(pointer);
            FinishDrag(dropColumn >= 0 && (!dragged.Item.IsDaily || dropColumn == sourceColumn));
            e.Handled = true;
        }

        internal void Cancel()
        {
            candidate = null;
            candidateFromHandle = false;
            if (dragged != null) FinishDrag(false);
        }

        internal void CompleteDropAnimation()
        {
            if (completeDropAnimation != null) completeDropAnimation();
        }

        internal void RenumberCards()
        {
            foreach (var column in columns)
            {
                int ordinal = 1;
                foreach (UIElement child in column.Children)
                {
                    var card = child as TaskCard;
                    if (card != null) card.Ordinal = ordinal++;
                    else if (child == gap) ordinal++;
                }
            }
        }

        private void StartDrag(TaskCard card)
        {
            if (card.IsEditing) return;
            StartDragVisuals(card);
            if (dragged == null) return;
            if (!Mouse.Capture(surface, CaptureMode.SubTree))
            {
                FinishDrag(false);
                return;
            }
            surface.Cursor = Cursors.SizeAll;
            MoveGhost();
            scrollTimer.Start();
        }

        // Keeping visual setup separate permits checks without desktop input.
        private void StartDragVisuals(TaskCard card)
        {
            if (disposed || dragged != null) return;
            int column, index;
            if (!FindCard(card, out column, out index)) return;
            CompleteDropAnimation();
            dragged = card;
            sourceColumn = targetColumn = column;
            sourceIndex = index;
            Point position = card.TranslatePoint(new Point(), surface);
            offset = new Point(pressed.X - position.X, pressed.Y - position.Y);
            card.SetDragAppearance(true);
            var preview = new TaskCard(card.Item)
            {
                Ordinal = card.Ordinal, IsHitTestVisible = false, Focusable = false
            };
            KeyboardNavigation.SetTabNavigation(preview, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(preview, KeyboardNavigationMode.None);
            preview.SetDragAppearance(true);
            ghost = new Border
            {
                Width = card.ActualWidth, Height = card.ActualHeight,
                Background = Brushes.White, CornerRadius = new CornerRadius(0), Child = preview,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.10 }
            };
            overlay.Children.Add(ghost);
            gap = new Border
            {
                Height = card.ActualHeight, Margin = card.Margin, CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(Color.FromRgb(247, 247, 247)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(221, 221, 221)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            columns[sourceColumn].Children.Remove(card);
            columns[sourceColumn].Children.Insert(sourceIndex, gap);
            columns[sourceColumn].ForgetPosition(card);
            UpdateDragPreview(sourceColumn, sourceIndex);
            RenumberCards();
            MoveGhost();
            RaiseDragStateChanged();
        }

        private void UpdateDragPreview(int column, int index)
        {
            var preview = (TaskCard)ghost.Child;
            preview.Ordinal = index + 1;
            ghost.Width = Math.Max(1, columns[column].ActualWidth);
            preview.Measure(new Size(ghost.Width, double.PositiveInfinity));
            ghost.Height = preview.DesiredSize.Height;
            if (gap != null) gap.Height = ghost.Height;
        }

        private void MoveGhost()
        {
            if (ghost == null) return;
            Canvas.SetLeft(ghost, pointer.X - offset.X);
            Canvas.SetTop(ghost, pointer.Y - offset.Y);
        }

        private int ColumnAt(Point position)
        {
            for (int index = 0; index < columns.Length; index++)
            {
                var top = scrollers[index].TranslatePoint(new Point(), surface);
                if (position.X >= top.X && position.X <= top.X + scrollers[index].ActualWidth &&
                    position.Y >= top.Y && position.Y <= top.Y + scrollers[index].ActualHeight) return index;
            }
            return -1;
        }

        private void UpdateDropTarget()
        {
            if (dragged == null) return;
            int column = ColumnAt(pointer);
            if (column < 0 || (dragged.Item.IsDaily && column != sourceColumn)) return;
            var panel = columns[column];
            double localY = surface.TranslatePoint(pointer, panel).Y;
            int index = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child == gap) continue;
                // Layout positions stay stable while FLIP animates the visuals.
                var slot = LayoutInformation.GetLayoutSlot((FrameworkElement)child);
                if (localY < slot.Top + child.DesiredSize.Height / 2) break;
                index++;
            }
            var peers = panel.Children.OfType<TaskCard>().ToList();
            int completedCount = peers.Count(t => t.Item.IsCompleted);
            if (dragged.Item.IsCompleted) index = Math.Min(index, completedCount);
            else index = Math.Max(completedCount, index);
            int current = columns[targetColumn].Children.IndexOf(gap);
            if (column == targetColumn && current == index)
            {
                UpdateDragPreview(column, index);
                return;
            }
            columns[targetColumn].Children.Remove(gap);
            targetColumn = column;
            panel.Children.Insert(Math.Min(index, panel.Children.Count), gap);
            RenumberCards();
            UpdateDragPreview(column, index);
            // Insertion can change scrollbar visibility and the content width.
            surface.UpdateLayout();
            UpdateDragPreview(column, index);
        }

        private void AutoScroll(object sender, EventArgs e)
        {
            if (dragged == null) return;
            int column = ColumnAt(pointer);
            if (column < 0) return;
            var scroll = scrollers[column];
            double y = surface.TranslatePoint(pointer, scroll).Y;
            double delta = y < 44 ? -Math.Min(13, (44 - y) * 0.3) :
                y > scroll.ActualHeight - 44 ? Math.Min(13, (y - scroll.ActualHeight + 44) * 0.3) : 0;
            if (Math.Abs(delta) > 0.1)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + delta);
                UpdateDropTarget();
            }
        }

        private void FinishDrag(bool commit)
        {
            if (dragged == null || finishing) return;
            finishing = true;
            scrollTimer.Stop();
            var card = dragged;
            int index = columns[targetColumn].Children.IndexOf(gap);
            columns[targetColumn].Children.Remove(gap);
            if (!commit) { targetColumn = sourceColumn; index = sourceIndex; }
            columns[targetColumn].Children.Insert(Math.Max(0, Math.Min(index, columns[targetColumn].Children.Count)), card);
            columns[targetColumn].ForgetPosition(card);
            if (commit && (targetColumn != sourceColumn || index != sourceIndex))
            {
                var ordered = columns.SelectMany(p => p.Children.OfType<TaskCard>()).ToList();
                dropCommitted(card, targetColumn, ordered);
            }
            RenumberCards();
            surface.UpdateLayout();
            Point destination = card.TranslatePoint(new Point(), surface);
            card.Opacity = 0;
            card.IsHitTestVisible = false;
            var departing = ghost;
            UpdateDragPreview(targetColumn, card.Ordinal - 1);
            var duration = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 140 : 1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var horizontal = new DoubleAnimation(Canvas.GetLeft(departing), destination.X, duration) { EasingFunction = ease };
            var vertical = new DoubleAnimation(Canvas.GetTop(departing), destination.Y, duration) { EasingFunction = ease };
            Action complete = null;
            complete = () =>
            {
                if (!ReferenceEquals(completeDropAnimation, complete)) return;
                completeDropAnimation = null;
                departing.BeginAnimation(Canvas.LeftProperty, null);
                departing.BeginAnimation(Canvas.TopProperty, null);
                overlay.Children.Remove(departing);
                card.Opacity = 1;
                card.IsHitTestVisible = true;
                card.SetDragAppearance(false);
            };
            completeDropAnimation = complete;
            vertical.Completed += (s, e) => complete();
            departing.BeginAnimation(Canvas.LeftProperty, horizontal);
            departing.BeginAnimation(Canvas.TopProperty, vertical);
            dragged = null;
            ghost = null;
            gap = null;
            surface.Cursor = null;
            if (surface.IsMouseCaptured) surface.ReleaseMouseCapture();
            finishing = false;
            RaiseDragStateChanged();
        }

        private bool FindCard(TaskCard card, out int column, out int index)
        {
            for (column = 0; column < columns.Length; column++)
            {
                index = columns[column].Children.IndexOf(card);
                if (index >= 0) return true;
            }
            column = index = -1;
            return false;
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!finishing && dragged != null && !surface.IsMouseCaptured) Cancel();
        }

        private void RaiseDragStateChanged()
        {
            var handler = DragStateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            CompleteDropAnimation();
            scrollTimer.Stop();
            scrollTimer.Tick -= AutoScroll;
            surface.LostMouseCapture -= OnLostMouseCapture;
            disposed = true;
        }

    }
}
