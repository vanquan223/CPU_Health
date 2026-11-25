using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CpuHealth
{
    internal enum Glyph { Pulse, Cpu, Ram, Disk, Temperature, Pin, Minimize, Close, Chevron, Compact, Label }

    internal static class WidgetArt
    {
        public static readonly Color Background = Color.FromArgb(13, 17, 29);
        public static readonly Color Card = Color.FromArgb(23, 29, 45);
        public static readonly Color Border = Color.FromArgb(47, 56, 79);
        public static readonly Color Track = Color.FromArgb(45, 55, 77);
        public static readonly Color Text = Color.FromArgb(242, 246, 255);
        public static readonly Color Muted = Color.FromArgb(166, 179, 203);

        public static readonly Font Title = MakeFont(22, FontStyle.Bold);
        public static readonly Font Large = MakeFont(46, FontStyle.Bold);
        public static readonly Font Medium = MakeFont(29, FontStyle.Bold);
        public static readonly Font Label = MakeFont(14, FontStyle.Bold);
        public static readonly Font Unit = MakeFont(17, FontStyle.Regular);
        public static readonly Font Body = MakeFont(13, FontStyle.Regular);
        public static readonly Font Small = MakeFont(12, FontStyle.Regular);

        private static Font MakeFont(int size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static GraphicsPath Round(float x, float y, float width, float height, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(Math.Min(width, height), radius * 2);
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color color, float x, float y, float width, float height, float radius)
        {
            using (GraphicsPath path = Round(x, y, width, height, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void StrokeRound(Graphics g, Color color, float x, float y, float width, float height, float radius)
        {
            using (GraphicsPath path = Round(x, y, width, height, radius))
            using (var pen = new Pen(color))
                g.DrawPath(pen, path);
        }

        public static void DrawCard(Graphics g, int x, int y, int width, int height)
        {
            FillRound(g, Card, x, y, width, height, 16);
            StrokeRound(g, Border, x + .5f, y + .5f, width - 1, height - 1, 16);
        }

        public static void Dot(Graphics g, Color color, float x, float y, float radius)
        {
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
        }

        public static void Write(Graphics g, string text, Font font, Color color, float x, float y, bool right = false)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
                g.DrawString(text, font, brush, x, y, format);
            }
        }

        public static float Measure(Graphics g, string text, Font font)
        {
            return g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        public static void WriteFitted(Graphics g, string text, Font font, Color color, int x, int y, int width)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(text, font, brush, new RectangleF(x - 2, y, width + 2, font.Height + 2), format);
        }

        public static void WriteOutlined(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (var path = new GraphicsPath())
            using (var outline = new Pen(Background, 2f) { LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(color))
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, new PointF(x, y), StringFormat.GenericTypographic);
                g.DrawPath(outline, path);
                g.FillPath(fill, path);
            }
        }

        public static void Badge(Graphics g, Glyph glyph, Color color, int x, int y, int size)
        {
            FillRound(g, Color.FromArgb(26, color), x, y, size, size, size / 4f);
            float iconSize = size == 26 ? 15 : 19;
            float inset = (size - iconSize) / 2;
            Icon(g, glyph, color, x + inset, y + inset, iconSize);
        }

        public static void Icon(Graphics g, Glyph glyph, Color color, float x, float y, float size, float strokeWidth = 1.65f)
        {
            GraphicsState previous = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(size / 20, size / 20);
            using (var pen = new Pen(color, strokeWidth)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                switch (glyph)
                {
                    case Glyph.Pulse:
                        g.DrawLines(pen, new[] { new PointF(1, 10), new PointF(5, 10), new PointF(8, 4),
                            new PointF(11, 16), new PointF(14, 8), new PointF(15.5f, 10), new PointF(19, 10) });
                        break;
                    case Glyph.Cpu:
                        using (GraphicsPath chip = Round(4, 4, 12, 12, 2))
                            g.DrawPath(pen, chip);
                        g.DrawRectangle(pen, 7.5f, 7.5f, 5, 5);
                        for (int pin = 7; pin <= 13; pin += 6)
                        {
                            g.DrawLine(pen, pin, 2, pin, 4);
                            g.DrawLine(pen, pin, 16, pin, 18);
                            g.DrawLine(pen, 2, pin, 4, pin);
                            g.DrawLine(pen, 16, pin, 18, pin);
                        }
                        break;
                    case Glyph.Ram:
                        using (GraphicsPath chip = Round(2, 5, 16, 9, 2))
                            g.DrawPath(pen, chip);
                        for (int pin = 5; pin <= 15; pin += 5)
                        {
                            g.DrawLine(pen, pin, 9, pin + 1, 9);
                            g.DrawLine(pen, pin, 14, pin, 16);
                        }
                        break;
                    case Glyph.Disk:
                        using (GraphicsPath drive = Round(2, 4, 16, 12, 2.5f))
                            g.DrawPath(pen, drive);
                        g.DrawLine(pen, 2, 10, 18, 10);
                        Dot(g, color, 6, 13, 1);
                        break;
                    case Glyph.Temperature:
                        using (var stem = new GraphicsPath())
                        {
                            stem.AddArc(7.5f, 1.5f, 5, 5, 180, 180);
                            stem.AddLine(12.5f, 4, 12.5f, 11);
                            stem.AddArc(5.5f, 10.4f, 9, 8.2f, -53, 286);
                            stem.CloseFigure();
                            g.DrawPath(pen, stem);
                        }
                        g.DrawLine(pen, 10, 8, 10, 14);
                        Dot(g, color, 10, 14.5f, 1.6f);
                        break;
                    case Glyph.Pin:
                        g.DrawPolygon(pen, new[] { new PointF(6, 3), new PointF(14, 3), new PointF(12.5f, 7),
                            new PointF(12.5f, 9), new PointF(15, 12), new PointF(5, 12), new PointF(7.5f, 9), new PointF(7.5f, 7) });
                        g.DrawLine(pen, 10, 12, 10, 18);
                        break;
                    case Glyph.Minimize:
                        g.DrawLine(pen, 4, 11, 16, 11);
                        break;
                    case Glyph.Close:
                        g.DrawLine(pen, 5, 5, 15, 15);
                        g.DrawLine(pen, 15, 5, 5, 15);
                        break;
                    case Glyph.Chevron:
                        g.DrawLines(pen, new[] { new PointF(5, 8), new PointF(10, 13), new PointF(15, 8) });
                        break;
                    case Glyph.Compact:
                        g.DrawLine(pen, 3, 3, 8, 8);
                        g.DrawLines(pen, new[] { new PointF(3, 8), new PointF(8, 8), new PointF(8, 3) });
                        g.DrawLine(pen, 17, 17, 12, 12);
                        g.DrawLines(pen, new[] { new PointF(12, 17), new PointF(12, 12), new PointF(17, 12) });
                        break;
                }
            }
            g.Restore(previous);
        }
    }

    internal sealed class WidgetButton : Button
    {
        private float scale = 1;
        private readonly Glyph glyph;

        public bool Selected { get; set; }
        public string Caption { get; set; }

        public WidgetButton(Glyph glyph, Color background)
        {
            this.glyph = glyph;
            BackColor = background;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        public void SetLayout(float drawingScale, Rectangle bounds)
        {
            scale = drawingScale;
            Bounds = bounds;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.ScaleTransform(scale, scale);
            WidgetArt.Prepare(g);

            float width = ClientSize.Width / scale;
            float height = ClientSize.Height / scale;
            bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
            Color foreground = WidgetArt.Muted;
            Color background = glyph == Glyph.Chevron ? WidgetArt.Track : BackColor;
            if (Selected || hover)
            {
                background = glyph == Glyph.Close ? Color.FromArgb(106, 50, 63) : Color.FromArgb(54, 57, 96);
                foreground = WidgetArt.Text;
            }

            WidgetArt.FillRound(g, background, 0.5f, 0.5f, width - 1, height - 1, 8);
            if (Selected || (Focused && ShowFocusCues))
                WidgetArt.StrokeRound(g, Color.FromArgb(114, 119, 187), .5f, .5f, width - 1, height - 1, 8);

            if (glyph == Glyph.Label)
            {
                float x = (width - WidgetArt.Measure(g, Caption, WidgetArt.Small)) / 2;
                WidgetArt.Write(g, Caption, WidgetArt.Small, WidgetArt.Text, x, 5);
            }
            else if (glyph == Glyph.Chevron)
            {
                WidgetArt.Write(g, Caption, WidgetArt.Small, WidgetArt.Text, 9, 5);
                WidgetArt.Icon(g, glyph, foreground, width - 19, (height - 15) / 2, 15);
            }
            else
            {
                WidgetArt.Icon(g, glyph, foreground, (width - 16) / 2, (height - 16) / 2, 16);
            }
        }
    }
}
