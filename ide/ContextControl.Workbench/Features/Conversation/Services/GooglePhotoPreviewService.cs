using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace ContextControl.Workbench.Services;

/// <summary>Optional, bounded previews of images supplied by search results or readable pages.</summary>
public sealed class GooglePhotoPreviewService(string cacheDirectory, HttpClient? client = null)
{
    public const int MaxInlineImageLength = 524288;
    public const int MaxDownloadBytes = 5 * 1024 * 1024;
    private static readonly HttpClient PublicClient = CreateClient();
    private static readonly SemaphoreSlim Workers = new(2);
    private static readonly object CacheGate = new();
    public static GooglePhotoPreviewService Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextControl", "WebPhotos"));
    private readonly string _cacheDirectory = Path.GetFullPath(cacheDirectory);
    private readonly HttpClient _client = client ?? PublicClient;

    public static bool IsImageLocation(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length <= 4096 && GoogleSearchContext.IsPublicWebUrl(value)) return true;
        if (value.Length > MaxInlineImageLength) return false;
        return new[] { "png", "jpeg", "webp", "gif" }.Any(type => value.StartsWith($"data:image/{type};base64,", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<string>> LoadManyAsync(IReadOnlyList<GoogleSearchSource> sources, CancellationToken cancellationToken)
    {
        // All previews together get six seconds. Text answers still work when images are slow or blocked.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        var paths = await Task.WhenAll(sources.Take(GoogleSearchContext.MaxSources).Select(source => LoadOptionalAsync(source.ImageUrl, deadline.Token)));
        cancellationToken.ThrowIfCancellationRequested();
        return paths;
    }

    private async Task<string> LoadOptionalAsync(string? location, CancellationToken cancellationToken)
    {
        if (!IsImageLocation(location)) return "";
        var entered = false;
        try
        {
            await Workers.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location!))).ToLowerInvariant();
            var path = Path.Combine(_cacheDirectory, key + ".jpg");
            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return path;
            }
            var bytes = location!.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(location[(location.IndexOf(',') + 1)..])
                : await DownloadAsync(new Uri(location), cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length > MaxDownloadBytes) return "";
            return await Task.Run(() => SavePreview(bytes, path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException)
        {
            return "";
        }
        finally { if (entered) Workers.Release(); }
    }

    private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            if (!GoogleSearchContext.IsPublicWebUrl(uri.AbsoluteUri)) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("ContextControl/0.4 (+photo-preview)");
            request.Headers.Accept.ParseAdd("image/jpeg,image/png,image/webp,image/gif");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location is not { } target) return null;
                uri = new Uri(uri, target);
                continue;
            }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes
                || response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif")) return null;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > MaxDownloadBytes) return null;
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        return null;
    }

    private string SavePreview(byte[] bytes, string path, CancellationToken cancellationToken)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp or SKEncodedImageFormat.Gif)) return "";
        var info = codec.Info;
        if (info.Width < 80 || info.Height < 50 || info.Width > 8000 || info.Height > 8000 || (long)info.Width * info.Height > 16_000_000) return "";
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) return "";
        var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = rotated ? info.Height : info.Width;
        var height = rotated ? info.Width : info.Height;
        var scale = Math.Min(1d, 1280d / Math.Max(width, height));
        using var preview = new SKBitmap(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        using (var canvas = new SKCanvas(preview))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.Scale((float)preview.Width / width, (float)preview.Height / height);
            var transform = codec.EncodedOrigin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, info.Width, 0, 1, 0, 0, 0, 1),
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, info.Width, 0, -1, info.Height, 0, 0, 1),
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, info.Height, 0, 0, 1),
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, info.Height, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, info.Height, -1, 0, info.Width, 0, 0, 1),
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, info.Width, 0, 0, 1),
                _ => SKMatrix.Identity
            };
            canvas.Concat(ref transform);
            canvas.DrawBitmap(bitmap, 0, 0, paint);
        }
        using var image = SKImage.FromBitmap(preview);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        cancellationToken.ThrowIfCancellationRequested();
        lock (CacheGate)
        {
            Directory.CreateDirectory(_cacheDirectory);
            // Only our hash-named cache files are pruned; chat records and original images are untouched.
            var files = new DirectoryInfo(_cacheDirectory).EnumerateFiles("*.jpg")
                .Where(file => file.Name.Length == 68 && file.Name[..64].All(Uri.IsHexDigit))
                .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            long total = 0;
            for (var i = 0; i < files.Length; i++)
            {
                total += files[i].Length;
                if (i >= 255 || total > 120 * 1024 * 1024)
                    try { files[i].Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            // CreateNew prevents a concurrent request from truncating a preview already in use.
            if (!File.Exists(path))
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                encoded.SaveTo(file);
            }
        }
        return path;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
            && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0))
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    private static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, MaxConnectionsPerServer = 2,
        ConnectTimeout = TimeSpan.FromSeconds(4), PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, token) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) throw new HttpRequestException("Photo host must resolve to public addresses.");
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    // Connect to the checked address itself, so a second DNS lookup cannot change the destination.
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("Photo host could not be reached.");
        }
    }) { Timeout = TimeSpan.FromSeconds(6) };
}
