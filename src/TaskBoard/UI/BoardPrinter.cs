using System;
using System.Collections.Generic;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    /// <summary>Previews a committed snapshot and submits the same vector page as a fixed document.</summary>
    public static class BoardPrinter
    {
        private const double UnitsPerMillimetre = 96.0 / 25.4;

        public static void Print(Window owner, BoardState state)
        {
            if (state == null) return;
            try
            {
                var snapshot = WeeklyPlanner.SnapshotForSave(state, state.Tasks);
                DateTime localNow = DateTime.Now;
                var preview = new PrintPreviewWindow(BoardPageRenderer.BuildPage(snapshot, localNow)) { Owner = owner };
                preview.PrintRequested += (s, e) => PrintPage(preview, snapshot, localNow);
                preview.ShowDialog();
            }
            catch (Exception exception)
            {
                MessageBox.Show(owner, "预览失败：" + exception.Message, "打印",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void PrintPage(PrintPreviewWindow preview, BoardState snapshot, DateTime localNow)
        {
            try
            {
                var dialog = new PrintDialog { UserPageRangeEnabled = false };
                var ticket = new PrintTicket();
                ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                ticket.PageOrientation = PageOrientation.Landscape;
                ticket.OutputQuality = OutputQuality.High;
                ticket.PageResolution = new PageResolution(600, 600);
                ticket = ValidateInitialTicket(ticket, requested =>
                {
                    var defaultQueue = dialog.PrintQueue;
                    return defaultQueue == null ? requested : defaultQueue.MergeAndValidatePrintTicket(
                        defaultQueue.DefaultPrintTicket ?? new PrintTicket(), requested).ValidatedPrintTicket;
                });
                dialog.PrintTicket = ticket;

                if (dialog.ShowDialog() != true) return;

                ticket = dialog.PrintTicket ?? ticket;
                var queue = dialog.PrintQueue;
                if (queue == null) throw new InvalidOperationException("未选择可用的打印机。");
                ticket = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket ?? new PrintTicket(), ticket).ValidatedPrintTicket;
                dialog.PrintTicket = ticket;
                PrintCapabilities capabilities = queue.GetPrintCapabilities(ticket);
                Size pageSize = ResolvePageSize(ticket, capabilities == null ? null : capabilities.PageMediaSizeCapability);
                Rect printableArea = new Rect(new Point(), pageSize);
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

                var page = BoardPageRenderer.BuildPage(snapshot, pageSize, printableArea, localNow);
                preview.SetPage(page);
                var document = BoardPrintDocument.Create(page);
                dialog.PrintDocument(document.DocumentPaginator, "Task Board");
            }
            catch (Exception exception)
            {
                MessageBox.Show(preview, "打印失败：" + exception.Message, "打印",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal static PrintTicket ValidateInitialTicket(PrintTicket requested, Func<PrintTicket, PrintTicket> validate)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            if (validate == null) throw new ArgumentNullException("validate");
            // A broken default provider must not prevent choosing another queue.
            try { return validate(requested) ?? requested; }
            catch (PrintSystemException) { return requested; }
            catch (COMException) { return requested; }
            catch (InvalidOperationException) { return requested; }
        }

        internal static Size ResolvePageSize(PrintTicket ticket, IEnumerable<PageMediaSize> supportedMedia)
        {
            if (ticket == null) throw new ArgumentNullException("ticket");
            PageMediaSize media = ticket.PageMediaSize;
            Size size;
            if (!TryDimensions(media, out size))
            {
                bool resolved = false;
                if (media != null && media.PageMediaSizeName.HasValue && supportedMedia != null)
                    foreach (PageMediaSize option in supportedMedia)
                        if (option != null && option.PageMediaSizeName == media.PageMediaSizeName && TryDimensions(option, out size))
                        { resolved = true; break; }
                if (!resolved && !TryStandardSize(media == null ? null : media.PageMediaSizeName, out size))
                    throw new InvalidOperationException("打印机未提供所选纸张的有效尺寸，请重新选择纸张。");
            }
            bool landscape = ticket.PageOrientation == PageOrientation.Landscape ||
                ticket.PageOrientation == PageOrientation.ReverseLandscape;
            bool portrait = ticket.PageOrientation == PageOrientation.Portrait ||
                ticket.PageOrientation == PageOrientation.ReversePortrait;
            if (landscape) return new Size(Math.Max(size.Width, size.Height), Math.Min(size.Width, size.Height));
            if (portrait) return new Size(Math.Min(size.Width, size.Height), Math.Max(size.Width, size.Height));
            return size;
        }

        private static bool TryDimensions(PageMediaSize media, out Size size)
        {
            size = new Size();
            if (media == null || !media.Width.HasValue || !media.Height.HasValue) return false;
            double width = media.Width.Value;
            double height = media.Height.Value;
            if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height) ||
                double.IsInfinity(width) || double.IsInfinity(height)) return false;
            size = new Size(width, height);
            return true;
        }

        private static bool TryStandardSize(PageMediaSizeName? name, out Size size)
        {
            switch (name)
            {
                case PageMediaSizeName.ISOA3: size = new Size(297 * UnitsPerMillimetre, 420 * UnitsPerMillimetre); return true;
                case PageMediaSizeName.ISOA4: size = new Size(210 * UnitsPerMillimetre, 297 * UnitsPerMillimetre); return true;
                case PageMediaSizeName.ISOA5: size = new Size(148 * UnitsPerMillimetre, 210 * UnitsPerMillimetre); return true;
                case PageMediaSizeName.NorthAmericaLetter: size = new Size(8.5 * 96, 11 * 96); return true;
                case PageMediaSizeName.NorthAmericaLegal: size = new Size(8.5 * 96, 14 * 96); return true;
                case PageMediaSizeName.NorthAmericaTabloid: size = new Size(11 * 96, 17 * 96); return true;
                default: size = new Size(); return false;
            }
        }
    }
}
