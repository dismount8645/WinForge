using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.StabilityResilienceTests;

/// <summary>
/// Comprehensive resilience and stability test suite for PureinfotechScraper.
/// Covers HTTP error codes (400, 401, 403, 404, 429, 500, 502, 503, 504),
/// network drops, timeouts, cancellation tokens, invalid URLs, empty responses,
/// and fallback catalog integrity.
/// </summary>
public class ScraperResilienceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> synchronousHandler)
        {
            _handler = (req, ct) => Task.FromResult(synchronousHandler(req));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    #region Category 1: HTTP Error Status Codes

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]          // 400
    [InlineData(HttpStatusCode.Unauthorized)]        // 401
    [InlineData(HttpStatusCode.Forbidden)]           // 403
    [InlineData(HttpStatusCode.NotFound)]            // 404
    [InlineData((HttpStatusCode)429)]                // 429 Too Many Requests
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)]          // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]      // 504
    public async Task FetchAndParseAsync_VariousHttpErrorCodes_ThrowsHttpRequestExceptionWithStatusCode(HttpStatusCode statusCode)
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/test");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(statusCode);
    }

    #endregion

    #region Category 2: Network Failures, Disconnections & Timeouts

    [Fact]
    public async Task FetchAndParseAsync_SocketException_ThrowsHttpRequestExceptionWithInnerSocketException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
            throw new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused)));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/test");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.InnerException.Should().BeOfType<SocketException>();
    }

    [Fact]
    public async Task FetchAndParseAsync_NetworkTimeout_ThrowsTaskCanceledExceptionOrTimeout()
    {
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/test");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task FetchAndParseAsync_PreCancelledToken_ThrowsOperationCanceledExceptionImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/test", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchAndParseAsync_CancellationToken_CancelsPromptly()
    {
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            await Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var act = async () => await scraper.FetchAndParseAsync("https://pureinfotech.com/test", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Category 3: Invalid URLs & Payload Edge Cases

    [Theory]
    [InlineData("not_a_valid_url")]
    [InlineData("ftp://pureinfotech.com/codes")]
    [InlineData("file:///C:/test.html")]
    [InlineData("mailto:test@example.com")]
    [InlineData("://missing-scheme")]
    public async Task FetchAndParseAsync_InvalidOrUnsupportedUrlScheme_ThrowsArgumentException(string invalidUrl)
    {
        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var act = async () => await scraper.FetchAndParseAsync(invalidUrl);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FetchAndParseAsync_NullOrEmptyCustomUrl_UsesDefaultUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body></body></html>")
            });
        });
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var result = await scraper.FetchAndParseAsync(null);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be(PureinfotechScraper.DefaultUrl);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAndParseAsync_EmptyHttpResponseBody_ReturnsEmptyListWithoutCrashing()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            }));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var result = await scraper.FetchAndParseAsync("https://pureinfotech.com/test");

        result.Should().BeEmpty();
    }

    #endregion

    #region Category 4: Offline Fallback & Resilient Recovery

    [Fact]
    public void GetOfflineFallback_AlwaysReturnsPopulatedAndValidFeatures()
    {
        var scraper = new PureinfotechScraper();
        var fallback = scraper.GetOfflineFallback();

        fallback.Should().NotBeNull();
        fallback.Should().NotBeEmpty();
        fallback.Count.Should().BeGreaterThanOrEqualTo(10);

        foreach (var item in fallback)
        {
            item.Group.Should().NotBeNullOrWhiteSpace();
            item.BuildLabel.Should().NotBeNullOrWhiteSpace();
            item.Description.Should().NotBeNullOrWhiteSpace();
            item.IDs.Should().NotBeEmpty();
            item.IDsDisplay.Should().NotBeNullOrWhiteSpace();
            item.IsSelected.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Scraper_SimulatedOfflineFallbackWorkflow_RecoversGracefully()
    {
        // Simulate real application failure pattern: attempt live fetch -> fail -> fallback
        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        List<FeatureItem> loadedItems;
        try
        {
            loadedItems = await scraper.FetchAndParseAsync("https://pureinfotech.com/codes");
        }
        catch (HttpRequestException)
        {
            loadedItems = scraper.GetOfflineFallback();
        }

        loadedItems.Should().NotBeEmpty();
        loadedItems.Should().HaveCount(15);
    }

    #endregion

    #region Category 5: Concurrent Scrapes & Memory Stability

    [Fact]
    public async Task FetchAndParseAsync_HighConcurrencySimultaneousRequests_ExecutesCleanly()
    {
        var htmlContent = """
            <div class="entry-content">
                <h3>GA 2026</h3>
                <strong>September 2026</strong>
                <li><code>61754985</code> High concurrency feature test</li>
            </div>
            <!-- CONTENT END -->
            """;

        var handler = new MockHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(htmlContent)
            }));
        using var client = new HttpClient(handler);
        var scraper = new PureinfotechScraper(client);

        var tasks = new Task<List<FeatureItem>>[20];
        for (int i = 0; i < 20; i++)
        {
            tasks[i] = Task.Run(() => scraper.FetchAndParseAsync("https://pureinfotech.com/test"));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(20);
        foreach (var list in results)
        {
            list.Should().HaveCount(1);
            list[0].IDs.Should().Equal(61754985);
        }
    }

    #endregion
}
