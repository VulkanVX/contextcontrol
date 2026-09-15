using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ContextPromptBar : UserControl
{
    private const double PromptSendWarningPulseHighOpacity = 1.0;
    private const double PromptSendWarningPulseLowOpacity = 0.90;

    private readonly DispatcherTimer _promptSendWarningPulseTimer;
    private bool _isPromptSendWarningPulseActive;
    private bool _isPromptSendWarningPulseDimmed;

    public ContextPromptBar()
    {
        InitializeComponent();
        _promptSendWarningPulseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(950)
        };
        _promptSendWarningPulseTimer.Tick += OnPromptSendWarningPulseTimerTick;

        Loaded += OnContextPromptBarLoaded;
        Unloaded += OnContextPromptBarUnloaded;
    }

    internal TextBox PromptTextBox => ContextPromptTextBox;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnContextPromptBarLoaded(object? sender, RoutedEventArgs e) => _promptSendWarningPulseTimer.Start();

    private void OnContextPromptBarUnloaded(object? sender, RoutedEventArgs e)
    {
        _promptSendWarningPulseTimer.Stop();
        ResetPromptSendWarningPulse();
    }

    private void OnPromptSendWarningPulseTimerTick(object? sender, EventArgs e)
    {
        var isWarning = PromptSendButton.Classes.Contains("warning") && PromptSendButton.IsVisible;
        if (!isWarning)
        {
            ResetPromptSendWarningPulse();
            return;
        }

        if (!_isPromptSendWarningPulseActive)
        {
            _isPromptSendWarningPulseActive = true;
            _isPromptSendWarningPulseDimmed = false;
            SetPromptSendWarningOpacity(PromptSendWarningPulseHighOpacity);
            return;
        }

        _isPromptSendWarningPulseDimmed = !_isPromptSendWarningPulseDimmed;
        SetPromptSendWarningOpacity(_isPromptSendWarningPulseDimmed
            ? PromptSendWarningPulseLowOpacity
            : PromptSendWarningPulseHighOpacity);
    }

    private void ResetPromptSendWarningPulse()
    {
        if (!_isPromptSendWarningPulseActive && PromptSendButton.Opacity == PromptSendWarningPulseHighOpacity)
        {
            return;
        }

        _isPromptSendWarningPulseActive = false;
        _isPromptSendWarningPulseDimmed = false;
        SetPromptSendWarningOpacity(PromptSendWarningPulseHighOpacity);
    }

    private void SetPromptSendWarningOpacity(double opacity)
    {
        if (Math.Abs(PromptSendButton.Opacity - opacity) <= 0.001)
        {
            return;
        }

        PromptSendButton.Opacity = opacity;
    }

    private void OnPromptTextBoxGotFocus(object? sender, GotFocusEventArgs e) => OwnerWindow?.OnPromptTextBoxGotFocus(sender, e);

    private void OnPromptTextBoxLostFocus(object? sender, RoutedEventArgs e) => OwnerWindow?.OnPromptTextBoxLostFocus(sender, e);

    private void OnPromptTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (ActionPopup.IsOpen)
        {
            if (e.Key == Key.Escape) { ActionPopup.IsOpen = false; e.Handled = true; return; }
            if (e.Key is Key.Up or Key.Down)
            {
                SlashActions.SelectedIndex = Math.Clamp(SlashActions.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, SlashActions.ItemCount - 1);
                e.Handled = true; return;
            }
            if (e.Key is Key.Tab or Key.Enter) { InsertSlashAction(); e.Handled = true; return; }
        }
        OwnerWindow?.OnPromptTextBoxKeyDown(sender, e);
    }

    private void OnPromptTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        OwnerWindow?.OnPromptTextBoxTextChanged(sender, e);
        if (ActionPopup is null || SlashActions is null) return;
        var text = ContextPromptTextBox.Text ?? "";
        var matches = text.StartsWith('/') && !text.Any(char.IsWhiteSpace) ? ChatActionCatalog.Match(text) : [];
        SlashActions.ItemsSource = matches;
        SlashActions.SelectedIndex = matches.Count > 0 ? 0 : -1;
        ActionPopup.IsOpen = ContextPromptTextBox.IsFocused && matches.Count > 0;
    }
    private void OnSlashActionTapped(object? sender, TappedEventArgs e) => InsertSlashAction();
    private void InsertSlashAction()
    {
        if (SlashActions.SelectedItem is not ChatAction action) return;
        ActionPopup.IsOpen = false;
        ContextPromptTextBox.Text = action.Command + " ";
        ContextPromptTextBox.CaretIndex = ContextPromptTextBox.Text.Length;
        ContextPromptTextBox.Focus();
    }

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRowTapped(sender, e);

    private void OnAttachmentRemoveTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRemoveTapped(sender, e);
}
