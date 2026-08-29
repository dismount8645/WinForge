using FluentAssertions;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.StabilityResilienceTests;

public class ViVeToolDownloaderTests
{
    [Fact]
    public void ExtractZipUrlFromReleaseJson_ValidReleaseJson_ReturnsDownloadUrl()
    {
        var downloader = new ViVeToolDownloader();
        var json = """
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

        var url = downloader.ExtractZipUrlFromReleaseJson(json);

        url.Should().Be("https://github.com/thebookisclosed/ViVe/releases/download/v0.3.3/ViVeTool-v0.3.3.zip");
    }

    [Fact]
    public void ExtractZipUrlFromReleaseJson_NoZipAsset_ReturnsNull()
    {
        var downloader = new ViVeToolDownloader();
        var json = """
        {
            "tag_name": "v0.3.3",
            "assets": [
                {
                    "name": "source_code.tar.gz",
                    "browser_download_url": "https://github.com/thebookisclosed/ViVe/archive/v0.3.3.tar.gz"
                }
            ]
        }
        """;

        var url = downloader.ExtractZipUrlFromReleaseJson(json);

        url.Should().BeNull();
    }

    [Fact]
    public void ExtractZipUrlFromReleaseJson_EmptyOrMalformedJson_ReturnsNull()
    {
        var downloader = new ViVeToolDownloader();

        var url1 = downloader.ExtractZipUrlFromReleaseJson(string.Empty);
        var url2 = downloader.ExtractZipUrlFromReleaseJson("{ invalid json }");

        url1.Should().BeNull();
        url2.Should().BeNull();
    }
}
