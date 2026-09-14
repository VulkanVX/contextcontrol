// CC-DESC: Local LLM service slice extracted from LocalLlmService.cs.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private sealed class OllamaPullProgressParser(string operation, IProgress<LocalLlmTransferProgress>? progress)
    {
        private readonly StringBuilder _line = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long? _lastCurrentBytes;

        public void Append(string chunk)
        {
            if (progress is null || string.IsNullOrEmpty(chunk))
            {
                return;
            }

            foreach (var ch in chunk)
            {
                if (ch is '\r' or '\n')
                {
                    Flush();
                    continue;
                }

                _line.Append(ch);
            }

            Flush();
        }

        private void Flush()
        {
            if (_line.Length == 0)
            {
                return;
            }

            var text = CleanProgressText(_line.ToString());
            if (string.IsNullOrWhiteSpace(text))
            {
                _line.Clear();
                return;
            }

            var sizeMatch = SizeProgressRegex.Match(text);
            if (sizeMatch.Success)
            {
                var current = ParseByteCount(sizeMatch.Groups["current"].Value, sizeMatch.Groups["currentUnit"].Value);
                var total = ParseByteCount(sizeMatch.Groups["total"].Value, sizeMatch.Groups["totalUnit"].Value);
                var speed = sizeMatch.Groups["speed"].Success
                    ? ParseByteRate(sizeMatch.Groups["speed"].Value, sizeMatch.Groups["speedUnit"].Value)
                    : EstimateSpeed(current);
                progress!.Report(new LocalLlmTransferProgress(
                    operation,
                    text,
                    current,
                    total,
                    speed,
                    current is not null && total is > 0 ? Math.Clamp(current.Value * 100d / total.Value, 0, 100) : null));
                _line.Clear();
                return;
            }

            var percentMatch = PercentRegex.Match(text);
            if (percentMatch.Success
                && double.TryParse(percentMatch.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                progress!.Report(new LocalLlmTransferProgress(
                    operation,
                    text,
                    null,
                    null,
                    EstimateSpeed(null),
                    Math.Clamp(percent, 0, 100)));
                _line.Clear();
                return;
            }

            progress!.Report(new LocalLlmTransferProgress(
                operation,
                text,
                _lastCurrentBytes,
                null,
                EstimateSpeed(_lastCurrentBytes),
                null));
            _line.Clear();
        }

        private double? EstimateSpeed(long? currentBytes)
        {
            if (currentBytes is null)
            {
                return null;
            }

            _lastCurrentBytes = currentBytes;
            return _stopwatch.Elapsed.TotalSeconds <= 0
                ? null
                : currentBytes.Value / _stopwatch.Elapsed.TotalSeconds;
        }
    }

}
