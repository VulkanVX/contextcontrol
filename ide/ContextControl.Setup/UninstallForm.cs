internal sealed class UninstallForm : Form, ISetupWindow
{
    private readonly string _installDir;
    private readonly CheckBox _removeUserDataBox = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _uninstallButton = new();
    private readonly Button _cancelButton = new();

    public UninstallForm(SetupOptions initialOptions)
    {
        _installDir = initialOptions.InstallDir;
        Text = "ContextControl Uninstall";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 360);
        MinimumSize = new Size(660, 330);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi(initialOptions);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ExitCode { get; private set; } = 1;

    private void BuildUi(SetupOptions initialOptions)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        shell.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Text = "Uninstall ContextControl",
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Remove the installed app folder and shortcuts.",
            Margin = new Padding(0, 0, 0, 14),
        }, 0, 1);

        var pathLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 44,
            Text = $"Install folder:{Environment.NewLine}{_installDir}",
            Margin = new Padding(0, 0, 0, 10),
        };
        shell.Controls.Add(pathLabel, 0, 2);

        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        middle.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _removeUserDataBox.AutoSize = true;
        _removeUserDataBox.Text = "Also remove ContextControl logs and user data";
        _removeUserDataBox.Checked = initialOptions.RemoveUserData;
        _removeUserDataBox.Margin = new Padding(0, 0, 0, 12);
        middle.Controls.Add(_removeUserDataBox, 0, 0);

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Ready to uninstall.";
        _statusLabel.TextAlign = ContentAlignment.BottomLeft;
        middle.Controls.Add(_statusLabel, 0, 1);
        shell.Controls.Add(middle, 0, 3);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Height = 18;
        _progressBar.Margin = new Padding(0, 0, 0, 14);
        bottom.Controls.Add(_progressBar, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };

        _uninstallButton.Text = "Uninstall";
        _uninstallButton.AutoSize = true;
        _uninstallButton.Click += async (_, _) => await UninstallAsync().ConfigureAwait(true);
        buttons.Controls.Add(_uninstallButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.Click += (_, _) => Close();
        buttons.Controls.Add(_cancelButton);

        bottom.Controls.Add(buttons, 0, 1);
        shell.Controls.Add(bottom, 0, 4);
        Controls.Add(shell);

        AcceptButton = _uninstallButton;
        CancelButton = _cancelButton;
    }

    private async Task UninstallAsync()
    {
        SetUninstalling(true);
        var progress = new UiProgressSink(this);
        var options = new SetupOptions
        {
            Mode = SetupMode.Uninstall,
            InstallDir = _installDir,
            RemoveUserData = _removeUserDataBox.Checked,
            UninstallRelaunched = true,
        };

        try
        {
            var result = await Task.Run(() => InstallerEngine.Uninstall(options, progress)).ConfigureAwait(true);
            ExitCode = 0;
            SetStatus("ContextControl was uninstalled.");
            MessageBox.Show(this, $"ContextControl was uninstalled.{Environment.NewLine}{Environment.NewLine}Log:{Environment.NewLine}{result.LogPath}", "ContextControl Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            InstallerEngine.WriteEmergencyLog(ex);
            SetStatus("Uninstall failed. Details were written to the uninstall log.");
            MessageBox.Show(this, ex.Message, "ContextControl Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetUninstalling(false);
        }
    }

    private void SetUninstalling(bool uninstalling)
    {
        _uninstallButton.Enabled = !uninstalling;
        _cancelButton.Enabled = !uninstalling;
        _removeUserDataBox.Enabled = !uninstalling;
        _progressBar.Style = uninstalling ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
    }

    internal void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private sealed class UiProgressSink(UninstallForm form) : IInstallProgress
    {
        public void Report(string message)
        {
            if (form.IsDisposed)
            {
                return;
            }

            if (form.InvokeRequired)
            {
                form.BeginInvoke((Action)(() => form.SetStatus(message)));
            }
            else
            {
                form.SetStatus(message);
            }
        }
    }
}
