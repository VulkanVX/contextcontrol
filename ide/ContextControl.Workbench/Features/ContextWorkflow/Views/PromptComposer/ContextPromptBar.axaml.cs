using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

    private void OnPromptTextBoxKeyDown(object? sender, KeyEventArgs e) => OwnerWindow?.OnPromptTextBoxKeyDown(sender, e);

    private void OnPromptTextBoxTextChanged(object? sender, TextChangedEventArgs e) => OwnerWindow?.OnPromptTextBoxTextChanged(sender, e);

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRowTapped(sender, e);

    private void OnAttachmentRemoveTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRemoveTapped(sender, e);
}
