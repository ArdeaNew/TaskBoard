using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using System.Xml;
using TaskBoard.Core;
using TaskBoard.Rendering;
using TaskBoard.UI;

namespace TaskBoard.Tests
{
    public static class PrintPipelineTests
    {
        private static readonly Size A4 = new Size(297 * 96.0 / 25.4, 210 * 96.0 / 25.4);
        private static readonly DateTime TestDate = new DateTime(2026, 10, 15, 12, 0, 0);
        private static int checks;

        [STAThread]
        public static int Main()
        {
            try
            {
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                PaperSizeResolution();
                InitialTicketValidation();
                BoardPage page = SamplePage();
                FixedDocumentPage(page);
                XpsRoundTrip(page);
                PreviewUsesSameVectorPage(page);
                Console.WriteLine("Print pipeline: " + checks + " checks passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static BoardPage SamplePage()
        {
            var state = new BoardState { ActiveWeekStart = "2026-12-28" };
            state.Tasks.Add(new TaskItem
            {
                Column = 1, Text = "中文任务 English 123\n保持清晰", Color = "#315FA3",
                BackgroundColor = "#FFF2CC", IsCompleted = true
            });
            state.Tasks.Add(new TaskItem { Column = 1, Text = "下一项 / Next", BackgroundColor = "#DDEBF7" });
            state.Tasks.Add(new TaskItem { Column = 7, Text = "最后一天 Sunday", Color = "#8F423D" });
            return BoardPageRenderer.BuildPage(state, A4, new Rect(new Point(), A4), TestDate);
        }

        private static void PaperSizeResolution()
        {
            var sizes = new[]
            {
                new { Name = PageMediaSizeName.ISOA4, Width = 210 * 96.0 / 25.4, Height = 297 * 96.0 / 25.4 },
                new { Name = PageMediaSizeName.ISOA3, Width = 297 * 96.0 / 25.4, Height = 420 * 96.0 / 25.4 },
                new { Name = PageMediaSizeName.NorthAmericaLetter, Width = 8.5 * 96, Height = 11 * 96.0 }
            };
            foreach (var standard in sizes)
                foreach (PageOrientation orientation in new[] { PageOrientation.Portrait, PageOrientation.Landscape,
                    PageOrientation.ReversePortrait, PageOrientation.ReverseLandscape })
                {
                    var ticket = new PrintTicket { PageMediaSize = new PageMediaSize(standard.Name), PageOrientation = orientation };
                    Assert(!ticket.PageMediaSize.Width.HasValue && !ticket.PageMediaSize.Height.HasValue,
                        "Fixture exercises named-only paper rather than implicit A4 dimensions.");
                    Size actual = BoardPrinter.ResolvePageSize(ticket, null);
                    bool landscape = orientation == PageOrientation.Landscape || orientation == PageOrientation.ReverseLandscape;
                    Size expected = landscape ? new Size(standard.Height, standard.Width) : new Size(standard.Width, standard.Height);
                    Assert(SameSize(actual, expected), "Named paper has its own physical dimensions and orientation: " + standard.Name + ".");
                }

            var named = new PrintTicket { PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA3), PageOrientation = PageOrientation.Landscape };
            var media = new[] { new PageMediaSize(PageMediaSizeName.ISOA4, 600, 800), new PageMediaSize(PageMediaSizeName.ISOA3, 777, 1111) };
            Assert(SameSize(BoardPrinter.ResolvePageSize(named, media), new Size(1111, 777)),
                "Selected queue's matching media dimensions take precedence over standard fallbacks.");
            named.PageOrientation = PageOrientation.Portrait;
            Assert(SameSize(BoardPrinter.ResolvePageSize(named, media), new Size(777, 1111)),
                "Queue media dimensions are oriented for portrait.");
            named.PageMediaSize = new PageMediaSize(920, 640);
            Assert(SameSize(BoardPrinter.ResolvePageSize(named, media), new Size(640, 920)),
                "Explicit custom paper dimensions take precedence over capabilities.");

            var unsupported = new PrintTicket { PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA0) };
            bool rejected = false;
            try { BoardPrinter.ResolvePageSize(unsupported, null); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Unresolved paper dimensions produce a clear failure instead of silently becoming A4.");
            Assert(SameSize(BoardPrinter.ResolvePageSize(unsupported,
                new[] { new PageMediaSize(PageMediaSizeName.ISOA0, 841 * 96.0 / 25.4, 1189 * 96.0 / 25.4) }),
                new Size(841 * 96.0 / 25.4, 1189 * 96.0 / 25.4)),
                "Other named paper sizes resolve through queue capabilities without any printer access.");
        }

        private static void InitialTicketValidation()
        {
            var requested = new PrintTicket { OutputQuality = OutputQuality.High, PageResolution = new PageResolution(600, 600) };
            var validated = new PrintTicket { OutputQuality = OutputQuality.Normal, PageResolution = new PageResolution(300, 300) };
            Assert(ReferenceEquals(BoardPrinter.ValidateInitialTicket(requested, ticket => validated), validated),
                "Supported default-queue validation uses its validated ticket.");
            Assert(ReferenceEquals(BoardPrinter.ValidateInitialTicket(requested,
                ticket => { throw new System.Runtime.InteropServices.COMException("Synthetic unavailable default queue"); }), requested),
                "Unavailable default provider retains requested settings so another printer can be chosen.");
            Assert(requested.OutputQuality == OutputQuality.High && requested.PageResolution.X == 600 && requested.PageResolution.Y == 600,
                "Best-effort fallback retains the requested high quality and 600 DPI defaults.");
        }

        private static bool SameSize(Size first, Size second)
        {
            return Math.Abs(first.Width - second.Width) < .001 && Math.Abs(first.Height - second.Height) < .001;
        }

        private static void FixedDocumentPage(BoardPage page)
        {
            FixedDocument document = BoardPrintDocument.Create(page);
            document.DocumentPaginator.ComputePageCount();
            Assert(document.DocumentPaginator.PageCount == 1, "Printing creates exactly one fixed page.");
            Assert(document.DocumentPaginator.PageSize == page.PageSize,
                "Paginator keeps the selected physical paper size.");
            DocumentPage fixedPage = document.DocumentPaginator.GetPage(0);
            Assert(fixedPage.Size == page.PageSize, "Fixed document page has unscaled A4 landscape dimensions.");
            PageVisual display = Descendants<PageVisual>(fixedPage.Visual).Single();
            Assert(ReferenceEquals(display.Page, page), "Fixed document draws the same vector page that was previewed.");
            Assert(Math.Abs(display.ZoomScale - 1) < 0.001, "Printing does not inherit preview zoom.");
        }

        private static void XpsRoundTrip(BoardPage page)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print-vector-roundtrip.xps");
            using (var xps = new XpsDocument(path, FileAccess.Write))
                XpsDocument.CreateXpsDocumentWriter(xps).Write(BoardPrintDocument.Create(page));

            const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
            int pathCount = 0;
            using (Package package = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                foreach (PackagePart part in package.GetParts())
                {
                    Assert(!part.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
                        "XPS has no raster image resources.");
                    Assert(part.ContentType.IndexOf("opentype", StringComparison.OrdinalIgnoreCase) < 0,
                        "XPS does not depend on embedded or runtime composite fonts.");
                    if (!part.Uri.OriginalString.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase)) continue;
                    var xml = new XmlDocument();
                    using (Stream stream = part.GetStream(FileMode.Open, FileAccess.Read)) xml.Load(stream);
                    Assert(xml.GetElementsByTagName("Glyphs", xpsNamespace).Count == 0,
                        "Serialized text uses outlines instead of font-dependent Glyphs.");
                    Assert(xml.GetElementsByTagName("ImageBrush", xpsNamespace).Count == 0,
                        "Serialized print page contains no bitmap text or page image.");
                    pathCount += xml.GetElementsByTagName("Path", xpsNamespace).Count;
                }
            }
            Assert(pathCount >= page.TextRuns.Count, "Every visible text run is preserved as XPS vector paths.");

            RenderTargetBitmap original = Render(page);
            using (var xps = new XpsDocument(path, FileAccess.Read))
            {
                FixedDocumentSequence sequence = xps.GetFixedDocumentSequence();
                sequence.DocumentPaginator.ComputePageCount();
                Assert(sequence.DocumentPaginator.PageCount == 1, "Reopened XPS retains a single page.");
                DocumentPage reopened = sequence.DocumentPaginator.GetPage(0);
                Assert(reopened.Size == page.PageSize, "Reopened XPS retains physical paper dimensions.");
                RenderTargetBitmap restored = Render(reopened.Visual);
                foreach (PageTextRun run in page.TextRuns)
                {
                    Rect region = run.Bounds;
                    region.Inflate(1, 1);
                    int expected = DarkPixels(original, region);
                    int actual = DarkPixels(restored, region);
                    Assert(expected > 0 && actual > 0, "Visible text survives XPS reopening: " + run.Text + ".");
                    Assert(Math.Abs(expected - actual) <= Math.Max(8, expected * 0.025),
                        "CJK and Latin outline ink is preserved after print serialization: " + run.Text + ".");
                }
                Assert(SamplePixel(restored, new Point(35, 110)) == Color.FromRgb(255, 242, 204),
                    "Full task background fill survives XPS serialization.");
                SaveBitmap(restored, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print-xps-preview.png"));
            }
        }

        private static void PreviewUsesSameVectorPage(BoardPage page)
        {
            var preview = new PrintPreviewWindow(page);
            try
            {
                var content = (FrameworkElement)preview.Content;
                content.Measure(new Size(1120, 720));
                content.Arrange(new Rect(0, 0, 1120, 720));
                content.UpdateLayout();
                Assert(ReferenceEquals(preview.Page, page), "Preview holds the actual printable page.");
                PageVisual visual = Descendants<PageVisual>(content).Single();
                Assert(ReferenceEquals(visual.Page, page), "Preview draws vector geometry directly from the printable page.");
                preview.SetZoom(1.5);
                Assert(Math.Abs(preview.ZoomScale - 1.5) < 0.001 && Math.Abs(visual.ZoomScale - 1.5) < 0.001,
                    "Preview zoom updates its page display without rebuilding text.");
                Assert(ReferenceEquals(preview.Page, page) && page.PageSize == A4,
                    "Zoom never changes printable geometry or physical size.");
                int printRequests = 0;
                preview.PrintRequested += (sender, args) => printRequests++;
                Button print = Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Print");
                print.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(printRequests == 1, "Preview print button requests printing without a separate bitmap conversion.");

                preview.FitToPage();
                content.UpdateLayout();
                var previewBitmap = new RenderTargetBitmap(1120, 720, 96, 96, PixelFormats.Pbgra32);
                var backdrop = new DrawingVisual();
                using (DrawingContext drawing = backdrop.RenderOpen())
                    drawing.DrawRectangle(preview.Background, null, new Rect(0, 0, 1120, 720));
                previewBitmap.Render(backdrop);
                previewBitmap.Render(content);
                SaveBitmap(previewBitmap, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print-preview-window.png"));

                var second = BoardPageRenderer.BuildPage(new BoardState(), new Size(900, 650),
                    new Rect(0, 0, 900, 650), TestDate);
                preview.SetPage(second);
                Assert(ReferenceEquals(preview.Page, second) && ReferenceEquals(visual.Page, second),
                    "Changing printer paper updates the displayed and printable page together.");
                preview.FitToPage();
                Assert(preview.ZoomScale > 0 && !double.IsNaN(preview.ZoomScale) && !double.IsInfinity(preview.ZoomScale),
                    "Fit-to-page produces a valid zoom after paper changes.");
            }
            finally { preview.Close(); }
        }

        private static RenderTargetBitmap Render(Visual visual)
        {
            const double dpi = 192;
            var element = visual as UIElement;
            if (element != null)
            {
                element.Measure(A4);
                element.Arrange(new Rect(new Point(), A4));
                element.UpdateLayout();
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(A4.Width * dpi / 96),
                (int)Math.Ceiling(A4.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private static int DarkPixels(BitmapSource bitmap, Rect region)
        {
            const double scale = 2;
            int left = Math.Max(0, (int)Math.Floor(region.Left * scale));
            int top = Math.Max(0, (int)Math.Floor(region.Top * scale));
            int right = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(region.Right * scale));
            int bottom = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(region.Bottom * scale));
            int stride = (right - left) * 4;
            var pixels = new byte[stride * (bottom - top)];
            bitmap.CopyPixels(new Int32Rect(left, top, right - left, bottom - top), pixels, stride, 0);
            int count = 0;
            for (int index = 0; index < pixels.Length; index += 4)
                if (pixels[index + 3] > 0 && Math.Min(pixels[index], Math.Min(pixels[index + 1], pixels[index + 2])) < 150) count++;
            return count;
        }

        private static Color SamplePixel(BitmapSource bitmap, Point point)
        {
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)(point.X * 2), (int)(point.Y * 2), 1, 1), pixel, 4, 0);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }

        private static void SaveBitmap(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write)) encoder.Save(stream);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            var match = parent as T;
            if (match != null) yield return match;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                foreach (T child in Descendants<T>(VisualTreeHelper.GetChild(parent, index))) yield return child;
        }

        private static void Assert(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
