namespace EmpireModManager;

internal sealed record LibraryDropHint(int Index, bool After);
internal sealed record LibraryDrag(string[] Paths);
internal sealed record CategoryDrag(string Id);
internal sealed record PresetDrag(Preset Preset, Mod Mod);

public sealed partial class MainForm
{
    IEnumerable<Mod> SelectedLibraryMods() => mods.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Mod>();

    void EditCategory(ModCategory? category)
    {
        using var dialog = new Form { Text = category is null ? "New category" : "Rename category", ClientSize = new Size(420, 155),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = Font };
        dialog.SuspendLayout();
        var input = new TextBox { Text = category?.Name ?? "", Dock = DockStyle.Top, MaxLength = 100 };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Absolute, 28));
        layout.RowStyles.Add(new(SizeType.Absolute, 40));
        layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Text = "Category name", Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(input, 0, 1);
        var save = new ModernButton { Text = "Save", AutoSize = true, Height = 32 };
        var cancel = new ModernButton { Text = "Cancel", AutoSize = true, Height = 32, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try { organization.Rename(category?.Id, input.Text); dialog.DialogResult = DialogResult.OK; }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Category name"); }
        };
        layout.Controls.Add(Buttons(save, cancel), 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save; dialog.CancelButton = cancel;
        Theme.StyleDialog(dialog); Theme.StyleButton(save, Theme.Amber);
        dialog.AutoScaleDimensions = new SizeF(96, 96); dialog.AutoScaleMode = AutoScaleMode.Dpi;
        dialog.ResumeLayout(true);
        if (dialog.ShowDialog(this) == DialogResult.OK) { Filter(); status.Text = "Category saved."; }
    }

    void InstallDragAndDrop()
    {
        long nextLibraryScroll = 0, nextPresetScroll = 0;
        mods.AllowDrop = order.AllowDrop = true;
        mods.ItemDrag += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.Item is not ListViewItem item || categoryArrowGesture) return;
            object payload = item.Tag is ModCategory c ? new CategoryDrag(c.Id)
                : new LibraryDrag(SelectedLibraryMods().Select(m => m.Path).ToArray());
            libraryDragActive = true;
            try { mods.DoDragDrop(payload, DragDropEffects.Move); }
            finally
            {
                libraryDragActive = false;
                DragFeedback.Library(mods, null);
                if (pendingLibrarySort) { pendingLibrarySort = false; RefreshLibrarySort(); }
            }
        };
        mods.DragOver += (_, e) =>
        {
            var point = mods.PointToClient(new Point(e.X, e.Y));
            var hit = mods.GetItemAt(point.X, point.Y);
            var valid = hit is not null && (e.Data?.GetDataPresent(typeof(LibraryDrag)) == true ||
                e.Data?.GetDataPresent(typeof(CategoryDrag)) == true && hit.Tag is ModCategory);
            e.Effect = valid ? DragDropEffects.Move : DragDropEffects.None;
            DragFeedback.Library(mods, valid ? new LibraryDropHint(hit!.Index, hit.Tag is ModCategory || point.Y > hit.Bounds.Top + hit.Bounds.Height / 2) : null);
            if (valid && hit is not null && Environment.TickCount64 >= nextLibraryScroll)
            {
                var target = point.Y < 48 ? hit.Index - 1 : point.Y > mods.ClientSize.Height - 28 ? hit.Index + 1 : -1;
                if (target >= 0 && target < mods.Items.Count)
                {
                    nextLibraryScroll = Environment.TickCount64 + 120;
                    mods.EnsureVisible(target);
                }
            }
        };
        mods.DragLeave += (_, _) => DragFeedback.Library(mods, null);
        mods.DragDrop += (_, e) =>
        {
            try
            {
                var point = mods.PointToClient(new Point(e.X, e.Y));
                var hit = mods.GetItemAt(point.X, point.Y);
                if (hit is null) return;
                if (e.Data?.GetData(typeof(CategoryDrag)) is CategoryDrag category && hit.Tag is ModCategory destination)
                    organization.MoveCategory(category.Id, destination.Id);
                else if (e.Data?.GetData(typeof(LibraryDrag)) is LibraryDrag drag)
                {
                    var groupId = hit.Tag is ModCategory group ? group.Id : organization.CategoryOf(((Mod)hit.Tag!).Path);
                    var anchor = (hit.Tag as Mod)?.Path;
                    organization.Move(drag.Paths, groupId, anchor, point.Y > hit.Bounds.Top + hit.Bounds.Height / 2);
                    if (anchor is not null && !drag.Paths.Contains(anchor, StringComparer.OrdinalIgnoreCase))
                        ClearLibrarySort(refresh: false);
                }
                else return;
                DragFeedback.Library(mods, null);
                Filter();
                status.Text = "Library organization saved. Preset load order is unchanged.";
            }
            catch (Exception ex) { Error(ex); }
            finally { DragFeedback.Library(mods, null); }
        };

        Point start = Point.Empty;
        Mod? pressed = null;
        order.MouseDown += (_, e) =>
        {
            start = e.Location;
            var index = order.IndexFromPoint(e.Location);
            pressed = e.Button == MouseButtons.Left && index >= 0 ? order.Items[index] as Mod : null;
        };
        order.MouseUp += (_, _) => pressed = null;
        order.MouseMove += (_, e) =>
        {
            var threshold = new Rectangle(start.X - SystemInformation.DragSize.Width / 2, start.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
            if (e.Button != MouseButtons.Left || pressed is null || Current is not { } preset || threshold.Contains(e.Location)) return;
            var payload = new PresetDrag(preset, pressed); pressed = null;
            try { order.DoDragDrop(payload, DragDropEffects.Move); }
            finally { DragFeedback.Preset(order, null); }
        };
        int Slot(Point point)
        {
            var index = order.IndexFromPoint(point);
            if (index < 0) return point.Y < 0 ? 0 : order.Items.Count;
            var bounds = order.GetItemRectangle(index);
            return index + (point.Y > bounds.Top + bounds.Height / 2 ? 1 : 0);
        }
        order.DragOver += (_, e) =>
        {
            if (e.Data?.GetData(typeof(PresetDrag)) is not PresetDrag drag || Current != drag.Preset)
            { e.Effect = DragDropEffects.None; DragFeedback.Preset(order, null); return; }
            e.Effect = DragDropEffects.Move;
            var point = order.PointToClient(new Point(e.X, e.Y));
            DragFeedback.Preset(order, Slot(point));
            if (Environment.TickCount64 >= nextPresetScroll)
            {
                var target = point.Y < 22 ? order.TopIndex - 1 : point.Y > order.ClientSize.Height - 22 ? order.TopIndex + 1 : -1;
                if (target >= 0 && target < order.Items.Count)
                {
                    nextPresetScroll = Environment.TickCount64 + 120;
                    order.TopIndex = target;
                }
            }
        };
        order.DragLeave += (_, _) => DragFeedback.Preset(order, null);
        order.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(PresetDrag)) is PresetDrag drag && Current == drag.Preset)
            {
                var index = Organization.ReorderPreset(drag.Preset, drag.Mod, Slot(order.PointToClient(new Point(e.X, e.Y))));
                if (index >= 0) { DragFeedback.Preset(order, null); MarkDirty(); RefreshOrder(index); }
            }
            DragFeedback.Preset(order, null);
        };
    }
}
