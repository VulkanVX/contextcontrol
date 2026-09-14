using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;
public sealed partial class ProjectGraphRenderControl
{
    private void FitViewportIfRequested()
    {
        if (!_fitRequested || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        _fitRequested = false;
        _viewportInitialized = true;

        var availableWidth = Math.Max(1.0, Bounds.Width - 44.0);
        var availableHeight = Math.Max(1.0, Bounds.Height - 44.0);
        var scale = Math.Min(availableWidth / _contentBounds.Width, availableHeight / _contentBounds.Height);
        _zoom = Math.Clamp(scale, MinZoom, 1.15);
        _pan = new Vector(
            22.0 - _contentBounds.Left * _zoom,
            22.0 - _contentBounds.Top * _zoom);
    }

}
