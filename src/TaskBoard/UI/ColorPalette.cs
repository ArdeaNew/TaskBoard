using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    // One palette implementation serves text color and optional task fill.
    internal sealed class ColorPalette : Popup
    {
        private static readonly Brush lineBrush = CreateBrush("#DDDDDD");
        private TextBox hexEditor;
        private readonly string[] colors;
        private readonly string accessibleName;
        private readonly bool allowClear;
        private readonly Action<string> selected;
        private readonly Action restoreFocus;

        internal ColorPalette(string[] colors, bool allowClear, string accessibleName,
            Action<string> selected, Action restoreFocus)
        {
            this.colors = colors;
            this.allowClear = allowClear;
            this.accessibleName = accessibleName;
            this.selected = selected;
            this.restoreFocus = restoreFocus;
            Placement = PlacementMode.Bottom;
            HorizontalOffset = -145;
            VerticalOffset = 4;
            AllowsTransparency = true;
            StaysOpen = false;
            PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None;
        }

        private void EnsureContent()
        {
            if (Child != null) return;
            var stack = new StackPanel();
            var swatches = new UniformGrid { Rows = 2, Columns = 5 };
            foreach (string value in colors)
            {
                string choice = value;
                Shape swatch = allowClear
                    ? (Shape)new Rectangle { RadiusX = 1, RadiusY = 1, Stroke = lineBrush, StrokeThickness = 0.5 }
                    : new Ellipse();
                swatch.Width = swatch.Height = 13;
                swatch.Fill = CreateBrush(value);
                var button = MinimalButton.Create(swatch, value);
                button.Width = 31;
                button.Height = 29;
                button.Click += (s, e) => { Select(choice); e.Handled = true; };
                swatches.Children.Add(button);
            }
            stack.Children.Add(swatches);
            hexEditor = new TextBox
            {
                FontFamily = Typography.TaskFont, FontSize = 12,
                MaxLength = 7, Margin = new Thickness(5, 7, 5, 3),
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = lineBrush, BorderThickness = new Thickness(1),
                Background = Brushes.White, Foreground = Brushes.Black, ToolTip = "#RRGGBB"
            };
            InputMethod.SetIsInputMethodEnabled(hexEditor, false);
            AutomationProperties.SetName(hexEditor, accessibleName);
            hexEditor.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                string color = TaskColors.NormalizeHex(hexEditor.Text);
                if (color != null) Select(color);
                else hexEditor.BorderBrush = CreateBrush("#B94A48");
                e.Handled = true;
            };
            hexEditor.TextChanged += (s, e) => hexEditor.BorderBrush = lineBrush;
            stack.Children.Add(hexEditor);
            if (allowClear)
            {
                var clear = MinimalButton.Create("无填充", "无填充");
                clear.FontFamily = Typography.TaskFont;
                clear.FontSize = 12;
                clear.Height = 25;
                clear.Margin = new Thickness(5, 2, 5, 2);
                clear.Click += (s, e) => { Select(null); e.Handled = true; };
                stack.Children.Add(clear);
            }
            var frame = new Border
            {
                Background = Brushes.White, BorderBrush = lineBrush,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6), Child = stack
            };
            frame.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                IsOpen = false;
                restoreFocus();
                e.Handled = true;
            };
            Child = frame;
        }

        internal void Open(UIElement target, string currentColor)
        {
            EnsureContent();
            PlacementTarget = target;
            hexEditor.Text = TaskColors.NormalizeHex(currentColor) ?? (allowClear ? "#FFFFFF" : "#111111");
            IsOpen = true;
        }

        private void Select(string color)
        {
            try { selected(color); }
            finally
            {
                IsOpen = false;
                restoreFocus();
            }
        }

        internal static SolidColorBrush CreateBrush(string value)
        {
            string color = TaskColors.NormalizeHex(value);
            if (color == null) return null;
            var brush = new SolidColorBrush(Color.FromRgb(
                byte.Parse(color.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(color.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(color.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            brush.Freeze();
            return brush;
        }

    }
}
