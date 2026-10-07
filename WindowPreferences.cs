using System.Text.Json;

namespace EmpireModManager;

internal sealed class WindowPreferences
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Dpi { get; set; } = 96;
    public bool Maximized { get; set; }
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "window.json");
    public static WindowPreferences? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(path), Store.JsonOptions) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public Rectangle Fit(Rectangle workArea, Size minimum, int dpi)
    {
        var scale = (double)dpi / Math.Max(96, Dpi);
        var width = (int)Math.Clamp(Width * scale, Math.Min(minimum.Width, workArea.Width), workArea.Width);
        var height = (int)Math.Clamp(Height * scale, Math.Min(minimum.Height, workArea.Height), workArea.Height);
        return new(Math.Clamp(X, workArea.Left, workArea.Right - width), Math.Clamp(Y, workArea.Top, workArea.Bottom - height), width, height);
    }
}

public sealed partial class MainForm
{
    bool lastMaximized;
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!onlineNames) return; // Smoke tests never read/write the user's window placement.
        if (WindowPreferences.Load(WindowPreferences.DefaultPath) is { Width: > 0, Height: > 0 } saved)
        {
            var screen = Screen.FromRectangle(new Rectangle(saved.X, saved.Y, saved.Width, saved.Height));
            StartPosition = FormStartPosition.Manual;
            Bounds = saved.Fit(screen.WorkingArea, MinimumSize, DeviceDpi);
            if (saved.Maximized) WindowState = FormWindowState.Maximized;
        }
        lastMaximized = WindowState == FormWindowState.Maximized;
        SizeChanged += (_, _) => { if (WindowState != FormWindowState.Minimized) lastMaximized = WindowState == FormWindowState.Maximized; };
        FormClosed += (_, _) =>
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            try
            {
                Store.WriteJson(new WindowPreferences { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                    Dpi = DeviceDpi, Maximized = lastMaximized }, WindowPreferences.DefaultPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MessageBox.Show("Window size could not be saved: " + ex.Message, Text); }
        };
    }
}
