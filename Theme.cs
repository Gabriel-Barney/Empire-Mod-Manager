using System.Runtime.InteropServices;

namespace EmpireModManager;

internal static class Theme
{
    public static readonly Color Canvas = Color.FromArgb(10, 15, 23);
    public static readonly Color Panel = Color.FromArgb(18, 26, 38);
    public static readonly Color Input = Color.FromArgb(13, 20, 30);
    public static readonly Color Row = Color.FromArgb(21, 30, 43);
    public static readonly Color Text = Color.FromArgb(235, 240, 247);
    public static readonly Color Muted = Color.FromArgb(155, 171, 192);
    public static readonly Color Border = Color.FromArgb(44, 58, 76);
    public static readonly Color Amber = Color.FromArgb(239, 190, 107);
    public static readonly Color Selected = Color.FromArgb(57, 47, 33);
    public static readonly Color SelectedMod = Color.FromArgb(36, 61, 85);
    public static readonly Color SelectedModEdge = Color.FromArgb(130, 193, 228);
    public static readonly Color Green = Color.FromArgb(113, 204, 165);
    public static readonly Color Elevated = Color.FromArgb(29, 41, 57);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void StyleTitleBar(Form form)
    {
        using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("EmpireModManager.AppIcon"))
        {
            if (stream is not null)
            {
                using var source = new Icon(stream);
                var icon = (Icon)source.Clone();
                form.Icon = icon;
                form.Disposed += (_, _) => icon.Dispose();
            }
        }
        void Apply()
        {
            if (SystemInformation.HighContrast) return;
            var enabled = 1;
            // Older Windows versions ignore unsupported caption-color attributes.
            DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
            var caption = ColorTranslator.ToWin32(Panel);
            var text = ColorTranslator.ToWin32(Text);
            var border = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        }
        form.HandleCreated += (_, _) => Apply();
        if (form.IsHandleCreated) Apply();
    }

    public static void StyleButton(Button button, Color? fill = null)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = fill ?? Elevated;
        button.ForeColor = fill == Amber ? Canvas : Text;
        button.FlatAppearance.BorderColor = fill == Amber ? Amber : Border;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(button.BackColor, .12f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(button.BackColor, .12f);
    }

    public static void StyleDialog(Form form)
    {
        StyleTitleBar(form);
        form.BackColor = Panel;
        form.ForeColor = Text;
        void Visit(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TextBox box) { box.BackColor = Input; box.ForeColor = Text; box.BorderStyle = BorderStyle.FixedSingle; }
                if (child is Button button) StyleButton(button);
                Visit(child);
            }
        }
        Visit(form);
    }

    public static void StyleEdition(RadioButton radio)
    {
        radio.Appearance = Appearance.Button;
        radio.FlatStyle = FlatStyle.Flat;
        radio.FlatAppearance.BorderSize = 0;
        radio.FlatAppearance.CheckedBackColor = SelectedMod;
        radio.FlatAppearance.MouseOverBackColor = Elevated;
        radio.Padding = new Padding(7, 0, 7, 0);
        radio.Margin = new Padding(0, 4, 5, 0);
        radio.Font = new Font("Segoe UI", 9);
        radio.Cursor = Cursors.Hand;
        void Refresh() => radio.ForeColor = radio.Checked ? Text : Muted;
        radio.CheckedChanged += (_, _) => Refresh();
        Refresh();
    }

    public static void StyleList(ListBox list)
    {
        list.DrawMode = DrawMode.OwnerDrawFixed;
        var hovered = -1;
        void Hover(int index)
        {
            if (index == hovered) return;
            var previous = hovered;
            hovered = index;
            if (previous >= 0 && previous < list.Items.Count) list.Invalidate(list.GetItemRectangle(previous));
            if (hovered >= 0 && hovered < list.Items.Count) list.Invalidate(list.GetItemRectangle(hovered));
        }
        list.MouseMove += (_, e) => Hover(list.IndexFromPoint(e.Location));
        list.MouseLeave += (_, _) => Hover(-1);
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            var mod = list.Items[e.Index] as Mod;
            var preset = list.Items[e.Index] as Preset;
            var scale = list.DeviceDpi / 96f;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var background = new SolidBrush(list.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            var card = Rectangle.Inflate(e.Bounds, -(int)(2 * scale), -(int)(3 * scale));
            using var shape = ModernDrawing.Rounded(card, 8 * scale);
            var fill = selected ? mod is not null ? SelectedMod : Selected : hovered == e.Index ? Elevated : mod is not null ? Panel : list.BackColor;
            using var cardFill = new System.Drawing.Drawing2D.LinearGradientBrush(card, ControlPaint.Light(fill, .025f), fill, 90f);
            using var cardEdge = new Pen(selected ? Color.FromArgb(110, mod is not null ? SelectedModEdge : Amber) : Border);
            e.Graphics.FillPath(cardFill, shape);
            if (selected || hovered == e.Index || mod is not null) e.Graphics.DrawPath(cardEdge, shape);
            if (selected)
            {
                using var accent = new SolidBrush(mod is not null ? SelectedModEdge : Amber);
                e.Graphics.FillRectangle(accent, card.X, card.Y + 10 * scale, 2 * scale, card.Height - 20 * scale);
            }
            var bounds = Rectangle.Inflate(e.Bounds, -(int)(12 * scale), 0);
            if (mod is not null)
            {
                var number = new Rectangle(bounds.X, bounds.Y + (bounds.Height - (int)(26 * scale)) / 2, (int)(28 * scale), (int)(26 * scale));
                ModernDrawing.Badge(e.Graphics, number, (e.Index + 1).ToString("00"), e.Font!, selected ? SelectedModEdge : Amber, Elevated);
                bounds.X += (int)(38 * scale);
                bounds.Width -= (int)(38 * scale);
            }
            using var caption = new Font(list.Font.FontFamily, 8.5f);
            var titleBounds = new Rectangle(bounds.X, bounds.Y + (int)(7 * scale), bounds.Width, (int)(23 * scale));
            var subtitleBounds = new Rectangle(bounds.X, bounds.Y + (int)(30 * scale), bounds.Width, (int)(18 * scale));
            TextRenderer.DrawText(e.Graphics, preset?.Name ?? (mod is not null ? (mod.Installed ? "" : "[Missing] ") + mod.Name : list.Items[e.Index].ToString()), e.Font, titleBounds, Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var subtitle = preset is not null ? $"{(preset.Game == "EaW" ? "Empire at War" : "FoC")} · {preset.Mods.Count} mods"
                : mod is not null ? $"{mod.Source}{(string.IsNullOrEmpty(mod.Version) ? "" : " · " + mod.Version)}" : "";
            TextRenderer.DrawText(e.Graphics, subtitle, caption, subtitleBounds, selected && preset is not null ? Amber : Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0 && list.Focused)
            {
                using var focus = new Pen(Amber) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                e.Graphics.DrawPath(focus, shape);
            }
            if (list.Tag is int slot && (slot == e.Index || slot == list.Items.Count && e.Index == slot - 1))
            {
                using var pen = new Pen(Amber, 2);
                var y = slot == e.Index ? e.Bounds.Top : e.Bounds.Bottom - 1;
                e.Graphics.DrawLine(pen, e.Bounds.Left, y, e.Bounds.Right, y);
            }
        };
    }

    public static void StyleTable(ListView list)
    {
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) =>
        {
            if (e.Bounds.Width <= 0) return;
            var scale = list.DeviceDpi / 96f;
            var resizing = list is BufferedModList table && table.ActiveResizeColumn == e.ColumnIndex;
            using var background = new SolidBrush(Elevated);
            e.Graphics.FillRectangle(background, e.Bounds);
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            // Reserve room for the grip so long headings never paint over it.
            var inset = (int)Math.Ceiling(6 * scale);
            var gripWidth = (int)Math.Ceiling(8 * scale);
            var sorted = list is BufferedModList sortedTable && sortedTable.SortColumn == e.ColumnIndex;
            var sortWidth = sorted ? (int)Math.Ceiling(16 * scale) : 0;
            var textBounds = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - inset - gripWidth - sortWidth), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, textBounds, Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (sorted && e.Bounds.Width >= inset + gripWidth + sortWidth)
            {
                var x = e.Bounds.Right - gripWidth - sortWidth / 2f;
                var y = e.Bounds.Top + e.Bounds.Height / 2f;
                var direction = ((BufferedModList)list).ColumnSortOrder == SortOrder.Ascending ? -1 : 1;
                using var arrow = new SolidBrush(Amber);
                e.Graphics.FillPolygon(arrow, new PointF[]
                {
                    new(x - 4 * scale, y - direction * 2 * scale),
                    new(x + 4 * scale, y - direction * 2 * scale),
                    new(x, y + direction * 3 * scale)
                });
            }
            if (e.Bounds.Width >= gripWidth)
            {
                var drawingState = e.Graphics.Save();
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var x = e.Bounds.Right - 4 * scale;
                var height = Math.Max(0, Math.Min(12 * scale, e.Bounds.Height - 8 * scale));
                var top = e.Bounds.Top + (e.Bounds.Height - height) / 2f;
                if (resizing)
                {
                    using var halo = ModernDrawing.Rounded(new RectangleF(x - 3 * scale, top - 2 * scale,
                        6 * scale, height + 4 * scale), 3 * scale);
                    using var glow = new SolidBrush(Color.FromArgb(28, Amber));
                    e.Graphics.FillPath(glow, halo);
                }
                using var grip = new Pen(resizing ? Amber : Color.FromArgb(117, 135, 157), (resizing ? 1.8f : 1.4f) * scale)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round
                };
                e.Graphics.DrawLine(grip, x, top, x, top + height);
                e.Graphics.Restore(drawingState);
            }
        };
        list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item is null || e.SubItem is null) return;
            var bounds = e.ColumnIndex == 0 ? CategoryDisclosure.NameCellBounds(list, e.Item) : e.Bounds;
            var category = e.Item.Tag is ModCategory;
            var selectedMod = e.Item.Selected && !category;
            var scale = list.DeviceDpi / 96f;
            using var background = new SolidBrush(e.Item.Selected ? (category ? Selected : SelectedMod) : category ? Elevated : e.ItemIndex % 2 == 0 ? Panel : Row);
            e.Graphics.FillRectangle(background, bounds);
            if (selectedMod && e.ColumnIndex == 0)
            {
                using var accent = new SolidBrush(SelectedModEdge);
                e.Graphics.FillRectangle(accent, bounds.Left, bounds.Top, 3, bounds.Height);
            }
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (e.Item is CategoryListItem heading && e.ColumnIndex == 0)
            {
                var target = CategoryDisclosure.Bounds(list, heading);
                var center = new PointF(target.Left + target.Width / 2f, target.Top + target.Height / 2f);
                using var button = ModernDrawing.Rounded(new RectangleF(center.X - 12 * scale, center.Y - 12 * scale, 24 * scale, 24 * scale), 6 * scale);
                using var buttonFill = new SolidBrush(Input);
                e.Graphics.FillPath(buttonFill, button);
                using var arrow = new Pen(Amber, 2.3f * scale) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                PointF[] points = heading.Collapsed
                    ? [new(center.X - 2 * scale, center.Y - 5 * scale), new(center.X + 3 * scale, center.Y), new(center.X - 2 * scale, center.Y + 5 * scale)]
                    : [new(center.X - 5 * scale, center.Y - 2 * scale), new(center.X, center.Y + 3 * scale), new(center.X + 5 * scale, center.Y - 2 * scale)];
                e.Graphics.DrawLines(arrow, points);
                using var categoryFont = new Font(list.Font.FontFamily, 9, FontStyle.Bold);
                var label = new Rectangle(target.Right + (int)(3 * scale), target.Top, Math.Max(0, list.Columns[0].Width - target.Width - (int)(8 * scale)), target.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, categoryFont, label, Amber,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else if (!category && e.ColumnIndex is 1 or 2 && e.SubItem.Text.Length > 0)
            {
                using var badgeFont = new Font(list.Font.FontFamily, 8);
                var width = Math.Min(bounds.Width - (int)(12 * scale), TextRenderer.MeasureText(e.SubItem.Text, badgeFont).Width + (int)(14 * scale));
                var badge = new Rectangle(bounds.X + (int)(5 * scale), bounds.Y + (bounds.Height - (int)(21 * scale)) / 2, width, (int)(21 * scale));
                var foreground = e.ColumnIndex == 2 ? Muted : e.SubItem.Text == "Workshop" ? SelectedModEdge : Green;
                ModernDrawing.Badge(e.Graphics, badge, e.SubItem.Text, badgeFont, foreground, Input);
            }
            else
            {
                using var categoryFont = new Font(list.Font.FontFamily, 9, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, category ? categoryFont : list.Font, Rectangle.Inflate(bounds, -5, 0),
                    selectedMod ? Color.White : category ? Amber : e.ColumnIndex == 0 ? Text : Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (list.Tag is LibraryDropHint hint && hint.Index == e.ItemIndex)
            {
                using var pen = new Pen(Amber, 2);
                var y = hint.After ? bounds.Bottom - 1 : bounds.Top;
                e.Graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
            }
        };
    }

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? Muted : Text;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var brush = new SolidBrush(e.Item.Selected ? SelectedMod : Panel);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }

    public sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemBorder => Amber;
        public override Color MenuBorder => Border;
    }
}
