using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace ContextControl.Workbench.Controls;

public sealed partial class CodeEditor
{
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F
            && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
            && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            ShowFindPanel();
            e.Handled = true;
        }
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_findPanel.IsVisible || IsPointerInside(e.Source as Visual, _findPanel))
        {
            return;
        }

        _findPanel.IsVisible = false;
    }

    private static bool IsPointerInside(Visual? source, Visual target)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, target))
            {
                return true;
            }
        }

        return false;
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _findPanel.IsVisible = false;
            _surface.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            UpdateFind();
            e.Handled = true;
        }
    }

    private void ShowFindPanel()
    {
        _findPanel.IsVisible = true;
        _findBox.Focus();
        _findBox.SelectAll();
        UpdateFind();
    }

    private void UpdateFind()
    {
        var (current, total) = _surface.FindAndReveal(_findBox.Text ?? "");
        _findCount.Text = total <= 0 ? "0/0" : $"{current}/{total}";
    }
}
