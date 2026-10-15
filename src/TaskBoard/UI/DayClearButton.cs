using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    public sealed class DayClearButton : Button
    {
        private static readonly Brush lineBrush = MakeBrush(221, 221, 221);
        private static readonly Brush hoverBrush = MakeBrush(247, 247, 247);
        private readonly Border chrome;
        private readonly DispatcherTimer revealTimer;
        private bool available;
        private bool revealed;

        public event EventHandler ClearRequested;

        public bool IsAvailable
        {
            get { return available; }
            set
            {
                if (available == value) return;
                available = value;
                IsEnabled = value;
                IsTabStop = value;
                if (!value)
                {
                    revealTimer.Stop();
                    SetRevealed(false, false);
                }
                else if (IsKeyboardFocusWithin) SetRevealed(true, true);
                else if (IsMouseOver) StartDwell();
            }
        }

        internal bool IsRevealed { get { return revealed; } }
        internal bool IsRevealPending { get { return revealTimer.IsEnabled; } }

        public DayClearButton()
        {
            Height = 40;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            Padding = new Thickness(0);
            Margin = new Thickness(0);
            FontFamily = Typography.TaskFont;
            FontSize = 14;
            FocusVisualStyle = null;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            IsEnabled = false;
            IsTabStop = false;
            AutomationProperties.SetName(this, "清空今日任务");

            chrome = new Border
            {
                Background = Brushes.White, BorderBrush = lineBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0), Padding = new Thickness(6, 0, 6, 0),
                Opacity = 0,
                Child = new TextBlock
                {
                    Text = "清空今日任务", FontFamily = Typography.TaskFont, FontSize = 14,
                    Foreground = MakeBrush(112, 112, 112),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Content = chrome;
            Template = CreateTemplate();
            revealTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            revealTimer.Tick += (s, e) =>
            {
                revealTimer.Stop();
                if (available && (IsMouseOver || IsKeyboardFocusWithin)) SetRevealed(true, true);
            };
            MouseEnter += (s, e) =>
            {
                if (!available) return;
                if (IsKeyboardFocusWithin) SetRevealed(true, true);
                else StartDwell();
            };
            MouseLeave += (s, e) =>
            {
                revealTimer.Stop();
                if (!IsKeyboardFocusWithin) SetRevealed(false, true);
            };
            IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if (!available) return;
                if (IsKeyboardFocusWithin)
                {
                    revealTimer.Stop();
                    SetRevealed(true, true);
                }
                else if (!IsMouseOver) SetRevealed(false, true);
            };
            PreviewMouseLeftButtonDown += (s, e) =>
            {
                // A click on the invisible region must not focus, reveal and
                // activate the button before its pointer dwell has completed.
                if (available && revealed) return;
                e.Handled = true;
                if (available) StartDwell();
            };
            Click += (s, e) =>
            {
                if (!available || !revealed)
                {
                    e.Handled = true;
                    return;
                }
                var handler = ClearRequested;
                if (handler != null) handler(this, EventArgs.Empty);
                e.Handled = true;
            };
            Loaded += (s, e) =>
            {
                if (available && IsMouseOver) StartDwell();
            };
            Unloaded += (s, e) =>
            {
                revealTimer.Stop();
                SetRevealed(false, false);
            };
        }

        private void StartDwell()
        {
            if (!available || revealed || revealTimer.IsEnabled) return;
            revealTimer.Start();
        }

        private void SetRevealed(bool value, bool animate)
        {
            value = value && available;
            revealed = value;
            chrome.Background = value ? hoverBrush : Brushes.White;
            double target = value ? 1 : 0;
            double current = chrome.Opacity;
            chrome.BeginAnimation(OpacityProperty, null);
            chrome.Opacity = target;
            if (!animate || !SystemParameters.ClientAreaAnimation || Math.Abs(current - target) < 0.001) return;
            chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }

        private static ControlTemplate CreateTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var region = new FrameworkElementFactory(typeof(Border));
            region.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Stretch);
            region.AppendChild(presenter);
            template.VisualTree = region;
            return template;
        }

        private static Brush MakeBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
