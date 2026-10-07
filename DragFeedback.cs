using System.Runtime.InteropServices;

namespace EmpireModManager;

// A separate owned window lets the desktop compositor restore pixels beneath
// the guide. Moving a child Panel over sibling HWNDs leaves stale paint trails.
internal sealed class PanelResizeGuide : Form
{
    public PanelResizeGuide()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Amber;
        AccessibleName = "Panel resize preview";
    }

    protected override bool ShowWithoutActivation => true;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
        if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // NOACTIVATE | TOOLWINDOW
            return parameters;
        }
    }
}

internal sealed class BufferedLayoutPanel : TableLayoutPanel
{
    public BufferedLayoutPanel() => DoubleBuffered = true;
    [System.ComponentModel.DefaultValue(false)]
    public bool Card { get; set; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!Card) { base.OnPaintBackground(e); return; }
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Canvas);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var shape = ModernDrawing.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class BufferedModList : ListView
{
    readonly DarkHeader header;
    public int ActiveResizeColumn => header.ActiveResizeColumn;
    public int SortColumn { get; private set; } = -1;
    public SortOrder ColumnSortOrder { get; private set; }
    public void SetSortIndicator(int column, SortOrder order)
    {
        SortColumn = column;
        ColumnSortOrder = order;
        if (header.Handle != IntPtr.Zero) InvalidateRect(header.Handle, IntPtr.Zero, false);
    }
    bool nativeToolTipsEnabled = true;
    bool disclosurePressed;
    public BufferedModList()
    {
        header = new DarkHeader(this);
        // ListView maps this to the native LVS_EX_DOUBLEBUFFER style.
        DoubleBuffered = true;
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var handle = SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
        if (handle != IntPtr.Zero) header.AssignHandle(handle);
        nativeToolTipsEnabled = true;
        SetNativeToolTips(false);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        header.ResetResizeFeedback();
        header.ReleaseHandle();
        base.OnHandleDestroyed(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        SetNativeToolTips(false);
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        SetNativeToolTips(false);
        base.OnMouseLeave(e);
    }
    protected override void WndProc(ref Message m)
    {
        // Windows may offer a tooltip for an ellipsized label independently of
        // item tooltip text. Cancel its show notification for every library row.
        if (m.Msg == 0x004E && m.LParam != IntPtr.Zero
            && Marshal.PtrToStructure<NativeNotification>(m.LParam).Code == -521) // TTN_SHOW
        {
            m.Result = (IntPtr)1;
            return;
        }
        // Give the disclosure its own press/release gesture before the native
        // list starts selection or drag detection. Category names retain normal
        // ListView behavior, including dragging and double-clicking.
        if (m.Msg is 0x0201 or 0x0203 or 0x0202 or 0x0200)
        {
            var position = new Point((short)((long)m.LParam & 0xffff), (short)(((long)m.LParam >> 16) & 0xffff));
            if (m.Msg is 0x0201 or 0x0203 && CategoryDisclosure.Hit(this, position) is { } category)
            {
                disclosurePressed = true;
                Focus();
                SelectedIndices.Clear();
                category.Selected = category.Focused = true;
                Capture = true;
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, m.Msg == 0x0203 ? 2 : 1, position.X, position.Y, 0));
                return;
            }
            if (m.Msg == 0x0203 && GetItemAt(position.X, position.Y)?.Tag is ModCategory)
            {
                OnMouseDoubleClick(new MouseEventArgs(MouseButtons.Left, 2, position.X, position.Y, 0));
                return;
            }
            if (disclosurePressed && m.Msg == 0x0200)
            {
                OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, position.X, position.Y, 0));
                return;
            }
            if (disclosurePressed && m.Msg == 0x0202)
            {
                disclosurePressed = false;
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, position.X, position.Y, 0));
                Capture = false;
                return;
            }
        }
        if (m.Msg == 0x0215) disclosurePressed = false; // WM_CAPTURECHANGED
        base.WndProc(ref m);
        // Native dark-mode painting adds column dividers below the final row.
        // Keep unused list space plain while leaving the header and rows intact.
        if (m.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated || View != View.Details) return;
        var top = 0;
        if (header.Handle != IntPtr.Zero && GetClientRect(header.Handle, out var headerBounds))
            top = headerBounds.Bottom;
        if (Items.Count > 0) top = Math.Max(top, Items[Items.Count - 1].Bounds.Bottom);
        var empty = Rectangle.Intersect(ClientRectangle, new Rectangle(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top)));
        if (empty.IsEmpty) return;
        using var graphics = m.Msg is 0x0317 or 0x0318 && m.WParam != IntPtr.Zero
            ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle);
        using var background = new SolidBrush(BackColor);
        graphics.FillRectangle(background, empty);
    }
    void SetNativeToolTips(bool enabled)
    {
        if (nativeToolTipsEnabled == enabled || !IsHandleCreated) return;
        // Suppress both explicit item tips and Windows' automatic clipped-label
        // tips without recreating the ListView or losing selection.
        var tooltip = SendMessage(Handle, 0x104E, IntPtr.Zero, IntPtr.Zero); // LVM_GETTOOLTIPS
        if (tooltip != IntPtr.Zero)
        {
            if (!enabled) SendMessage(tooltip, 0x041C, IntPtr.Zero, IntPtr.Zero); // TTM_POP
            SendMessage(tooltip, 0x0401, enabled ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero); // TTM_ACTIVATE
        }
        nativeToolTipsEnabled = enabled;
    }
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr GetHeaderItemRect(IntPtr window, int message, IntPtr index, ref NativeRect rectangle);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr HitTestHeader(IntPtr window, int message, IntPtr unused, ref HeaderHitTest hit);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, bool erase);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TrackMouseEvent(ref MouseTracking tracking);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeNotification { public IntPtr Window, Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)]
    struct HeaderHitTest { public int X, Y; public uint Flags; public int Item; }
    [StructLayout(LayoutKind.Sequential)]
    struct MouseTracking { public uint Size, Flags; public IntPtr Window; public uint HoverTime; }

    sealed class DarkHeader(BufferedModList owner) : NativeWindow
    {
        public int ActiveResizeColumn { get; private set; } = -1;
        int draggedColumn = -1;
        public void ResetResizeFeedback() { draggedColumn = -1; SetResizeColumn(-1); }
        void SetResizeColumn(int column)
        {
            if (ActiveResizeColumn == column) return;
            ActiveResizeColumn = column;
            if (Handle != IntPtr.Zero) InvalidateRect(Handle, IntPtr.Zero, false);
        }
        int ResizeColumn(IntPtr position)
        {
            var hit = new HeaderHitTest { X = (short)((long)position & 0xffff), Y = (short)(((long)position >> 16) & 0xffff) };
            HitTestHeader(Handle, 0x1206, IntPtr.Zero, ref hit); // HDM_HITTEST
            // Match the native resize target, preserving heading reordering and
            // double-click autosizing. Hidden columns must not advertise a grip.
            return (hit.Flags & 0x0004) != 0 && hit.Item >= 0 && hit.Item < owner.Columns.Count && owner.Columns[hit.Item].Width > 0
                ? hit.Item : -1; // HHT_ONDIVIDER
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0200) // WM_MOUSEMOVE
            {
                SetResizeColumn(draggedColumn >= 0 ? draggedColumn : ResizeColumn(m.LParam));
                var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 2, Window = Handle }; // TME_LEAVE
                TrackMouseEvent(ref tracking);
            }
            if (m.Msg == 0x0201) { draggedColumn = ResizeColumn(m.LParam); SetResizeColumn(draggedColumn); }
            if (m.Msg is 0x0202 or 0x0215 or 0x001F) ResetResizeFeedback(); // Release, lost capture, cancel
            if (m.Msg == 0x02A3 && draggedColumn < 0) SetResizeColumn(-1); // WM_MOUSELEAVE
            base.WndProc(ref m);
            // Owner-draw column events cover only real columns. Paint the unused
            // native header area too, including during column resizing/printing.
            if (m.Msg is not (0x000F or 0x0317 or 0x0318) || Handle == IntPtr.Zero) return;
            if (!GetClientRect(Handle, out var client)) return;
            var right = 0;
            for (var i = 0; i < owner.Columns.Count; i++)
            {
                var column = new NativeRect();
                if (GetHeaderItemRect(Handle, 0x1207, (IntPtr)i, ref column) != IntPtr.Zero)
                    right = Math.Max(right, column.Right);
            }
            if (right >= client.Right) return;
            using var graphics = m.Msg is 0x0317 or 0x0318 && m.WParam != IntPtr.Zero ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle);
            using var fill = new SolidBrush(Theme.Elevated);
            graphics.FillRectangle(fill, right, 0, client.Right - right, client.Bottom);
            using var line = new Pen(Theme.Border);
            graphics.DrawLine(line, right, client.Bottom - 1, client.Right, client.Bottom - 1);
        }
    }
}

