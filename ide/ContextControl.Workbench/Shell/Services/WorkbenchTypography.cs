using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ContextControl.Workbench.Services;

/// <summary>One layout scale for native text, custom renderers, and their authored spacing.</summary>
public static class WorkbenchTypography
{
    public const double BaseUiSize = 11.0;
    private static readonly ConditionalWeakTable<Window, ScaleState> States = new();

    public static double NormalizeSize(double size) => double.IsFinite(size) ? Math.Clamp(size, 8, 22) : BaseUiSize;

    public static void Apply(Window window, double size)
    {
        var state = States.GetValue(window, CreateState);
        state.Timer.Stop();
        state.PendingSize = NormalizeSize(size);
        ApplyScale(window, state);
    }

    public static void Schedule(Window window, double size)
    {
        var state = States.GetValue(window, CreateState);
        state.PendingSize = NormalizeSize(size);
        state.Timer.Stop();
        state.Timer.Start();
    }

    public static double GetScale(Window window) => States.TryGetValue(window, out var state) ? state.Scale : 1;

    private static ScaleState CreateState(Window window)
    {
        var state = new ScaleState();
        state.Timer.Tick += (_, _) =>
        {
            state.Timer.Stop();
            ApplyScale(window, state);
        };
        window.Closed += (_, _) => state.Timer.Stop();
        return state;
    }

    private static void ApplyScale(Window window, ScaleState state)
    {
        if (state.Host is null && window.Content is Control content)
        {
            window.Content = null;
            state.Host = new LayoutTransformControl { Child = content };
            window.Content = state.Host;
        }

        // Keep authored font/spacing ratios intact. Scaling only Window.FontSize leaves
        // fixed-size XAML and custom-drawn controls unchanged and causes mixed typography.
        if (!window.Resources.TryGetValue("UiFontSize", out var current) || !Equals(current, BaseUiSize))
            window.Resources["UiFontSize"] = BaseUiSize;
        var scale = state.PendingSize / BaseUiSize;
        if (state.Host is not null && Math.Abs(scale - state.Scale) > 0.0001)
        {
            state.Host.LayoutTransform = new ScaleTransform(scale, scale);
            state.Scale = scale;
        }
    }

    private sealed class ScaleState
    {
        public LayoutTransformControl? Host;
        public double PendingSize = BaseUiSize;
        public double Scale = 1;
        public DispatcherTimer Timer { get; } = new() { Interval = TimeSpan.FromMilliseconds(90) };
    }
}
