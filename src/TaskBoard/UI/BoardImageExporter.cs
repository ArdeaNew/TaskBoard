using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TaskBoard.Core;
using TaskBoard.Rendering;

namespace TaskBoard.UI
{
    public static class BoardImageExporter
    {
        public static void Export(Window owner, BoardState state)
        {
            var selected = string.IsNullOrEmpty(state.ActiveWeekStart) ? DateTime.Now : WeeklyPlanner.CurrentWeekStart(state);
            var metrics = DateMetrics.From(selected);
            var dialog = new SaveFileDialog
            {
                Filter = "PNG (*.png)|*.png", DefaultExt = ".png", AddExtension = true,
                FileName = "TaskBoard-" + metrics.WeekYear.ToString(CultureInfo.InvariantCulture) +
                    "-W" + metrics.WeekNumber.ToString("00", CultureInfo.InvariantCulture) + ".png"
            };
            if (dialog.ShowDialog(owner) != true) return;
            try { Save(state, dialog.FileName); }
            catch (Exception error)
            {
                MessageBox.Show(owner, "导出失败：" + error.Message, "Task", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // The selected week's complete vector layout includes every off-screen task.
        public static void Save(BoardState state, string path)
        {
            var page = BoardPageRenderer.BuildPage(state);
            Rect bounds = page.ContentBounds;
            const double scale = 2;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * scale),
                (int)Math.Ceiling(bounds.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            string temporary = Path.Combine(directory, ".TaskBoard-image-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { encoder.Save(stream); stream.Flush(true); }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
