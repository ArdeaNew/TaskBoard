using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskBoard.UI
{
    internal static class MinimalButton
    {
        public static Button Create(object content, string name)
        {
            var button = new Button
            {
                Content = content, ToolTip = name, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Foreground = Brushes.Black, Cursor = Cursors.Hand, FocusVisualStyle = null
            };
            AutomationProperties.SetName(button, name);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(242, 242, 242))));
            template.Triggers.Add(hover);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabled);
            button.Template = template;
            return button;
        }
    }
}
