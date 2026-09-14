using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ContextControl.Workbench.Controls;

public sealed class WebView2Host : NativeControlHost
{
    public static readonly StyledProperty<string?> UserDataFolderProperty =
        AvaloniaProperty.Register<WebView2Host, string?>(nameof(UserDataFolder));

    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;

    private IntPtr _hostWindow;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private string? _pendingUrl = "https://chatgpt.com";
    private bool _isDisposed;

    public event EventHandler<WebView2NavigationStartingEventArgs>? NavigationStarted;
    public event EventHandler<WebView2NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<WebView2InitializationFailedEventArgs>? InitializationFailed;

    public string? UserDataFolder
    {
        get => GetValue(UserDataFolderProperty);
        set => SetValue(UserDataFolderProperty, value);
    }

    public string? Source => _webView?.Source ?? _pendingUrl;

    public bool CanGoBack => _webView?.CanGoBack ?? false;

    public bool CanGoForward => _webView?.CanGoForward ?? false;

    public void Navigate(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _pendingUrl = url;
        if (_webView is not null)
        {
            _webView.Navigate(url);
        }
    }

    public void GoBack()
    {
        if (_webView?.CanGoBack == true)
        {
            _webView.GoBack();
        }
    }

    public void GoForward()
    {
        if (_webView?.CanGoForward == true)
        {
            _webView.GoForward();
        }
    }

    public void Reload()
    {
        _webView?.Reload();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            Dispatcher.UIThread.Post(() =>
                InitializationFailed?.Invoke(this, new WebView2InitializationFailedEventArgs("Embedded browser is currently available on Windows only.")));
            return base.CreateNativeControlCore(parent);
        }

        _hostWindow = CreateHostWindow(parent.Handle);
        _ = InitializeAsync(_hostWindow);
        return new PlatformHandle(_hostWindow, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _isDisposed = true;
        _webView = null;

        try
        {
            _controller?.Close();
        }
        catch (COMException)
        {
        }

        _controller = null;

        if (control.Handle != IntPtr.Zero)
        {
            DestroyWindow(control.Handle);
        }

        _hostWindow = IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        ResizeController(finalSize);
        return arranged;
    }

    private async Task InitializeAsync(IntPtr hostWindow)
    {
        try
        {
            var userDataFolder = ResolveUserDataFolder();

            Directory.CreateDirectory(userDataFolder);
            _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
            if (_isDisposed || hostWindow == IntPtr.Zero)
            {
                return;
            }

            _controller = await _environment.CreateCoreWebView2ControllerAsync(hostWindow).ConfigureAwait(true);
            _webView = _controller.CoreWebView2;
            _controller.IsVisible = true;

            HookEvents(_webView);
            ResizeController(Bounds.Size);

            var url = _pendingUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                _webView.Navigate(url);
            }
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            Dispatcher.UIThread.Post(() =>
                InitializationFailed?.Invoke(this, new WebView2InitializationFailedEventArgs($"Embedded browser unavailable: {ex.Message}")));
        }
    }

    private string ResolveUserDataFolder()
    {
        if (!string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return UserDataFolder;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContextControl",
            "WebView2");
    }

    private void HookEvents(CoreWebView2 webView)
    {
        webView.NavigationStarting += (_, args) =>
        {
            var url = args.Uri;
            _pendingUrl = url;
            Dispatcher.UIThread.Post(() =>
                NavigationStarted?.Invoke(this, new WebView2NavigationStartingEventArgs(url)));
        };

        webView.NavigationCompleted += (_, args) =>
            Dispatcher.UIThread.Post(() => RaiseNavigationCompleted(args.IsSuccess));

        webView.HistoryChanged += (_, _) =>
            Dispatcher.UIThread.Post(() => RaiseNavigationCompleted(succeeded: true));

        webView.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (!string.IsNullOrWhiteSpace(args.Uri))
            {
                Navigate(args.Uri);
            }
        };
    }

    private void RaiseNavigationCompleted(bool succeeded)
    {
        NavigationCompleted?.Invoke(
            this,
            new WebView2NavigationCompletedEventArgs(
                Source,
                succeeded,
                CanGoBack,
                CanGoForward));
    }

    private void ResizeController(Size size)
    {
        if (_controller is null)
        {
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var width = Math.Max(1, (int)Math.Round(size.Width * scale));
        var height = Math.Max(1, (int)Math.Round(size.Height * scale));
        _controller.Bounds = new DrawingRectangle(0, 0, width, height);
    }

    private static IntPtr CreateHostWindow(IntPtr parentHandle)
    {
        var handle = CreateWindowExW(
            0,
            "STATIC",
            "",
            WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            0,
            0,
            1,
            1,
            parentHandle,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}

public sealed class WebView2NavigationStartingEventArgs(string url) : EventArgs
{
    public string Url { get; } = url;
}

public sealed class WebView2NavigationCompletedEventArgs(
    string? url,
    bool succeeded,
    bool canGoBack,
    bool canGoForward) : EventArgs
{
    public string? Url { get; } = url;

    public bool Succeeded { get; } = succeeded;

    public bool CanGoBack { get; } = canGoBack;

    public bool CanGoForward { get; } = canGoForward;
}

public sealed class WebView2InitializationFailedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
