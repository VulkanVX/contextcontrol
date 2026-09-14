using Avalonia.Controls;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class DependenciesPage : UserControl
{
    public DependenciesPage()
    {
        InitializeComponent();
    }

    internal TextBox SearchBox => DependencySearchBox;
}