internal static class DragFeedback
{
    public static void Library(ListView list, LibraryDropHint? next)
    {
        var previous = list.Tag as LibraryDropHint;
        if (Equals(previous, next)) return;
        list.Tag = next;
        Repaint(previous);
        if (previous?.Index != next?.Index) Repaint(next);

        void Repaint(LibraryDropHint? hint)
        {
            if (hint is null || hint.Index < 0 || hint.Index >= list.Items.Count) return;
            var row = list.Items[hint.Index].Bounds;
            list.Invalidate(Rectangle.Intersect(list.ClientRectangle, new Rectangle(0, row.Top - 2, list.ClientSize.Width, row.Height + 4)));
        }
    }

    public static void Preset(ListBox list, int? next)
    {
        var previous = list.Tag as int?;
        if (previous == next) return;
        list.Tag = next;
        Repaint(previous); Repaint(next);

        void Repaint(int? slot)
        {
            if (slot is null || list.Items.Count == 0) return;
            var index = Math.Clamp(slot.Value, 0, list.Items.Count - 1);
            var row = list.GetItemRectangle(index);
            list.Invalidate(Rectangle.Intersect(list.ClientRectangle, new Rectangle(0, row.Top - 2, list.ClientSize.Width, row.Height + 4)));
        }
    }
}
