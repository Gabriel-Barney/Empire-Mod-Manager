namespace EmpireModManager;

public sealed class ListingEditorForm : Form
{
    public ListingDetails? Result { get; private set; }

    public ListingEditorForm(Mod mod)
    {
        SuspendLayout();
        Text = "Edit mod listing";
        ClientSize = new Size(570, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 9 };
        foreach (var height in new[] { 44, 27, 40, 27, 40, 47, 27, 40, 45 })
            layout.RowStyles.Add(new(SizeType.Absolute, height));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Custom labels apply throughout the manager and survive rescans.\nChanges are saved immediately; mod files are not edited.", Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(new Label { Text = "Display name", Dock = DockStyle.Fill }, 0, 1);
        var name = new TextBox { Text = mod.Name, Dock = DockStyle.Fill, MaxLength = 200 };
        layout.Controls.Add(name, 0, 2);
        layout.Controls.Add(new Label { Text = "Version", Dock = DockStyle.Fill }, 0, 3);
        var version = new TextBox { Text = mod.Version, Dock = DockStyle.Fill, MaxLength = 100 };
        layout.Controls.Add(version, 0, 4);
        layout.Controls.Add(new Label { Text = "Use any version label, such as 3.5, beta, or my custom build.\nLeave the version empty to hide it.", Dock = DockStyle.Fill }, 0, 5);
        layout.Controls.Add(new Label { Text = "Mod type (choose or enter your own)", Dock = DockStyle.Fill }, 0, 6);
        var modType = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Input, ForeColor = Theme.Text };
        modType.Items.AddRange(["Main mod", "Submod", "Compatibility patch", "Utility"]);
        modType.Text = mod.ModType;
        layout.Controls.Add(modType, 0, 7);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Button MakeButton(string text) => new ModernButton() { Text = text, AutoSize = true, Height = 32, Margin = new Padding(0, 0, 10, 0) };
        var save = MakeButton("Save listing");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            { MessageBox.Show(this, "Enter a display name.", Text); name.Focus(); return; }
            Result = new(name.Text.Trim(), version.Text.Trim(), modType.Text.Trim());
            DialogResult = DialogResult.OK;
        };
        var reset = MakeButton("Reset to detected");
        reset.Click += (_, _) => { Result = null; DialogResult = DialogResult.OK; };
        var cancel = MakeButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([save, reset, cancel]);
        layout.Controls.Add(buttons, 0, 8);
        AcceptButton = save;
        CancelButton = cancel;
        Theme.StyleDialog(this);
        Theme.StyleButton(save, Theme.Amber);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }
}
