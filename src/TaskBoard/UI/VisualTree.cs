using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TaskBoard.UI
{
    internal static class VisualTree
    {
        public static T FindAncestor<T>(DependencyObject item) where T : DependencyObject
        {
            while (item != null)
            {
                var match = item as T;
                if (match != null) return match;
                item = ParentOf(item);
            }
            return null;
        }

        public static bool IsWithin(DependencyObject item, DependencyObject ancestor)
        {
            while (item != null)
            {
                if (item == ancestor) return true;
                item = ParentOf(item);
            }
            return false;
        }

        private static DependencyObject ParentOf(DependencyObject item)
        {
            return item is Visual || item is Visual3D
                ? VisualTreeHelper.GetParent(item)
                : LogicalTreeHelper.GetParent(item);
        }
    }
}
