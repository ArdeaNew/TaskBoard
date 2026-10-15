using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    internal sealed class PrintPreviewWindow : Window
    {
        private readonly PageVisual pageVisual;
        private readonly ScrollViewer scroll;
        private readonly TextBlock zoomLabel;
        private bool fitMode = true;
        internal event EventHandler PrintRequested;
        internal BoardPage Page { get { return pageVisual.Page; } }
        internal double ZoomScale { get { return pageVisual.ZoomScale; } }

        internal PrintPreviewWindow(BoardPage page)
        {
            Title = "Print preview";
            FontFamily = Typography.TaskFont;
            FontSize = 13;
            Background = Brushes.White;
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(1200, workArea.Width * .85);
            Height = Math.Min(900, workArea.Height * .85);
            MinWidth = Math.Min(600, workArea.Width);
            MinHeight = Math.Min(400, workArea.Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            root.RowDefinitions.Add(new RowDefinition());
            var toolbar = new DockPanel { Margin = new Thickness(12, 6, 12, 6), LastChildFill = false };
            var print = ToolbarButton("Print", "Print", 70);
            print.Click += (s, e) => { var handler = PrintRequested; if (handler != null) handler(this, EventArgs.Empty); };
            DockPanel.SetDock(print, Dock.Right);
            toolbar.Children.Add(print);
            var minus = ToolbarButton("−", "Zoom out", 32);
            minus.Click += (s, e) => SetZoom(ZoomScale / 1.2);
            toolbar.Children.Add(minus);
            zoomLabel = new TextBlock { Width = 56, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            toolbar.Children.Add(zoomLabel);
            var plus = ToolbarButton("+", "Zoom in", 32);
            plus.Click += (s, e) => SetZoom(ZoomScale * 1.2);
            toolbar.Children.Add(plus);
            var fit = ToolbarButton("Fit page", "Fit page", 92);
            fit.Margin = new Thickness(10, 0, 0, 0);
            fit.Click += (s, e) => FitToPage();
            toolbar.Children.Add(fit);
            root.Children.Add(toolbar);
            pageVisual = new PageVisual(page);
            scroll = new ScrollViewer
            {
                Background = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Child = pageVisual, Margin = new Thickness(24), Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)), BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top
                }
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;
            Loaded += (s, e) => FitToPage();
            scroll.SizeChanged += (s, e) => { if (fitMode) FitToPage(); };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    var handler = PrintRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                    e.Handled = true;
                }
            };
            RefreshZoomLabel();
        }

        internal void SetPage(BoardPage page)
        {
            pageVisual.SetPage(page);
            if (fitMode) FitToPage();
        }

        internal void SetZoom(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException("value");
            fitMode = false;
            pageVisual.ZoomScale = Math.Max(.1, Math.Min(4, value));
            RefreshZoomLabel();
        }

        internal void FitToPage()
        {
            fitMode = true;
            double width = scroll.ActualWidth - 52;
            double height = scroll.ActualHeight - 52;
            if (width <= 0 || height <= 0) return;
            pageVisual.ZoomScale = Math.Max(.1, Math.Min(4,
                Math.Min(width / Page.PageSize.Width, height / Page.PageSize.Height)));
            RefreshZoomLabel();
        }

        private void RefreshZoomLabel()
        {
            zoomLabel.Text = (ZoomScale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static Button ToolbarButton(string content, string name, double width)
        {
            var button = MinimalButton.Create(content, name);
            button.Width = width;
            button.Height = 32;
            button.VerticalAlignment = VerticalAlignment.Center;
            return button;
        }
    }
}
