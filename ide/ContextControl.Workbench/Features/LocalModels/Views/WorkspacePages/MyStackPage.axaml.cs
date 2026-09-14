using Avalonia.Controls;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class MyStackPage : UserControl
{
    public MyStackPage()
    {
        InitializeComponent();
    }

    internal TextBox SearchBox => StackLlmSearchBox;
}
