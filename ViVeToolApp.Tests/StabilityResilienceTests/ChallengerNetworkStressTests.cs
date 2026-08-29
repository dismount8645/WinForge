using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.StabilityResilienceTests;

/// <summary>
/// Challenger 1 Adversarial Test Suite for Milestone 3:
/// Network & Timeout Stress Testing across Socket Disconnections, Partial HTTP Responses,
/// HTTP 500/503/502/504/429 Status Codes, Zero-Delay Cancellation Tokens, Offline Fallback,
/// and Concurrency Stress.
/// </summary>
[Collection("ViVeToolDownloaderSharedTempCollection")]
public class ChallengerNetworkStressTests : IDisposable
{
    private readonly string _tempTestDir;

    public ChallengerNetworkStressTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), $"ChallengerNetTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    #region Helper Mock Handler

    private sealed class DynamicMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DynamicMockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(request, cancellationToken);
        }
    }

    private static byte[] CreateDummyZip(string exeName = "vivetool.exe", string content = "MZ Dummy ViVeTool binary")
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(exeName);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(content);
        }
        return ms.ToArray();
    }

    #endregion

    #region Section 1: Socket Disconnections & Low-Level Network Failures

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.HostUnreachable)]
    [InlineData(SocketError.NetworkDown)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.TimedOut)]
    [InlineData(SocketError.Shutdown)]
    public async Task Scraper_SuddenSocketFailures_ThrowsHttpRequestExceptionWithInnerSocketException(SocketError socketError)
    {
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
            throw new HttpRequestException($"Network failed with {socketError}", new SocketException((int)socketError)));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/codes");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.InnerException.Should().BeOfType<SocketException>();
        ((SocketException)ex.Which.InnerException!).SocketErrorCode.Should().Be(socketError);
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.HostUnreachable)]
    [InlineData(SocketError.NetworkDown)]
    public async Task Downloader_ReleaseCheckSocketFailures_ThrowsHttpRequestExceptionWithInnerSocketException(SocketError socketError)
    {
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
            throw new HttpRequestException($"Socket drop: {socketError}", new SocketException((int)socketError)));
        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.InnerException.Should().BeOfType<SocketException>();
    }

    [Fact]
    public async Task Downloader_ZipDownloadSocketFailure_ThrowsAndCleansUpTempZipFile()
    {
        var releaseJson = """
        {
            "tag_name": "v0.3.3",
            "assets": [
                {
                    "name": "ViVeTool-v0.3.3.zip",
                    "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip"
                }
            ]
        }
        """;

        var tempFilesBefore = new HashSet<string>(Directory.GetFiles(Path.GetTempPath(), "ViVeTool_*.zip"));

        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                });
            }

            // Simulate connection drop when downloading zip
            throw new HttpRequestException("Connection reset by peer during zip transfer", new SocketException((int)SocketError.ConnectionReset));
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir);

        await act.Should().ThrowAsync<HttpRequestException>();

        var newTempFiles = Directory.GetFiles(Path.GetTempPath(), "ViVeTool_*.zip")
            .Where(f => !tempFilesBefore.Contains(f));
        newTempFiles.Should().BeEmpty("Any created temporary zip files must be cleaned up in finally block upon failure");
    }

    #endregion

    #region Section 2: Partial Responses, Stream Drops & Truncation

    private class TruncatedStreamContent : HttpContent
    {
        private readonly byte[] _data;
        private readonly int _truncateAfterBytes;

        public TruncatedStreamContent(byte[] data, int truncateAfterBytes)
        {
            _data = data;
            _truncateAfterBytes = truncateAfterBytes;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_data.AsMemory(0, Math.Min(_truncateAfterBytes, _data.Length)));
            await stream.FlushAsync();
            throw new IOException("Connection abruptly closed by remote peer while sending stream.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _data.Length;
            return true;
        }
    }

    [Fact]
    public async Task Scraper_PartialStreamDropDuringHtmlDownload_ThrowsIOExceptionOrHttpRequestException()
    {
        var fullHtml = """
        <div class="entry-content">
            <h3>GA 2026</h3>
            <strong>September 2026</strong>
            <li><code>61754985</code> Valid feature</li>
        </div>
        """;
        var bytes = Encoding.UTF8.GetBytes(fullHtml);

        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new TruncatedStreamContent(bytes, truncateAfterBytes: 50)
            };
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/codes");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Scraper_TruncatedHtmlFragment_ParsesAvailableValidItemsWithoutCrashing()
    {
        // Truncated HTML: cut in the middle of a code tag or second list item
        var truncatedHtml = """
        <div class="entry-content">
            <h3>GA 2026</h3>
            <strong>September 2026</strong>
            <li><code>61754985</code> First feature fully present</li>
            <li><code>62762248</code> Second feature with unclosed code tag <code>6123
        """;

        var scraper = new PureinfotechScraper();
        var results = scraper.ParseHtml(truncatedHtml);

        results.Should().NotBeEmpty();
        results.Should().Contain(f => f.IDs.Contains(61754985));
        results.Should().Contain(f => f.IDs.Contains(62762248));
    }

    [Fact]
    public async Task Downloader_PartialZipStreamDrop_ThrowsAndCleansUpTempFiles()
    {
        var zipBytes = CreateDummyZip();
        var releaseJson = """
        {
            "tag_name": "v0.3.3",
            "assets": [
                {
                    "name": "ViVeTool-v0.3.3.zip",
                    "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip"
                }
            ]
        }
        """;

        var tempFilesBefore = new HashSet<string>(Directory.GetFiles(Path.GetTempPath(), "ViVeTool_*.zip"));

        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new TruncatedStreamContent(zipBytes, truncateAfterBytes: 40)
            });
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir);

        await act.Should().ThrowAsync<Exception>();

        var newTempFiles = Directory.GetFiles(Path.GetTempPath(), "ViVeTool_*.zip")
            .Where(f => !tempFilesBefore.Contains(f));
        newTempFiles.Should().BeEmpty("Any created temporary zip files must be cleaned up in finally block upon failure");
    }

    #endregion

    #region Section 3: HTTP 500/503/502/504/429 Status Codes & Error Payloads

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "<h1>500 Internal Server Error</h1><p>Database connection failed</p>")]
    [InlineData(HttpStatusCode.BadGateway, "<html><head><title>502 Bad Gateway</title></head><body><center><h1>502 Bad Gateway</h1></center><hr><center>nginx/1.24.0</center></body></html>")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503 Service Unavailable: Server maintenance in progress")]
    [InlineData(HttpStatusCode.GatewayTimeout, "504 Gateway Timeout")]
    [InlineData((HttpStatusCode)429, "{\"error\": \"Rate limit exceeded\", \"retry_after\": 60}")]
    public async Task Scraper_ServerAndRateLimitErrors_ThrowsHttpRequestExceptionWithAccurateStatus(HttpStatusCode statusCode, string errorBody)
    {
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(errorBody, Encoding.UTF8, "text/html")
            }));

        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/codes");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData((HttpStatusCode)429)]
    public async Task Downloader_ReleaseApiErrors_ThrowsHttpRequestExceptionWithAccurateStatus(HttpStatusCode statusCode)
    {
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{\"message\": \"Server error\"}", Encoding.UTF8, "application/json")
            }));

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Downloader_ZipAssetDownloadErrors_ThrowsHttpRequestExceptionWithAccurateStatus(HttpStatusCode statusCode)
    {
        var releaseJson = """
        {
            "tag_name": "v0.3.3",
            "assets": [
                {
                    "name": "ViVeTool-v0.3.3.zip",
                    "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip"
                }
            ]
        }
        """;

        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("CDN error", Encoding.UTF8, "text/plain")
            });
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(statusCode);
    }

    #endregion

    #region Section 4: Zero-Delay Cancellation Tokens & Instant Aborts

    [Fact]
    public async Task Scraper_PreCancelledToken_AbortsImmediatelyWithoutNetworkCall()
    {
        bool requestAttempted = false;
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            requestAttempted = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/codes", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        requestAttempted.Should().BeFalse("Pre-cancelled token should abort before invoking network handler");
    }

    [Fact]
    public async Task Downloader_PreCancelledToken_AbortsImmediatelyWithoutNetworkCall()
    {
        bool requestAttempted = false;
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            requestAttempted = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        requestAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Downloader_ZeroDelayCancellationDuringReleaseFetch_AbortsCleanly()
    {
        using var cts = new CancellationTokenSource();
        var handler = new DynamicMockHttpMessageHandler(async (req, ct) =>
        {
            // Trigger cancellation immediately as soon as request starts
            cts.Cancel();
            await Task.Delay(1000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Downloader_CancellationBetweenReleaseAndZipFetch_CleansUpAndAbortsPromptly()
    {
        var releaseJson = """
        {
            "tag_name": "v0.3.3",
            "assets": [
                {
                    "name": "ViVeTool-v0.3.3.zip",
                    "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip"
                }
            ]
        }
        """;

        using var cts = new CancellationTokenSource();
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                // Cancel token right after release response is ready
                cts.Cancel();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_tempTestDir, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Section 5: Offline Fallback Catalog & Seamless Recovery

    [Fact]
    public async Task FullWorkflow_SimulatedNetworkDisconnection_SeamlesslyActivatesOfflineCatalog()
    {
        // 1. Setup scraper with simulated network blackout
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
            throw new HttpRequestException("Host unreachable: pureinfotech.com", new SocketException((int)SocketError.HostUnreachable)));

        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        // 2. Perform safe fetch with fallback pattern matching MainWindow logic
        List<FeatureItem> loadedFeatures;
        bool fallbackActivated = false;

        try
        {
            loadedFeatures = await scraper.FetchAndParseAsync("https://pureinfotech.com/codes");
        }
        catch (Exception)
        {
            fallbackActivated = true;
            loadedFeatures = scraper.GetOfflineFallback();
        }

        // 3. Assertions
        fallbackActivated.Should().BeTrue();
        loadedFeatures.Should().NotBeNull();
        loadedFeatures.Should().HaveCount(15);

        // Verify distribution across all Windows 11 channels
        var groups = loadedFeatures.Select(f => f.Group).Distinct().ToList();
        groups.Should().Contain("GA 2026");
        groups.Should().Contain("GA 2025");
        groups.Should().Contain("26H2 Insider");
        groups.Should().Contain("25H2 Insider");
        groups.Should().Contain("Canary / Feature Platforms");

        // Verify ID validity
        foreach (var item in loadedFeatures)
        {
            item.IDs.Should().NotBeEmpty();
            item.IDs.Should().OnlyContain(id => id >= 1_000_000 && id <= 999_999_999);
            item.Description.Should().NotBeNullOrWhiteSpace();
            item.Description.Should().NotContain("<");
            item.Description.Should().NotContain(">");
            item.IsSelected.Should().BeTrue();
        }
    }

    [Fact]
    public void OfflineCatalog_DeepIntegrityAudit_All15FeaturesMatchExpectedFormat()
    {
        var features = OfflineCatalog.GetFeatures();

        features.Should().HaveCount(15);

        for (int i = 0; i < features.Count; i++)
        {
            var item = features[i];
            item.Group.Should().NotBeNullOrWhiteSpace($"Item [{i}] group must not be null/empty");
            item.BuildLabel.Should().NotBeNullOrWhiteSpace($"Item [{i}] build label must not be null/empty");
            item.Description.Should().NotBeNullOrWhiteSpace($"Item [{i}] description must not be null/empty");
            item.IDs.Should().NotBeEmpty($"Item [{i}] must contain at least one ID");
            item.IDsDisplay.Should().Be(string.Join(", ", item.IDs), $"Item [{i}] IDsDisplay must match IDs");
            item.IsSelected.Should().BeTrue($"Item [{i}] must be selected by default");
        }
    }

    #endregion

    #region Section 6: Concurrency & Stress Load Verification

    [Fact]
    public async Task Concurrency_MixedNetworkFailuresAndSuccesses_ExecutesWithoutHangsOrCrashes()
    {
        var htmlContent = """
        <div class="entry-content">
            <h3>GA 2026</h3>
            <strong>September 2026</strong>
            <li><code>61754985</code> Concurrency stress test feature</li>
        </div>
        <!-- CONTENT END -->
        """;

        var random = new Random(42);
        var handler = new DynamicMockHttpMessageHandler((req, ct) =>
        {
            var choice = random.Next(4);
            return choice switch
            {
                0 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(htmlContent) }),
                1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                2 => throw new HttpRequestException("Socket reset", new SocketException((int)SocketError.ConnectionReset)),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
            };
        });

        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var tasks = new List<Task<List<FeatureItem>>>();
        for (int i = 0; i < 60; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    return await scraper.FetchAndParseAsync("https://pureinfotech.com/test");
                }
                catch
                {
                    return scraper.GetOfflineFallback();
                }
            }));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(60);
        foreach (var list in results)
        {
            list.Should().NotBeEmpty();
            list.Should().Match<List<FeatureItem>>(items => items.Count == 1 || items.Count == 15);
        }
    }

    #endregion
}
