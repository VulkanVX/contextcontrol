using Avalonia.Controls;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class LocalLlmPage : UserControl
{
    public LocalLlmPage()
    {
        InitializeComponent();
    }

    internal TextBox SearchBox => LlmSearchBox;
}
