using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContextControl.Workbench.Services;
using SkiaSharp;

internal static class GooglePhotoPreviewTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    internal static byte[] Photo(int width = 640, int height = 360)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Coral })
        {
            canvas.Clear(SKColors.DarkSlateBlue);
            canvas.DrawCircle(width * .3f, height * .4f, height * .25f, paint);
            paint.Color = SKColors.MediumAquamarine;
            canvas.DrawRect(width * .55f, height * .4f, width * .3f, height * .45f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static async Task<int> Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ContextControlPhotoTests", Guid.NewGuid().ToString("N"));
        var bytes = Photo(1600, 900);
        var handler = new FixtureHandler(_ => Image(bytes));
        using var client = new HttpClient(handler);
        var cache = new GooglePhotoPreviewService(root, client);
        static GoogleSearchSource Source(string? image) => new("Photo source", "https://example.com/article", "Example", image);
        var first = (await cache.LoadManyAsync([Source("https://images.example.com/photo.png")], default)).Single();
        Check(File.Exists(first) && handler.Calls == 1, "Public source images must be downloaded and cached.");
        using (var decoded = SKBitmap.Decode(first))
            Check(decoded.Width == 1280 && decoded.Height == 720, "Cached previews must be downscaled with their aspect ratio intact.");
        Check((await cache.LoadManyAsync([Source("https://images.example.com/photo.png")], default)).Single() == first && handler.Calls == 1,
            "Repeated photos must reuse the local cache without another request.");
        var inline = "data:image/png;base64," + Convert.ToBase64String(Photo());
        Check(File.Exists((await cache.LoadManyAsync([Source(inline)], default)).Single()) && handler.Calls == 1,
            "Embedded Google thumbnails must work without a network request.");
        foreach (var invalid in new[] { "file:///C:/private.png", "http://127.0.0.1/photo", "data:image/svg+xml;base64,AAAA", "blob:https://example.com/a", "data:text/html;base64,AAAA", "data:image/png;base64," + new string('A', GooglePhotoPreviewService.MaxInlineImageLength) })
            Check(!GooglePhotoPreviewService.IsImageLocation(invalid), "Unsafe or oversized photo location accepted.");
        Check((await cache.LoadManyAsync([Source("data:image/png;base64,invalid!")], default)).Single() == "", "Malformed embedded images must fall back to a text source.");
        foreach (var address in new[] { "127.0.0.1", "10.0.0.2", "169.254.169.254", "192.168.1.1", "100.64.0.1", "198.18.0.1", "::1", "::ffff:127.0.0.1", "fd00::1", "fe80::1", "2001:db8::1" })
            Check(!GooglePhotoPreviewService.IsPublicAddress(IPAddress.Parse(address)), "Private or reserved DNS destination accepted.");
        Check(GooglePhotoPreviewService.IsPublicAddress(IPAddress.Parse("8.8.8.8")) && GooglePhotoPreviewService.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")), "Public IPv4 and IPv6 destinations must remain usable.");
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://127.0.0.1/photo") } };
        var before = handler.Calls;
        Check((await cache.LoadManyAsync([Source("https://images.example.com/redirect")], default)).Single() == "" && handler.Calls == before + 1,
            "Redirects to local services must be rejected before issuing another request.");
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/loop", UriKind.Relative) } };
        before = handler.Calls;
        Check((await cache.LoadManyAsync([Source("https://images.example.com/loop")], default)).Single() == "" && handler.Calls == before + 4,
            "Redirect loops must stop after three hops.");
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        Check((await cache.LoadManyAsync([Source("https://images.example.com/blocked")], default)).Single() == "", "Blocked photos must not fail the chat.");
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login required</html>", Encoding.UTF8, "text/html") };
        Check((await cache.LoadManyAsync([Source("https://images.example.com/login")], default)).Single() == "", "HTML must never be treated as an image.");
        handler.Reply = _ => Image(Encoding.UTF8.GetBytes("not a photo"));
        Check((await cache.LoadManyAsync([Source("https://images.example.com/malformed")], default)).Single() == "", "Invalid image bytes must fall back without interrupting research.");
        handler.Reply = _ => Image(new byte[GooglePhotoPreviewService.MaxDownloadBytes + 1], streamed: true);
        Check((await cache.LoadManyAsync([Source("https://images.example.com/huge")], default)).Single() == "", "Responses without a Content-Length must obey the download byte limit.");
        handler.Reply = _ => Image(Photo(8001, 50));
        Check((await cache.LoadManyAsync([Source("https://images.example.com/wide")], default)).Single() == "", "Oversized dimensions must be rejected before bitmap allocation.");
        handler.Reply = _ => Image(Photo(16, 16));
        Check((await cache.LoadManyAsync([Source("https://images.example.com/icon")], default)).Single() == "", "Tiny favicons and tracking images should not become photo cards.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await cache.LoadManyAsync([Source(inline)], cancellation.Token); Check(false, "User cancellation must propagate."); }
        catch (OperationCanceledException) { _checks++; }
        var parsed = GooglePageReader.Parse(JsonSerializer.Serialize(new { url = "https://example.com/article", text = "Article", imageUrl = "https://images.example.com/hero.jpg" }));
        Check(parsed.ImageUrl == "https://images.example.com/hero.jpg", "Readable pages must preserve their preview metadata.");
        Check(GooglePageReader.Parse("{\"url\":\"https://example.com\",\"text\":\"Article\",\"imageUrl\":\"http://localhost/private.png\"}").ImageUrl is null,
            "Page metadata must not introduce local image reads.");
        var query = "photo test";
        var search = GoogleSearchContext.ParseBrowserResult(query, JsonSerializer.Serialize(new { url = GoogleSearchContext.SearchUrl(query), results = new[] { new { title = "Source", url = "https://example.com/article", snippet = "Article", imageUrl = inline } } }));
        Check(search.Sources.Single().ImageUrl == inline, "Google result thumbnails must survive extraction parsing.");
        Check(!GoogleSearchContext.AugmentPrompt("question", new GoogleResearchResult(search, [])).Contains("data:image"), "Photo bytes must stay out of text-only model context.");
        Console.WriteLine($"Google photo preview regression passed: {_checks} checks.");
        return _checks;
    }

    private static HttpResponseMessage Image(byte[] bytes, bool streamed = false)
    {
        HttpContent content = streamed ? new StreamContent(new UnseekableStream(bytes)) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpRequestMessage, HttpResponseMessage> Reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Reply(request));
        }
    }
}
