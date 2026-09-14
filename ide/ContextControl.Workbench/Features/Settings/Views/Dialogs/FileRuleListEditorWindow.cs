using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class FileRuleListEditorWindow : Window
{
    private static readonly Uri AppIconUri = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol64x64.png");
    private static readonly Uri MicroIconUri = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol32x32.png");

    private readonly StackPanel _itemsPanel;
    private readonly TextBox _newEntryBox;
    private readonly List<TextBox> _entryEditors = [];
    private readonly string _focusValue;
    private Image? _titleIconImage;

    public FileRuleListEditorWindow(
        string title,
        IEnumerable<string> values,
        string watermark,
        string? focusValue = null)
    {
        _focusValue = focusValue ?? "";
        Title = title;
        Width = 520;
        Height = 720;
        MinWidth = 380;
        MinHeight = 420;
        CanResize = true;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(AssetLoader.Open(AppIconUri));

        _itemsPanel = new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(0)
        };

        _newEntryBox = new TextBox
        {
            Watermark = watermark,
            FontSize = 9.5,
            MinHeight = 20,
            Padding = new Thickness(5, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _newEntryBox.Classes.Add("rule-add");
        _newEntryBox.KeyDown += OnNewEntryKeyDown;

        Content = BuildContent(title);
        foreach (var value in Normalize(values))
        {
            AddEntryEditor(value);
        }

        ApplyTheme("empty");
    }

    public IReadOnlyList<string> Values { get; private set; } = [];

    public void ApplyTheme(
        string? themeKey,
        string? uiFontFamily = null,
        string? codeFontFamily = null,
        string? skinKey = null,
        string? uiFontColorModeKey = null,
        string? customUiFontColor = null)
    {
        WorkbenchThemeResources.Apply(this, themeKey, uiFontFamily, codeFontFamily, skinKey: skinKey, uiFontColorModeKey: uiFontColorModeKey, customUiFontColor: customUiFontColor);
        if (_titleIconImage is not null
            && Resources.TryGetValue("AppMicroIconImage", out var icon)
            && icon is IImage image)
        {
            _titleIconImage.Source = image;
        }

        if (Resources.TryGetValue("AppBackgroundBrush", out var brush) && brush is IBrush background)
        {
            Background = background;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToWorkingArea();

        if (!string.IsNullOrWhiteSpace(_focusValue))
        {
            var editor = _entryEditors.FirstOrDefault(item =>
                string.Equals(item.Text?.Trim(), _focusValue.Trim(), StringComparison.OrdinalIgnoreCase));
            if (editor is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    editor.Focus();
                    editor.SelectAll();
                });
                return;
            }
        }

        Dispatcher.UIThread.Post(() => _newEntryBox.Focus());
    }

    private Control BuildContent(string title)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.Bold
        };
        titleBlock.Classes.Add("settings-title");

        var addButton = SmallButton("+");
        addButton.Width = 30;
        addButton.Click += (_, _) => AddPendingEntry();

        var saveButton = CommandButton("Save");
        saveButton.Click += (_, _) =>
        {
            AddPendingEntry();
            Values = Normalize(_entryEditors.Select(editor => editor.Text ?? "")).ToArray();
            Close(true);
        };

        var cancelButton = CommandButton("Cancel");
        cancelButton.Click += (_, _) => Close(false);

        var addRow = BuildAddRow(addButton);
        var listHost = BuildListHost();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                saveButton
            }
        };
        Grid.SetRow(addRow, 1);
        Grid.SetRow(listHost, 2);
        Grid.SetRow(actions, 3);

        var contentPanel = new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(7),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 5,
                Children =
                {
                    titleBlock,
                    addRow,
                    listHost,
                    actions
                }
            }
        };
        contentPanel.Classes.Add("settings-panel");
        Grid.SetRow(contentPanel, 1);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("31,*"),
        };
        root.Children.Add(BuildTitleBar(title));
        root.Children.Add(contentPanel);
        AddResizeEdges(root);
        return root;
    }

    private void AddResizeEdges(Grid root)
    {
        root.Children.Add(BuildResizeEdge(5, null, HorizontalAlignment.Left, VerticalAlignment.Stretch, StandardCursorType.SizeWestEast, OnResizeLeftPointerPressed));
        root.Children.Add(BuildResizeEdge(5, null, HorizontalAlignment.Right, VerticalAlignment.Stretch, StandardCursorType.SizeWestEast, OnResizeRightPointerPressed));
        root.Children.Add(BuildResizeEdge(null, 5, HorizontalAlignment.Stretch, VerticalAlignment.Top, StandardCursorType.SizeNorthSouth, OnResizeTopPointerPressed));
        root.Children.Add(BuildResizeEdge(null, 5, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, StandardCursorType.SizeNorthSouth, OnResizeBottomPointerPressed));
        root.Children.Add(BuildResizeEdge(10, 10, HorizontalAlignment.Left, VerticalAlignment.Top, StandardCursorType.TopLeftCorner, OnResizeTopLeftPointerPressed));
        root.Children.Add(BuildResizeEdge(10, 10, HorizontalAlignment.Right, VerticalAlignment.Top, StandardCursorType.TopRightCorner, OnResizeTopRightPointerPressed));
        root.Children.Add(BuildResizeEdge(10, 10, HorizontalAlignment.Left, VerticalAlignment.Bottom, StandardCursorType.BottomLeftCorner, OnResizeBottomLeftPointerPressed));
        root.Children.Add(BuildResizeEdge(10, 10, HorizontalAlignment.Right, VerticalAlignment.Bottom, StandardCursorType.BottomRightCorner, OnResizeBottomRightPointerPressed));
    }

    private static Border BuildResizeEdge(
        double? width,
        double? height,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment,
        StandardCursorType cursorType,
        EventHandler<PointerPressedEventArgs> pointerPressed)
    {
        var edge = new Border
        {
            Cursor = new Cursor(cursorType),
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment
        };

        if (width.HasValue)
        {
            edge.Width = width.Value;
        }

        if (height.HasValue)
        {
            edge.Height = height.Value;
        }

        edge.Classes.Add("resize-edge");
        edge.PointerPressed += pointerPressed;
        Grid.SetRowSpan(edge, 2);
        return edge;
    }

    private Control BuildTitleBar(string title)
    {
        var titleBar = new Border
        {
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 8,
                Children =
                {
                    BuildTitleIcon(),
                    new TextBlock
                    {
                        Text = title,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    BuildWindowControls()
                }
            }
        };
        titleBar.Classes.Add("custom-titlebar");

        Grid.SetColumn(((Grid)titleBar.Child!).Children[1], 1);
        Grid.SetColumn(((Grid)titleBar.Child!).Children[2], 2);
        ((Grid)titleBar.Child!).Children[1].Classes.Add("window-title");
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        return titleBar;
    }

    private Control BuildTitleIcon()
    {
        _titleIconImage = new Image
        {
            Source = new Bitmap(AssetLoader.Open(MicroIconUri)),
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new Border
        {
            Child = _titleIconImage
        };
        icon.Classes.Add("title-icon");
        return icon;
    }

    private Control BuildWindowControls()
    {
        var minimize = WindowButton("-");
        minimize.Click += OnMinimizeClick;

        var maximize = WindowButton("□");
        maximize.Click += OnMaximizeRestoreClick;

        var close = WindowButton("x");
        close.Click += OnCloseClick;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                minimize,
                maximize,
                close
            }
        };
    }

    private static Button WindowButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Width = 34,
            Height = 26,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Focusable = false,
            IsTabStop = false,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("window-control");
        if (string.Equals(text, "x", StringComparison.OrdinalIgnoreCase))
        {
            button.Classes.Add("close");
        }

        return button;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnResizeLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.West, e);
    }

    private void OnResizeRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.East, e);
    }

    private void OnResizeTopPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.North, e);
    }

    private void OnResizeBottomPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.South, e);
    }

    private void OnResizeTopLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthWest, e);
    }

    private void OnResizeTopRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthEast, e);
    }

    private void OnResizeBottomLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthWest, e);
    }

    private void OnResizeBottomRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthEast, e);
    }

    private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private Control BuildAddRow(Button addButton)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 4
        };
        row.Children.Add(_newEntryBox);
        Grid.SetColumn(addButton, 1);
        row.Children.Add(addButton);
        return row;
    }

    private Control BuildListHost()
    {
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _itemsPanel
        };
        HoverScrollbarBehavior.SetIsEnabled(scrollViewer, true);
        HoverScrollbarBehavior.SetReserveRight(scrollViewer, 12);

        var host = new Border
        {
            Child = scrollViewer
        };
        host.Classes.Add("rule-list-host");
        return host;
    }

    private void AddEntryEditor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (_entryEditors.Any(editor => string.Equals(editor.Text?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var editor = new TextBox
        {
            Text = value.Trim(),
            FontSize = 9.5,
            MinHeight = 18,
            Padding = new Thickness(5, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        editor.Classes.Add("rule-add");

        var removeButton = SmallButton("x");
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                editor,
                removeButton
            }
        };
        Grid.SetColumn(removeButton, 1);

        removeButton.Click += (_, _) =>
        {
            _entryEditors.Remove(editor);
            _itemsPanel.Children.Remove(row);
        };

        _entryEditors.Add(editor);
        _itemsPanel.Children.Add(row);
    }

    private void AddPendingEntry()
    {
        foreach (var value in Split(_newEntryBox.Text ?? ""))
        {
            AddEntryEditor(value);
        }

        _newEntryBox.Text = "";
        _newEntryBox.Focus();
    }

    private void OnNewEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddPendingEntry();
        e.Handled = true;
    }

    private void FitToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var usableWidth = Math.Max(MinWidth, (screen.WorkingArea.Width / screen.Scaling) - 24);
        var usableHeight = Math.Max(MinHeight, (screen.WorkingArea.Height / screen.Scaling) - 24);
        MaxWidth = usableWidth;
        MaxHeight = usableHeight;
        Width = Math.Min(Math.Max(Width, MinWidth), usableWidth);
        Height = usableHeight;

        var widthPixels = (int)Math.Ceiling(Width * screen.Scaling);
        var heightPixels = (int)Math.Ceiling(Height * screen.Scaling);
        var maxX = Math.Max(screen.WorkingArea.X, screen.WorkingArea.X + screen.WorkingArea.Width - widthPixels);
        var maxY = Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Y + screen.WorkingArea.Height - heightPixels);
        Position = new PixelPoint(
            Math.Clamp(Position.X, screen.WorkingArea.X, maxX),
            Math.Clamp(Position.Y, screen.WorkingArea.Y, maxY));
    }

    private static Button SmallButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Width = 17,
            Height = 17,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 8,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add(string.Equals(text, "x", StringComparison.OrdinalIgnoreCase) ? "rule-x" : "rule-mini");
        return button;
    }

    private static Button CommandButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 24,
            Padding = new Thickness(8, 0),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("dialog-command");
        if (string.Equals(text, "Save", StringComparison.OrdinalIgnoreCase))
        {
            button.Classes.Add("primary");
        }

        return button;
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> values)
    {
        return values
            .SelectMany(Split)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Split(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }
}
