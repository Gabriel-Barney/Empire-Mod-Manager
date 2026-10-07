using System.Reflection;
using System.Runtime.InteropServices;

namespace EmpireModManager;

internal static class CategoryControlChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    public static void Run(MainForm form)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var list = (ListView)typeof(MainForm).GetField("mods", flags)!.GetValue(form)!;
        var search = (TextBox)typeof(MainForm).GetField("search", flags)!.GetValue(form)!;
        var results = new List<string>();
        var original = list.Items.OfType<CategoryListItem>().FirstOrDefault();
        if (original is null) throw new InvalidOperationException("Expected a category heading.");
        var id = original.Category.Id;
        CategoryListItem Heading() => list.Items.OfType<CategoryListItem>().Single(i => i.Category.Id == id);
        Point Target()
        {
            var item = Heading();
            item.EnsureVisible();
            var bounds = CategoryDisclosure.Bounds(list, item);
            // Deliberately click outside the drawn icon, near the target's edge.
            return new(bounds.Right - (int)(3 * list.DeviceDpi / 96f), bounds.Top + bounds.Height / 2);
        }
        void Mouse(int message, Point point, bool held = false) => PostMessage(list.Handle, message, held ? (IntPtr)1 : IntPtr.Zero,
            (IntPtr)((point.Y << 16) | (point.X & 0xffff)));
        void Click(bool doubleClick = false)
        {
            var point = Target();
            Mouse(doubleClick ? 0x0203 : 0x0201, point, true);
            Mouse(0x0202, point);
            Application.DoEvents();
        }
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            results.Add("PASS: " + description);
        }
        var collapsed = Heading().Collapsed;
        Check(string.IsNullOrEmpty(Heading().ToolTipText), "Category headings have no instructional tooltip.");
        Mouse(0x0200, Target());
        Application.DoEvents();
        typeof(BufferedModList).GetMethod("OnMouseMove", flags)!.Invoke(list, [new MouseEventArgs(MouseButtons.None, 0, Target().X, Target().Y, 0)]);
        Check(!(bool)typeof(BufferedModList).GetField("nativeToolTipsEnabled", flags)!.GetValue(list)!, "Hovering a category suppresses native tooltips, including clipped labels.");
        if (list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is Mod) is { } mod)
        {
            mod.EnsureVisible();
            Mouse(0x0200, new Point(10, mod.Bounds.Top + mod.Bounds.Height / 2));
            Application.DoEvents();
            typeof(BufferedModList).GetMethod("OnMouseMove", flags)!.Invoke(list, [new MouseEventArgs(MouseButtons.None, 0, 10, mod.Bounds.Top + mod.Bounds.Height / 2, 0)]);
            Check(!list.ShowItemToolTips && string.IsNullOrEmpty(mod.ToolTipText)
                && !(bool)typeof(BufferedModList).GetField("nativeToolTipsEnabled", flags)!.GetValue(list)!,
                "Hovering mod rows keeps native tooltips disabled and has no popup text.");
        }
        Click();
        Check(Heading().Collapsed != collapsed, "One click near the large arrow target's edge toggles the category.");
        Click(true);
        Check(Heading().Collapsed != collapsed, "A double-click's second press does not toggle the arrow back.");
        Click();
        Check(Heading().Collapsed == collapsed, "The next single click reliably restores the category.");
        var start = Target();
        Mouse(0x0201, start, true);
        var moved = new Point(start.X - SystemInformation.DragSize.Width - 2, start.Y);
        Mouse(0x0200, moved, true);
        Mouse(0x0202, moved);
        Application.DoEvents();
        Check(Heading().Collapsed == collapsed, "Dragging away from an arrow does not toggle or reorder a category.");
        var label = Target();
        label.X += (int)(25 * list.DeviceDpi / 96f);
        Mouse(0x0201, label, true); Mouse(0x0202, label);
        Mouse(0x0203, label, true); Mouse(0x0202, label);
        Application.DoEvents();
        Check(Heading().Collapsed != collapsed, "Double-clicking the category name still toggles it.");
        Click();
        var displayIndex = list.Columns[0].DisplayIndex;
        list.Columns[0].DisplayIndex = 2;
        SendMessage(list.Handle, 0x1014, (IntPtr)80, IntPtr.Zero); // LVM_SCROLL
        Click();
        Check(Heading().Collapsed != collapsed, "Arrow hit testing follows reordered columns and horizontal scrolling.");
        using (var bitmap = new Bitmap(list.Width, list.Height))
        {
            list.DrawToBitmap(bitmap, new Rectangle(Point.Empty, list.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "category-reordered-preview.png"));
        }
        Click();
        list.Columns[0].DisplayIndex = displayIndex;
        SendMessage(list.Handle, 0x1014, (IntPtr)(-10000), IntPtr.Zero);
        if (list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is Mod) is { Tag: Mod match })
        {
            search.Text = match.Name;
            id = list.Items.OfType<CategoryListItem>().First().Category.Id;
            Check(!Heading().Collapsed, "A new search initially expands matching categories.");
            Click();
            Check(Heading().Collapsed, "Category arrows collapse matching search results.");
            Click();
            Check(!Heading().Collapsed, "Category arrows expand matching search results.");
            search.Clear();
        }
        list.SelectedIndices.Clear();
        list.TopItem = list.Items[0];
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "category-control-check-results.txt"), results);
    }
}
