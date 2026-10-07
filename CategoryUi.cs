using System.Runtime.InteropServices;

namespace EmpireModManager;

internal sealed class CategoryListItem(string name, int count, ModCategory category, bool collapsed)
    : ListViewItem([ $"{name}  ({count})", "", "", "", "", "", "" ])
{
    public bool Collapsed { get; } = collapsed;
    public ModCategory Category { get; } = category;
}

internal static class CategoryDisclosure
{
    [DllImport("user32.dll")]
    static extern int GetScrollPos(IntPtr window, int bar);

    // Use the same DPI-scaled target for painting and hit testing. Header order
    // and native horizontal scrolling both affect the Mod column's location.
    public static Rectangle Bounds(Rectangle cell, int dpi) =>
        new(cell.Left, cell.Top, Math.Min(cell.Width, (int)(34 * dpi / 96f)), cell.Height);

    public static Rectangle NameCellBounds(ListView list, ListViewItem item)
    {
        var name = list.Columns[0];
        var left = list.Columns.Cast<ColumnHeader>().Where(c => c.DisplayIndex < name.DisplayIndex).Sum(c => c.Width)
            - GetScrollPos(list.Handle, 0);
        return new Rectangle(left, item.Bounds.Top, name.Width, item.Bounds.Height);
    }
    public static Rectangle Bounds(ListView list, ListViewItem item) => Bounds(NameCellBounds(list, item), list.DeviceDpi);

    public static CategoryListItem? Hit(ListView list, Point location) =>
        list.ClientRectangle.Contains(location) && list.GetItemAt(location.X, location.Y) is CategoryListItem category && Bounds(list, category).Contains(location)
            ? category : null;
}

public sealed partial class MainForm
{
    readonly HashSet<string> searchCollapsedCategories = [];
    bool categoryArrowGesture;
    bool categoryArrowCanceled;
    ModCategory? pressedCategoryArrow;
    Point categoryArrowStart;

    void InstallCategoryDisclosure()
    {
        mods.MouseDown += (_, e) =>
        {
            var hit = e.Button == MouseButtons.Left ? CategoryDisclosure.Hit(mods, e.Location) : null;
            categoryArrowGesture = hit is not null;
            pressedCategoryArrow = e.Clicks == 1 ? hit?.Category : null;
            categoryArrowCanceled = false;
            categoryArrowStart = e.Location;
        };
        mods.MouseMove += (_, e) =>
        {
            mods.Cursor = CategoryDisclosure.Hit(mods, e.Location) is null ? Cursors.Default : Cursors.Hand;
            if (!categoryArrowGesture) return;
            var drag = SystemInformation.DragSize;
            var threshold = new Rectangle(categoryArrowStart.X - drag.Width / 2, categoryArrowStart.Y - drag.Height / 2, drag.Width, drag.Height);
            if (!threshold.Contains(e.Location)) categoryArrowCanceled = true;
        };
        mods.MouseLeave += (_, _) => { mods.Cursor = Cursors.Default; categoryArrowCanceled = true; };
        mods.MouseCaptureChanged += (_, _) =>
        {
            if (mods.Capture) return;
            categoryArrowCanceled = true;
            categoryArrowGesture = false;
            pressedCategoryArrow = null;
        };
        mods.MouseUp += (_, e) =>
        {
            var category = pressedCategoryArrow;
            var activate = e.Button == MouseButtons.Left && !categoryArrowCanceled && category is not null
                && CategoryDisclosure.Hit(mods, e.Location)?.Category.Id == category.Id;
            categoryArrowGesture = false;
            pressedCategoryArrow = null;
            if (activate) ToggleCategory(category!);
        };
        mods.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || CategoryDisclosure.Hit(mods, e.Location) is not null) return;
            if (mods.GetItemAt(e.X, e.Y)?.Tag is ModCategory category) ToggleCategory(category);
        };
        mods.DoubleClick += (_, _) => { if (SelectedLibraryMods().Any()) AddSelected(); };
        mods.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || mods.SelectedItems.Count != 1 || mods.SelectedItems[0].Tag is not ModCategory category) return;
            ToggleCategory(category);
            e.Handled = e.SuppressKeyPress = true;
        };
    }

    void ToggleCategory(ModCategory category)
    {
        var top = mods.TopItem?.Tag;
        var collapsed = search.Text.Trim().Length == 0 ? collapsedCategories : searchCollapsedCategories;
        if (!collapsed.Add(category.Id)) collapsed.Remove(category.Id);
        Filter();
        var previousTop = mods.Items.Cast<ListViewItem>().FirstOrDefault(i => Equals(i.Tag, top));
        if (previousTop is not null) mods.TopItem = previousTop;
        var heading = mods.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is ModCategory c && c.Id == category.Id);
        if (heading is not null) { heading.Selected = true; heading.Focused = true; heading.EnsureVisible(); }
    }
}
