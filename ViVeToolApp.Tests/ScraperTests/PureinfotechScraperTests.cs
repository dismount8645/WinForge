using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.ScraperTests;

public class PureinfotechScraperTests
{
    private readonly PureinfotechScraper _scraper = new();

    private const string SampleFullHtml = """
        <!DOCTYPE html>
        <html>
        <head><title>ViVeTool GUI Test Page</title></head>
        <body>
        <div class="entry-content">
            <h3>Windows 11 2026 Update features</h3>
            <p><strong>September 2026:</strong></p>
            <ul>
                <li><code>61754985</code> Start menu resize and customization</li>
                <li><code>62762248</code> Windows Search settings</li>
                <li><code>27829265, 61457898</code> Pointer Indicator enhancement</li>
            </ul>

            <h4>Windows 11 2025 Update features</h4>
            <p><strong>December 2025</strong></p>
            <ul>
                <li><code>59162732, 55994763</code> Widgets redesign</li>
            </ul>

            <h3>Windows 11 version 26H2 (Dev / Beta)</h3>
            <p><strong>Build 26300.8697:</strong></p>
            <ul>
                <li><code>61267302, 61344081, 61482515</code> Search web toggle</li>
            </ul>

            <h3>Windows 11 version 25H2</h3>
            <p><strong>Build 26220.7271:</strong></p>
            <ul>
                <li><code>59765208</code> Xbox Full Screen Experience</li>
            </ul>

            <h3>Canary Channel and Feature Platforms</h3>
            <p><strong>Build 29648:</strong></p>
            <ul>
                <li><code>61121285</code> Unified memory for games</li>
            </ul>
        </div>
        <!-- CONTENT END -->
        </body>
        </html>
        """;

    [Fact]
    public void ParseHtml_ValidFullHtmlFragment_ExtractsAllSectionsAndFeatures()
    {
        var features = _scraper.ParseHtml(SampleFullHtml);

        features.Should().HaveCount(7);

        // Feature 1: GA 2026
        features[0].Group.Should().Be("GA 2026");
        features[0].BuildLabel.Should().Be("September 2026");
        features[0].Description.Should().Be("Start menu resize and customization");
        features[0].IDs.Should().Equal(61754985);
        features[0].IDsDisplay.Should().Be("61754985");
        features[0].IsSelected.Should().BeTrue();

        // Feature 3: Multi-ID GA 2026
        features[2].Group.Should().Be("GA 2026");
        features[2].Description.Should().Be("Pointer Indicator enhancement");
        features[2].IDs.Should().Equal(27829265, 61457898);
        features[2].IDsDisplay.Should().Be("27829265, 61457898");

        // Feature 4: GA 2025
        features[3].Group.Should().Be("GA 2025");
        features[3].BuildLabel.Should().Be("December 2025");
        features[3].Description.Should().Be("Widgets redesign");
        features[3].IDs.Should().Equal(59162732, 55994763);

        // Feature 5: 26H2 Insider
        features[4].Group.Should().Be("26H2 Insider");
        features[4].BuildLabel.Should().Be("Build 26300.8697");
        features[4].IDs.Should().Equal(61267302, 61344081, 61482515);

        // Feature 6: 25H2 Insider
        features[5].Group.Should().Be("25H2 Insider");
        features[5].BuildLabel.Should().Be("Build 26220.7271");
        features[5].IDs.Should().Equal(59765208);

        // Feature 7: Canary
        features[6].Group.Should().Be("Canary / Feature Platforms");
        features[6].BuildLabel.Should().Be("Build 29648");
        features[6].IDs.Should().Equal(61121285);
    }

    [Theory]
    [InlineData("Windows 11 2026 Update features", "GA 2026")]
    [InlineData("Windows 11 2025 Update features", "GA 2025")]
    [InlineData("Windows 11 version 26H2 (Dev / Beta)", "26H2 Insider")]
    [InlineData("Windows 11 version 25H2 features", "25H2 Insider")]
    [InlineData("Canary Channel builds", "Canary / Feature Platforms")]
    [InlineData("Feature Platforms 2026", "Canary / Feature Platforms")]
    [InlineData("Windows 11 26H1 Preview", "Canary / Feature Platforms")]
    [InlineData("Custom Track Header", "Custom Track Header")]
    [InlineData("", "General")]
    public void MapGroup_MapsHeadingsCorrectly(string heading, string expectedGroup)
    {
        PureinfotechScraper.MapGroup(heading).Should().Be(expectedGroup);
    }

