internal sealed class SetupForm : Form, ISetupWindow
{
    private readonly TextBox _installDirBox = new();
    private readonly CheckBox _startMenuShortcutBox = new();
    private readonly CheckBox _desktopShortcutBox = new();
    private readonly CheckBox _installWebView2RuntimeBox = new();
    private readonly CheckBox _launchBox = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _installButton = new();
    private readonly Button _cancelButton = new();
    private readonly SetupOptions _initialOptions;

    public SetupForm(SetupOptions initialOptions)
    {
        _initialOptions = initialOptions;
        Text = "ContextControl Setup";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 540);
        MinimumSize = new Size(720, 500);
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
            RowCount = 3,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 6,
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Text = "Install ContextControl",
            Margin = new Padding(0, 0, 0, 8),
        };
        content.Controls.Add(title, 0, 0);

        var intro = new Label
        {
            AutoSize = true,
            Text = "Choose where the full app folder will be installed.",
            Margin = new Padding(0, 0, 0, 14),
        };
        content.Controls.Add(intro, 0, 1);

        var installLabel = new Label
        {
            AutoSize = true,
            Text = "Install folder",
            Margin = new Padding(0, 0, 0, 6),
        };
        content.Controls.Add(installLabel, 0, 2);

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14),
            AutoSize = true,
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _installDirBox.Dock = DockStyle.Top;
        _installDirBox.Text = initialOptions.InstallDir;
        _installDirBox.Margin = new Padding(0, 0, 8, 0);
        pathRow.Controls.Add(_installDirBox, 0, 0);

        var browseButton = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            Margin = new Padding(0),
        };
        browseButton.Click += OnBrowse;
        pathRow.Controls.Add(browseButton, 1, 0);
        content.Controls.Add(pathRow, 0, 3);

        var optionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        _startMenuShortcutBox.AutoSize = true;
        _startMenuShortcutBox.Text = "Create Start Menu shortcut";
        _startMenuShortcutBox.Checked = initialOptions.StartMenuShortcut;
        optionPanel.Controls.Add(_startMenuShortcutBox);

        _desktopShortcutBox.AutoSize = true;
        _desktopShortcutBox.Text = "Create desktop shortcut";
        _desktopShortcutBox.Checked = initialOptions.DesktopShortcut;
        optionPanel.Controls.Add(_desktopShortcutBox);

        _installWebView2RuntimeBox.AutoSize = true;
        _installWebView2RuntimeBox.Text = "Install or repair Microsoft Edge WebView2 Runtime for the Browser workspace";
        _installWebView2RuntimeBox.Checked = initialOptions.InstallWebView2Runtime;
        optionPanel.Controls.Add(_installWebView2RuntimeBox);

        _launchBox.AutoSize = true;
        _launchBox.Text = "Launch ContextControl when setup finishes";
        _launchBox.Checked = initialOptions.Launch;
        optionPanel.Controls.Add(_launchBox);

        content.Controls.Add(optionPanel, 0, 4);

        var statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 110),
        };
        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Ready to install.";
        _statusLabel.TextAlign = ContentAlignment.BottomLeft;
        statusPanel.Controls.Add(_statusLabel);
        content.Controls.Add(statusPanel, 0, 5);
        shell.Controls.Add(content, 0, 0);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Height = 18;
        _progressBar.Margin = new Padding(0, 0, 0, 14);
        shell.Controls.Add(_progressBar, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };

        _installButton.Text = "Install";
        _installButton.AutoSize = true;
        _installButton.Click += async (_, _) => await InstallAsync().ConfigureAwait(true);
        buttons.Controls.Add(_installButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.Click += (_, _) => Close();
        buttons.Controls.Add(_cancelButton);

        shell.Controls.Add(buttons, 0, 2);
        Controls.Add(shell);

        AcceptButton = _installButton;
        CancelButton = _cancelButton;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the ContextControl install folder",
            UseDescriptionForTitle = true,
            SelectedPath = Environment.ExpandEnvironmentVariables(_installDirBox.Text),
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installDirBox.Text = dialog.SelectedPath;
        }
    }

    private async Task InstallAsync()
    {
        SetInstalling(true);
        var options = CaptureOptions();
        var progress = new UiProgressSink(this);

        try
        {
            var result = await Task.Run(() => InstallerEngine.Install(options, progress)).ConfigureAwait(true);
            ExitCode = 0;
            SetStatus($"Installed to {result.InstallDir}");

            var message = $"ContextControl was installed to:{Environment.NewLine}{result.InstallDir}";
            var icon = MessageBoxIcon.Information;
            if (!string.IsNullOrWhiteSpace(result.LaunchError))
            {
                message += $"{Environment.NewLine}{Environment.NewLine}Setup could not keep the app running after launch:{Environment.NewLine}{result.LaunchError}{Environment.NewLine}{Environment.NewLine}Install log:{Environment.NewLine}{result.LogPath}";
                icon = MessageBoxIcon.Warning;
            }
            else
            {
                message += $"{Environment.NewLine}{Environment.NewLine}Install log:{Environment.NewLine}{result.LogPath}";
            }

            MessageBox.Show(this, message, "ContextControl Setup", MessageBoxButtons.OK, icon);
            Close();
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            InstallerEngine.WriteEmergencyLog(ex);
            SetStatus("Install failed. Details were written to the install log.");
            MessageBox.Show(this, ex.Message, "ContextControl Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetInstalling(false);
        }
    }

    private SetupOptions CaptureOptions()
    {
        var installDir = _installDirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installDir))
        {
            installDir = SetupOptions.DefaultInstallDir;
        }

        return new SetupOptions
        {
            InstallDir = installDir,
            StartMenuShortcut = _startMenuShortcutBox.Checked,
            DesktopShortcut = _desktopShortcutBox.Checked,
            InstallWebView2Runtime = _installWebView2RuntimeBox.Checked,
            Launch = _launchBox.Checked,
            WaitForProcessId = _initialOptions.WaitForProcessId,
        };
    }

    private void SetInstalling(bool installing)
    {
        _installButton.Enabled = !installing;
        _cancelButton.Enabled = !installing;
        _installDirBox.Enabled = !installing;
        _startMenuShortcutBox.Enabled = !installing;
        _desktopShortcutBox.Enabled = !installing;
        _installWebView2RuntimeBox.Enabled = !installing;
        _launchBox.Enabled = !installing;
        _progressBar.Style = installing ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
    }

    internal void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private sealed class UiProgressSink(SetupForm form) : IInstallProgress
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
