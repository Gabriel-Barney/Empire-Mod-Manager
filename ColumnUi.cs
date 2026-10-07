using System.Text.Json;

namespace EmpireModManager;

internal sealed class ColumnVisibility
{
    public HashSet<string> Hidden { get; set; } = [];
    public List<string> Order { get; set; } = [];
    public List<string> ResolveOrder(IEnumerable<string> available)
    {
        var names = available.ToList();
        return (Order ?? []).Where(names.Contains).Concat(names).Distinct().ToList();
    }
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "columns.json");
    public static ColumnVisibility Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ColumnVisibility>(File.ReadAllText(path), Store.JsonOptions) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
}

public sealed partial class MainForm
{
    bool fittingColumns;
    readonly Dictionary<int, float> preferredColumnWidths = [];
    void FitLibraryColumns()
    {
        if (fittingColumns || mods.Columns.Count != 7) return;
        fittingColumns = true;
        try
        {
            int[] defaults = [280, 90, 85, 130, 125, 100, 160];
            for (var i = 1; i < mods.Columns.Count; i++)
            {
                var column = mods.Columns[i];
                if (column.Width == 0) continue;
                var readable = Math.Max((int)(Math.Max(defaults[i], preferredColumnWidths.GetValueOrDefault(i)) * DeviceDpi / 96f),
                    TextRenderer.MeasureText(column.Text, mods.Font).Width + (int)(20 * DeviceDpi / 96f));
                if (column.Width != readable) column.Width = readable;
            }
            mods.Columns[0].Width = Math.Max((int)(280 * DeviceDpi / 96f), mods.ClientSize.Width -
                mods.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width) - SystemInformation.VerticalScrollBarWidth - 4);
        }
        finally { fittingColumns = false; }
    }
    Control LibraryHeading()
    {
        var heading = Grid(2, 1);
        heading.ColumnStyles.Add(new(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new(SizeType.Absolute, 115));
        heading.Controls.Add(Heading("BROWSE & ORGANIZE", "Mod library"), 0, 0);
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), BackColor = Surface, ForeColor = Ink };
        var preferences = ColumnVisibility.Load(ColumnVisibility.DefaultPath);
        preferences.Hidden ??= [];
        var columnOrder = preferences.ResolveOrder(mods.Columns.Cast<ColumnHeader>().Select(c => c.Text));
        for (var i = 0; i < columnOrder.Count; i++)
            mods.Columns.Cast<ColumnHeader>().Single(c => c.Text == columnOrder[i]).DisplayIndex = i;
        mods.AllowColumnReorder = true;
        mods.ColumnClick += (_, e) => SortLibraryColumn(e.Column);
        mods.ColumnReordered += (_, e) =>
        {
            // This event fires before the native header commits the new order.
            var reordered = mods.Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex).Select(c => c.Text).ToList();
            var moved = reordered[e.OldDisplayIndex];
            reordered.RemoveAt(e.OldDisplayIndex);
            reordered.Insert(e.NewDisplayIndex, moved);
            preferences.Order = reordered;
            SaveColumns();
            BeginInvoke(() => mods.Invalidate());
        };
        var widths = new Dictionary<int, int>();
        var choices = new Dictionary<int, ToolStripMenuItem>();
        void SetVisible(int index, bool visible)
        {
            var column = mods.Columns[index];
            if (visible) { preferences.Hidden.Remove(column.Text); if (column.Width == 0) column.Width = widths.GetValueOrDefault(index, 120); }
            else { if (column.Width > 0) widths[index] = column.Width; preferences.Hidden.Add(column.Text); column.Width = 0; }
            choices[index].Checked = visible;
            FitLibraryColumns();
        }
        void SaveColumns()
        {
            try { Store.WriteJson(preferences, ColumnVisibility.DefaultPath); }
            catch (Exception ex) { Error(ex); }
        }
        // Keep names available for identifying and dragging library entries.
        menu.Items.Add(new ToolStripMenuItem("Mod name (always shown)") { Enabled = false, Checked = true });
        for (var i = 1; i < mods.Columns.Count; i++)
        {
            var index = i;
            widths[index] = mods.Columns[index].Width;
            var choice = new ToolStripMenuItem(mods.Columns[index].Text);
            choices[index] = choice;
            choice.Click += (_, _) => { SetVisible(index, !choice.Checked); SaveColumns(); };
            menu.Items.Add(choice);
            SetVisible(index, !preferences.Hidden.Contains(mods.Columns[index].Text));
        }
        mods.ColumnWidthChanging += (_, e) =>
        {
            if (preferences.Hidden.Contains(mods.Columns[e.ColumnIndex].Text)) { e.Cancel = true; e.NewWidth = 0; }
            else if (!fittingColumns && e.ColumnIndex > 0)
            { e.NewWidth = Math.Max(1, e.NewWidth); preferredColumnWidths[e.ColumnIndex] = e.NewWidth * 96f / DeviceDpi; }
        };
        menu.Items.Add(new ToolStripSeparator());
        var savedOrder = new ToolStripMenuItem("Use saved category order");
        savedOrder.Click += (_, _) => ClearLibrarySort();
        menu.Opening += (_, _) => savedOrder.Enabled = librarySortColumn >= 0;
        menu.Items.Add(savedOrder);
        foreach (var visible in new[] { true, false })
        {
            var action = new ToolStripMenuItem(visible ? "Show all columns" : "Hide all optional columns");
            action.Click += (_, _) => { foreach (var index in choices.Keys) SetVisible(index, visible); SaveColumns(); };
            menu.Items.Add(action);
        }
        var button = Button("Columns ▾", () => { });
        button.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2, 12, 2, 10);
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        heading.Controls.Add(button, 1, 0);
        Disposed += (_, _) => menu.Dispose();
        return heading;
    }
}