    [Theory]
    [InlineData("61267302, 61344081, 61482515", new long[] { 61267302, 61344081, 61482515 })]
    [InlineData("61267302 61344081 61482515", new long[] { 61267302, 61344081, 61482515 })]
    [InlineData("vivetool /enable /id:61267302,61344081", new long[] { 61267302, 61344081 })]
    [InlineData("61267302 or 61344081", new long[] { 61267302, 61344081 })]
    [InlineData("61267302 ,  61344081  , 61482515", new long[] { 61267302, 61344081, 61482515 })]
    public void ParseIds_MultiIdCodes_ExtractsAllValidDistinctIds(string raw, long[] expectedIds)
    {
        var ids = PureinfotechScraper.ParseIds(raw);
        ids.Should().Equal(expectedIds);
    }

    [Fact]
    public void ParseIds_FiltersOutOfRangeAndInvalidIds()
    {
        var raw = "123, 999999, 1000000, 59213768, 999999999, 1000000000, abcdef";
        var ids = PureinfotechScraper.ParseIds(raw);

        ids.Should().Equal(1000000, 59213768, 999999999);
    }

    [Fact]
    public void ParseIds_DeduplicatesDuplicateIds()
    {
        var raw = "59213768, 59213768, 60813048, 59213768, 60813048";
        var ids = PureinfotechScraper.ParseIds(raw);

        ids.Should().Equal(59213768, 60813048);
    }

    [Fact]
    public void ParseHtml_MissingDescription_FallsBackToDefault()
    {
        var html = "<li><code>59213768</code></li>";
        var features = _scraper.ParseHtml(html);

        features.Should().HaveCount(1);
        features[0].Description.Should().Be("(No description)");
        features[0].IDs.Should().Equal(59213768);
    }

    [Fact]
    public void ParseHtml_PunctuationOnlyDescription_FallsBackToDefault()
    {
        var html = "<li><code>59213768</code> : - . </li>";
        var features = _scraper.ParseHtml(html);

        features.Should().HaveCount(1);
        features[0].Description.Should().Be("(No description)");
    }

    [Fact]
    public void ParseHtml_MalformedHtml_HandlesGracefullyWithoutThrowing()
    {
        var malformedHtml = """
            <div>
               <h3>GA 2026
               <li><code>59213768</code> Unclosed list item test
               <p><strong>Build 26300.1234
               <li><code>60813048</code> Second feature with <broken <tag>>
            </div>
            """;

        var features = _scraper.ParseHtml(malformedHtml);

        features.Should().HaveCount(2);
        features[0].Group.Should().Be("GA 2026");
        features[0].Description.Should().Be("Unclosed list item test");
        features[1].BuildLabel.Should().Be("Build 26300.1234");
        features[1].Description.Should().Contain("Second feature");
    }

    [Fact]
    public void ParseHtml_HtmlEntities_DecodesEntitiesCorrectly()
    {
        var html = "<li><code>59213768</code> What&#8217;s new &amp; &quot;cool&quot; in Windows 11</li>";
        var features = _scraper.ParseHtml(html);

        features.Should().HaveCount(1);
        features[0].Description.Should().Be("What’s new & \"cool\" in Windows 11");
    }

    [Fact]
    public void ParseHtml_NullOrEmptyOrWhitespace_ReturnsEmptyList()
    {
        _scraper.ParseHtml("").Should().BeEmpty();
        _scraper.ParseHtml("   ").Should().BeEmpty();
        _scraper.ParseHtml(null!).Should().BeEmpty();
    }

    [Fact]
    public void ParseHtml_MissingEndMarker_ParsesUntilEndOfString()
    {
        var html = """
            <div class="entry-content">
               <h3>GA 2026</h3>
               <li><code>59213768</code> Feature without end marker</li>
            """;

        var features = _scraper.ParseHtml(html);

        features.Should().HaveCount(1);
        features[0].Description.Should().Be("Feature without end marker");
    }

    [Fact]
    public void GetOfflineFallback_ReturnsOfflineCatalogFeatures()
    {
        var features = _scraper.GetOfflineFallback();

        features.Should().HaveCount(15);
        features.Should().OnlyContain(f => f.IDs.Length > 0);
    }

    [Fact]
    public async Task FetchAndParseAsync_WithCustomHttpMessageHandler_ReturnsParsedFeatures()
    {
        var handler = new MockHttpMessageHandler(SampleFullHtml, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var scraper = new PureinfotechScraper(httpClient);

        var features = await scraper.FetchAndParseAsync("https://custom-test.url");

        features.Should().HaveCount(7);
        features[0].IDs.Should().Equal(61754985);
    }

    [Fact]
    public async Task FetchAndParseAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var handler = new MockHttpMessageHandler(SampleFullHtml, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var scraper = new PureinfotechScraper(httpClient);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await scraper.FetchAndParseAsync("https://custom-test.url", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(response);
        }
    }
}
