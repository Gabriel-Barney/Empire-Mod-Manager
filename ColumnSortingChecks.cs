using System.Reflection;
using System.Runtime.InteropServices;

namespace EmpireModManager;

internal static class ColumnSortingChecks
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr HeaderRect(IntPtr window, int message, IntPtr index, ref NativeRect rectangle);
    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    public static void Run(MainForm form)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainForm);
        var list = (BufferedModList)type.GetField("mods", flags)!.GetValue(form)!;
        var search = (TextBox)type.GetField("search", flags)!.GetValue(form)!;
        var organization = (Organization)type.GetField("organization", flags)!.GetValue(form)!;
        var originalLibrary = type.GetField("library", flags)!.GetValue(form);
        var originalCategories = organization.Categories.ToArray();
        var originalQuery = search.Text;
        var originalOrder = list.Columns.Cast<ColumnHeader>().Select(c => c.DisplayIndex).ToArray();
        var results = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            results.Add("PASS: " + name);
        }
        void Invoke(string method, params object[] args) => type.GetMethod(method, flags)!.Invoke(form, args);
        var game = ((Preset?)type.GetProperty("Current", flags)!.GetValue(form))?.Game ?? "FoC";
        var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Mod Fixture(string name, string source, string version, string id, int? day, long? size, string modType) => new()
        {
            Name = name, Path = Path.Combine(AppContext.BaseDirectory, "sorting-fixture", name), Game = game,
            Source = source, Version = version, WorkshopId = id, ModType = modType,
            LastUpdatedUtc = day is { } localDay ? date.AddDays(localDay) : null,
            WorkshopUpdatedUtc = day is { } workshopDay ? new DateTimeOffset(date.AddDays(workshopDay)) : null,
            SizeBytes = size, FileDetailsLoaded = true
        };
        var zeta = Fixture("Zeta", "Workshop", "3.10", "10", 3, 5 * 1024 * 1024, "Utility");
        var alpha = Fixture("alpha", "Local", "3.9", "2", 1, 900 * 1024, "Main mod");
        var beta = Fixture("Beta", "Workshop", "", "", null, null, "");
        var gamma = Fixture("gamma", "Local", "3.11", "100", 2, 2 * 1024 * 1024, "Compatibility patch");
        var separate = Fixture("Another category", "Local", "1.0", "", 1, 1, "Utility");
        Mod[] fixture = [zeta, alpha, beta, gamma, separate];
        var first = new ModCategory { Id = "sorting-check-first", Name = "First category", Paths = [zeta.Path, alpha.Path, beta.Path, gamma.Path] };
        var second = new ModCategory { Id = "uncategorized", Name = "Second category", Paths = [separate.Path] };
        string[] Names() => list.Items.Cast<ListViewItem>().Select(i => i.Tag).OfType<Mod>().Where(m => m != separate).Select(m => m.Name).ToArray();
        void Click(int column)
        {
            if (list.Columns[column].Width == 0)
            {
                // Hidden headings have no mouse target; still exercise their
                // event path without changing the user's visibility preferences.
                typeof(ListView).GetMethod("OnColumnClick", flags)!.Invoke(list, [new ColumnClickEventArgs(column)]);
                return;
            }
            // Move each heading into view, then use native mouse messages. This
            // checks column identity after reordering, not just a method call.
            list.Columns[column].DisplayIndex = 0;
            SendMessage(list.Handle, 0x1014, (IntPtr)(-10000), IntPtr.Zero);
            var header = SendMessage(list.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
            var bounds = new NativeRect();
            if (HeaderRect(header, 0x1207, (IntPtr)column, ref bounds) == IntPtr.Zero)
                throw new InvalidOperationException("Could not locate a column heading.");
            var x = bounds.Left + Math.Min(20, (bounds.Right - bounds.Left) / 2);
            var y = (bounds.Top + bounds.Bottom) / 2;
            var point = (IntPtr)((y << 16) | (x & 0xffff));
            PostMessage(header, 0x0201, (IntPtr)1, point);
            PostMessage(header, 0x0202, IntPtr.Zero, point);
            Application.DoEvents();
        }
        try
        {
            organization.Categories.Clear();
            organization.Categories.AddRange([first, second]);
            type.GetField("library", flags)!.SetValue(form, fixture.ToList());
            search.Clear();
            Invoke("Filter");
            var chosen = list.Items.Cast<ListViewItem>().Single(i => i.Tag == zeta);
            chosen.Selected = chosen.Focused = true;
            string[][] ascending =
            [
                ["alpha", "Beta", "gamma", "Zeta"], ["alpha", "gamma", "Beta", "Zeta"],
                ["alpha", "Zeta", "gamma", "Beta"], ["alpha", "Zeta", "gamma", "Beta"],
                ["alpha", "gamma", "Zeta", "Beta"], ["alpha", "gamma", "Zeta", "Beta"],
                ["gamma", "alpha", "Beta", "Zeta"]
            ];
            string[][] descending =
            [
                ["Zeta", "gamma", "Beta", "alpha"], ["Beta", "Zeta", "alpha", "gamma"],
                ["gamma", "Zeta", "alpha", "Beta"], ["gamma", "Zeta", "alpha", "Beta"],
                ["Zeta", "gamma", "alpha", "Beta"], ["Zeta", "gamma", "alpha", "Beta"],
                ["Zeta", "Beta", "alpha", "gamma"]
            ];
            for (var column = 0; column < 7; column++)
            {
                Click(column);
                Check(list.SortColumn == column && list.ColumnSortOrder == SortOrder.Ascending && Names().SequenceEqual(ascending[column]),
                    $"Heading click sorts {list.Columns[column].Text} ascending.");
                Click(column);
                Check(list.ColumnSortOrder == SortOrder.Descending && Names().SequenceEqual(descending[column]),
                    $"Second click sorts {list.Columns[column].Text} descending, with missing values last.");
                Check(list.Items.OfType<CategoryListItem>().Select(i => i.Category.Id).SequenceEqual([first.Id, second.Id])
                    && list.Items[list.Items.Count - 1].Tag == separate, "Sorting keeps category headings and membership together.");
                Check(list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag == zeta && list.FocusedItem?.Tag == zeta,
                    "Sorting preserves selected mods and keyboard focus.");
            }
            search.Text = "alpha";
            Check(Names().SequenceEqual(["alpha"]) && list.SortColumn == 6, "Search retains the active sort.");
            search.Clear();
            Check(Names().SequenceEqual(descending[6]), "Clearing search restores the sorted category contents.");
            Invoke("ClearLibrarySort", true);
            Check(list.SortColumn == -1 && Names().SequenceEqual(["Zeta", "alpha", "Beta", "gamma"]),
                "Clearing sorting restores saved manual order.");
            Click(5);
            zeta.SizeBytes = 1;
            Invoke("RefreshLibrarySort");
            Check(Names().SequenceEqual(["Zeta", "alpha", "gamma", "Beta"]), "Newly loaded sizes update the active sort.");
            type.GetField("libraryDragActive", flags)!.SetValue(form, true);
            zeta.SizeBytes = 6 * 1024 * 1024;
            Invoke("RefreshLibrarySort");
            Check(Names().SequenceEqual(["Zeta", "alpha", "gamma", "Beta"])
                && (bool)type.GetField("pendingLibrarySort", flags)!.GetValue(form)!, "Background sorting waits until a library drag finishes.");
            type.GetField("libraryDragActive", flags)!.SetValue(form, false);
            type.GetField("pendingLibrarySort", flags)!.SetValue(form, false);
            Invoke("RefreshLibrarySort");
            Check(Names().SequenceEqual(["alpha", "gamma", "Zeta", "Beta"]), "Deferred sorting uses newly loaded values after the drag.");
            Invoke("ToggleCategory", first);
            Click(0);
            Check(!Names().Any() && list.Items.OfType<CategoryListItem>().First().Collapsed,
                "Clicking a heading preserves collapsed categories.");
            Invoke("ToggleCategory", first);
            Check(first.Paths.SequenceEqual([zeta.Path, alpha.Path, beta.Path, gamma.Path]), "Sorting never rewrites saved category order.");
            using var bitmap = new Bitmap(list.Width, list.Height);
            list.DrawToBitmap(bitmap, new Rectangle(Point.Empty, list.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "column-sorting-preview.png"));
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "column-sorting-check-results.txt"), results);
        }
        finally
        {
            type.GetField("libraryDragActive", flags)!.SetValue(form, false);
            type.GetField("pendingLibrarySort", flags)!.SetValue(form, false);
            organization.Categories.Clear();
            organization.Categories.AddRange(originalCategories);
            type.GetField("library", flags)!.SetValue(form, originalLibrary);
            search.Text = originalQuery;
            for (var i = 0; i < originalOrder.Length; i++) list.Columns[i].DisplayIndex = originalOrder[i];
            Invoke("ClearLibrarySort", true);
        }
    }
}
