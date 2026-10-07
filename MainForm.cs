using System.Diagnostics;

namespace EmpireModManager;

public sealed partial class MainForm : Form
{
    static readonly Color Background = Theme.Canvas;
    static readonly Color Surface = Theme.Panel;
    static readonly Color Ink = Theme.Text;
    static readonly Color Muted = Theme.Muted;
    readonly AppState state;
    readonly ListingOverrides listings;
    readonly Organization organization = new(Organization.DefaultPath);
    readonly WorkshopNames workshopNames = new(WorkshopNames.DefaultPath);
    readonly CancellationTokenSource closing = new();
    readonly bool onlineNames;
    int scanVersion;
    readonly HashSet<string> collapsedCategories = [];
    List<Mod> library = [];
    List<string> warnings = [];
    readonly ListBox presets = new EmptyStateList("No saved presets", "Create a preset below to get started.") { Dock = DockStyle.Fill };
    readonly ListBox order = new EmptyStateList("Build your next campaign", "Select mods in your library, then add them here.") { Dock = DockStyle.Fill };
    readonly ListView mods = new BufferedModList() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false, ShowItemToolTips = false };
    readonly TextBox search = new SearchInput { Dock = DockStyle.Fill, PlaceholderText = "Search mods, IDs, and folders…", AccessibleName = "Search mods by name, Workshop ID or folder" };
    readonly TextBox presetName = new() { Dock = DockStyle.Fill, AccessibleName = "Preset name" };
    readonly TableLayoutPanel edition = Grid(2, 1);
    readonly RadioButton foc = new EditionChoice { Text = "Forces of Corruption", Dock = DockStyle.Fill, Checked = true };
    readonly RadioButton eaw = new EditionChoice { Text = "Empire at War", Dock = DockStyle.Fill };
    readonly TextBox preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    readonly ModInspector detail = new();
    readonly LibrarySummary summary = new();
    readonly Label launchState = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9) };
    readonly TableLayoutPanel configuration = Grid(1, 11);
    readonly Button togglePreview;
    readonly InputSurface commandSurface;
    bool previewExpanded;
    bool previewRequired;
    bool fittingCommandHeight;
    readonly Label count = new() { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button launch;
    bool updating;
    bool dirty;
    Preset? Current => presets.SelectedItem as Preset;

    public MainForm(bool onlineNames = true)
    {
        this.onlineNames = onlineNames;
        SuspendLayout();
        state = Store.Load(Store.StatePath);
        listings = new(ListingOverrides.DefaultPath);
        Text = "Empire Mod Manager";
        Theme.StyleTitleBar(this);
        Size = new Size(1340, 850);
        MinimumSize = new Size(1150, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Background;
        ForeColor = Ink;
        var shell = Grid(1, 3);
        shell.Padding = new Padding(24);
        shell.RowStyles.Add(new(SizeType.Absolute, 122));
        shell.RowStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.Absolute, 34));
        Controls.Add(shell);

        var header = Grid(3, 1);
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 310));
        header.ColumnStyles.Add(new(SizeType.Absolute, 202));
        header.Controls.Add(new BrandHeader(), 0, 0);
        summary.Margin = new Padding(8, 12, 14, 23);
        header.Controls.Add(summary, 1, 0);
        var headerActions = Grid(2, 2);
        headerActions.ColumnStyles.Add(new(SizeType.Percent, 50));
        headerActions.ColumnStyles.Add(new(SizeType.Percent, 50));
        headerActions.Padding = new Padding(0, 12, 0, 24);
        headerActions.RowStyles.Add(new(SizeType.Percent, 50));
        headerActions.RowStyles.Add(new(SizeType.Percent, 50));
        foreach (var action in new[] { Button("Folders", Configure), Button("↻  Rescan", Scan) })
        {
            action.AutoSize = false;
            action.Dock = DockStyle.Fill;
            headerActions.Controls.Add(action);
        }
        updateButton.Text = "Updates";
        updateButton.Dock = DockStyle.Fill;
        updateButton.Font = new Font("Segoe UI", 9);
        updateButton.Margin = new Padding(0, 3, 6, 3);
        updateButton.Cursor = Cursors.Hand;
        Theme.StyleButton(updateButton);
        updateButton.Click += (_, _) => ShowUpdates();
        headerActions.Controls.Add(updateButton, 0, 1);
        headerActions.SetColumnSpan(updateButton, 2);
        header.Controls.Add(headerActions, 2, 0);
        shell.Controls.Add(header, 0, 0);

        var content = Grid(4, 1);
        content.ColumnStyles.Add(new(SizeType.Absolute, 224));
        content.ColumnStyles.Add(new(SizeType.Percent, 52));
        content.ColumnStyles.Add(new(SizeType.Absolute, 16));
        content.ColumnStyles.Add(new(SizeType.Percent, 48));
        var divider = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Background, Cursor = Cursors.VSplit, TabStop = true, AccessibleName = "Resize library and preset panels" };
        var resizeGuide = new PanelResizeGuide();
        Disposed += (_, _) => resizeGuide.Dispose();
        bool resizingPanels = false;
        float pendingSplit = 52;
        void PreviewSplit(Point location)
        {
            var widths = content.GetColumnWidths();
            var available = widths[1] + widths[3];
            if (available <= 0) return;
            var minLeft = Math.Min((int)(400 * DeviceDpi / 96f), available / 2);
            var minRight = Math.Min((int)(360 * DeviceDpi / 96f), available / 2);
            var point = content.PointToClient(divider.PointToScreen(location));
            var leftWidth = Math.Clamp(point.X - widths[0], minLeft, available - minRight);
            pendingSplit = 100f * leftWidth / available;
            var origin = content.PointToScreen(new Point(widths[0] + leftWidth, 0));
            resizeGuide.Bounds = new Rectangle(origin, new Size(Math.Max(3, DeviceDpi / 32), content.Height));
            if (!resizeGuide.Visible) resizeGuide.Show(this);
        }
        divider.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            resizingPanels = true;
            divider.Capture = true;
            PreviewSplit(e.Location);
        };
        divider.MouseMove += (_, e) =>
        {
            if (resizingPanels && divider.Capture && e.Button == MouseButtons.Left) PreviewSplit(e.Location);
        };
        divider.MouseUp += (_, e) =>
        {
            if (!resizingPanels || e.Button != MouseButtons.Left) return;
            resizingPanels = false;
            resizeGuide.Visible = false;
            divider.Capture = false;
            // Commit both percentages together: no partially updated layout and
            // no repeated child-window resizing while the pointer is moving.
            content.SuspendLayout();
            try
            {
                content.ColumnStyles[1].Width = pendingSplit;
                content.ColumnStyles[3].Width = 100 - pendingSplit;
            }
            finally { content.ResumeLayout(true); }
            content.Refresh();
        };
        divider.MouseCaptureChanged += (_, _) =>
        {
            if (divider.Capture) return;
            resizingPanels = false;
            resizeGuide.Visible = false;
        };
        divider.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 2 * DeviceDpi / 96f);
            var x = divider.Width / 2;
            e.Graphics.DrawLine(pen, x, divider.Height / 2 - 14, x, divider.Height / 2 + 14);
        };
        content.Controls.Add(divider, 2, 0);
        shell.Controls.Add(content, 0, 1);
        shell.Controls.Add(status, 0, 2);

        var left = Grid(1, 3);
        ((BufferedLayoutPanel)left).Card = true;
        left.BackColor = Surface;
        left.Padding = new Padding(14);
        left.Margin = new Padding(0, 0, 16, 0);
        Rows(left, 60, -1, 0);
        left.Controls.Add(Heading("YOUR COLLECTION", "Saved presets"), 0, 0);
        left.Controls.Add(presets, 0, 1);
        var presetActions = Grid(2, 2);
        presetActions.AutoSize = true;
        presetActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        presetActions.ColumnStyles.Add(new(SizeType.Percent, 50));
        presetActions.ColumnStyles.Add(new(SizeType.Percent, 50));
        Rows(presetActions, 0, 0);
        var presetButtons = new[] { Button("+  New preset", NewPreset), Button("Duplicate", Duplicate), Button("Delete", DeletePreset) };
        for (var i = 0; i < presetButtons.Length; i++)
        {
            presetButtons[i].AutoSize = false;
            presetButtons[i].Dock = DockStyle.Fill;
            presetButtons[i].Margin = new Padding(0, 3, 0, 3);
            presetActions.Controls.Add(presetButtons[i], i == 0 ? 0 : i - 1, i == 0 ? 0 : 1);
        }
        presetActions.SetColumnSpan(presetButtons[0], 2);
        presetButtons[1].Margin = new Padding(0, 3, 3, 3);
        presetButtons[2].Margin = new Padding(3, 3, 0, 3);
        left.Controls.Add(presetActions, 0, 2);
        content.Controls.Add(left, 0, 0);

        var middle = Grid(1, 6);
        ((BufferedLayoutPanel)middle).Card = true;
        middle.Padding = new Padding(16);
        middle.Margin = Padding.Empty;
        middle.BackColor = Surface;
        Rows(middle, 60, 44, 34, -1, 94, 0);
        middle.Controls.Add(new InputSurface(search, true), 0, 1);
        middle.Controls.Add(count, 0, 2);
        mods.Columns.Add("Mod", 230);
        mods.Columns.Add("Source", 85);
        mods.Columns.Add("Version", 70);
        mods.Columns.Add("Workshop ID", 120);
        mods.Columns.Add("Last updated", 125);
        mods.Columns.Add("Size", 90);
        mods.Columns.Add("Mod type", 145);
        var rowSpacing = new ImageList { ImageSize = new Size(1, (int)(32 * DeviceDpi / 96f)), ColorDepth = ColorDepth.Depth32Bit };
        mods.SmallImageList = rowSpacing;
        DpiChanged += (_, e) => rowSpacing.ImageSize = new Size(1, Math.Min(256, (int)(32 * e.DeviceDpiNew / 96f)));
        Disposed += (_, _) => rowSpacing.Dispose();
        middle.Controls.Add(LibraryHeading(), 0, 0);
        InstallListingContextMenu();
        InstallCategoryDisclosure();
        InstallDragAndDrop();
        mods.Resize += (_, _) => FitLibraryColumns();
        Shown += (_, _) => FitLibraryColumns();
        middle.Controls.Add(mods, 0, 3);
        middle.Controls.Add(detail, 0, 4);
        var libraryActions = Grid(3, 2);
        libraryActions.AutoSize = true;
        libraryActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        for (var column = 0; column < 3; column++) libraryActions.ColumnStyles.Add(new(SizeType.Percent, 100f / 3));
        Rows(libraryActions, 0, 0);
        var actionButtons = new[] {
            Button("Add to preset →", AddSelected, Theme.SelectedMod), Button("Open folder", OpenFolder), Button("+ Category", () => EditCategory(null)),
            Button("Edit listing", EditListing), Button("Workshop page", WorkshopPage), Button("Scan details", ScanDetails)
        };
        for (var i = 0; i < actionButtons.Length; i++)
        {
            actionButtons[i].AutoSize = false;
            actionButtons[i].Dock = DockStyle.Fill;
            libraryActions.Controls.Add(actionButtons[i], i % 3, i / 3);
        }
        middle.Controls.Add(libraryActions, 0, 5);
        content.Controls.Add(middle, 1, 0);

        var right = configuration;
        ((BufferedLayoutPanel)right).Card = true;
        right.Margin = Padding.Empty;
        right.Padding = new Padding(16);
        right.BackColor = Surface;
        Rows(right, 60, 42, 38, 38, -1, 0, 44, 0, 0, 52, 25);
        // The command is an optional detail. Invalid configurations expand it
        // automatically so launch errors remain visible.
        right.RowStyles[7].SizeType = SizeType.Absolute;
        right.RowStyles[8].SizeType = SizeType.Absolute;
        right.Controls.Add(Heading("PREPARE YOUR CAMPAIGN", "Preset configuration"), 0, 0);
        right.Controls.Add(new InputSurface(presetName), 0, 1);
        Theme.StyleEdition(foc);
        Theme.StyleEdition(eaw);
        edition.ColumnStyles.Add(new(SizeType.Percent, 57));
        edition.ColumnStyles.Add(new(SizeType.Percent, 43));
        edition.Controls.Add(foc, 0, 0);
        edition.Controls.Add(eaw, 1, 0);
        right.Controls.Add(edition, 0, 2);
        right.Controls.Add(new Label { Text = "LOAD ORDER\nDrag to reorder · Follow the mod author’s instructions.", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("Segoe UI", 9), Padding = new Padding(0, 3, 0, 0) }, 0, 3);
        right.Controls.Add(order, 0, 4);
        right.Controls.Add(Buttons(Button("↑ Up", () => MoveMod(-1)), Button("↓ Down", () => MoveMod(1)), Button("Remove", Remove)), 0, 5);
        togglePreview = Button("Command  ›", () => SetPreviewExpanded(!previewExpanded));
        togglePreview.AccessibleName = "Show launch command";
        togglePreview.AutoSize = false;
        togglePreview.Dock = DockStyle.Fill;
        togglePreview.MinimumSize = Size.Empty;
        var commandActions = Grid(3, 1);
        commandActions.ColumnStyles.Add(new(SizeType.Percent, 36));
        commandActions.ColumnStyles.Add(new(SizeType.Percent, 32));
        commandActions.ColumnStyles.Add(new(SizeType.Percent, 32));
        var copy = Button("Copy command", CopyCommand);
        var save = Button("Save preset", Save);
        foreach (var action in new[] { togglePreview, copy, save })
        {
            action.AutoSize = false;
            action.Dock = DockStyle.Fill;
            commandActions.Controls.Add(action);
        }
        right.Controls.Add(commandActions, 0, 6);
        commandSurface = new InputSurface(preview) { Visible = false };
        right.Controls.Add(commandSurface, 0, 7);
        launch = Button("▶   Save && launch preset", Launch);
        launch.AutoSize = false;
        launch.Dock = DockStyle.Fill;
        Theme.StyleButton(launch, Theme.Amber);
        launch.Font = new Font(Font, FontStyle.Bold);
        right.Controls.Add(launch, 0, 9);
        right.Controls.Add(launchState, 0, 10);
        content.Controls.Add(right, 3, 0);

        foreach (var control in new Control[] { presets, order, mods, search, presetName, preview })
        {
            control.BackColor = Theme.Input;
            control.ForeColor = Ink;
        }
        edition.BackColor = Color.Transparent;
        edition.ForeColor = Ink;
        preview.BackColor = Theme.Input;
        preview.ForeColor = Theme.Muted;
        presets.BorderStyle = order.BorderStyle = BorderStyle.None;
        presets.BackColor = Surface;
        presets.IntegralHeight = order.IntegralHeight = false;
        presets.ItemHeight = 56;
        order.ItemHeight = 52;
        presets.AccessibleName = "Saved presets";
        order.AccessibleName = "Preset mod load order";
        mods.AccessibleName = "Installed mod library";
        preview.AccessibleName = "Launch command preview";
        Theme.StyleList(presets);
        Theme.StyleList(order);
        // Native owner-drawn list boxes can restore their unscaled item height
        // when their handle is recreated during layout or a monitor change.
        void FitListRows()
        {
            presets.ItemHeight = (int)(56 * presets.DeviceDpi / 96f);
            order.ItemHeight = (int)(52 * order.DeviceDpi / 96f);
        }
        presets.Resize += (_, _) => FitListRows();
        order.Resize += (_, _) => FitListRows();
        order.Resize += (_, _) => FitCommandHeight();
        configuration.Layout += (_, _) => FitCommandHeight();
        presets.DpiChangedAfterParent += (_, _) => FitListRows();
        order.DpiChangedAfterParent += (_, _) => FitListRows();
        Shown += (_, _) => FitListRows();
        Theme.StyleTable(mods);
        search.BorderStyle = presetName.BorderStyle = preview.BorderStyle = BorderStyle.None;
        mods.BorderStyle = BorderStyle.None;
        preview.Font = new Font("Consolas", 9);
        count.Font = new Font("Segoe UI", 9);
        search.TextChanged += (_, _) => { searchCollapsedCategories.Clear(); Filter(); };
        presets.SelectedIndexChanged += (_, _) => SelectPreset();
        presetName.TextChanged += (_, _) =>
        {
            if (updating || Current is not { } p) return;
            p.Name = presetName.Text;
            var index = presets.SelectedIndex;
            updating = true;
            presets.Items[index] = p;
            updating = false;
            MarkDirty();
        };
        foc.CheckedChanged += (_, _) =>
        {
            if (updating || Current is not { } p) return;
            p.Game = foc.Checked ? "FoC" : "EaW";
            MarkDirty(); Filter(); UpdatePreview();
        };
        mods.SelectedIndexChanged += (_, _) =>
        {
            detail.ShowMod(SelectedMod());
        };
        FormClosing += (_, e) =>
        {
            if (!dirty) return;
            var answer = MessageBox.Show(this, "Save your preset changes before closing?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) e.Cancel = true;
            else if (answer == DialogResult.Yes) { try { Save(); } catch (Exception ex) { Error(ex); e.Cancel = true; } }
        };
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
        RefreshPresets(state.SelectedPreset);
        Scan();
        Shown += (_, _) => { if (onlineNames) { _ = RefreshWorkshopNames(scanVersion); _ = RefreshFileDetails(scanVersion); _ = CheckUpdatesOnLaunch(); } };
        FormClosed += (_, _) => closing.Cancel();
    }

    static TableLayoutPanel Grid(int columns, int rows) => new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };
    static void Rows(TableLayoutPanel panel, params int[] heights)
    { foreach (var height in heights) panel.RowStyles.Add(new(height < 0 ? SizeType.Percent : height == 0 ? SizeType.AutoSize : SizeType.Absolute, height < 0 ? 100 : height)); }
    static Control Heading(string eyebrow, string title) => new SectionHeading(eyebrow, title) { Dock = DockStyle.Fill, AccessibleName = title, Margin = Padding.Empty };
    static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
        row.Controls.AddRange(buttons);
        return row;
    }
    Button Button(string text, Action action, Color? fill = null)
    {
        var b = new ModernButton { Text = text, AutoSize = true, Height = 34, MinimumSize = new Size(0, 34), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 3, 6, 3), Padding = new Padding(7, 0, 7, 0), Cursor = Cursors.Hand };
        Theme.StyleButton(b, fill);
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { Error(ex); } };
        return b;
    }
    void Error(Exception ex) => MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    void MarkDirty() { dirty = true; status.Text = "Unsaved changes · Save preset, or launch to save automatically."; }
    void RefreshPresets(int selected)
    {
        updating = true;
        presets.Items.Clear();
        presets.Items.AddRange(state.Presets.Cast<object>().ToArray());
        presets.SelectedIndex = state.Presets.Count == 0 ? -1 : Math.Clamp(selected, 0, state.Presets.Count - 1);
        updating = false;
        SelectPreset();
        summary.UpdateCounts(library.Count, state.Presets.Count, library.Count(m => m.Source == "Workshop"));
    }
    void SelectPreset()
    {
        if (updating) return;
        updating = true;
        presetName.Text = Current?.Name ?? "";
        foc.Checked = Current?.Game != "EaW";
        eaw.Checked = !foc.Checked;
        presetName.Enabled = edition.Enabled = Current != null;
        state.SelectedPreset = presets.SelectedIndex;
        updating = false;
        RefreshOrder(); Filter();
    }
    void RefreshOrder(int selected = -1)
    {
        presets.Invalidate();
        order.Items.Clear();
        if (Current is { } p) order.Items.AddRange(p.Mods.Cast<object>().ToArray());
        if (selected >= 0 && selected < order.Items.Count) order.SelectedIndex = selected;
        UpdatePreview();
    }
    void Scan()
    {
        scanVersion++;
        UseWaitCursor = true;
        try
        {
            var result = Library.Scan(state.Settings);
            library = result.Mods; warnings = result.Warnings;
            summary.UpdateCounts(library.Count, state.Presets.Count, library.Count(m => m.Source == "Workshop"));
            workshopNames.Apply(library);
            listings.Refresh(library, state.Presets);
            Filter(); RefreshOrder();
            status.Text = dirty ? "Unsaved changes · Save preset, or launch to save automatically." : "";
        }
        finally { UseWaitCursor = false; }
        if (onlineNames && IsHandleCreated) _ = RefreshWorkshopNames(scanVersion);
        if (IsHandleCreated && onlineNames) _ = RefreshFileDetails(scanVersion);
    }
    async Task RefreshFileDetails(int version)
    {
        var snapshot = library.ToArray();
        try
        {
            var results = await Task.Run(() => snapshot.Select(mod =>
                (Mod: mod, Details: ModFileDetails.Read(mod.Path, closing.Token))).ToArray(), closing.Token);
            if (IsDisposed || closing.IsCancellationRequested || version != scanVersion) return;
            foreach (var (mod, details) in results)
            {
                mod.SizeBytes = details.SizeBytes;
                mod.LastUpdatedUtc = details.UpdatedUtc;
                mod.FileDetailsLoaded = true;
            }
            // Update cells in place so background work doesn't interrupt selection or dragging.
            foreach (ListViewItem item in mods.Items)
                if (item.Tag is Mod mod)
                { item.SubItems[4].Text = UpdatedText(mod); item.SubItems[5].Text = SizeText(mod); }
            if (librarySortColumn is 4 or 5) RefreshLibrarySort();
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed && !closing.IsCancellationRequested) status.Text = "File details unavailable: " + ex.Message; }
    }
    internal static string UpdatedText(Mod mod) => mod.Source == "Workshop"
        ? mod.WorkshopUpdatedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Unavailable"
        : !mod.FileDetailsLoaded ? "Scanning…" : mod.LastUpdatedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Unavailable";
    static string SizeText(Mod mod) => !mod.FileDetailsLoaded ? "Scanning…" : ModFileDetails.SizeText(mod.SizeBytes);
    async Task RefreshWorkshopNames(int version)
    {
        try
        {
            var warning = await workshopNames.RefreshAsync(library.Where(m => m.Source == "Workshop").Select(m => m.WorkshopId).ToArray(), closing.Token);
            if (IsDisposed || closing.IsCancellationRequested || version != scanVersion) return;
            workshopNames.Apply(library);
            listings.Refresh(library, state.Presets);
            Filter(); RefreshOrder(order.SelectedIndex);
            if (warning is not null) { warnings.Add(warning); status.Text = warning + (dirty ? " · Unsaved preset changes" : ""); }
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed && !closing.IsCancellationRequested) status.Text = "Workshop lookup unavailable: " + ex.Message; }
    }
    void Filter()
    {
        var selected = mods.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Mod>().Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var focused = mods.FocusedItem?.Tag;
        var query = search.Text.Trim();
        var game = Current?.Game ?? "FoC";
        var found = library.Where(m => m.Game == game && $"{m.Name} {m.Version} {m.WorkshopId} {m.Path} {m.ModType}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        organization.Remember(library);
        mods.BeginUpdate(); mods.Items.Clear();
        foreach (var category in organization.Categories)
        {
            var members = organization.OrderedMods(category.Id, found);
            if (librarySortColumn >= 0)
                members = members.OrderBy(m => m, new ModColumnComparer(librarySortColumn, librarySortDescending)).ToList();
            if (query.Length > 0 && members.Count == 0) continue;
            var collapsed = (query.Length == 0 ? collapsedCategories : searchCollapsedCategories).Contains(category.Id);
            var categoryItem = new CategoryListItem(category.Name, members.Count, category, collapsed) { Tag = category };
            mods.Items.Add(categoryItem);
            categoryItem.Focused = Equals(focused, category);
            if (collapsed) continue;
            foreach (var mod in members)
            {
            var item = new ListViewItem([mod.Name, mod.Source, mod.Version, string.IsNullOrEmpty(mod.WorkshopId) ? "—" : mod.WorkshopId,
                UpdatedText(mod), SizeText(mod), string.IsNullOrEmpty(mod.ModType) ? "Unspecified" : mod.ModType])
            { Tag = mod, BackColor = mods.Items.Count % 2 == 0 ? Surface : Theme.Row, ForeColor = Ink };
            mods.Items.Add(item);
            item.Selected = selected.Contains(mod.Path);
            item.Focused = focused is Mod focusedMod && focusedMod.Path.Equals(mod.Path, StringComparison.OrdinalIgnoreCase);
            }
        }
        mods.EndUpdate();
        count.Text = $"{found.Count} mods · {(game == "FoC" ? "Forces of Corruption" : "Empire at War")}";
    }
    Mod? SelectedMod() => mods.SelectedItems.Count > 0 ? mods.SelectedItems[0].Tag as Mod : null;
    void InstallListingContextMenu()
    {
        var menu = new ContextMenuStrip { BackColor = Surface, ForeColor = Ink, Renderer = new Theme.MenuRenderer() };
        var edit = new ToolStripMenuItem("Edit listing…");
        edit.Click += (_, _) => { try { EditListing(); } catch (Exception ex) { Error(ex); } };
        menu.Items.Add(edit);
        var openLocation = new ToolStripMenuItem("Open file location");
        openLocation.Click += (_, _) => { try { OpenFolder(); } catch (Exception ex) { Error(ex); } };
        menu.Items.Add(openLocation);
        var move = new ToolStripMenuItem("Move to category");
        var rename = new ToolStripMenuItem("Rename category…");
        var delete = new ToolStripMenuItem("Delete category");
        rename.Click += (_, _) => { if (mods.SelectedItems[0].Tag is ModCategory c) EditCategory(c); };
        delete.Click += (_, _) =>
        {
            if (mods.SelectedItems[0].Tag is not ModCategory c) return;
            if (MessageBox.Show(this, $"Delete '{c.Name}' and move its mods to Uncategorized?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { organization.Delete(c.Id); Filter(); } catch (Exception ex) { Error(ex); }
        };
        menu.Items.AddRange([move, rename, delete]);
        move.DropDown.Renderer = new Theme.MenuRenderer();
        move.DropDown.BackColor = Surface;
        move.DropDown.ForeColor = Ink;
        menu.Opening += (_, e) =>
        {
            e.Cancel = mods.SelectedItems.Count == 0;
            var category = mods.SelectedItems.Count == 1 ? mods.SelectedItems[0].Tag as ModCategory : null;
            edit.Visible = mods.SelectedItems.Count == 1 && category is null;
            openLocation.Visible = mods.SelectedItems.Count == 1 && SelectedMod() is not null;
            rename.Visible = delete.Visible = category is not null;
            delete.Enabled = category?.Id != Organization.Uncategorized;
            move.Visible = category is null;
            move.DropDownItems.Clear();
            foreach (var group in organization.Categories)
            {
                var entry = new ToolStripMenuItem(group.Name) { ForeColor = Ink };
                entry.Click += (_, _) =>
                { try { organization.Move(SelectedLibraryMods().Select(m => m.Path), group.Id); Filter(); } catch (Exception ex) { Error(ex); } };
                move.DropDownItems.Add(entry);
            }
        };
        mods.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var item = mods.GetItemAt(e.X, e.Y);
            // A right click targets this row, including when other rows were selected.
            mods.SelectedItems.Clear();
            if (item is null) return;
            item.Selected = true;
            item.Focused = true;
            mods.Focus();
        };
        mods.ContextMenuStrip = menu;
        mods.Disposed += (_, _) => menu.Dispose();
    }
    void EditListing()
    {
        if (mods.SelectedItems.Count != 1 || SelectedMod() is not { } mod)
        {
            MessageBox.Show(this, "Select one mod in the library to edit its name and version.", "Edit listing");
            return;
        }
        using var dialog = new ListingEditorForm(mod);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        listings.Set(mod, dialog.Result);
        Scan();
        foreach (ListViewItem item in mods.Items)
            if (item.Tag is Mod match && match.Path.Equals(mod.Path, StringComparison.OrdinalIgnoreCase))
            { item.Selected = true; item.EnsureVisible(); break; }
        status.Text = (dialog.Result is null ? "Detected name and version restored." : "Custom name and version saved.")
            + (dirty ? " · Preset changes are still unsaved." : "");
    }
    void AddSelected()
    {
        if (Current is not { } p) return;
        if (p.AddModsToTop(SelectedLibraryMods()) > 0) { MarkDirty(); RefreshOrder(0); }
    }
    void MoveMod(int delta)
    {
        if (Current is not { } p || order.SelectedIndex < 0) return;
        var index = order.SelectedIndex;
        var target = index + delta;
        if (target < 0 || target >= p.Mods.Count) return;
        (p.Mods[index], p.Mods[target]) = (p.Mods[target], p.Mods[index]);
        MarkDirty(); RefreshOrder(target);
    }
    void Remove()
    {
        if (Current is not { } p || order.SelectedIndex < 0) return;
        var index = order.SelectedIndex;
        p.Mods.RemoveAt(index); MarkDirty(); RefreshOrder(Math.Min(index, p.Mods.Count - 1));
    }
    void NewPreset()
    { state.Presets.Add(new() { Name = $"Preset {state.Presets.Count + 1}" }); RefreshPresets(state.Presets.Count - 1); MarkDirty(); }
    void Duplicate()
    {
        if (Current is not { } p) return;
        state.Presets.Add(new() { Name = p.Name + " (copy)", Game = p.Game, Mods = [.. p.Mods] });
        RefreshPresets(state.Presets.Count - 1); MarkDirty();
    }
    void DeletePreset()
    {
        if (Current is not { } p) return;
        if (MessageBox.Show(this, $"Delete preset '{p.Name}'? Mod files remain installed.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        state.Presets.Remove(p); RefreshPresets(0); MarkDirty();
    }
    void Save()
    {
        if (state.Presets.Any(p => string.IsNullOrWhiteSpace(p.Name))) throw new InvalidOperationException("Give each preset a name before saving.");
        Store.Save(state, Store.StatePath); dirty = false; status.Text = "Presets saved · " + Store.StatePath;
    }
    void UpdatePreview()
    {
        launch.Enabled = false;
        previewRequired = false;
        launchState.ForeColor = Theme.Muted;
        if (Current is not { } p)
        {
            preview.Text = "Create a preset to get started.";
            launchState.Text = "Create a preset to prepare your campaign.";
            togglePreview.AccessibleName = previewExpanded ? "Hide launch command" : "Show launch command";
            return;
        }
        try
        {
            preview.Text = Launcher.Build(state.Settings, p).Preview;
            launch.Enabled = true;
            togglePreview.AccessibleName = previewExpanded ? "Hide launch command" : "Show launch command";
            launchState.ForeColor = Theme.Green;
            launchState.Text = p.Mods.Count == 0 ? "●  Unmodded game · Command ready" : $"●  {p.Mods.Count} mods in launch order · Command ready";
        }
        catch (Exception ex)
        {
            preview.Text = ex.Message;
            previewRequired = true;
            launchState.ForeColor = Theme.Amber;
            launchState.Text = "●  Needs attention · See launch details above";
            SetPreviewExpanded(true);
        }
    }
    void SetPreviewExpanded(bool expanded)
    {
        previewExpanded = expanded || previewRequired;
        configuration.SuspendLayout();
        commandSurface.Visible = previewExpanded;
        configuration.RowStyles[7].Height = previewExpanded ? 58 * DeviceDpi / 96f : 0;
        togglePreview.Text = previewExpanded ? "Command  ⌄" : "Command  ›";
        togglePreview.AccessibleName = previewExpanded ? "Hide launch command" : "Show launch command";
        if (previewRequired) togglePreview.AccessibleName = "Launch details require attention";
        configuration.ResumeLayout(true);
        FitCommandHeight();
    }
    void FitCommandHeight()
    {
        if (fittingCommandHeight || !previewExpanded || configuration.ClientSize.Height == 0) return;
        var rows = configuration.GetRowHeights();
        if (rows.Length != configuration.RowCount) return;
        var preferred = (int)Math.Round(58 * DeviceDpi / 96f);
        var minimum = preview.Font.Height + commandSurface.Padding.Vertical + commandSurface.Margin.Vertical;
        // Reserve a complete load-order card before assigning space to the
        // expanded command. Hosted desktops can clamp the form below MinimumSize.
        var fixedHeight = rows.Where((_, index) => index != 4 && index != 7).Sum();
        var available = configuration.ClientSize.Height - configuration.Padding.Vertical - fixedHeight
            - order.Margin.Vertical - order.ItemHeight;
        var height = Math.Max(minimum, Math.Min(preferred, available));
        if (Math.Abs(configuration.RowStyles[7].Height - height) < .5f) return;
        fittingCommandHeight = true;
        try { configuration.RowStyles[7].Height = height; }
        finally { fittingCommandHeight = false; }
    }
    void CopyCommand()
    { if (Current is { } p) { Clipboard.SetText(Launcher.Build(state.Settings, p).Preview); status.Text = "Launch command copied."; } }
    void Launch()
    {
        if (Current is not { } p) return;
        var plan = Launcher.Build(state.Settings, p);
        var running = Process.GetProcessesByName("StarWarsG");
        try { if (running.Length > 0) throw new InvalidOperationException("Close the running game before launching another preset."); }
        finally { foreach (var process in running) process.Dispose(); }
        Save();
        using var started = Process.Start(plan.StartInfo()) ?? throw new InvalidOperationException("The game process could not be started.");
        status.Text = $"Launch requested: {p.Name}. Steam must be running for the Steam edition.";
    }
    void OpenFolder()
    { if (SelectedMod() is { } m) Process.Start(new ProcessStartInfo(m.Path) { UseShellExecute = true }); }
    void WorkshopPage()
    { if (SelectedMod() is { Source: "Workshop" } m) Process.Start(new ProcessStartInfo("https://steamcommunity.com/sharedfiles/filedetails/?id=" + m.WorkshopId) { UseShellExecute = true }); }
    void ScanDetails() => MessageBox.Show(this, warnings.Count == 0 ? "All available folders scanned successfully.\nMods without modinfo.json appear under their folder name or Workshop ID." : string.Join("\n\n", warnings), "Scan details");
    void Configure()
    {
        using var dialog = new FolderSettingsForm(state.Settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        state.Settings = dialog.Result;
        MarkDirty(); Scan();
    }
}

public sealed class FolderSettingsForm : Form
{
    readonly TextBox game = new() { Dock = DockStyle.Fill };
    readonly TextBox workshop = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    public Settings Result { get; private set; }
    public FolderSettingsForm(Settings settings)
    {
        SuspendLayout();
        Result = settings;
        Text = "Game and Workshop folders";
        Size = new Size(750, 450);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100)); layout.ColumnStyles.Add(new(SizeType.Absolute, 115));
        foreach (var height in new[] { 35, 40, 55, 100, 40, 45, 40 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Game installation (contains corruption and GameData)", AutoSize = true }, 0, 0);
        game.Text = settings.GameRoot; workshop.Text = string.Join(Environment.NewLine, settings.WorkshopRoots);
        layout.Controls.Add(game, 0, 1);
        var browseGame = new ModernButton { Text = "Browse…", Dock = DockStyle.Fill };
        browseGame.Click += (_, _) => { using var picker = new FolderBrowserDialog(); if (picker.ShowDialog(this) == DialogResult.OK) game.Text = picker.SelectedPath; };
        layout.Controls.Add(browseGame, 1, 1);
        layout.Controls.Add(new Label { Text = "Workshop folders: one per line, ending in workshop\\content\\32470.\nLocal mods are found in corruption\\Mods and GameData\\Mods.", Dock = DockStyle.Fill }, 0, 2);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 2)!, 2);
        layout.Controls.Add(workshop, 0, 3); layout.SetColumnSpan(workshop, 2);
        var add = new ModernButton { Text = "Add folder…", AutoSize = true };
        add.Click += (_, _) => { using var picker = new FolderBrowserDialog(); if (picker.ShowDialog(this) == DialogResult.OK) workshop.AppendText((workshop.Text.Length == 0 ? "" : Environment.NewLine) + picker.SelectedPath); };
        layout.Controls.Add(add, 0, 4);
        var detect = new ModernButton { Text = "Auto-detect", AutoSize = true };
        detect.Click += (_, _) => { var found = Settings.Detect(); game.Text = found.GameRoot; workshop.Text = string.Join(Environment.NewLine, found.WorkshopRoots); };
        layout.Controls.Add(detect, 1, 4);
        layout.Controls.Add(new Label { Text = "Names come from modinfo.json when available. Steam manages downloads.\nPreset files are stored in the manager's data folder.", Dock = DockStyle.Fill }, 0, 5);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 5)!, 2);
        var save = new ModernButton { Text = "Apply", Dock = DockStyle.Fill };
        save.Click += (_, _) =>
        {
            try
            {
                var root = Path.GetFullPath(game.Text.Trim());
                if (!File.Exists(Path.Combine(root, "corruption", "StarWarsG.exe")) && !File.Exists(Path.Combine(root, "GameData", "StarWarsG.exe")))
                    throw new InvalidOperationException("Choose the game root containing corruption or GameData with StarWarsG.exe.");
                Result = new() { GameRoot = root, WorkshopRoots = workshop.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => Path.GetFullPath(l.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check folders"); }
        };
        layout.Controls.Add(save, 1, 6);
        AcceptButton = save;
        Theme.StyleDialog(this);
        Theme.StyleButton(save, Theme.Amber);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }
}
