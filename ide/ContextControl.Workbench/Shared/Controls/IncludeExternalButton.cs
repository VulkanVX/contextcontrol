using Avalonia.Controls;
using Avalonia.Input;

namespace ContextControl.Workbench.Controls;

public sealed class IncludeExternalButton : Button
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Content = "skip";
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Content = "include";
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Content = "skip";
    }
}
