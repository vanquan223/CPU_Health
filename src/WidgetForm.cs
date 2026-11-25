using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CpuHealth
{
    internal sealed class WidgetForm : Form
    {
        private const int WidgetWidth = 420;
        private const int WidgetHeight = 466;
        private const int CompactWidth = 244;
        private const int CompactHeight = 32;
        private const float MinimumWidgetScale = .75f;
        private const int HistorySeconds = 60;
        private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        private static readonly Color Cpu = Color.FromArgb(157, 159, 255);
        private static readonly Color Ram = Color.FromArgb(99, 224, 195);
        private static readonly Color Disk = Color.FromArgb(111, 188, 255);
        private static readonly Color Heat = Color.FromArgb(255, 168, 133);

        private readonly SystemMetrics metrics = new SystemMetrics();
        private readonly DiskMonitor diskMonitor = new DiskMonitor();
        private readonly Queue<double?> cpuHistory = new Queue<double?>();
        private readonly Dictionary<int, Queue<DiskReading>> diskHistory = new Dictionary<int, Queue<DiskReading>>();
        private readonly Timer metricsTimer = new Timer { Interval = 1000 };
        private readonly Timer temperatureTimer = new Timer { Interval = 5000 };
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip driveMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip compactMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem transparentBackground = new ToolStripMenuItem("Nền trong suốt")
        {
            Checked = true,
            CheckOnClick = true
        };
        private readonly WidgetButton pinButton, minimizeButton, closeButton, driveButton, diskDetailsButton, compactButton;
        private readonly float dpiScale;
        private float fullScale;
        private bool compact;
        private Point? resizeStart;
        private float resizeStartScale;
        private Point? compactDragStart;
        private Point compactDragOrigin;
        private bool compactDragging;

        private DiskSnapshot[] disks = new DiskSnapshot[0];
        private DiskSnapshot selectedDisk;
        private int? diskNumber;
        private Task<DiskSnapshot[]> diskRead;
        private bool diskDetails;
        private double? cpuPercent;
        private Capacity? memory;
        private TemperatureReading temperature;
        private DateTime? lastUpdate;
        private bool temperaturePending;
        private bool temperatureChecked;

        public WidgetForm()
        {
            Text = "CPU Health";
            AccessibleName = Text;
            BackColor = WidgetArt.Background;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            using (Graphics desktop = Graphics.FromHwnd(IntPtr.Zero))
                dpiScale = desktop.DpiX / 96f;

            compactButton = AddButton("Thu gọn (Ctrl+M)", Glyph.Compact, WidgetArt.Background);
            compactButton.Click += (sender, args) => ToggleCompactMode();
            pinButton = AddButton("Ghim trên cùng", Glyph.Pin, WidgetArt.Background);
            pinButton.Selected = TopMost;
            pinButton.AccessibleDescription = "Đang bật";
            pinButton.Click += (sender, args) =>
            {
                TopMost = !TopMost;
                pinButton.Selected = TopMost;
                pinButton.AccessibleDescription = TopMost ? "Đang bật" : "Đang tắt";
                pinButton.Invalidate();
            };

            minimizeButton = AddButton("Thu nhỏ", Glyph.Minimize, WidgetArt.Background);
            minimizeButton.Click += (sender, args) => WindowState = FormWindowState.Minimized;
            closeButton = AddButton("Đóng", Glyph.Close, WidgetArt.Background);
            closeButton.Click += (sender, args) => Close();

            driveButton = AddButton("Chọn ổ đĩa vật lý", Glyph.Chevron, WidgetArt.Card);
            driveButton.Caption = "—";
            driveButton.Click += (sender, args) => ShowDriveMenu();
            diskDetailsButton = AddButton("Chi tiết ổ đĩa", Glyph.Label, WidgetArt.Card);
            diskDetailsButton.Caption = "Chi tiết";
            diskDetailsButton.Click += (sender, args) => ToggleDiskDetails();
            driveMenu.ShowImageMargin = false;
            driveMenu.BackColor = WidgetArt.Card;
            driveMenu.ForeColor = WidgetArt.Text;
            driveMenu.Renderer = new ToolStripProfessionalRenderer(new DriveMenuColors());
            compactMenu.Items.Add(transparentBackground);
            transparentBackground.Click += (sender, args) => UpdateCompactAppearance();

            SetWidgetScale(dpiScale);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(Math.Max(work.Left, work.Right - Width - Scale(24)),
                Math.Max(work.Top, work.Bottom - Height - Scale(24)));

            metricsTimer.Tick += (sender, args) => RefreshMetrics();
            temperatureTimer.Tick += async (sender, args) => await RefreshTemperature();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshMetrics();
            metricsTimer.Start();
            temperatureTimer.Start();
            await RefreshTemperature();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                metricsTimer.Dispose();
                temperatureTimer.Dispose();
                tips.Dispose();
                driveMenu.Dispose();
                compactMenu.Dispose();
                if (diskRead == null)
                    diskMonitor.Dispose();
                else
                    diskRead.ContinueWith(completed => diskMonitor.Dispose(), TaskScheduler.Default);
            }
            base.Dispose(disposing);
        }

        private float DrawingScale { get { return compact ? dpiScale : fullScale; } }
        private bool TransparentCompact { get { return compact && transparentBackground.Checked; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams style = base.CreateParams;
                if (TransparentCompact)
                    style.ExStyle |= LayeredWindow.ExtendedStyle;
                return style;
            }
        }

        private int Scale(int value) { return (int)Math.Round(value * DrawingScale); }

        private WidgetButton AddButton(string label, Glyph glyph, Color background)
        {
            var button = new WidgetButton(glyph, background)
            {
                AccessibleName = label,
                TabIndex = Controls.Count
            };
            tips.SetToolTip(button, label);
            Controls.Add(button);
            return button;
        }

        private void SetWidgetScale(float value)
        {
            if (value == fullScale)
                return;

            fullScale = value;
            LayoutWidget();
        }

        private void LayoutWidget()
        {
            ClientSize = new Size(Scale(compact ? CompactWidth : WidgetWidth), Scale(compact ? CompactHeight : WidgetHeight));
            using (GraphicsPath outline = WidgetArt.Round(0, 0, ClientSize.Width, ClientSize.Height, Scale(compact ? 10 : 23)))
                Region = new Region(outline);

            if (!compact)
            {
                LayoutButton(compactButton, 258, 26, 31, 30);
                LayoutButton(pinButton, 295, 26, 31, 30);
                LayoutButton(minimizeButton, 332, 26, 31, 30);
                LayoutButton(closeButton, 369, 26, 31, 30);
                LayoutButton(driveButton, 310, diskDetails ? 92 : 237, 73, 26);
                LayoutButton(diskDetailsButton, diskDetails ? 36 : 311, diskDetails ? 92 : 274, diskDetails ? 86 : 72, 25);
            }
            Invalidate();
        }

        private void LayoutButton(WidgetButton button, int x, int y, int width, int height)
        {
            button.SetLayout(DrawingScale, Rectangle.FromLTRB(Scale(x), Scale(y), Scale(x + width), Scale(y + height)));
        }

        private void ToggleCompactMode()
        {
            Rectangle previous = Bounds;
            Rectangle work = Screen.FromRectangle(previous).WorkingArea;
            resizeStart = null;
            compactDragStart = null;
            Capture = false;
            compact = !compact;
            if (compact)
                driveMenu.Close();
            ContextMenuStrip = compact ? compactMenu : null;
            foreach (Control button in Controls)
                button.Visible = !compact;

            LayoutWidget();
            PlaceInWorkArea(new Point(previous.Right - Width, previous.Bottom - Height), work);
            tips.SetToolTip(this, compact ? "Bấm để hiện đầy đủ · Kéo để di chuyển" : null);
            AccessibleDescription = compact ? "Chế độ thu gọn. Nhấn Enter để hiện đầy đủ." : null;
            UpdateCursor(PointToClient(MousePosition));
            UpdateCompactAppearance();

            if (!compact)
                compactButton.Focus();
        }

        private void UpdateCompactAppearance()
        {
            UpdateStyles();
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (TransparentCompact && IsHandleCreated && Visible && WindowState != FormWindowState.Minimized)
            {
                if (LayeredWindow.Update(Handle, ClientSize, DrawTransparentCompact))
                    return;

                // Keep the compact view usable if Windows cannot create the layered surface.
                transparentBackground.Checked = false;
                UpdateStyles();
            }
            Invalidate();
        }

        private void PlaceInWorkArea(Point preferred, Rectangle work)
        {
            Location = new Point(Math.Max(work.Left, Math.Min(work.Right - Width, preferred.X)),
                Math.Max(work.Top, Math.Min(work.Bottom - Height, preferred.Y)));
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.M) || (compact && keyData == Keys.Enter))
            {
                ToggleCompactMode();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private DiskReading CurrentDisk { get { return selectedDisk == null ? null : selectedDisk.Reading; } }

        private void ToggleDiskDetails()
        {
            driveMenu.Close();
            diskDetails = !diskDetails;
            diskDetailsButton.Caption = diskDetails ? "‹ Tổng quan" : "Chi tiết";
            diskDetailsButton.AccessibleName = diskDetails ? "Về tổng quan" : "Chi tiết ổ đĩa";
            tips.SetToolTip(diskDetailsButton, diskDetailsButton.AccessibleName);
            LayoutWidget();
        }

        private void ShowDriveMenu()
        {
            while (driveMenu.Items.Count > 0)
                driveMenu.Items[0].Dispose();

            foreach (DiskSnapshot snapshot in disks)
            {
                int number = snapshot.Disk.Number;
                var item = new ToolStripMenuItem((number == diskNumber ? "✓  " : "     ") + snapshot.Disk.Label);
                item.Click += (sender, args) =>
                {
                    diskNumber = number;
                    SelectDisk();
                    RefreshDisplay();
                };
                driveMenu.Items.Add(item);
            }
            if (driveMenu.Items.Count == 0)
                driveMenu.Items.Add(new ToolStripMenuItem("Không có ổ đĩa khả dụng") { Enabled = false });

            driveMenu.Show(driveButton, new Point(0, driveButton.Height), ToolStripDropDownDirection.BelowLeft);
        }

        private void SelectDisk()
        {
            selectedDisk = disks.FirstOrDefault(snapshot => snapshot.Disk.Number == diskNumber)
                ?? disks.FirstOrDefault(snapshot => snapshot.Disk.ContainsWindows) ?? disks.FirstOrDefault();
            if (selectedDisk != null)
                diskNumber = selectedDisk.Disk.Number;
            string caption = selectedDisk == null ? "—" : selectedDisk.Disk.Name;
            if (driveButton.Caption != caption)
            {
                driveButton.Caption = caption;
                driveButton.Invalidate();
            }
        }

        private async void RefreshDisks()
        {
            if (diskRead != null)
                return;

            diskRead = Task.Run((Func<DiskSnapshot[]>)diskMonitor.Read);
            try
            {
                DiskSnapshot[] current = await diskRead;
                if (IsDisposed || Disposing)
                    return;

                disks = current;
                foreach (int missing in diskHistory.Keys.Except(disks.Select(snapshot => snapshot.Disk.Number)).ToArray())
                    diskHistory.Remove(missing);
                foreach (DiskSnapshot snapshot in disks)
                {
                    Queue<DiskReading> history;
                    int number = snapshot.Disk.Number;
                    if (!diskHistory.TryGetValue(number, out history))
                        diskHistory[number] = history = new Queue<DiskReading>();
                    history.Enqueue(snapshot.Reading);
                    if (history.Count > HistorySeconds + 1)
                        history.Dequeue();
                }
                SelectDisk();
                RefreshDisplay();
            }
            finally { diskRead = null; }
        }

        private void RefreshMetrics()
        {
            cpuPercent = metrics.ReadCpuPercent();
            cpuHistory.Enqueue(cpuPercent);
            if (cpuHistory.Count > HistorySeconds + 1)
                cpuHistory.Dequeue();

            memory = SystemMetrics.ReadRam();
            if (cpuPercent.HasValue || memory.HasValue || CurrentDisk != null)
                lastUpdate = DateTime.Now;
            if (!diskDetails || compact)
                RefreshDisplay();
            RefreshDisks();
        }

        private async Task RefreshTemperature()
        {
            if (temperaturePending)
                return;

            temperaturePending = true;
            try
            {
                temperature = await Task.Run((Func<TemperatureReading>)TemperatureSensor.Read);
                temperatureChecked = true;
                if (!IsDisposed)
                {
                    if (compact)
                        RefreshDisplay();
                    else
                        Invalidate(new Rectangle(Scale(20), Scale(350), Scale(380), Scale(80)));
                }
            }
            finally
            {
                temperaturePending = false;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (compactDragStart.HasValue)
            {
                Point current = PointToScreen(e.Location);
                int dx = current.X - compactDragStart.Value.X;
                int dy = current.Y - compactDragStart.Value.Y;
                Size drag = SystemInformation.DragSize;
                if (!compactDragging && Math.Abs(dx) < drag.Width / 2 && Math.Abs(dy) < drag.Height / 2)
                    return;

                compactDragging = true;
                Cursor = Cursors.SizeAll;
                Location = new Point(compactDragOrigin.X + dx, compactDragOrigin.Y + dy);
                return;
            }
            if (resizeStart.HasValue)
            {
                Point current = PointToScreen(e.Location);
                int dx = current.X - resizeStart.Value.X;
                int dy = current.Y - resizeStart.Value.Y;
                // Project the drag onto the diagonal to keep the widget's aspect ratio.
                float change = (dx * WidgetWidth + dy * WidgetHeight) /
                    (float)(WidgetWidth * WidgetWidth + WidgetHeight * WidgetHeight);
                SetWidgetScale(Math.Max(dpiScale * MinimumWidgetScale, Math.Min(dpiScale, resizeStartScale + change)));
                return;
            }
            UpdateCursor(e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            if (compact)
            {
                compactDragStart = PointToScreen(e.Location);
                compactDragOrigin = Location;
                compactDragging = false;
                Capture = true;
            }
            else if (IsResizeGrip(e.Location))
            {
                resizeStart = PointToScreen(e.Location);
                resizeStartScale = fullScale;
                Cursor = Cursors.SizeNWSE;
                Capture = true;
            }
            else if (e.Y < Scale(77))
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || (!resizeStart.HasValue && !compactDragStart.HasValue))
                return;

            bool expand = compactDragStart.HasValue && !compactDragging && ClientRectangle.Contains(e.Location);
            resizeStart = null;
            compactDragStart = null;
            Capture = false;
            if (expand)
                ToggleCompactMode();
            else
            {
                Point pointer = PointToScreen(e.Location);
                if (compact)
                    PlaceInWorkArea(Location, Screen.FromPoint(pointer).WorkingArea);
                UpdateCursor(PointToClient(pointer));
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && (resizeStart.HasValue || compactDragStart.HasValue))
            {
                resizeStart = null;
                compactDragStart = null;
                UpdateCursor(PointToClient(MousePosition));
            }
        }

        private bool IsResizeGrip(Point point)
        {
            return ClientRectangle.Contains(point) && point.X >= Scale(384) && point.Y >= Scale(431);
        }

        private void UpdateCursor(Point point)
        {
            if (compact)
                Cursor = Cursors.Hand;
            else
                Cursor = IsResizeGrip(point) ? Cursors.SizeNWSE :
                ClientRectangle.Contains(point) && point.Y < Scale(77) ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (TransparentCompact)
                return;

            Graphics g = e.Graphics;
            g.ScaleTransform(DrawingScale, DrawingScale);
            WidgetArt.Prepare(g);
            if (compact)
            {
                WidgetArt.StrokeRound(g, WidgetArt.Border, .5f, .5f, CompactWidth - 1, CompactHeight - 1, 10);
                DrawCompact(g, false);
                return;
            }
            WidgetArt.StrokeRound(g, WidgetArt.Border, .5f, .5f, WidgetWidth - 1, WidgetHeight - 1, 23);
            DrawHeader(g);
            if (diskDetails)
                DrawDiskDetails(g);
            else
            {
                DrawCpu(g);
                DrawCapacity(g, 20, "RAM", Glyph.Ram, Ram, memory);
                DrawDiskCard(g);
                DrawTemperature(g);
            }
            DrawFooter(g);
        }

        private void DrawTransparentCompact(Graphics g)
        {
            // A nonzero alpha preserves drag and click handling between the metrics.
            g.Clear(Color.FromArgb(1, Color.Black));
            g.ScaleTransform(dpiScale, dpiScale);
            WidgetArt.Prepare(g);
            DrawCompact(g, true);
        }

        private void DrawCompact(Graphics g, bool transparent)
        {
            DrawCompactMetric(g, 9, Glyph.Cpu, Cpu, cpuPercent, "%", transparent);
            DrawCompactMetric(g, 66, Glyph.Ram, Ram, memory.HasValue ? memory.Value.Percent : (double?)null, "%", transparent);
            DrawCompactMetric(g, 123, Glyph.Disk, Disk, CurrentDisk == null ? null : CurrentDisk.Active, "%", transparent);
            DrawCompactMetric(g, 180, Glyph.Temperature, Heat, temperature == null ? (double?)null : temperature.Celsius, "°C", transparent);
        }

        private static void DrawCompactMetric(Graphics g, int x, Glyph glyph, Color accent, double? value, string unit, bool transparent)
        {
            if (transparent)
                WidgetArt.Icon(g, glyph, WidgetArt.Background, x, 8, 16, 4.2f);
            WidgetArt.Icon(g, glyph, accent, x, 8, 16);
            string text = value.HasValue ? value.Value.ToString("0", Vietnamese) + unit : "—";
            Color color = value.HasValue ? WidgetArt.Text : WidgetArt.Muted;
            if (transparent)
                WidgetArt.WriteOutlined(g, text, WidgetArt.Label, color, x + 20, 6);
            else
                WidgetArt.Write(g, text, WidgetArt.Label, color, x + 20, 6);
        }

        private static void DrawHeader(Graphics g)
        {
            using (GraphicsPath tile = WidgetArt.Round(20, 20, 42, 42, 13))
            using (var gradient = new LinearGradientBrush(new Rectangle(20, 20, 42, 42),
                Color.FromArgb(79, 79, 153), Color.FromArgb(36, 41, 80), 45f))
                g.FillPath(gradient, tile);

            WidgetArt.StrokeRound(g, Color.FromArgb(104, 105, 176), 20.5f, 20.5f, 41, 41, 13);
            WidgetArt.Icon(g, Glyph.Pulse, Color.FromArgb(218, 218, 255), 30, 30, 22);
            WidgetArt.Write(g, "CPU Health", WidgetArt.Title, WidgetArt.Text, 75, 18);
            WidgetArt.Write(g, "Giám sát hệ thống", WidgetArt.Body, WidgetArt.Muted, 76, 44);
        }

        private void DrawCpu(Graphics g)
        {
            WidgetArt.DrawCard(g, 20, 78, 380, 136);
            WidgetArt.Badge(g, Glyph.Cpu, Cpu, 36, 94, 26);
            WidgetArt.Write(g, "CPU", WidgetArt.Label, WidgetArt.Text, 70, 99);
            DrawPercent(g, cpuPercent, 35, 124, WidgetArt.Large);
            WidgetArt.Write(g, Environment.ProcessorCount + " luồng xử lý", WidgetArt.Body, WidgetArt.Muted, 37, 184);
            DrawChart(g, new RectangleF(173, 102, 205, 73), cpuHistory.ToArray(), Cpu, 100);
            WidgetArt.Write(g, "60 giây gần nhất", WidgetArt.Small, WidgetArt.Muted, 379, 185, true);
        }

        private static void DrawChart(Graphics g, RectangleF bounds, double?[] history, Color color, double maximum)
        {
            using (var grid = new Pen(WidgetArt.Border))
            {
                grid.DashPattern = new[] { 2f, 3f };
                for (int line = 0; line < 4; line++)
                {
                    float y = bounds.Top + line * bounds.Height / 3;
                    g.DrawLine(grid, bounds.Left, y, bounds.Right, y);
                }
            }

            using (var tint = new LinearGradientBrush(bounds, Color.FromArgb(82, color), Color.FromArgb(7, color), 90f))
            using (var stroke = new Pen(color, 1.8f) { LineJoin = LineJoin.Round })
            {
                var points = new List<PointF>(history.Length);
                for (int i = 0; i <= history.Length; i++)
                {
                    if (i < history.Length && history[i].HasValue)
                    {
                        float x = bounds.Right - (history.Length - 1 - i) * bounds.Width / HistorySeconds;
                        float y = bounds.Bottom - (float)(history[i].Value / maximum) * bounds.Height;
                        points.Add(new PointF(x, y));
                        continue;
                    }
                    if (points.Count > 1)
                    {
                        PointF[] line = points.ToArray();
                        points.Add(new PointF(line[line.Length - 1].X, bounds.Bottom));
                        points.Add(new PointF(line[0].X, bounds.Bottom));
                        g.FillPolygon(tint, points.ToArray());
                        g.DrawLines(stroke, line);
                    }
                    if (i == history.Length && points.Count > 0)
                    {
                        PointF last = new PointF(bounds.Right, bounds.Bottom - (float)(history[i - 1].Value / maximum) * bounds.Height);
                        WidgetArt.Dot(g, Color.FromArgb(65, color), last.X, last.Y, 6);
                        WidgetArt.Dot(g, color, last.X, last.Y, 3);
                    }
                    points.Clear();
                }
            }
        }

        private void DrawDiskCard(Graphics g)
        {
            WidgetArt.DrawCard(g, 216, 226, 184, 120);
            WidgetArt.Badge(g, Glyph.Disk, Disk, 230, 238, 26);
            WidgetArt.Write(g, "DISK", WidgetArt.Label, WidgetArt.Text, 264, 243);
            DiskReading reading = CurrentDisk;
            DrawPercent(g, reading == null ? null : reading.Active, 230, 265, WidgetArt.Medium);
            WidgetArt.Write(g, "Thời gian hoạt động", WidgetArt.Small, WidgetArt.Muted, 231, 302);
            WidgetArt.Write(g, "↓ " + FormatRate(reading == null ? null : reading.ReadRate), WidgetArt.Small, Disk, 231, 323);
            WidgetArt.Write(g, "↑ " + FormatRate(reading == null ? null : reading.WriteRate), WidgetArt.Small, WidgetArt.Muted, 385, 323, true);
        }

        private void DrawDiskDetails(Graphics g)
        {
            WidgetArt.DrawCard(g, 20, 78, 380, 350);
            PhysicalDisk disk = selectedDisk == null ? null : selectedDisk.Disk;
            DiskReading reading = CurrentDisk;
            WidgetArt.WriteFitted(g, disk == null ? "Ổ đĩa vật lý" : disk.Label, WidgetArt.Title, WidgetArt.Text, 36, 121, 344);
            WidgetArt.WriteFitted(g, disk == null ? "Chưa tìm thấy ổ đĩa" : disk.Model ?? "—", WidgetArt.Body, WidgetArt.Muted, 37, 148, 344);

            Queue<DiskReading> queue;
            DiskReading[] history = disk != null && diskHistory.TryGetValue(disk.Number, out queue) ? queue.ToArray() : new DiskReading[0];
            double?[] active = history.Select(sample => sample == null ? null : sample.Active).ToArray();
            double?[] transfer = history.Select(sample => sample == null ? null : sample.ReadRate + sample.WriteRate).ToArray();
            double peak = transfer.Length == 0 ? 0 : transfer.Max().GetValueOrDefault();
            double step = Math.Pow(10, Math.Max(4, Math.Floor(Math.Log10(Math.Max(1, peak)))));
            double ceiling = Math.Max(step, Math.Ceiling(peak / step) * step);

            WidgetArt.Write(g, "Thời gian hoạt động", WidgetArt.Small, WidgetArt.Muted, 37, 171);
            WidgetArt.Write(g, "100%", WidgetArt.Small, WidgetArt.Muted, 382, 171, true);
            DrawChart(g, new RectangleF(38, 194, 339, 34), active, Disk, 100);
            WidgetArt.Write(g, "Tốc độ truyền · Đọc + Ghi", WidgetArt.Small, WidgetArt.Muted, 37, 236);
            WidgetArt.Write(g, FormatRate(ceiling), WidgetArt.Small, WidgetArt.Muted, 382, 236, true);
            DrawChart(g, new RectangleF(38, 259, 339, 34), transfer, Disk, ceiling);
            WidgetArt.Write(g, "60 giây gần nhất", WidgetArt.Small, WidgetArt.Muted, 382, 299, true);
            if (reading != null && reading.FromVolumes)
                WidgetArt.Write(g, "Ước tính từ phân vùng", WidgetArt.Small, WidgetArt.Muted, 37, 299);

            DrawDiskStat(g, 37, "Hoạt động", FormatValue(reading == null ? null : reading.Active, "0", "%"));
            DrawDiskStat(g, 124, "Phản hồi TB", FormatValue(reading == null ? null : reading.ResponseMs, "0.#", " ms"));
            DrawDiskStat(g, 216, "Tốc độ đọc", FormatRate(reading == null ? null : reading.ReadRate));
            DrawDiskStat(g, 304, "Tốc độ ghi", FormatRate(reading == null ? null : reading.WriteRate));

            WidgetArt.Write(g, "Dung lượng  " + FormatDiskSize(disk == null ? null : disk.Capacity), WidgetArt.Small, WidgetArt.Muted, 37, 368);
            WidgetArt.Write(g, "Đã định dạng  " + FormatDiskSize(disk == null ? null : disk.Formatted), WidgetArt.Small, WidgetArt.Muted, 218, 368);
            WidgetArt.Write(g, "Ổ chứa Windows  " + FormatFlag(disk == null || disk.Volumes.Count == 0 ? (bool?)null : disk.ContainsWindows),
                WidgetArt.Small, WidgetArt.Muted, 37, 389);
            WidgetArt.Write(g, "Page file  " + FormatFlag(disk == null ? null : disk.HasPageFile), WidgetArt.Small, WidgetArt.Muted, 218, 389);
            WidgetArt.Write(g, "Loại  " + (disk == null ? "—" : disk.Type ?? "—"), WidgetArt.Small, WidgetArt.Muted, 37, 408);
        }

        private static void DrawDiskStat(Graphics g, int x, string label, string value)
        {
            WidgetArt.Write(g, label, WidgetArt.Small, WidgetArt.Muted, x, 323);
            WidgetArt.WriteFitted(g, value, WidgetArt.Label, WidgetArt.Text, x, 341, 85);
        }

        private static string FormatFlag(bool? value) { return value.HasValue ? (value.Value ? "Có" : "Không") : "—"; }

        private static string FormatDiskSize(long? bytes)
        {
            return FormatValue(bytes / 1073741824.0, "0.#", " GiB");
        }

        private static string FormatValue(double? value, string format, string unit)
        {
            return value.HasValue ? value.Value.ToString(format, Vietnamese) + unit : "—";
        }

        private static string FormatRate(double? rate)
        {
            if (rate >= 1073741824)
                return FormatValue(rate / 1073741824, "0.#", " GB/s");
            if (rate >= 1048576)
                return FormatValue(rate / 1048576, "0.#", " MB/s");
            return FormatValue(rate / 1024, "0.#", " KB/s");
        }

        private static void DrawCapacity(Graphics g, int x, string name, Glyph glyph, Color accent, Capacity? capacity)
        {
            WidgetArt.DrawCard(g, x, 226, 184, 120);
            WidgetArt.Badge(g, glyph, accent, x + 14, 238, 26);
            WidgetArt.Write(g, name, WidgetArt.Label, WidgetArt.Text, x + 48, 243);

            double? percent = capacity.HasValue ? capacity.Value.Percent : (double?)null;
            DrawPercent(g, percent, x + 14, 267, WidgetArt.Medium);
            string detail = capacity.HasValue
                ? FormatCapacity(capacity.Value)
                : "Không có dữ liệu";
            WidgetArt.Write(g, detail, WidgetArt.Body, WidgetArt.Muted, x + 15, 304);

            WidgetArt.FillRound(g, WidgetArt.Track, x + 15, 327, 154, 5, 2.5f);
            if (percent > 0)
                WidgetArt.FillRound(g, accent, x + 15, 327, (float)(154 * percent.Value / 100), 5, 2.5f);
        }

        private static string FormatCapacity(Capacity capacity)
        {
            const double gibibyte = 1024.0 * 1024 * 1024;
            const double tebibyte = gibibyte * 1024;
            double divisor = capacity.Total >= tebibyte ? tebibyte : gibibyte;
            string unit = capacity.Total >= tebibyte ? "TiB" : "GiB";
            string format = capacity.Total >= tebibyte ? "0.00" : "0.#";
            return (capacity.Used / divisor).ToString(format, Vietnamese) + " / " +
                (capacity.Total / divisor).ToString(format, Vietnamese) + " " + unit;
        }

        private static void DrawPercent(Graphics g, double? percent, float x, float y, Font font)
        {
            string value = percent.HasValue ? percent.Value.ToString("0", Vietnamese) : "—";
            WidgetArt.Write(g, value, font, WidgetArt.Text, x, y);
            if (percent.HasValue)
                WidgetArt.Write(g, "%", WidgetArt.Unit, WidgetArt.Muted,
                    x + WidgetArt.Measure(g, value, font) + 4, y + font.Size - WidgetArt.Unit.Size + 1);
        }

        private void DrawTemperature(Graphics g)
        {
            WidgetArt.DrawCard(g, 20, 358, 380, 70);
            WidgetArt.Badge(g, Glyph.Temperature, Heat, 36, 375, 36);
            WidgetArt.Write(g, "Nhiệt độ", WidgetArt.Label, WidgetArt.Text, 84, 373);

            string source = "Đang đọc cảm biến…";
            string value = "—";
            if (temperature != null)
            {
                value = temperature.Celsius.ToString("0.#", Vietnamese) + " °C";
                source = temperature.IsCpu ? "CPU · LibreHardwareMonitor" : "Vùng nhiệt · Windows";
            }
            else if (temperatureChecked)
            {
                source = "Không có dữ liệu cảm biến";
            }
            WidgetArt.Write(g, source, WidgetArt.Body, WidgetArt.Muted, 84, 393);
            WidgetArt.Write(g, value, WidgetArt.Medium, temperature == null ? WidgetArt.Muted : Heat, 382, 373, true);
        }

        private void DrawFooter(Graphics g)
        {
            WidgetArt.Dot(g, lastUpdate.HasValue ? Ram : WidgetArt.Muted, 26, 447, 3);
            WidgetArt.Write(g, lastUpdate.HasValue ? "Đang theo dõi" : "Đang chờ dữ liệu",
                WidgetArt.Small, WidgetArt.Muted, 36, 439);
            if (lastUpdate.HasValue)
                WidgetArt.Write(g, "Cập nhật " + lastUpdate.Value.ToString("HH:mm:ss", Vietnamese),
                    WidgetArt.Small, WidgetArt.Muted, 378, 439, true);
            using (var grip = new Pen(WidgetArt.Muted, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(grip, 390, 451, 401, 440);
                g.DrawLine(grip, 396, 451, 401, 446);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class DriveMenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder { get { return WidgetArt.Border; } }
        public override Color MenuItemBorder { get { return WidgetArt.Track; } }
        public override Color MenuItemSelected { get { return WidgetArt.Track; } }
        public override Color ToolStripDropDownBackground { get { return WidgetArt.Card; } }
    }
}
