// CC-DESC: Tracks one in-flight local chat request for prompt progress rows.

using Avalonia.Threading;
using System.Text;

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatRequestProgressViewModel : ObservableObject
{
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly StringBuilder _thinkingText = new();
    private string _status = "Loading model...";
    private string _sizeLabel = "0 output tok";
    private string _speedLabel = "speed pending";
    private string _elapsedLabel = "0s";
    private string _thinkingPreviewText = "";
    private double _value;
    private bool _isIndeterminate = true;

    public ChatRequestProgressViewModel(string sessionId, string title, bool isCancellable = false)
    {
        SessionId = sessionId ?? "";
        Title = title ?? "";
        IsCancellable = isCancellable;
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => RefreshElapsed();
        RefreshElapsed();
        _elapsedTimer.Start();
    }

    public string SessionId { get; }

    public string Title { get; }

    public bool IsCancellable { get; }

    public double ElapsedSeconds => Math.Max(0, (DateTime.UtcNow - _startedUtc).TotalSeconds);

    public string Status
    {
        get => _status;
        set { if (SetProperty(ref _status, value ?? "")) OnPropertyChanged(nameof(CompactStatus)); }
    }

    public string CompactStatus => CompactStage(Status);
    public static string CompactStage(string? status)
    {
        var text = (status ?? "").ToLowerInvariant();
        if (text.Contains("photo") || text.Contains("image")) return "Photos";
        if (text.Contains("googl") || text.Contains("search")) return "Googling";
        if (text.Contains("reading") || text.Contains("source [")) return "Reading";
        if (text.Contains("think") || text.Contains("reason")) return "Thinking";
        if (text.Contains("load") || text.Contains("prepar") || text.Contains("warm")) return "Loading";
        if (text.Contains("stop") || text.Contains("cancel")) return "Stopping";
        if (text.Contains("complet") || text.Contains("ready")) return "Done";
        if (text.Contains("writ") || text.Contains("generat") || text.Contains("analyz") || text.Contains("answer")) return "Writing";
        return "Working";
    }

    public string SizeLabel
    {
        get => _sizeLabel;
        set => SetProperty(ref _sizeLabel, value ?? "");
    }

    public string SpeedLabel
    {
        get => _speedLabel;
        set => SetProperty(ref _speedLabel, value ?? "");
    }

    public string ElapsedLabel
    {
        get => _elapsedLabel;
        set => SetProperty(ref _elapsedLabel, value ?? "");
    }

    public string ThinkingPreviewText
    {
        get => _thinkingPreviewText;
        private set
        {
            if (SetProperty(ref _thinkingPreviewText, value ?? ""))
            {
                OnPropertyChanged(nameof(HasThinking));
            }
        }
    }

    public bool HasThinking => !string.IsNullOrWhiteSpace(ThinkingPreviewText);

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    public void RefreshElapsed()
    {
        ElapsedLabel = FormatElapsed(ElapsedSeconds);
        if (SpeedLabel.StartsWith("speed pending", StringComparison.OrdinalIgnoreCase)
            || SpeedLabel.StartsWith("pending ", StringComparison.OrdinalIgnoreCase))
        {
            SpeedLabel = $"pending {ElapsedLabel}";
        }
    }

    public void StopElapsedTimer()
    {
        RefreshElapsed();
        _elapsedTimer.Stop();
    }

    public void AppendThinking(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _thinkingText.Append(text);
        const int maxThinkingCharacters = 2400;
        if (_thinkingText.Length > maxThinkingCharacters)
        {
            _thinkingText.Remove(0, _thinkingText.Length - maxThinkingCharacters);
        }

        ThinkingPreviewText = BuildThinkingPreview(_thinkingText.ToString());
    }

    internal static string FormatElapsed(double seconds)
    {
        seconds = Math.Floor(Math.Max(0, seconds));
        return seconds < 60
            ? $"{seconds:0}s"
            : $"{(int)(seconds / 60)}m {seconds % 60:00}s";
    }

    private static string NormalizeThinkingDelta(string text)
    {
        return (text ?? "")
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static string BuildThinkingPreview(string text)
    {
        var clean = NormalizeThinkingDelta(text);
        const int maxPreviewCharacters = 420;
        return clean.Length <= maxPreviewCharacters
            ? clean
            : clean[^maxPreviewCharacters..];
    }
}
