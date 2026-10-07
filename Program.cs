using System.Text.Json;

namespace EmpireModManager;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Opt into WinForms' native dark captions and scrollbars before any handles exist.
#pragma warning disable WFO5001 // .NET 9 marks native dark-mode support experimental.
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        if (args.Length > 0) Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try
        {
            if (args.Length == 2 && args[0] == "--apply-update") return UpdateInstaller.Run(args[1]);
            if (args.Length == 2 && args[0] == "--updated") UpdateInstaller.CleanupAfterRestart(args[1]);
            if (args.Contains("--self-test")) return SelfTests.Run();
            if (args.Contains("--scan"))
            {
                var report = Library.Scan(Settings.Detect());
                var path = Path.Combine(AppContext.BaseDirectory, "scan-report.json");
                File.WriteAllText(path, JsonSerializer.Serialize(report, Store.JsonOptions));
                return 0;
            }
            if (args.Contains("--refresh-workshop"))
            {
                var report = Library.Scan(Store.Load(Store.StatePath).Settings);
                var names = new WorkshopNames(WorkshopNames.DefaultPath);
                var warning = names.RefreshAsync(report.Mods.Where(m => m.Source == "Workshop").Select(m => m.WorkshopId)).GetAwaiter().GetResult();
                names.Apply(report.Mods);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "workshop-lookup-report.json"), JsonSerializer.Serialize(new { report.Mods, Warning = warning }, Store.JsonOptions));
                return warning is null ? 0 : 1;
            }
            using var form = new MainForm(!args.Contains("--smoke-test"));
            if (args.Contains("--smoke-test"))
            {
                form.Shown += (_, _) =>
                {
                    var originalSize = form.Size;
                    var layoutChecks = new List<string>();
                    foreach (var size in new[] { originalSize, form.MinimumSize, Screen.FromControl(form).WorkingArea.Size })
                    {
                        form.Size = size;
                        form.PerformLayout();
                        CheckButtonBounds(form);
                        layoutChecks.Add($"PASS: Buttons fully visible at {form.Width}x{form.Height}, DPI {form.DeviceDpi}");
                        using var layoutBitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(layoutBitmap, new Rectangle(Point.Empty, form.Size));
                        layoutBitmap.Save(Path.Combine(AppContext.BaseDirectory, $"ui-layout-{form.Width}x{form.Height}.png"));
                    }
                    form.Size = originalSize;
                    CategoryControlChecks.Run(form);
                    ColumnSortingChecks.Run(form);
                    SelectPreviewMod(form);
                    CheckPanelAndMenu(form);
                    CheckCommandDisclosure(form, layoutChecks);
                    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "layout-check-results.txt"), layoutChecks);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(AppContext.BaseDirectory, "ui-preview.png"));
                    using var editor = new ListingEditorForm(new Mod { Name = "Thrawn's Revenge", Version = "3.5 beta" });
                    editor.Show();
                    using var editorBitmap = new Bitmap(editor.Width, editor.Height);
                    editor.DrawToBitmap(editorBitmap, new Rectangle(0, 0, editor.Width, editor.Height));
                    editorBitmap.Save(Path.Combine(AppContext.BaseDirectory, "listing-editor-preview.png"));
                    editor.Close();
                    using var updates = new UpdateForm(new UpdatePreferences(), new UpdateRelease(new Version(1, 3, 0), "v1.3.0",
                        "A newer build is available.\r\n\r\nPresets and settings are preserved during installation.",
                        new Uri(GithubUpdater.RepositoryUrl + "/releases"), new Uri(GithubUpdater.RepositoryUrl), new Uri(GithubUpdater.RepositoryUrl), 50000), () => { });
                    updates.Show();
                    updates.PerformLayout();
                    CheckButtonBounds(updates);
                    updates.Size = updates.MinimumSize;
                    updates.PerformLayout();
                    CheckButtonBounds(updates);
                    CheckUpdateBounds(updates);
                    using var updatesBitmap = new Bitmap(updates.Width, updates.Height);
                    updates.DrawToBitmap(updatesBitmap, new Rectangle(Point.Empty, updates.Size));
                    updatesBitmap.Save(Path.Combine(AppContext.BaseDirectory, "updates-preview.png"));
                    updates.Close();
                    form.Close();
                };
            }
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            if (!args.Any()) MessageBox.Show(ex.Message, "Empire Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), ex.ToString());
            return 1;
        }
    }

    static void CheckButtonBounds(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button && child.Visible && !parent.ClientRectangle.Contains(child.Bounds))
                throw new InvalidOperationException($"Clipped button '{child.Text}': {child.Bounds}, container {parent.ClientRectangle}");
            if (child is Button && child.Visible)
                for (var ancestor = parent.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    var bounds = ancestor.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));
                    if (!ancestor.ClientRectangle.Contains(bounds))
                        throw new InvalidOperationException($"Button '{child.Text}' is clipped by {ancestor.GetType().Name}.");
                }
            CheckButtonBounds(child);
        }
    }

    static void CheckUpdateBounds(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Visible && !parent.ClientRectangle.Contains(child.Bounds))
                throw new InvalidOperationException($"Clipped update control '{child.Text}': {child.Bounds}, container {parent.ClientRectangle}");
            CheckUpdateBounds(child);
        }
    }

    static void CheckCommandDisclosure(MainForm form, List<string> results)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(MainForm);
        var toggle = (Button)type.GetField("togglePreview", flags)!.GetValue(form)!;
        var preview = (TextBox)type.GetField("preview", flags)!.GetValue(form)!;
        var order = (ListBox)type.GetField("order", flags)!.GetValue(form)!;
        var required = (bool)type.GetField("previewRequired", flags)!.GetValue(form)!;
        var originalSize = form.Size;
        var wasExpanded = preview.Visible;
        var setExpanded = type.GetMethod("SetPreviewExpanded", flags)!;
        form.Size = form.MinimumSize;
        setExpanded.Invoke(form, [true]);
        CheckButtonBounds(form);
        if (!preview.Visible || preview.Height < preview.Font.Height || order.Height < order.ItemHeight)
            throw new InvalidOperationException("Expanded command must leave both the command and a full load-order entry readable at minimum size.");
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "command-expanded-preview.png"));
        toggle.PerformClick();
        if (preview.Visible != required)
            throw new InvalidOperationException("The command toggle must collapse valid commands and keep launch errors visible.");
        form.Size = originalSize;
        setExpanded.Invoke(form, [wasExpanded]);
        results.Add("PASS: Command expands at minimum size, preserves readable load order, and keeps launch errors visible.");
    }

    static void SelectPreviewMod(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is ListView list)
            {
                var item = list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is Mod);
                if (item is not null) { item.Selected = true; item.EnsureVisible(); }
            }
            SelectPreviewMod(child);
        }
    }

    static void CheckPanelAndMenu(Control root)
    {
        IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        var controls = Descendants(root).ToList();
        var divider = controls.OfType<Panel>().Single(p => p.AccessibleName == "Resize library and preset panels");
        var table = (TableLayoutPanel)divider.Parent!;
        var original = table.ColumnStyles[1].Width;
        var mouseDown = typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var mouseMove = typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var mouseUp = typeof(Control).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        mouseDown.Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 1, 2, 20, 0)]);
        var layoutCount = 0;
        LayoutEventHandler countLayouts = (_, _) => layoutCount++;
        table.Layout += countLayouts;
        for (var i = 0; i < 100; i++)
          {
              mouseMove.Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 0, i < 50 ? i * 6 : -(i - 49) * 6, 20, 0)]);
              Application.DoEvents();
          }
          var guide = ((Form)root).OwnedForms.Single(c => c.AccessibleName == "Panel resize preview");
          if (guide.Parent is not null || guide.Width > 8 || !divider.Capture || Form.ActiveForm == guide)
              throw new InvalidOperationException("Resize guide must be a thin, nonactivating owned window that preserves mouse capture.");
        if (layoutCount != 0 || table.ColumnStyles[1].Width != original || !guide.Visible)
            throw new InvalidOperationException("Divider drag must show a guide without reflowing the panels.");
        mouseUp.Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 1, 2, 20, 0)]);
        table.Layout -= countLayouts;
        if (guide.Visible) throw new InvalidOperationException("Divider preview was not cleared after drop.");
        if (Math.Abs(table.ColumnStyles[1].Width - original) < .1) throw new InvalidOperationException("Panel divider did not move.");
        CheckButtonBounds(root);
        var committed = table.ColumnStyles[1].Width;
        mouseDown.Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 1, 2, 20, 0)]);
        mouseMove.Invoke(divider, [new MouseEventArgs(MouseButtons.Left, 0, 100, 20, 0)]);
        divider.Capture = false;
        if (guide.Visible || table.ColumnStyles[1].Width != committed)
            throw new InvalidOperationException("Canceled divider drag must preserve the current layout.");
        table.SuspendLayout();
        table.ColumnStyles[1].Width = original; table.ColumnStyles[3].Width = 100 - original;
        table.ResumeLayout(true);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "panel-drag-check-results.txt"),
              "PASS: 100 divider drag events cause zero panel layouts.\nPASS: Guide is a thin owned window and preserves mouse capture without activation.\nPASS: Release commits the panel widths and clears the guide.\nPASS: Cancel preserves the existing layout.\n");
        var list = controls.OfType<ListView>().Single();
          var fitColumns = typeof(MainForm).GetMethod("FitLibraryColumns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
          var visibleColumns = list.Columns.Cast<ColumnHeader>().Skip(1).Where(c => c.Width > 0).ToArray();
          foreach (var column in visibleColumns) column.Width = 20;
          fitColumns.Invoke(root, null);
          if (visibleColumns.Any(c => c.Width < TextRenderer.MeasureText(c.Text, list.Font).Width + 10))
              throw new InvalidOperationException("Column headings must recover readable widths after resizing.");
          var hideForCheck = list.Columns[5];
          var widthBeforeCheck = hideForCheck.Width;
          hideForCheck.Width = 0;
          fitColumns.Invoke(root, null);
          if (hideForCheck.Width != 0) throw new InvalidOperationException("Responsive sizing must preserve hidden columns.");
          hideForCheck.Width = widthBeforeCheck;
        var widths = list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
        list.Columns[0].Width = 240; list.Columns[1].Width = 90; list.Columns[2].Width = 90;
        Capture(list, "header-resize-preview.png");
        for (var i = 0; i < widths.Length; i++) list.Columns[i].Width = widths[i];
        if (list.SelectedItems.Count > 0 && list.ContextMenuStrip is { } menu)
        {
            menu.Show(list, new Point(30, 60));
            var move = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Move to category");
            move.ShowDropDown();
            Capture(move.DropDown, "category-menu-preview.png");
            move.HideDropDown(); menu.Close();
        }
        void Capture(Control control, string file)
        {
            using var bitmap = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, file));
        }
    }
}
