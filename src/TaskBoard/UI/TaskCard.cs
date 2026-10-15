using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    public sealed class TaskCard : Border
    {
        private static readonly Brush hoverBrush = FrozenBrush("#F7F7F7");
        private static readonly Brush mutedBrush = FrozenBrush("#AAAAAA");
        private static readonly Brush lineBrush = FrozenBrush("#DDDDDD");
        private static readonly Brush completedBrush = FrozenBrush("#002FA7");
        private static readonly Brush incompleteBrush = FrozenBrush("#C6C6C6");
        private readonly TextBlock display;
        private readonly TextBlock ordinalDisplay;
        private readonly TextBox editor;
        private readonly FrameworkElement grip;
        private readonly Button colorButton;
        private readonly Ellipse colorDot;
        private readonly Button completionButton;
        private readonly Border completionSquare;
        private readonly ColorPalette palette;
        private readonly ColorPalette backgroundPalette;
        private readonly ContextMenu actions;
        private bool isEditing;
        private bool isNew;
        private bool isComposing;
        private bool isDragging;
        private bool paletteInteraction;
        private bool completionInteraction;
        private string originalText;
        private string originalColor;
        private string originalBackgroundColor;
        private Brush backgroundFill;
        private int ordinal;

        public TaskItem Item { get; private set; }
        public bool IsEditing { get { return isEditing; } }
        public bool IsPaletteOpen { get { return palette.IsOpen || backgroundPalette.IsOpen; } }
        public int Ordinal
        {
            get { return ordinal; }
            set
            {
                int next = Math.Max(0, value);
                if (ordinal == next) return;
                ordinal = next;
                UpdateOrdinal();
            }
        }

        public event EventHandler EditStarted;
        public event EventHandler Committed;
        public event EventHandler Cancelled;
        public event EventHandler DeleteRequested;
        public event EventHandler ApplyToEveryDayRequested;
        public event EventHandler CompletionRequested;

        public TaskCard(TaskItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            Item = item;
            Padding = new Thickness(12);
            Margin = new Thickness(0);
            MinHeight = 48;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            BorderThickness = new Thickness(0, 0, 0, 1);
            BorderBrush = lineBrush;
            CornerRadius = new CornerRadius(0);
            Background = Brushes.White;
            Focusable = true;
            Cursor = Cursors.Arrow;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            FocusVisualStyle = null;

            var content = new Grid { MinHeight = 20 };
            display = new TextBlock
            {
                FontFamily = Typography.TaskFont, FontSize = 14,
                TextWrapping = TextWrapping.Wrap, LineHeight = 20,
                Margin = new Thickness(0, 0, 34, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            editor = new TextBox
            {
                FontFamily = Typography.TaskFont, FontSize = 14,
                TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                AcceptsTab = false, MinHeight = 20,
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Background = Brushes.Transparent, FocusVisualStyle = null,
                Margin = new Thickness(0, 0, 34, 0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Visibility = Visibility.Collapsed
            };
            TextBlock.SetLineHeight(editor, 20);
            AutomationProperties.SetName(editor, "任务");
            content.Children.Add(display);
            content.Children.Add(editor);
            ordinalDisplay = new TextBlock
            {
                FontFamily = Typography.TaskFont, FontSize = 12,
                Foreground = FrozenBrush("#888888"), LineHeight = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0),
                IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            content.Children.Add(ordinalDisplay);

            grip = BuildGrip();
            grip.Margin = new Thickness(-11, 0, 0, 0);
            grip.HorizontalAlignment = HorizontalAlignment.Left;
            grip.VerticalAlignment = VerticalAlignment.Top;
            grip.IsHitTestVisible = true;
            grip.Cursor = Cursors.SizeAll;
            content.Children.Add(grip);

            colorDot = new Ellipse { Width = 5, Height = 5 };
            colorButton = MinimalButton.Create(colorDot, "颜色");
            colorButton.Width = 16;
            colorButton.Height = 20;
            colorButton.Margin = new Thickness(0, 0, 12, 0);
            colorButton.HorizontalAlignment = HorizontalAlignment.Right;
            colorButton.VerticalAlignment = VerticalAlignment.Top;
            colorButton.PreviewMouseLeftButtonDown += (s, e) => paletteInteraction = true;
            colorButton.LostMouseCapture += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsPaletteOpen) return;
                paletteInteraction = false;
                CommitAfterFocusLeaves();
            }), DispatcherPriority.Background);
            colorButton.Click += (s, e) => { OpenPalette(); e.Handled = true; };
            content.Children.Add(colorButton);

            completionSquare = new Border
            {
                Width = 8, Height = 8, BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(0.5), Background = Brushes.White,
                BorderBrush = incompleteBrush
            };
            completionButton = MinimalButton.Create(completionSquare, "完成");
            completionButton.Width = completionButton.Height = 20;
            completionButton.Margin = new Thickness(0, 0, -10, 0);
            completionButton.HorizontalAlignment = HorizontalAlignment.Right;
            completionButton.VerticalAlignment = VerticalAlignment.Top;
            completionButton.PreviewMouseLeftButtonDown += (s, e) => completionInteraction = true;
            completionButton.LostMouseCapture += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                completionInteraction = false;
                CommitAfterFocusLeaves();
            }), DispatcherPriority.Background);
            completionButton.Click += (s, e) =>
            {
                completionInteraction = false;
                bool blankEdit = isEditing && string.IsNullOrWhiteSpace(editor.Text);
                if (isEditing) CommitEdit();
                if (!blankEdit && !string.IsNullOrWhiteSpace(Item.Text)) Raise(CompletionRequested);
                e.Handled = true;
            };
            content.Children.Add(completionButton);
            Child = content;

            palette = new ColorPalette(new[] { "#111111", "#6B7280", "#B94A48", "#C27B2F", "#927538", "#4A7555", "#426787", "#785C93", "#A25B78", "#2D7C7B" },
                false, "自定颜色", ChooseColor, RestoreEditorFocus);
            backgroundPalette = new ColorPalette(new[] { "#FFF4CC", "#FCE4E4", "#FBEAD6", "#E8F0DD", "#DCF0E7", "#E4EFFA", "#EAE6F7", "#F4E3F0", "#EFEAE3", "#EEF0F3" },
                true, "自定背景色", color => ApplyColor(color, true), RestoreEditorFocus);
            palette.Closed += OnPaletteClosed;
            backgroundPalette.Closed += OnPaletteClosed;
            actions = BuildActions();
            ContextMenu = actions;
            // TextBox supplies its own default editing menu. Explicit ownership
            // keeps task actions available while typing as well as on the card.
            editor.ContextMenu = actions;
            display.ContextMenu = actions;
            ordinalDisplay.ContextMenu = actions;
            ContextMenuService.SetContextMenu(grip, actions);
            actions.Opened += (s, e) => RefreshChrome();
            actions.Closed += (s, e) => { RefreshChrome(); CommitAfterFocusLeaves(); };

            MouseEnter += (s, e) => RefreshChrome();
            MouseLeave += (s, e) => RefreshChrome();
            IsKeyboardFocusWithinChanged += (s, e) => RefreshChrome();
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            PreviewKeyDown += OnCardKeyDown;
            editor.PreviewKeyDown += OnEditorKeyDown;
            editor.LostKeyboardFocus += (s, e) =>
            {
                isComposing = false;
                CommitAfterFocusLeaves();
            };
            // IME composition sends Enter as ImeProcessed; also remember the
            // composition lifetime for input methods that send a regular Enter.
            TextCompositionManager.AddPreviewTextInputStartHandler(editor, (s, e) => isComposing = true);
            editor.PreviewTextInput += (s, e) => isComposing = false;
            Unloaded += (s, e) => ClosePalettes();
            RefreshAppearance();
        }

        public void BeginEdit(bool isNew = false)
        {
            if (isEditing) { editor.Focus(); return; }
            ClosePalettes();
            this.isNew = isNew;
            originalText = Item.Text ?? string.Empty;
            originalColor = Item.Color;
            originalBackgroundColor = Item.BackgroundColor;
            editor.Text = originalText;
            isEditing = true;
            isComposing = false;
            display.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            Raise(EditStarted);
            RefreshAppearance();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!isEditing) return;
                editor.Focus();
                editor.SelectAll();
            }), DispatcherPriority.Input);
        }

        public void CommitEdit()
        {
            if (!isEditing) return;
            string text = editor.Text.Trim();
            if (text.Length == 0)
            {
                // Leave the original item intact until the owner records its
                // deletion, so undo can restore the previous text and color.
                Item.Text = originalText;
                Item.Color = originalColor;
                Item.BackgroundColor = originalBackgroundColor;
                FinishEditing();
                Raise(isNew ? Cancelled : DeleteRequested);
                return;
            }
            Item.Text = text;
            FinishEditing();
            Raise(Committed);
        }

        public void CancelEdit()
        {
            if (!isEditing) return;
            Item.Text = originalText;
            Item.Color = originalColor;
            Item.BackgroundColor = originalBackgroundColor;
            FinishEditing();
            Raise(Cancelled);
        }

        private void FinishEditing()
        {
            isEditing = false;
            isComposing = false;
            ClosePalettes();
            editor.Visibility = Visibility.Collapsed;
            display.Visibility = Visibility.Visible;
            RefreshAppearance();
        }

        public void RefreshAppearance()
        {
            display.Text = Item.Text ?? string.Empty;
            UpdateOrdinal();
            Brush brush = ColorPalette.CreateBrush(Item.Color) ?? FrozenBrush("#111111");
            backgroundFill = ColorPalette.CreateBrush(Item.BackgroundColor);
            display.Foreground = editor.Foreground = brush;
            editor.CaretBrush = brush;
            colorDot.Fill = brush;
            completionSquare.Background = Item.IsCompleted ? completedBrush : Brushes.White;
            completionSquare.BorderBrush = Item.IsCompleted ? completedBrush : incompleteBrush;
            string completionName = Item.IsCompleted ? "取消完成" : "完成";
            completionButton.ToolTip = completionName;
            AutomationProperties.SetName(completionButton, completionName);
            AutomationProperties.SetName(this, Item.Text ?? string.Empty);
            RefreshChrome();
        }

        private void UpdateOrdinal()
        {
            double indent = 0;
            if (ordinal > 0)
            {
                ordinalDisplay.Text = ordinal.ToString(CultureInfo.InvariantCulture) + ".";
                ordinalDisplay.Visibility = Visibility.Visible;
                ordinalDisplay.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                indent = Math.Ceiling(ordinalDisplay.DesiredSize.Width) + 5;
            }
            else
            {
                ordinalDisplay.Text = string.Empty;
                ordinalDisplay.Visibility = Visibility.Collapsed;
            }
            display.Margin = editor.Margin = new Thickness(indent, 0, 34, 0);
        }

        public void SetDragAppearance(bool dragging)
        {
            isDragging = dragging;
            RefreshChrome();
        }

        public bool IsDragHandle(DependencyObject source)
        {
            return VisualTree.IsWithin(source, grip);
        }

        private void RefreshChrome()
        {
            bool active = !isDragging && (IsMouseOver || IsKeyboardFocusWithin || isEditing || IsPaletteOpen || actions.IsOpen);
            Background = backgroundFill ?? (active ? hoverBrush : Brushes.White);
            grip.Opacity = active ? 0.7 : 0;
            colorButton.Opacity = active ? 1 : 0;
            colorButton.IsHitTestVisible = active;
            colorButton.IsTabStop = active;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsDragHandle(e.OriginalSource as DependencyObject) || isEditing || e.OriginalSource is TextBox || IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2)
            {
                BeginEdit();
                e.Handled = true;
            }
            else Focus();
        }

        private void OnCardKeyDown(object sender, KeyEventArgs e)
        {
            if (IsPaletteOpen && e.Key == Key.Escape)
            {
                ClosePalettes();
                RestoreEditorFocus();
                e.Handled = true;
                return;
            }
            if (isEditing || e.Handled || IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (e.Key == Key.F2 || e.Key == Key.Enter)
            {
                BeginEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                RequestDelete();
                e.Handled = true;
            }
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed || isComposing) return;
            if (e.Key == Key.Escape)
            {
                CancelEdit();
                Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                CommitEdit();
                Focus();
                e.Handled = true;
            }
        }

        private void CommitAfterFocusLeaves()
        {
            if (!isEditing) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (isEditing && !editor.IsKeyboardFocusWithin && !paletteInteraction && !completionInteraction && !IsPaletteOpen && !actions.IsOpen)
                    CommitEdit();
            }), DispatcherPriority.Background);
        }

        private ContextMenu BuildActions()
        {
            var menu = new ContextMenu { FontFamily = Typography.TaskFont, FontSize = 13 };
            var edit = new MenuItem { Header = "编辑" };
            edit.Click += (s, e) => BeginEdit();
            var color = new MenuItem { Header = "颜色" };
            color.Click += (s, e) => OpenPalette();
            var background = new MenuItem { Header = "背景色" };
            background.Click += (s, e) => OpenBackgroundPalette();
            var delete = new MenuItem { Header = "删除" };
            delete.Click += (s, e) => RequestDelete();
            var everyDay = new MenuItem { Header = "应用到每天" };
            everyDay.Click += (s, e) =>
            {
                bool blankEdit = isEditing && string.IsNullOrWhiteSpace(editor.Text);
                if (isEditing) CommitEdit();
                if (!blankEdit && !string.IsNullOrWhiteSpace(Item.Text)) Raise(ApplyToEveryDayRequested);
            };
            menu.Items.Add(edit);
            menu.Items.Add(color);
            menu.Items.Add(background);
            menu.Items.Add(everyDay);
            menu.Items.Add(delete);
            return menu;
        }

        private void RequestDelete()
        {
            if (isEditing)
            {
                if (isNew && string.IsNullOrWhiteSpace(originalText)) { CancelEdit(); return; }
                Item.Text = originalText;
                Item.Color = originalColor;
                Item.BackgroundColor = originalBackgroundColor;
                FinishEditing();
            }
            Raise(DeleteRequested);
        }

        private void OpenPalette()
        {
            ClosePalettes();
            paletteInteraction = true;
            palette.Open(colorButton, Item.Color);
            RefreshChrome();
        }

        private void OpenBackgroundPalette()
        {
            ClosePalettes();
            paletteInteraction = true;
            backgroundPalette.Open(colorButton, Item.BackgroundColor);
            RefreshChrome();
        }

        private void ChooseColor(string color)
        {
            ApplyColor(color, false);
        }

        private void ApplyColor(string color, bool background)
        {
            string normalized = TaskColors.NormalizeHex(color);
            if (!background && normalized == null) return;
            string current = background ? Item.BackgroundColor : Item.Color;
            if (TaskColors.NormalizeHex(current) == normalized) return;
            if (!isEditing) Raise(EditStarted);
            if (background) Item.BackgroundColor = normalized;
            else Item.Color = normalized;
            RefreshAppearance();
            if (!isEditing) Raise(Committed);
        }

        private void ClosePalettes()
        {
            palette.IsOpen = false;
            backgroundPalette.IsOpen = false;
            paletteInteraction = false;
        }

        private void RestoreEditorFocus()
        {
            paletteInteraction = false;
            if (isEditing) editor.Focus(); else Focus();
            RefreshChrome();
        }

        private void OnPaletteClosed(object sender, EventArgs e)
        {
            paletteInteraction = IsPaletteOpen;
            RefreshChrome();
            CommitAfterFocusLeaves();
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static Brush FrozenBrush(string value)
        {
            return ColorPalette.CreateBrush(value) ?? Brushes.Black;
        }

        private static FrameworkElement BuildGrip()
        {
            var dots = new Canvas { Width = 10, Height = 20, Background = Brushes.Transparent };
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 2; column++)
                {
                    var dot = new Ellipse { Width = 1.3, Height = 1.3, Fill = mutedBrush };
                    Canvas.SetLeft(dot, 2 + column * 3);
                    Canvas.SetTop(dot, 4 + row * 4);
                    dots.Children.Add(dot);
                }
            return dots;
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            return VisualTree.FindAncestor<ButtonBase>(source) != null;
        }
    }
}
