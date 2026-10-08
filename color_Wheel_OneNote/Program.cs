using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace OneNotePenWheel;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "OneNotePenWheel.Single", out bool first);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "OneNotePenWheel.Show");

        // Already running? Tell the first copy to show the wheel and exit.
        // This is how the Surface Pen top button works: it "launches" the exe.
        if (!first) { signal.Set(); return; }

        ApplicationConfiguration.Initialize();
        Application.Run(new Host(signal));
    }
}

// Hidden form: tray icon, Ctrl+Alt+F12 hotkey, and the "show" signal.
class Host : Form
{
    const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    readonly NotifyIcon tray;
    PenWheel? wheel;

    public Host(EventWaitHandle signal)
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        _ = Handle;

        tray = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "Pen Wheel v5 (drag center to move)" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show wheel", null, (s, e) => ShowWheel());
        menu.Items.Add("Reset wheel position", null, (s, e) => ResetPos());
        menu.Items.Add("Dump OneNote UI to Desktop (debug)", null, (s, e) => Driver.Dump());
        menu.Items.Add("Quit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWheel(); };

        new Thread(() => { while (true) { signal.WaitOne(); BeginInvoke(ShowWheel); } }) { IsBackground = true }.Start();
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(Handle, 1, 0x0001 | 0x0002, 0x7B); // Alt+Ctrl+F12
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, 1);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) ShowWheel();
        base.WndProc(ref m);
    }

    static readonly string PosFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OneNotePenWheel", "pos.txt");

    static Point? LoadPos()
    {
        try
        {
            var p = File.ReadAllText(PosFile).Split(',');
            return new Point(int.Parse(p[0]), int.Parse(p[1]));
        }
        catch { return null; }
    }

    static void SavePos(Point center)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PosFile)!);
            File.WriteAllText(PosFile, $"{center.X},{center.Y}");
        }
        catch { }
    }

    static void ResetPos()
    {
        try { File.Delete(PosFile); } catch { }
    }

    DateTime lastClosed = DateTime.MinValue;

    void ShowWheel()
    {
        // Wheel already open? Treat this as "toggle": close it and stop.
        if (wheel != null && !wheel.IsDisposed && wheel.Visible) { wheel.Close(); return; }

        // The wheel also closes itself when it loses focus, which can happen a split second
        // before the second launch reaches us. Don't instantly reopen in that case.
        if ((DateTime.Now - lastClosed).TotalMilliseconds < 700) return;

        // Grab the pens (and a picture of each) BEFORE the wheel covers the screen.
        var pens = Driver.FindPens();
        if (pens.Count == 0)
        {
            tray.ShowBalloonTip(2000, "No pens found", "Open desktop OneNote with the Draw tab showing.", ToolTipIcon.Info);
            return;
        }
        var imgs = pens.Select(Driver.Capture).ToList();
        // Set OpenAtCursor to true to go back to opening where the Windows cursor is.
        const bool OpenAtCursor = false;
        var at = LoadPos() ?? (OpenAtCursor ? Cursor.Position : (Driver.OneNoteCenter() ?? Cursor.Position));
        wheel = new PenWheel(imgs, i => Driver.SelectPen(i), at);
        wheel.Moved = SavePos;
        wheel.FormClosed += (s, e) => lastClosed = DateTime.Now;
        wheel.Show();
        try
        {
            File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "onenote-wheel-log.txt"),
                $"{DateTime.Now:T} target={at} cursor={Cursor.Position} wheelAt={wheel.Location} screens={Screen.AllScreens.Length}\n");
        }
        catch { }
    }
}

// Radial menu: one slice per pen, showing a screenshot of that pen from OneNote's gallery.
class PenWheel : Form
{
    const int R = 150, INNER = 45;
    readonly List<Bitmap> imgs;
    readonly Action<int> onPick;
    int hot = -1;
    bool dragging;
    Point dragOffset;
    public Action<Point>? Moved; // reports the wheel's center after the user drags it

    public PenWheel(List<Bitmap> imgs, Action<int> onPick, Point at)
    {
        this.imgs = imgs;
        this.onPick = onPick;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(2 * R + 10, 2 * R + 10);
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;

        var p = at;
        var area = Screen.FromPoint(p).WorkingArea;
        int x = Math.Max(area.Left, Math.Min(area.Right - Width, p.X - Width / 2));
        int y = Math.Max(area.Top, Math.Min(area.Bottom - Height, p.Y - Height / 2));
        Location = new Point(x, y);
    }

    float Sweep => 360f / imgs.Count;

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None; // keeps the transparent edge clean
        var outer = new Rectangle(5, 5, 2 * R, 2 * R);
        var inner = new Rectangle(5 + R - INNER, 5 + R - INNER, 2 * INNER, 2 * INNER);

        // center grip: drag this circle to move the wheel
        using (var gripFill = new SolidBrush(Color.FromArgb(60, 60, 60)))
        {
            g.FillEllipse(gripFill, inner);
            g.DrawEllipse(Pens.Gray, inner);
        }
        using (var gripFont = new Font("Segoe UI Symbol", 18f))
        {
            var sz = g.MeasureString("✥", gripFont);
            g.DrawString("✥", gripFont, Brushes.LightGray, 5 + R - sz.Width / 2, 5 + R - sz.Height / 2);
        }

