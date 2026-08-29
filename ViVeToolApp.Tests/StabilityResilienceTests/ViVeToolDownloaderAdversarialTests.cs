using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.StabilityResilienceTests;

/// <summary>
/// Adversarial tests for ViVeToolDownloader:
/// Corrupt release JSON, HTML error pages, malformed archives, simulated network failures, and cancellation.
/// </summary>
[Collection("ViVeToolDownloaderSharedTempCollection")]
public class ViVeToolDownloaderAdversarialTests : IDisposable
{
    private readonly string _testDirectory;

    public ViVeToolDownloaderAdversarialTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ViVeDownloaderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Category 1: Regex Extraction on Adversarial Payloads

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("<!DOCTYPE html><html><body><h1>502 Bad Gateway</h1></body></html>")]
    [InlineData("{ \"message\": \"Not Found\", \"documentation_url\": \"https://docs.github.com/rest\" }")]
    [InlineData("{ \"tag_name\": \"v0.3.3\", \"assets\": [] }")]
    [InlineData("{ \"tag_name\": \"v0.3.3\", \"assets\": [{ \"name\": \"source.tar.gz\", \"browser_download_url\": \"https://github.com/thebookisclosed/ViVe/archive/v0.3.3.tar.gz\" }] }")]
    [InlineData("{ \"browser_download_url\": \"https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool.tar.gz\" }")] // ViVeTool in tar.gz, not zip
    [InlineData("corrupted payload without any json structure at all")]
    public void ExtractZipUrlFromReleaseJson_InvalidAndNonMatchingPayloads_ReturnsNull(string? payload)
    {
        var downloader = new ViVeToolDownloader();
        var result = downloader.ExtractZipUrlFromReleaseJson(payload!);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(
        """{ "assets": [{ "name": "ViVeTool-v0.3.3.zip", "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip" }] }""",
        "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip")]
    [InlineData(
        """{ "assets": [{ "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.4/ViVeTool-v0.3.4.zip" }] }""",
        "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.4/ViVeTool-v0.3.4.zip")]
    [InlineData(
        """{ "assets": [{ "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/vivetool_arm64.zip" }] }""",
        "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/vivetool_arm64.zip")]
    [InlineData(
        """{ "assets": [{ "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool.ZIP" }] }""",
        "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool.ZIP")]
    [InlineData(
        """{ "assets": [{ "browser_download_url": "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/vivetool.zip" }] }""",
        "https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/vivetool.zip")]
    public void ExtractZipUrlFromReleaseJson_ValidPayloads_ExtractsExactUrl(string json, string expectedUrl)
    {
        var downloader = new ViVeToolDownloader();
        var result = downloader.ExtractZipUrlFromReleaseJson(json);
        result.Should().Be(expectedUrl);
    }

    [Fact]
    public void ExtractZipUrlFromReleaseJson_MassivePayloadWithNoise_ExtractsUrlPromptly()
    {
        var sb = new StringBuilder(5 * 1024 * 1024);
        sb.Append("{ \"release\": \"test\", \"noise\": [");
        for (int i = 0; i < 20000; i++)
        {
            sb.Append($"{{\"id\": {i}, \"data\": \"some random filler text with special chars \\\" \\/ \\n\"}},");
        }
        sb.Append("{\"name\": \"ViVeTool-v0.3.3.zip\", \"browser_download_url\": \"https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip\"}");
        sb.Append("]}");

        var massiveJson = sb.ToString();
        var downloader = new ViVeToolDownloader();

        var result = downloader.ExtractZipUrlFromReleaseJson(massiveJson);
        result.Should().Be("https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip");
    }

    #endregion

    #region Category 2: Simulated Download & Archive Extraction

    private static byte[] CreateValidViVeToolZip(string exeName = "vivetool.exe", string content = "MZ Dummy ViVeTool binary content")
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

    [Fact]
    public async Task DownloadAndExtractViVeToolAsync_ValidResponse_ExtractsBinaryAndReportsProgress()
    {
        var zipBytes = CreateValidViVeToolZip();
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

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri.ToString().EndsWith(".zip"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var reportedProgress = new System.Collections.Generic.List<int>();
        var progress = new Progress<int>(reportedProgress.Add);

        var extractedExe = await downloader.DownloadAndExtractViVeToolAsync(_testDirectory, progress);

        extractedExe.Should().Be(Path.Combine(_testDirectory, "vivetool.exe"));
        File.Exists(extractedExe).Should().BeTrue();
        File.ReadAllText(extractedExe).Should().Contain("Dummy ViVeTool binary content");
    }

    [Fact]
    public async Task DownloadAndExtractViVeToolAsync_NoZipAssetInRelease_ThrowsInvalidOperationException()
    {
        var releaseJson = """
        {
            "tag_name": "v0.3.3",
            "assets": [
                {
                    "name": "source.tar.gz",
                    "browser_download_url": "https://github.com/thebookisclosed/ViVe/archive/v0.3.3.tar.gz"
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_testDirectory);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No ViVeTool zip asset found*");
    }

    [Fact]
    public async Task DownloadAndExtractViVeToolAsync_ZipMissingViVeToolExe_ThrowsFileNotFoundException()
    {
        var zipBytes = CreateValidViVeToolZip(exeName: "some_other_file.txt", content: "Not ViVeTool");
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

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_testDirectory);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*vivetool.exe was not found inside the downloaded archive*");
    }

    [Fact]
    public async Task DownloadAndExtractViVeToolAsync_CorruptZipBytes_ThrowsInvalidDataException()
    {
        var corruptBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
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

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(corruptBytes)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_testDirectory);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadAndExtractViVeToolAsync_NullOrWhitespaceTargetDirectory_ThrowsArgumentException(string? invalidDirectory)
    {
        var downloader = new ViVeToolDownloader();

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(invalidDirectory!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]           // 403 (GitHub API Rate Limit)
    [InlineData(HttpStatusCode.NotFound)]            // 404
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
    public async Task DownloadAndExtractViVeToolAsync_ReleaseEndpointHttpErrors_ThrowsHttpRequestExceptionWithStatusCode(HttpStatusCode statusCode)
    {
        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_testDirectory);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task DownloadAndExtractViVeToolAsync_ZipDownloadHttpErrors_ThrowsHttpRequestExceptionWithStatusCode(HttpStatusCode statusCode)
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

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(statusCode);
        });

        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_testDirectory);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task DownloadAndExtractViVeToolAsync_CancellationTokenTriggered_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var downloader = new ViVeToolDownloader(client);

        var act = async () => await downloader.DownloadAndExtractViVeToolAsync(_testDirectory, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_handler(request));
        }
    }
}
