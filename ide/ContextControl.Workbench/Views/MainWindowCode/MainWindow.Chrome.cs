using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow
{
    private const double WorkingAreaInset = 8d;
    private const double MaximizedWorkingAreaInset = 0d;

    private PixelPoint? _restorePositionBeforeCustomMaximize;
    private double _restoreWidthBeforeCustomMaximize;
    private double _restoreHeightBeforeCustomMaximize;
    private bool _isCustomMaximized;

    internal void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (_isCustomMaximized)
            {
                return;
            }

            BeginMoveDrag(e);
        }
    }

    internal void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    internal void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    internal void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        if (_isCustomMaximized || WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            RestoreFromCustomMaximize();
            FitToWorkingArea();
            return;
        }

        ApplyCustomMaximize(captureRestoreBounds: true);
    }

    private void OnSettingsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        OpenSettingsWindow();
        e.Handled = true;
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
        e.Handled = true;
    }

    internal async void OnEditOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        CloseEditMenuFlyout();
        await PickAndLoadProjectAsync("Open Project");
        e.Handled = true;
    }

    internal async void OnEditNewProjectClick(object? sender, RoutedEventArgs e)
    {
        CloseEditMenuFlyout();
        await PickAndLoadProjectAsync("Add Project");
        e.Handled = true;
    }

    internal void OnEditSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseEditMenuFlyout();
        OpenSettingsWindow();
        e.Handled = true;
    }

    private void CloseEditMenuFlyout()
    {
        EditMenuButton.Flyout?.Hide();
    }

    private void OpenSettingsWindow()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (_themeSettingsWindow is null)
        {
            _themeSettingsWindow = new ThemeSettingsWindow();
            _themeSettingsWindow.Closed += (_, _) => _themeSettingsWindow = null;
        }

        _themeSettingsWindow.DataContext = viewModel;
        _themeSettingsWindow.ApplyTheme(
            viewModel.ThemeKey,
            viewModel.UiFontFamily,
            viewModel.CodeFontFamily,
            viewModel.SkinKey,
            viewModel.UiFontColorModeKey,
            viewModel.CustomUiFontColorHex,
            viewModel.UiFontSize,
            viewModel.ChatAppearanceKey);

        if (!_themeSettingsWindow.IsVisible)
        {
            _themeSettingsWindow.Show(this);
        }

        if (_themeSettingsWindow.WindowState == WindowState.Minimized)
        {
            _themeSettingsWindow.WindowState = WindowState.Normal;
        }

        _themeSettingsWindow.Activate();
    }

    internal void OnResizeLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.West, e);
    }

    internal void OnResizeRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.East, e);
    }

    internal void OnResizeTopPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.North, e);
    }

    internal void OnResizeBottomPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.South, e);
    }

    internal void OnResizeTopLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthWest, e);
    }

    internal void OnResizeTopRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthEast, e);
    }

    internal void OnResizeBottomLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthWest, e);
    }

    internal void OnResizeBottomRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthEast, e);
    }

    private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized
            || _isCustomMaximized
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void FitToWorkingArea()
    {
        if (_isCustomMaximized)
        {
            ApplyCustomMaximize(captureRestoreBounds: false);
            return;
        }

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var usableWidth = Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - WorkingAreaInset * 2);
        var usableHeight = Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - WorkingAreaInset * 2);
        MaxWidth = usableWidth;
        MaxHeight = usableHeight;

        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        Width = Math.Min(Math.Max(Width, MinWidth), usableWidth);
        Height = Math.Min(Math.Max(Height, MinHeight), usableHeight);

        var widthPixels = (int)Math.Ceiling(Width * screen.Scaling);
        var heightPixels = (int)Math.Ceiling(Height * screen.Scaling);
        var insetPixels = (int)Math.Round(WorkingAreaInset * screen.Scaling);
        var minX = screen.WorkingArea.X + insetPixels;
        var minY = screen.WorkingArea.Y + insetPixels;
        var maxX = Math.Max(minX, screen.WorkingArea.X + screen.WorkingArea.Width - widthPixels - insetPixels);
        var maxY = Math.Max(minY, screen.WorkingArea.Y + screen.WorkingArea.Height - heightPixels - insetPixels);
        Position = new PixelPoint(
            Math.Clamp(Position.X, minX, maxX),
            Math.Clamp(Position.Y, minY, maxY));
    }

    private void FitMaximizedWindowToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        MaxWidth = Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - MaximizedWorkingAreaInset * 2);
        MaxHeight = Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - MaximizedWorkingAreaInset * 2);
    }

    private void ApplyCustomMaximize(bool captureRestoreBounds)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        if (captureRestoreBounds)
        {
            _restorePositionBeforeCustomMaximize = Position;
            _restoreWidthBeforeCustomMaximize = Width;
            _restoreHeightBeforeCustomMaximize = Height;
        }

        WindowState = WindowState.Normal;
        FitMaximizedWindowToWorkingArea();

        var insetPixels = (int)Math.Round(MaximizedWorkingAreaInset * screen.Scaling);
        Position = new PixelPoint(
            screen.WorkingArea.X + insetPixels,
            screen.WorkingArea.Y + insetPixels);
        Width = MaxWidth;
        Height = MaxHeight;
        _isCustomMaximized = true;
        Classes.Set("custom-maximized", true);
    }

    private void RestoreFromCustomMaximize()
    {
        _isCustomMaximized = false;
        Classes.Set("custom-maximized", false);
        if (_restorePositionBeforeCustomMaximize is { } position)
        {
            Position = position;
        }

        if (_restoreWidthBeforeCustomMaximize > 0)
        {
            Width = _restoreWidthBeforeCustomMaximize;
        }

        if (_restoreHeightBeforeCustomMaximize > 0)
        {
            Height = _restoreHeightBeforeCustomMaximize;
        }
    }
}
