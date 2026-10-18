using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    // App-owned prompts keep their labels in English regardless of Windows language.
    internal static class BoardDialog
    {
        internal static void Show(Window owner, string message, string title)
        {
            Create(owner, message, title).ShowDialog();
        }

        internal static bool Confirm(Window owner, string message, string title, string confirmLabel)
        {
            return Create(owner, message, title, confirmLabel).ShowDialog() == true;
        }

        internal static Window Create(Window owner, string message, string title, string confirmLabel = null)
        {
            var dialog = new Window
            {
                Title = title, FontFamily = Typography.TaskFont, FontSize = 13,
                Background = Brushes.White, SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 340, MaxWidth = Math.Max(340, SystemParameters.WorkArea.Width * .8),
                MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height * .8),
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
            };
            if (owner != null) dialog.Owner = owner;
            var layout = new Grid { Margin = new Thickness(24) };
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(new ScrollViewer
            {
                MaxHeight = Math.Max(100, dialog.MaxHeight - 150),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480,
                    Margin = new Thickness(0, 0, 0, 22)
                }
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (confirmLabel != null)
            {
                var cancel = ActionButton("Cancel");
                cancel.IsCancel = true;
                cancel.IsDefault = true;
                cancel.Click += (s, e) => dialog.DialogResult = false;
                buttons.Children.Add(cancel);
            }
            var accept = ActionButton(confirmLabel ?? "OK");
            accept.IsDefault = confirmLabel == null;
            accept.IsCancel = confirmLabel == null;
            accept.Click += (s, e) => dialog.DialogResult = true;
            buttons.Children.Add(accept);
            Grid.SetRow(buttons, 1);
            layout.Children.Add(buttons);
            dialog.Content = layout;
            return dialog;
        }

        private static Button ActionButton(string label)
        {
            var button = MinimalButton.Create(label, label);
            button.MinWidth = 76;
            button.Height = 32;
            button.Margin = new Thickness(8, 0, 0, 0);
            return button;
        }
    }
}
