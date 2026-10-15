using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TaskBoard.UI
{
    // FLIP: keep the visual position continuous while the actual layout changes.
    internal sealed class ColumnPanel : Panel
    {
        private readonly Dictionary<UIElement, double> positions = new Dictionary<UIElement, double>();
        public bool Animate { get; set; } = true;

        public void ForgetPosition(UIElement child)
        {
            positions.Remove(child);
            var transform = child.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double height = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                height += child.DesiredSize.Height;
            }
            return new Size(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, height + 48);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double y = 0;
            foreach (UIElement child in InternalChildren)
            {
                double old;
                var transform = child.RenderTransform as TranslateTransform;
                if (transform == null) child.RenderTransform = transform = new TranslateTransform();
                bool known = positions.TryGetValue(child, out old);
                double from = known ? old + transform.Y - y : 0;
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                positions[child] = y;
                if (Animate && SystemParameters.ClientAreaAnimation && Math.Abs(from) > 0.5)
                    transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(170))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else transform.BeginAnimation(TranslateTransform.YProperty, null);
                y += child.DesiredSize.Height;
            }
            foreach (var stale in positions.Keys.Where(c => !InternalChildren.Contains(c)).ToArray()) positions.Remove(stale);
            return finalSize;
        }
    }
}
