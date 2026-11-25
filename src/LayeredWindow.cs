using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CpuHealth
{
    internal static class LayeredWindow
    {
        public const int ExtendedStyle = 0x00080000; // WS_EX_LAYERED

        public static bool Update(IntPtr window, Size size, Action<Graphics> draw)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return false;

            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                var info = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                    Width = size.Width,
                    Height = -size.Height, // Top-down BGRA surface.
                    Planes = 1,
                    BitCount = 32
                };
                IntPtr pixels;
                bitmap = CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero)
                    return false;

                previous = SelectObject(dc, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                    return false;

                using (var image = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, pixels))
                using (Graphics graphics = Graphics.FromImage(image))
                    draw(graphics);

                var origin = Point.Empty;
                var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA
                return UpdateLayeredWindow(window, IntPtr.Zero, IntPtr.Zero, ref size, dc, ref origin, 0, ref blend, 2);
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1))
                    SelectObject(dc, previous);
                if (bitmap != IntPtr.Zero)
                    DeleteObject(bitmap);
                DeleteDC(dc);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPixelsPerMeter, YPixelsPerMeter;
            public uint ColorsUsed, ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage,
            out IntPtr pixels, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, IntPtr destination,
            ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);
    }
}
