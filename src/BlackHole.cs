using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopBlackHole
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int Width, Height; public SIZE(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    }

    internal static class Lens
    {
        private static readonly Bitmap Art = LoadArt();
        private static int Clamp(int value) { return value < 0 ? 0 : (value > 255 ? 255 : value); }

        private static Bitmap LoadArt()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlackHoleArt"))
            {
                if (stream == null) return null;
                using (Bitmap loaded = new Bitmap(stream)) return new Bitmap(loaded);
            }
        }

        // Render a true radial displacement of the captured pixels, then add an accretion disk.
        internal static unsafe Bitmap Render(Bitmap source, double time, double gravity)
        {
            int n = source.Width;
            Bitmap result = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
            Rectangle rect = new Rectangle(0, 0, n, n);
            BitmapData s = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData d = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                byte* sp = (byte*)s.Scan0;
                byte* dp = (byte*)d.Scan0;
                double c = (n - 1) * 0.5;
                double reach = n * 0.464;
                double core = n * 0.112;

                for (int y = 0; y < n; y++)
                {
                    byte* row = dp + y * d.Stride;
                    double dy = y - c;
                    for (int x = 0; x < n; x++)
                    {
                        int i = x * 4;
                        double dx = x - c;
                        double r = Math.Sqrt(dx * dx + dy * dy);
                        if (r >= reach) { row[i + 0] = row[i + 1] = row[i + 2] = row[i + 3] = 0; continue; }

                        double edge = Math.Min(1.0, Math.Max(0.0, (reach - r) / (n * 0.075)));
                        edge = edge * edge * (3.0 - 2.0 * edge);
                        int alpha = Clamp((int)(255 * edge));
                        double b = 0, g = 0, red = 0;

                        if (r >= core)
                        {
                            double falloff = Math.Max(0.0, 1.0 - r / reach);
                            double displacement = n * 0.235 * gravity * falloff * falloff * Math.Min(1.0, core / Math.Max(r, 1.0));
                            double twist = 0.13 * falloff * falloff;
                            double angle = Math.Atan2(dy, dx) + twist;
                            double sr = r + displacement;
                            double sx = Math.Max(0, Math.Min(n - 1.001, c + sr * Math.Cos(angle)));
                            double sy = Math.Max(0, Math.Min(n - 1.001, c + sr * Math.Sin(angle)));
                            int ix = (int)sx, iy = (int)sy;
                            double fx = sx - ix, fy = sy - iy;
                            byte* p00 = sp + iy * s.Stride + ix * 4;
                            byte* p10 = p00 + 4;
                            byte* p01 = p00 + s.Stride;
                            byte* p11 = p01 + 4;
                            b = (p00[0] * (1 - fx) + p10[0] * fx) * (1 - fy) + (p01[0] * (1 - fx) + p11[0] * fx) * fy;
                            g = (p00[1] * (1 - fx) + p10[1] * fx) * (1 - fy) + (p01[1] * (1 - fx) + p11[1] * fx) * fy;
                            red = (p00[2] * (1 - fx) + p10[2] * fx) * (1 - fy) + (p01[2] * (1 - fx) + p11[2] * fx) * fy;

                            // The dark band emphasizes the gravitational shadow just outside the event horizon.
                            double shadow = 0.55 * Math.Exp(-Math.Pow((r - core - n * 0.026) / (n * 0.026), 2));
                            b *= 1 - shadow; g *= 1 - shadow; red *= 1 - shadow;

                        }
                        else
                        {
                            // A tiny inner gradient keeps the center dimensional without showing source pixels.
                            double rim = Math.Pow(r / core, 10);
                            red = 3 + 12 * rim; g = 5 + 8 * rim; b = 11 + 15 * rim;
                        }

                        row[i + 0] = (byte)(Clamp((int)b) * alpha / 255);
                        row[i + 1] = (byte)(Clamp((int)g) * alpha / 255);
                        row[i + 2] = (byte)(Clamp((int)red) * alpha / 255);
                        row[i + 3] = (byte)alpha;
                    }
                }
            }
            finally { source.UnlockBits(s); result.UnlockBits(d); }
            if (Art != null)
            {
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Art, new Rectangle(0, 0, n, n));
                }
            }
            return result;
        }

        internal static Icon MakeIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (Art != null) g.DrawImage(Art, new Rectangle(0, 0, 32, 32));
                IntPtr hicon = bitmap.GetHicon();
                try { using (Icon temporary = Icon.FromHandle(hicon)) return (Icon)temporary.Clone(); }
                finally { Native.DestroyIcon(hicon); }
            }
        }
    }

    internal sealed class BlackHoleForm : Form
    {
        private readonly Timer timer = new Timer();
        private bool paused, dragging, excluded;
        private bool hidden, clickThrough;
        private double gravity = 1.0;
        private Point dragOrigin, formOrigin;
        private int diameter = 440;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray = new NotifyIcon();
        private ToolStripMenuItem pauseItem, showItem, clickItem;
        private DateTime started = DateTime.UtcNow;

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80; return cp; }
        }

        internal BlackHoleForm()
        {
            Text = "桌面黑洞";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(diameter, diameter);
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(screen.Right - diameter - 80, screen.Top + (screen.Height - diameter) / 2);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            pauseItem = new ToolStripMenuItem("暂停效果", null, delegate { paused = !paused; pauseItem.Text = paused ? "继续效果" : "暂停效果"; if (!paused) DrawFrame(); });
            showItem = new ToolStripMenuItem("隐藏黑洞  Ctrl+Alt+B", null, delegate { ToggleVisibility(); });
            clickItem = new ToolStripMenuItem("鼠标穿透", null, delegate { ToggleClickThrough(); });
            menu.Items.Add(showItem);
            menu.Items.Add(pauseItem);
            ToolStripMenuItem gravityMenu = new ToolStripMenuItem("引力强度");
            AddGravityChoice(gravityMenu, "轻微", 0.55);
            AddGravityChoice(gravityMenu, "标准", 1.0);
            AddGravityChoice(gravityMenu, "强烈", 1.7);
            menu.Items.Add(gravityMenu);
            menu.Items.Add(clickItem);
            menu.Items.Add("重置大小", null, delegate { ResizeHole(440); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出黑洞", null, delegate { Close(); });
            ContextMenuStrip = menu;
            tray.Icon = Lens.MakeIcon();
            tray.Text = "桌面黑洞：双击显示或隐藏";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ToggleVisibility(); };
            tray.Visible = true;

            timer.Interval = 45;
            timer.Tick += delegate { if (!paused) DrawFrame(); };
            Shown += delegate
            {
                excluded = Native.SetWindowDisplayAffinity(Handle, 0x11);
                Native.RegisterHotKey(Handle, 0xB001, 0x0003, 0x42);
                DrawFrame();
                timer.Start();
            };
            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += delegate { dragging = false; Capture = false; };
            MouseWheel += delegate(object sender, MouseEventArgs e) { ResizeHole(diameter + (e.Delta > 0 ? 40 : -40)); };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private void AddGravityChoice(ToolStripMenuItem parent, string label, double value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = value;
            item.Checked = value == gravity;
            item.Click += delegate
            {
                gravity = value;
                foreach (ToolStripMenuItem choice in parent.DropDownItems) choice.Checked = (double)choice.Tag == value;
                DrawFrame();
            };
            parent.DropDownItems.Add(item);
        }

        private void ToggleVisibility()
        {
            hidden = !hidden;
            Visible = !hidden;
            showItem.Text = hidden ? "显示黑洞  Ctrl+Alt+B" : "隐藏黑洞  Ctrl+Alt+B";
            if (!hidden) { TopMost = true; DrawFrame(); }
        }

        private void ToggleClickThrough()
        {
            clickThrough = !clickThrough;
            int style = Native.GetWindowLong(Handle, -20);
            Native.SetWindowLong(Handle, -20, clickThrough ? style | 0x20 : style & ~0x20);
            clickItem.Checked = clickThrough;
            tray.ShowBalloonTip(1500, "桌面黑洞", clickThrough ? "已启用鼠标穿透。可从托盘恢复操作。" : "已关闭鼠标穿透。", ToolTipIcon.Info);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 0xB001) { ToggleVisibility(); return; }
            base.WndProc(ref m);
        }

        private void ResizeHole(int size)
        {
            size = Math.Max(300, Math.Min(620, size));
            if (size == diameter) return;
            Point center = new Point(Left + diameter / 2, Top + diameter / 2);
            diameter = size;
            Size = new Size(size, size);
            Location = new Point(center.X - size / 2, center.Y - size / 2);
            DrawFrame();
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true; dragOrigin = Cursor.Position; formOrigin = Location; Capture = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point p = Cursor.Position;
            Location = new Point(formOrigin.X + p.X - dragOrigin.X, formOrigin.Y + p.Y - dragOrigin.Y);
        }

        private void DrawFrame()
        {
            if (IsDisposed || !IsHandleCreated || hidden) return;
            try
            {
                using (Bitmap capture = new Bitmap(diameter, diameter, PixelFormat.Format32bppArgb))
                {
                    if (!excluded)
                    {
                        // Compatibility path for older systems that cannot exclude this window from capture.
                        Visible = false;
                        Application.DoEvents();
                    }
                    using (Graphics g = Graphics.FromImage(capture))
                        g.CopyFromScreen(Left, Top, 0, 0, capture.Size, CopyPixelOperation.SourceCopy);
                    if (!excluded) Visible = true;
                    using (Bitmap rendered = Lens.Render(capture, (DateTime.UtcNow - started).TotalSeconds, gravity))
                        Present(rendered);
                }
            }
            catch
            {
                if (!Visible) Visible = true;
            }
        }

        private void Present(Bitmap bitmap)
        {
            IntPtr screenDc = IntPtr.Zero, memoryDc = IntPtr.Zero, hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                memoryDc = Native.CreateCompatibleDC(screenDc);
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = Native.SelectObject(memoryDc, hBitmap);
                Native.POINT dst = new Native.POINT(Left, Top), src = new Native.POINT(0, 0);
                Native.SIZE size = new Native.SIZE(diameter, diameter);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memoryDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) Native.SelectObject(memoryDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                if (memoryDc != IntPtr.Zero) Native.DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Native.UnregisterHotKey(Handle, 0xB001);
                timer.Dispose();
                tray.Visible = false;
                tray.Icon.Dispose();
                tray.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            if (args.Length == 2 && args[0] == "--preview") { MakePreview(args[1]); return; }
            if (args.Length == 2 && args[0] == "--icon")
            {
                using (Icon icon = Lens.MakeIcon())
                using (FileStream file = new FileStream(args[1], FileMode.Create, FileAccess.Write)) icon.Save(file);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BlackHoleForm());
        }

        private static void MakePreview(string path)
        {
            int n = 520;
            using (Bitmap background = new Bitmap(n, n, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(background))
                using (Font title = new Font("Microsoft YaHei", 27, FontStyle.Bold))
                using (Font body = new Font("Microsoft YaHei", 16, FontStyle.Regular))
                {
                    g.Clear(Color.FromArgb(238, 242, 248));
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    for (int y = 34; y < n; y += 48)
                        g.DrawLine(new Pen(Color.FromArgb(216, 224, 235)), 0, y, n, y);
                    g.DrawString("引力会弯曲光线", title, Brushes.MidnightBlue, 30, 57);
                    g.DrawString("桌面文字在这里发生扭曲", body, Brushes.DarkSlateGray, 29, 151);
                    g.DrawString("Gravity bends the light around us", body, Brushes.DarkSlateGray, 29, 214);
                    g.DrawString("拖动黑洞 · 滚轮调整大小", body, Brushes.DarkSlateGray, 29, 312);
                    g.DrawString("BLACK HOLE  /  LIVE LENS", body, Brushes.DarkSlateGray, 29, 400);
                }
                using (Bitmap overlay = Lens.Render(background, 1.2, 1.0))
                using (Graphics g = Graphics.FromImage(background)) g.DrawImageUnscaled(overlay, 0, 0);
                background.Save(path, ImageFormat.Png);
            }
        }
    }
}
