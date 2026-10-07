using System.Drawing.Drawing2D;

namespace EmpireModManager;

internal static class ModernDrawing
{
    public static void Badge(Graphics graphics, Rectangle bounds, string text, Font font, Color foreground, Color background)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var shape = Rounded(bounds, bounds.Height / 2f);
        using var fill = new SolidBrush(background);
        graphics.FillPath(fill, shape);
        TextRenderer.DrawText(graphics, text, font, bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// Keep native button semantics, keyboard activation and accessibility while
// painting a consistent surface at the window's current DPI.
internal sealed class ModernButton : Button
{
    bool hovered;
    bool pressed;
    public ModernButton() => DoubleBuffered = true;
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Panel);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = !Enabled ? Theme.Input : pressed ? FlatAppearance.MouseDownBackColor : hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        using var shape = ModernDrawing.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 7 * scale);
        using var brush = new LinearGradientBrush(ClientRectangle, ControlPaint.Light(fill, BackColor == Theme.Amber ? .14f : .025f), fill, 90f);
        using var border = new Pen(Focused && ShowFocusCues ? Theme.Amber : FlatAppearance.BorderColor, Math.Max(1, scale));
        e.Graphics.FillPath(brush, shape);
        e.Graphics.DrawPath(border, shape);
        if (Focused && ShowFocusCues)
        {
            using var focus = ModernDrawing.Rounded(new RectangleF(4 * scale, 4 * scale, Width - 8 * scale, Height - 8 * scale), 5 * scale);
            using var focusPen = new Pen(ForeColor) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(focusPen, focus);
        }
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
        else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -(int)(7 * scale), -2), Enabled ? ForeColor : Theme.Muted, flags);
    }
}

internal sealed class InputSurface : Panel
{
    readonly TextBox input;
    readonly bool search;
    public InputSurface(TextBox input, bool search = false)
    {
        this.input = input;
        this.search = search;
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackColor = Theme.Input;
        Padding = new Padding(search ? 34 : 11, 8, 11, 6);
        Margin = new Padding(0, 3, 0, 3);
        input.BorderStyle = BorderStyle.None;
        input.Margin = Padding.Empty;
        input.Dock = DockStyle.Fill;
        Controls.Add(input);
        input.GotFocus += (_, _) => Invalidate();
        input.LostFocus += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = ModernDrawing.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * DeviceDpi / 96f);
        using var pen = new Pen(input.Focused ? Theme.Amber : Theme.Border);
        e.Graphics.DrawPath(pen, shape);
        if (search)
        {
            var s = DeviceDpi / 96f;
            using var icon = new Pen(Theme.Muted, 1.5f * s);
            e.Graphics.DrawEllipse(icon, 12 * s, Height / 2f - 6 * s, 10 * s, 10 * s);
            e.Graphics.DrawLine(icon, 21 * s, Height / 2f + 3 * s, 26 * s, Height / 2f + 8 * s);
        }
    }
}

internal sealed class EmptyStateList(string title, string description) : ListBox
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (Items.Count != 0 || m.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated) return;
        using var graphics = m.Msg is 0x0317 or 0x0318 && m.WParam != IntPtr.Zero
            ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle);
        var s = DeviceDpi / 96f;
        var y = Math.Max(4, (Height - (int)(54 * s)) / 2);
        using var heading = new Font("Segoe UI Semibold", 10);
        using var caption = new Font("Segoe UI", 9);
        TextRenderer.DrawText(graphics, title, heading, new Rectangle(10, y, Width - 20, (int)(24 * s)), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var captionTop = y + (int)(26 * s);
        var captionHeight = Math.Max(0, Math.Min((int)(40 * s), Height - captionTop - 3));
        TextRenderer.DrawText(graphics, description, caption, new Rectangle(10, captionTop, Width - 20, captionHeight), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class BrandHeader : Control
{
    readonly Bitmap programIcon;
    public BrandHeader()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        AccessibleName = "Empire Mod Manager. Your next campaign starts here.";
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("EmpireModManager.AppIcon")
            ?? throw new InvalidOperationException("The application icon is missing.");
        using var icon = new Icon(stream, new Size(256, 256));
        programIcon = icon.ToBitmap();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) programIcon.Dispose();
        base.Dispose(disposing);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width > 650 * s)
        {
            var center = new PointF(Width - 105 * s, 51 * s);
            using var orbit = new Pen(Color.FromArgb(40, 59, 77), s);
            orbit.DashStyle = DashStyle.Dash;
            e.Graphics.DrawEllipse(orbit, center.X - 92 * s, center.Y - 40 * s, 184 * s, 80 * s);
            orbit.DashStyle = DashStyle.Solid;
            e.Graphics.DrawEllipse(orbit, center.X - 48 * s, center.Y - 48 * s, 96 * s, 96 * s);
            e.Graphics.DrawEllipse(orbit, center.X - 31 * s, center.Y - 31 * s, 62 * s, 62 * s);
            using var planet = new LinearGradientBrush(new RectangleF(center.X - 18 * s, center.Y - 18 * s, 36 * s, 36 * s), Theme.Elevated, Theme.Canvas, 35f);
            e.Graphics.FillEllipse(planet, center.X - 18 * s, center.Y - 18 * s, 36 * s, 36 * s);
            using var star = new SolidBrush(Theme.Amber);
            e.Graphics.FillEllipse(star, center.X + 41 * s, center.Y - 20 * s, 5 * s, 5 * s);
            using var dimStar = new SolidBrush(Theme.Muted);
            for (var i = 0; i < 12; i++)
                e.Graphics.FillEllipse(dimStar, center.X + ((i * 47 % 190) - 95) * s, (12 + i * 23 % 84) * s, s, s);
        }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(programIcon, new RectangleF(0, 20 * s, 62 * s, 62 * s));
        using var eyebrow = new Font("Segoe UI Semibold", 8);
        using var title = new Font("Segoe UI Semibold", 23);
        using var subtitle = new Font("Segoe UI", 9.5f);
        void Line(string text, Font font, Color color, int top, int height) => TextRenderer.DrawText(e.Graphics, text, font,
            new Rectangle((int)(76 * s), (int)(top * s), Math.Max(0, Width - (int)(76 * s)), (int)(height * s)), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        Line("STAR WARS  /  EMPIRE AT WAR", eyebrow, Theme.Amber, 10, 18);
        Line("Empire Mod Manager", title, Theme.Text, 29, 40);
        Line("Your galaxy. Your rules. Your next campaign.", subtitle, Theme.Muted, 73, 22);
    }
}
