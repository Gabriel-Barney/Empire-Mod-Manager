using System.Drawing.Drawing2D;

namespace EmpireModManager;

internal sealed class SectionHeading(string eyebrow, string title) : Control
{
    public SectionHeading() : this("", "") { }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DeviceDpi / 96f;
        using var small = new Font("Segoe UI Semibold", 7.5f);
        using var heading = new Font("Segoe UI Semibold", 13.5f);
        TextRenderer.DrawText(e.Graphics, eyebrow, small, new Rectangle(0, 3, Width, (int)(18 * s)), Theme.Amber,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, title, heading, new Rectangle(0, (int)(20 * s), Width, (int)(29 * s)), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class LibrarySummary : Control
{
    int mods, presets, workshop;
    public LibrarySummary() { Dock = DockStyle.Fill; DoubleBuffered = true; }
    public void UpdateCounts(int modCount, int presetCount, int workshopCount)
    {
        mods = modCount; presets = presetCount; workshop = workshopCount;
        AccessibleName = $"{mods} installed mods, {presets} saved presets, {workshop} Workshop mods";
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var heading = new Font("Segoe UI Semibold", 19);
        using var caption = new Font("Segoe UI", 8);
        var width = Width / 3;
        var values = new[] { (mods, "INSTALLED"), (presets, "PRESETS"), (workshop, "WORKSHOP") };
        for (var i = 0; i < 3; i++)
        {
            var bounds = new Rectangle(i * width, (int)(7 * s), width - (int)(8 * s), Height - (int)(14 * s));
            using var shape = ModernDrawing.Rounded(bounds, 9 * s);
            using var fill = new SolidBrush(Theme.Panel);
            using var border = new Pen(Theme.Border);
            e.Graphics.FillPath(fill, shape);
            e.Graphics.DrawPath(border, shape);
            TextRenderer.DrawText(e.Graphics, values[i].Item1.ToString("00"), heading,
                new Rectangle(bounds.X, bounds.Y + (int)(4 * s), bounds.Width, (int)(32 * s)), i == 1 ? Theme.Amber : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, values[i].Item2, caption,
                new Rectangle(bounds.X, bounds.Y + (int)(39 * s), bounds.Width, (int)(19 * s)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

internal sealed class ModInspector : Control
{
    Mod? mod;
    public ModInspector() { Dock = DockStyle.Fill; DoubleBuffered = true; Margin = new Padding(0, 8, 0, 8); }
    public void ShowMod(Mod? selected)
    {
        mod = selected;
        AccessibleName = selected is null ? "Select a mod to inspect its details" : $"{selected.Name}. {selected.Source}. {selected.Path}. {selected.Summary}";
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = ModernDrawing.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 9 * s);
        using var fill = new LinearGradientBrush(ClientRectangle, Theme.Elevated, Theme.Input, 0f);
        e.Graphics.FillPath(fill, shape);
        using var title = new Font("Segoe UI Semibold", 9.5f);
        using var caption = new Font("Segoe UI", 8.5f);
        var inset = (int)(12 * s);
        TextRenderer.DrawText(e.Graphics, mod?.Name ?? "A closer look at your mods", title,
            new Rectangle(inset, (int)(8 * s), Width - 2 * inset, (int)(23 * s)), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, mod is null ? "Select a mod to view its details and location." : $"{mod.Source}  ·  {(string.IsNullOrEmpty(mod.Version) ? "No version label" : mod.Version)}  ·  {(string.IsNullOrEmpty(mod.ModType) ? "Installed mod" : mod.ModType)}", caption,
            new Rectangle(inset, (int)(32 * s), Width - 2 * inset, (int)(18 * s)), Theme.SelectedModEdge,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (mod is not null)
            TextRenderer.DrawText(e.Graphics, mod.Path, caption,
                new Rectangle(inset, (int)(53 * s), Width - 2 * inset, (int)(18 * s)), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class EditionChoice : RadioButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var s = DeviceDpi / 96f;
        e.Graphics.Clear(Theme.Panel);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = ModernDrawing.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 7 * s);
        using var fill = new SolidBrush(Checked ? Theme.SelectedMod : Theme.Input);
        using var border = new Pen(Focused && ShowFocusCues ? Theme.Amber : Checked ? Theme.SelectedModEdge : Theme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
        using var dot = new SolidBrush(Checked ? Theme.SelectedModEdge : Theme.Muted);
        e.Graphics.FillEllipse(dot, 10 * s, Height / 2f - 2.5f * s, 5 * s, 5 * s);
        var text = new Rectangle((int)(21 * s), 0, Width - (int)(25 * s), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled && Checked ? Theme.Text : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

// Handles the native textbox's print path as well as normal painting, so the
// search hint is visible in exported previews and on screen.
internal sealed class SearchInput : TextBox
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is not (0x0317 or 0x0318) || m.WParam == IntPtr.Zero || TextLength != 0 || Focused) return;
        using var graphics = Graphics.FromHdc(m.WParam);
        TextRenderer.DrawText(graphics, PlaceholderText, Font, ClientRectangle, Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
