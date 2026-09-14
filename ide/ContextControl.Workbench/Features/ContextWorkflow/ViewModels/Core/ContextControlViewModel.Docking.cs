// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private void SelectDockPanel(string panelKey)
    {
        var clean = string.Equals(panelKey, "chat", StringComparison.OrdinalIgnoreCase)
            ? "chat"
            : "log";
        if (string.Equals(_dockPanelKey, clean, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _dockPanelKey = clean;
        OnPropertyChanged(nameof(IsLogPanelOpen));
        OnPropertyChanged(nameof(IsChatPanelOpen));
    }

}