        for (int i = 0; i < imgs.Count; i++)
        {
            float start = -90 + i * Sweep - Sweep / 2;
            using var path = new GraphicsPath();
            path.AddArc(outer, start, Sweep);
            path.AddArc(inner, start + Sweep, -Sweep);
            path.CloseFigure();
            using var fill = new SolidBrush(i == hot ? Color.FromArgb(90, 90, 90) : Color.FromArgb(40, 40, 40));
            g.FillPath(fill, path);
            g.DrawPath(Pens.Gray, path);

            // pen picture at the middle of the slice
            double mid = (start + Sweep / 2) * Math.PI / 180, rad = (R + INNER) / 2.0;
            float cx = 5 + R + (float)(Math.Cos(mid) * rad), cy = 5 + R + (float)(Math.Sin(mid) * rad);
            var img = imgs[i];
            float scale = Math.Min(60f / img.Width, 60f / img.Height);
            float w = img.Width * scale, h = img.Height * scale;
            g.DrawImage(img, cx - w / 2, cy - h / 2, w, h);
        }
    }

    int HitTest(int mx, int my)
    {
        double dx = mx - 5 - R, dy = my - 5 - R, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < INNER || d > R) return -1;
        double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
        double rel = ((ang + 90 + Sweep / 2) % 360 + 360) % 360;
        return (int)(rel / Sweep) % imgs.Count;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            var s = PointToScreen(e.Location);
            Location = new Point(s.X - dragOffset.X, s.Y - dragOffset.Y);
            return;
        }
        int h = HitTest(e.X, e.Y);
        if (h != hot) { hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        double dx = e.X - 5 - R, dy = e.Y - 5 - R;
        if (Math.Sqrt(dx * dx + dy * dy) < INNER)
        {
            // pressed the center grip: start dragging the wheel
            dragging = true;
            dragOffset = e.Location;
            Capture = true;
            return;
        }
        int h = HitTest(e.X, e.Y);
        Close();
        if (h >= 0) onPick(h);
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging)
        {
            dragging = false;
            Capture = false;
            Moved?.Invoke(new Point(Location.X + Width / 2, Location.Y + Height / 2));
        }
        base.OnMouseUp(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var b in imgs) b.Dispose();
        base.OnFormClosed(e);
    }
}

class PenInfo
{
    public AutomationElement El = null!;
    public double X, Y, W, H;
}

// Finds OneNote's pen gallery items and selects one directly (no menus, no flashing).
static class Driver
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    static AutomationElement? FindOneNote()
    {
        foreach (var p in Process.GetProcessesByName("ONENOTE"))
            if (p.MainWindowHandle != IntPtr.Zero) return AutomationElement.FromHandle(p.MainWindowHandle);
        return null;
    }

    public static Point? OneNoteCenter()
    {
        var win = FindOneNote();
        if (win == null) return null;
        var r = win.Current.BoundingRectangle;
        return new Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
    }

    // Pens = selectable ribbon items whose name mentions pen/highlighter, left-to-right, max 8.
    public static List<PenInfo> FindPens()
    {
        var list = new List<PenInfo>();
        var win = FindOneNote();
        if (win == null) return list;

        var cond = new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty, true);
        foreach (AutomationElement e in win.FindAll(TreeScope.Descendants, cond))
        {
            try
            {
                if (e.Current.IsOffscreen) continue;
                var n = e.Current.Name;
                if (!n.Contains("pen", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("highlighter", StringComparison.OrdinalIgnoreCase)) continue;
                var r = e.Current.BoundingRectangle;
                if (r.Width < 5 || r.Height < 5) continue;
                list.Add(new PenInfo { El = e, X = r.X, Y = r.Y, W = r.Width, H = r.Height });
            }
            catch { /* element vanished mid-scan */ }
        }
        return list.OrderBy(p => p.X).ThenBy(p => p.Y).Take(8).ToList();
    }

    public static Bitmap Capture(PenInfo p)
    {
        var bmp = new Bitmap((int)p.W, (int)p.H);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, bmp.Size);
        return bmp;
    }

    public static void SelectPen(int index)
    {
        try
        {
            var pens = FindPens();
            if (index >= pens.Count) return;
            var el = pens[index].El;
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)) ((SelectionItemPattern)p).Select();
            else Click(pens[index]);
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "onenote-wheel-log.txt"),
                $"{DateTime.Now}: {ex.Message}\n");
        }
    }

    static void Click(PenInfo p)
    {
        SetCursorPos((int)(p.X + p.W / 2), (int)(p.Y + p.H / 2));
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    // Debug: dump all OneNote UI element names to the Desktop.
    public static void Dump()
    {
        var win = FindOneNote();
        if (win == null) { MessageBox.Show("Open OneNote first."); return; }
        var sb = new StringBuilder();
        foreach (AutomationElement e in win.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            try { sb.AppendLine($"{e.Current.ControlType.ProgrammaticName} | {e.Current.Name} | {e.Current.ClassName}"); } catch { }
        }
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "onenote-ui-dump.txt");
        File.WriteAllText(path, sb.ToString());
        MessageBox.Show("Saved to " + path);
    }
}
