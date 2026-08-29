using System.Linq;
using FluentAssertions;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.ScraperTests;

public class OfflineCatalogTests
{
    [Fact]
    public void GetFeatures_ReturnsExactlyFifteenItems()
    {
        var items = OfflineCatalog.GetFeatures();
        items.Should().HaveCount(15);
    }

    [Fact]
    public void GetFeatures_ContainsAllFiveExpectedTracks()
    {
        var items = OfflineCatalog.GetFeatures();
        var groups = items.Select(i => i.Group).Distinct().ToList();

        groups.Should().BeEquivalentTo(new[]
        {
            "GA 2026",
            "GA 2025",
            "26H2 Insider",
            "25H2 Insider",
            "Canary / Feature Platforms"
        });
    }

    [Fact]
    public void GetFeatures_AllItemsHaveValidIdsWithinRange()
    {
        var items = OfflineCatalog.GetFeatures();

        foreach (var item in items)
        {
            item.IDs.Should().NotBeEmpty();
            item.IDs.Should().OnlyContain(id => id >= 1_000_000 && id <= 999_999_999);
            item.IDsDisplay.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GetFeatures_AllItemsHaveNonEmptyMetadataFields()
    {
        var items = OfflineCatalog.GetFeatures();

        foreach (var item in items)
        {
            item.Group.Should().NotBeNullOrWhiteSpace();
            item.BuildLabel.Should().NotBeNullOrWhiteSpace();
            item.Description.Should().NotBeNullOrWhiteSpace();
            item.IDsDisplay.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GetFeatures_AllItemsAreSelectedByDefault()
    {
        var items = OfflineCatalog.GetFeatures();
        items.Should().OnlyContain(item => item.IsSelected == true);
    }

    [Fact]
    public void GetFeatures_ReturnsFreshInstancesOnEachCall()
    {
        var firstCall = OfflineCatalog.GetFeatures();
        var secondCall = OfflineCatalog.GetFeatures();

        firstCall.Should().NotBeSameAs(secondCall);
        firstCall[0].Should().NotBeSameAs(secondCall[0]);
    }

    [Fact]
    public void GetFallbackFeatures_AliasReturnsSameCount()
    {
        var items = OfflineCatalog.GetFallbackFeatures();
        items.Should().HaveCount(15);
    }
}
