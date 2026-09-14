// CC-DESC: Main window event bridge plus compact top-bar and external-update integration.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow : Window
{
    private static readonly IBrush PositiveFallbackBrush = Brush.Parse("#2FA36B");
    private static readonly IBrush NegativeFallbackBrush = Brush.Parse("#D95D5D");

    private readonly DispatcherTimer _closeHistoryTimer;
    private readonly DispatcherTimer _promptTypingTimer;
    private WorkbenchViewModel? _subscribedViewModel;
    private ContextControlViewModel? _subscribedContextControl;
    private ThemeSettingsWindow? _themeSettingsWindow;

    private int _fileDragHoverCount;

    private IBrush PositiveBrush => ThemeBrush("FixedGoodBrush", PositiveFallbackBrush);
    private IBrush NegativeBrush => ThemeBrush("FixedBadBrush", NegativeFallbackBrush);

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ProjectTreeList.AddHandler(Control.RequestBringIntoViewEvent, OnProjectTreeRequestBringIntoView, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ProjectTreeList.AddHandler(InputElement.PointerPressedEvent, OnProjectTreePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ProjectTreeList.AddHandler(InputElement.PointerWheelChangedEvent, OnProjectTreePointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: true);
        ConfigurePromptFileDropTargets();

        _closeHistoryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520)
        };
        _closeHistoryTimer.Tick += (_, _) =>
        {
            // History must only close from an explicit double-click toggle or close command.
            // Keep this timer harmless in case an old XAML event still starts it.
            _closeHistoryTimer.Stop();
        };

        _promptTypingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1400)
        };
        _promptTypingTimer.Tick += (_, _) =>
        {
            _promptTypingTimer.Stop();
            ViewModel?.ContextControl.SetPromptTypingActive(false);
        };

        WorkbenchThemeResources.Apply(this, "empty");
    }

    private WorkbenchViewModel? ViewModel => DataContext as WorkbenchViewModel;

    private Button EditMenuButton => MainTitleBar.EditMenu;
    private ScrollViewer ProjectTreeList => ProjectFilesPane.TreeList;
    private Border ProjectTreeSearchPanel => ProjectFilesPane.TreeSearchPanel;
    private TextBox ProjectTreeSearchBox => ProjectFilesPane.TreeSearchBox;
    private ProjectTreeRenderControl ProjectTreeView => ProjectFilesPane.TreeView;
    private TextBox ContextPromptTextBox => ContextPromptBar.PromptTextBox;
    private WebView2Host BrowserWebView => BrowserSurface.BrowserWebViewControl;
    private TextBox LlmSearchBox => LocalLlmPage.SearchBox;
    private TextBox StackLlmSearchBox => MyStackPage.SearchBox;
    private TextBox DependencySearchBox => DependenciesPage.SearchBox;
    private ProjectGraphRenderControl ProjectGraphView => ProjectGraphPage.GraphView;
    private Border ProjectGraphSearchPanel => ProjectGraphPage.SearchPanel;
    private TextBox ProjectGraphSearchBox => ProjectGraphPage.SearchBox;
    private Grid AttachmentRegion => AttachmentBridgePane.AttachmentRegionControl;
    private Border AttachmentListHost => AttachmentBridgePane.AttachmentListHostControl;
    private ListBox AttachmentList => AttachmentBridgePane.AttachmentListControl;
    private Border PromptFileDropOverlay => MainWindowOverlays.FileDropOverlay;

    private IBrush ThemeBrush(string key, IBrush fallback)
    {
        return Resources.TryGetValue(key, out var value) && value is IBrush brush
            ? brush
            : fallback;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AttachViewModelHandler();
        ConfigureContextControlBridge();
        ApplySelectedTheme();
        RefreshWindowTitle();
        RefreshProjectInfoHeader();
        ScheduleUiPolish();
        FitToWorkingArea();
        UpdateChatMonitor();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachViewModelHandler();
        ConfigureContextControlBridge();
        ApplySelectedTheme();
        RefreshWindowTitle();
        RefreshProjectInfoHeader();
        ScheduleUiPolish();
    }

    private async void OnOpenProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await PickAndLoadProjectAsync("Open Project");
    }

    private async void OnNewProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await PickAndLoadProjectAsync("Add Project");
    }

    internal async void OnProjectScannerCopyClick(object? sender, RoutedEventArgs e)
    {
        var text = ViewModel?.ProjectScanResultText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async Task PickAndLoadProjectAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        var folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(folder) || ViewModel is null)
        {
            return;
        }

        await ViewModel.LoadProjectAsync(folder);
        ResetProjectInfoHeaderBinding();
        RefreshWindowTitle();
        RefreshProjectInfoHeader();
        ScheduleUiPolish();
    }

    private void AttachViewModelHandler()
    {
        if (ReferenceEquals(_subscribedViewModel, ViewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.ExternalChanges.CollectionChanged -= OnExternalChangesCollectionChanged;
        }

        if (_subscribedContextControl is not null)
        {
            _subscribedContextControl.PropertyChanged -= OnContextControlPropertyChanged;
            _subscribedContextControl = null;
        }

        _subscribedViewModel = ViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel.ExternalChanges.CollectionChanged += OnExternalChangesCollectionChanged;
            _subscribedContextControl = _subscribedViewModel.ContextControl;
            _subscribedContextControl.PropertyChanged += OnContextControlPropertyChanged;
        }
    }

    private static bool ContainsPointer(Control control, PointerEventArgs e)
    {
        var point = e.GetPosition(control);
        return point.X >= 0
            && point.Y >= 0
            && point.X <= control.Bounds.Width
            && point.Y <= control.Bounds.Height;
    }
}
