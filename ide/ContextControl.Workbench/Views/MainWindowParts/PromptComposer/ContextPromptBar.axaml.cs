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

    private readonly DispatcherTimer _promptModeFlyoutCloseTimer;
    private readonly DispatcherTimer _promptSendWarningPulseTimer;
    private bool _isPromptModeMenuPointerOver;
    private bool _isPromptModeFlyoutPointerOver;
    private bool _isPromptSendWarningPulseActive;
    private bool _isPromptSendWarningPulseDimmed;
    private DateTime _promptModeBridgeGraceUntilUtc = DateTime.MinValue;

    public ContextPromptBar()
    {
        InitializeComponent();
        _promptModeFlyoutCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };
        _promptModeFlyoutCloseTimer.Tick += (_, _) =>
        {
            _promptModeFlyoutCloseTimer.Stop();
            if (IsPromptModeDropdownPointerOver())
            {
                return;
            }

            if (DateTime.UtcNow < _promptModeBridgeGraceUntilUtc)
            {
                _promptModeFlyoutCloseTimer.Start();
                return;
            }

            if (PromptModePopup.IsOpen)
            {
                PromptModePopup.IsOpen = false;
            }
        };

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

    private void OnPromptModeMenuClick(object? sender, RoutedEventArgs e)
    {
        _isPromptModeMenuPointerOver = true;
        ExtendPromptModeBridgeGrace();
        _promptModeFlyoutCloseTimer.Stop();
        PromptModePopup.IsOpen = !PromptModePopup.IsOpen;
        e.Handled = true;
    }

    private void OnPromptModeMenuPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPromptModeMenuPointerOver = true;
        ExtendPromptModeBridgeGrace();
        _promptModeFlyoutCloseTimer.Stop();
    }

    private void OnPromptModeMenuPointerExited(object? sender, PointerEventArgs e)
    {
        _isPromptModeMenuPointerOver = false;
        ExtendPromptModeBridgeGrace();
        QueuePromptModeFlyoutClose();
    }

    private void OnPromptModeFlyoutPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPromptModeFlyoutPointerOver = true;
        ExtendPromptModeBridgeGrace();
        _promptModeFlyoutCloseTimer.Stop();
    }

    private void OnPromptModeFlyoutPointerExited(object? sender, PointerEventArgs e)
    {
        _isPromptModeFlyoutPointerOver = false;
        ExtendPromptModeBridgeGrace();
        QueuePromptModeFlyoutClose();
    }

    private void OnPromptModeOptionClick(object? sender, RoutedEventArgs e)
    {
        _isPromptModeMenuPointerOver = false;
        _isPromptModeFlyoutPointerOver = false;
        _promptModeBridgeGraceUntilUtc = DateTime.MinValue;
        _promptModeFlyoutCloseTimer.Stop();
        PromptModePopup.IsOpen = false;
    }

    private void QueuePromptModeFlyoutClose()
    {
        _promptModeFlyoutCloseTimer.Stop();
        _promptModeFlyoutCloseTimer.Start();
    }

    private bool IsPromptModeDropdownPointerOver()
    {
        return _isPromptModeMenuPointerOver
            || _isPromptModeFlyoutPointerOver
            || PromptModeMenuButton.IsPointerOver
            || PromptModeFlyoutHost.IsPointerOver;
    }

    private void ExtendPromptModeBridgeGrace()
    {
        _promptModeBridgeGraceUntilUtc = DateTime.UtcNow.AddMilliseconds(130);
    }
}
