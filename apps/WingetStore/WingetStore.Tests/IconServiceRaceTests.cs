using System.Net;
using System.Net.Http.Headers;

namespace WingetStore.Tests;

public class IconServiceRaceTests
{
    private const string RacePackageId = "Mock.Race.App";
    private const string DownloadUrl = "https://cdn.example.com/icons/Mock.Race.App.png";

    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static string LocalIconPath => Path.Combine(AppPaths.IconsCacheDir, IconService.GetSafeIconFileName(RacePackageId));

    private static void CleanupIconArtifacts()
    {
        try
        {
            if (!Directory.Exists(AppPaths.IconsCacheDir)) return;
            if (File.Exists(LocalIconPath)) File.Delete(LocalIconPath);
            string prefix = IconService.GetSafeIconFileName(RacePackageId) + ".";
            foreach (var tmp in Directory.GetFiles(AppPaths.IconsCacheDir, prefix + "*.tmp"))
            {
                try { File.Delete(tmp); } catch { }
            }
        }
        catch { }
    }

    [Fact]
    public void GetTempFilePath_KeysTempFileByUrl()
    {
        string local = Path.Combine(AppPaths.IconsCacheDir, "App.png");

        string t1 = IconService.GetTempFilePath(local, DownloadUrl);
        string t2 = IconService.GetTempFilePath(local, "https://cdn.example.com/other.png");

        Assert.StartsWith(local + ".", t1);
        Assert.EndsWith(".tmp", t1);
        Assert.NotEqual(t1, t2);
        Assert.NotEqual(t1, local);
    }

    [Fact]
    public async Task DownloadAndResolve_Concurrent_SamePackage_OnlyOneDownloads()
    {
        CleanupIconArtifacts();
        try
        {
            var handler = new ImageHttpHandler(MinimalPng, TimeSpan.FromMilliseconds(300));
            using var httpClient = new HttpClient(handler);
            var service = new IconService(httpClient);

            var downloadTask = service.DownloadIconAsync(RacePackageId, DownloadUrl);
            var resolveTask = service.ResolveIconOnlineAsync(RacePackageId);

            await Task.WhenAll(downloadTask, resolveTask);

            Assert.Single(handler.RequestedUrls);
            Assert.Contains(DownloadUrl, handler.RequestedUrls);
            Assert.True(File.Exists(LocalIconPath), "concurrent resolve path must not clobber the download's temp file");
            byte[] bytes = File.ReadAllBytes(LocalIconPath);
            Assert.True(IconService.IsValidImageHeader(bytes, bytes.Length));
        }
        finally
        {
            CleanupIconArtifacts();
        }
    }

    [Fact]
    public async Task ResolveThenDownload_Concurrent_SamePackage_OnlyResolveRuns()
    {
        CleanupIconArtifacts();
        try
        {
            var handler = new ImageHttpHandler(MinimalPng, TimeSpan.FromMilliseconds(50));
            using var httpClient = new HttpClient(handler);
            var service = new IconService(httpClient);

            var resolveTask = service.ResolveIconOnlineAsync(RacePackageId);
            var downloadTask = service.DownloadIconAsync(RacePackageId, DownloadUrl);

            await Task.WhenAll(downloadTask, resolveTask);

            Assert.DoesNotContain(DownloadUrl, handler.RequestedUrls);
            Assert.NotEmpty(handler.RequestedUrls);
            Assert.True(File.Exists(LocalIconPath));
            byte[] bytes = File.ReadAllBytes(LocalIconPath);
            Assert.True(IconService.IsValidImageHeader(bytes, bytes.Length));
        }
        finally
        {
            CleanupIconArtifacts();
        }
    }

    private sealed class ImageHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _imageBytes;
        private readonly TimeSpan _delay;

        public ImageHttpHandler(byte[] imageBytes, TimeSpan delay)
        {
            _imageBytes = imageBytes;
            _delay = delay;
        }

        public List<string> RequestedUrls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestedUrls) RequestedUrls.Add(request.RequestUri?.ToString() ?? "");
            await Task.Delay(_delay, cancellationToken);
            var content = new ByteArrayContent(_imageBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
