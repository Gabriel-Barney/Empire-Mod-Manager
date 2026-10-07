using System.Diagnostics;

namespace EmpireModManager;

public sealed partial class MainForm
{
    readonly Button updateButton = new ModernButton();
    readonly UpdatePreferences updatePreferences = UpdatePreferences.Load();
    UpdateRelease? availableUpdate;
    bool updateDialogOpen;

    async Task CheckUpdatesOnLaunch()
    {
        if (!updatePreferences.CheckOnLaunch) return;
        try
        {
            availableUpdate = await GithubUpdater.CheckAsync(closing.Token);
            if (IsDisposed || closing.IsCancellationRequested || availableUpdate is null) return;
            updateButton.Text = $"Update to {availableUpdate.Version}";
            Theme.StyleButton(updateButton, Theme.Amber);
            if (!updateDialogOpen) ShowUpdates();
        }
        catch (Exception)
        {
            // Offline launches remain usable. The Updates dialog supports retrying.
        }
    }
    void ShowUpdates()
    {
        if (updateDialogOpen) return;
        updateDialogOpen = true;
        try
        {
            using var dialog = new UpdateForm(updatePreferences, availableUpdate, () => { if (dirty) Save(); });
            if (dialog.ShowDialog(this) == DialogResult.OK) Close();
        }
        finally { updateDialogOpen = false; }
    }
}

internal sealed class UpdateForm : Form
{
    readonly Label headline = new() { Dock = DockStyle.Fill, ForeColor = Theme.Amber, Font = new Font("Segoe UI Semibold", 16) };
    readonly Label message = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted };
    readonly TextBox notes = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
    readonly Button check = new ModernButton { Text = "Check now", AutoSize = true, Height = 36 };
    readonly Button install = new ModernButton { Text = "Update and restart", AutoSize = true, Height = 36, Enabled = false };
    readonly Button later = new ModernButton { Text = "Close", AutoSize = true, Height = 36 };
    readonly CancellationTokenSource closed = new();
    readonly UpdatePreferences preferences;
    readonly Action saveChanges;
    UpdateRelease? release;
    bool busy;
    public UpdateForm(UpdatePreferences preferences, UpdateRelease? release, Action saveChanges)
    {
        this.preferences = preferences;
        this.release = release;
        this.saveChanges = saveChanges;
        Text = "Program updates";
        ClientSize = new Size(620, 490);
        MinimumSize = new Size(560, 460);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 44, 54, 32, 38, -1, 32, 36, 0 })
            layout.RowStyles.Add(new(height < 0 ? SizeType.Percent : height == 0 ? SizeType.AutoSize : SizeType.Absolute, height < 0 ? 100 : height));
        Controls.Add(layout);
        layout.Controls.Add(headline, 0, 0);
        layout.Controls.Add(message, 0, 1);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = $"Installed version: {GithubUpdater.CurrentVersion}", ForeColor = Theme.Muted }, 0, 2);
        var source = new LinkLabel { Dock = DockStyle.Fill, Text = "GitHub releases · Gabriel-Barney / Empire-Mod-Manager", LinkColor = Theme.SelectedModEdge, ActiveLinkColor = Theme.Amber };
        source.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(GithubUpdater.RepositoryUrl + "/releases") { UseShellExecute = true });
        layout.Controls.Add(source, 0, 3);
        layout.Controls.Add(notes, 0, 4);
        layout.Controls.Add(progress, 0, 5);
        var automatic = new CheckBox { Text = "Check for updates when the program starts", Dock = DockStyle.Fill, Checked = preferences.CheckOnLaunch, AutoSize = true };
        var restoringPreference = false;
        automatic.CheckedChanged += (_, _) =>
        {
            if (restoringPreference) return;
            var previous = preferences.CheckOnLaunch;
            preferences.CheckOnLaunch = automatic.Checked;
            try { preferences.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                preferences.CheckOnLaunch = previous;
                restoringPreference = true;
                automatic.Checked = previous;
                restoringPreference = false;
                message.Text = "Couldn't save the update preference: " + ex.Message;
            }
        };
        layout.Controls.Add(automatic, 0, 6);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        buttons.Controls.AddRange([check, install, later]);
        layout.Controls.Add(buttons, 0, 7);
        Theme.StyleDialog(this);
        Theme.StyleButton(install, Theme.Amber);
        later.Click += (_, _) => Close();
        check.Click += async (_, _) => await Check();
        install.Click += async (_, _) => await Install();
        FormClosing += (_, _) => closed.Cancel();
        FormClosed += (_, _) => closed.Dispose();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowRelease();
        Shown += async (_, _) => { if (release is null) await Check(); };
    }
    void SetBusy(bool value)
    {
        busy = value;
        progress.Visible = value;
        check.Enabled = !value;
        install.Enabled = !value && release is not null;
        later.Text = value ? "Cancel" : "Close";
    }
    void ShowRelease()
    {
        headline.Text = release is null ? "Program updates" : $"Version {release.Version} is available";
        message.Text = release is null ? "Check GitHub for the latest published release." : "The update will save your preset changes and restart the program. Your presets and settings will be kept.";
        notes.Text = release is null ? "" : string.IsNullOrWhiteSpace(release.Notes) ? "No release notes were provided." : release.Notes;
        install.Enabled = release is not null;
    }
    async Task Check()
    {
        if (busy) return;
        SetBusy(true);
        message.Text = "Checking GitHub for a newer release…";
        try
        {
            release = await GithubUpdater.CheckAsync(closed.Token);
            if (IsDisposed) return;
            ShowRelease();
            if (release is null) { headline.Text = "You're up to date"; message.Text = "No newer stable release is available."; }
        }
        catch (OperationCanceledException) { if (!IsDisposed) message.Text = "The update check timed out. Please try again."; }
        catch (InvalidDataException ex)
        {
            if (!IsDisposed) { message.Text = "The published release is missing valid update files. Open GitHub releases for details."; notes.Text = ex.Message; }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) { message.Text = "Couldn't check for updates. Please try again later."; notes.Text = ex.Message; }
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    async Task Install()
    {
        if (busy || release is null) return;
        SetBusy(true);
        PreparedUpdate? prepared = null;
        try
        {
            var reporting = new Progress<string>(text => { if (!IsDisposed) message.Text = text; });
            prepared = await GithubUpdater.DownloadAsync(release, reporting, closed.Token);
            if (IsDisposed) { GithubUpdater.DeleteWork(prepared.Work); return; }
            saveChanges();
            prepared.StartInstaller();
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) { if (!IsDisposed) message.Text = "The update download timed out. Your current installation has not been changed."; }
        catch (Exception ex)
        {
            if (!IsDisposed) { message.Text = "Couldn't prepare the update. Your current installation has not been changed."; notes.Text = ex.Message; }
        }
        finally
        {
            if (DialogResult != DialogResult.OK && prepared is not null) GithubUpdater.DeleteWork(prepared.Work);
            if (!IsDisposed) SetBusy(false);
        }
    }
}
