using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskBoard.Tools
{
    // Offline geometric counterpart of assets/TaskBoard.svg. Small ICO images
    // hint the same paths to the pixel grid so thin dividers stay legible.
    internal static class GenerateIcon
    {
        private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: GenerateIcon.exe <output-directory>");
                return 1;
            }
            string output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            var images = new List<byte[]>();
            foreach (int size in Sizes) images.Add(RenderPng(size));
            WriteIcon(Path.Combine(output, "TaskBoard.ico"), images);
            Console.WriteLine(Path.Combine(output, "TaskBoard.ico"));
            return 0;
        }

        private static byte[] RenderPng(int size)
        {
            double scale = size / 256.0;
            bool small = size <= 32;
            double frameWidth = Math.Max(5 * scale, small ? 1.4 : 1);
            double gridWidth = Math.Max(3.5 * scale, small ? 1.15 : 0.85);
            double left = Hint(31 * scale, frameWidth);
            double top = Hint(36 * scale, frameWidth);
            double right = Hint(225 * scale, frameWidth);
            double bottom = Hint(220 * scale, frameWidth);
            var visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRoundedRectangle(Brush(250, 250, 248),
                    new Pen(Brush(230, 230, 227), Math.Max(1.5 * scale, 0.45)),
                    new Rect(8 * scale, 8 * scale, 240 * scale, 240 * scale),
                    48 * scale, 48 * scale);
                var frame = new Pen(Brush(32, 32, 32), frameWidth);
                var grid = new Pen(Brush(32, 32, 32), gridWidth);
                drawing.DrawRectangle(null, frame, new Rect(left, top, right - left, bottom - top));
                double headerY = Hint(82 * scale, gridWidth);
                drawing.DrawLine(grid, new Point(left, headerY), new Point(right, headerY));
                foreach (double x in new[] { 79.5, 128.0, 176.5 })
                {
                    double divider = Hint(x * scale, gridWidth);
                    drawing.DrawLine(grid, new Point(divider, top), new Point(divider, bottom));
                }
                double accentX = small ? Math.Round(186 * scale) : 186 * scale;
                double accentY = small ? Math.Max(Math.Ceiling(headerY + gridWidth / 2), Math.Round(102 * scale)) : 102 * scale;
                double accentSize = small ? Math.Max(2, Math.Round(30 * scale)) : 30 * scale;
                drawing.DrawRoundedRectangle(Brush(0, 47, 167), null,
                    new Rect(accentX, accentY, accentSize, accentSize),
                    small ? 0 : 1.25 * scale, small ? 0 : 1.25 * scale);
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static double Hint(double coordinate, double stroke)
        {
            return stroke <= 1.5 ? Math.Floor(coordinate) + 0.5 : coordinate;
        }

        private static SolidColorBrush Brush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static void WriteIcon(string path, IList<byte[]> images)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)Sizes.Length);
                uint offset = (uint)(6 + 16 * Sizes.Length);
                for (int index = 0; index < Sizes.Length; index++)
                {
                    byte dimension = Sizes[index] == 256 ? (byte)0 : (byte)Sizes[index];
                    writer.Write(dimension);
                    writer.Write(dimension);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)images[index].Length);
                    writer.Write(offset);
                    offset += (uint)images[index].Length;
                }
                foreach (byte[] image in images) writer.Write(image);
            }
        }
    }
}
