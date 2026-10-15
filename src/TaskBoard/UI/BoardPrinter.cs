using System;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    /// <summary>Adapts the shared page renderer to the Windows print dialog.</summary>
    public static class BoardPrinter
    {
        private const double UnitsPerMillimetre = 96.0 / 25.4;

        public static void Print(Window owner, BoardState state)
        {
            if (state == null) return;

            try
            {
                var dialog = new PrintDialog { UserPageRangeEnabled = false };
                var ticket = dialog.PrintTicket ?? new PrintTicket();
                ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                ticket.PageOrientation = PageOrientation.Landscape;
                dialog.PrintTicket = ticket;

                if (dialog.ShowDialog() != true) return;

                ticket = dialog.PrintTicket ?? ticket;
                Size pageSize = GetPageSize(ticket);
                Rect printableArea = new Rect(new Point(), pageSize);
                PrintCapabilities capabilities = dialog.PrintQueue == null
                    ? null : dialog.PrintQueue.GetPrintCapabilities(ticket);
                PageImageableArea imageable = capabilities == null ? null : capabilities.PageImageableArea;
                if (imageable != null && imageable.ExtentWidth > 0 && imageable.ExtentHeight > 0)
                {
                    printableArea = new Rect(imageable.OriginWidth, imageable.OriginHeight,
                        imageable.ExtentWidth, imageable.ExtentHeight);
                    printableArea.Intersect(new Rect(new Point(), pageSize));
                }
                else if (dialog.PrintableAreaWidth > 0 && dialog.PrintableAreaHeight > 0)
                {
                    double width = Math.Min(pageSize.Width, dialog.PrintableAreaWidth);
                    double height = Math.Min(pageSize.Height, dialog.PrintableAreaHeight);
                    printableArea = new Rect((pageSize.Width - width) / 2,
                        (pageSize.Height - height) / 2, width, height);
                }

                dialog.PrintVisual(BoardPageRenderer.BuildPage(state, pageSize, printableArea), "Task Board");
            }
            catch (Exception exception)
            {
                MessageBox.Show(owner, "打印失败：" + exception.Message, "打印",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Size GetPageSize(PrintTicket ticket)
        {
            PageMediaSize media = ticket.PageMediaSize;
            double width = media != null && media.Width.HasValue ? media.Width.Value : 210 * UnitsPerMillimetre;
            double height = media != null && media.Height.HasValue ? media.Height.Value : 297 * UnitsPerMillimetre;
            bool landscape = ticket.PageOrientation == PageOrientation.Landscape ||
                ticket.PageOrientation == PageOrientation.ReverseLandscape;
            return landscape ? new Size(Math.Max(width, height), Math.Min(width, height))
                : new Size(Math.Min(width, height), Math.Max(width, height));
        }

    }
}
