using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.FeatureFilterTests;

public class FeatureFilterServiceTests
{
    private readonly List<FeatureItem> _testFeatures = new()
    {
        new FeatureItem { Group = "GA 2026", BuildLabel = "Build 26300", Description = "Start menu resize", IDsDisplay = "61754985", IDs = new long[] { 61754985 }, IsSelected = true },
        new FeatureItem { Group = "GA 2026", BuildLabel = "Build 26300", Description = "Windows Search settings", IDsDisplay = "62762248", IDs = new long[] { 62762248 }, IsSelected = false },
        new FeatureItem { Group = "26H2 Insider", BuildLabel = "Build 26300.8697", Description = "Search web toggle", IDsDisplay = "61267302, 61344081", IDs = new long[] { 61267302, 61344081 }, IsSelected = true },
        new FeatureItem { Group = "Canary / Feature Platforms", BuildLabel = "Build 29648", Description = "Unified memory for games", IDsDisplay = "61121285", IDs = new long[] { 61121285 }, IsSelected = true },
    };

    [Fact]
    public void Filter_ByDescription_MatchesCaseInsensitive()
    {
        var service = new FeatureFilterService();

        var results = service.Filter(_testFeatures, "search", "All Tracks").ToList();

        results.Should().HaveCount(2);
        results.Should().Contain(x => x.Description == "Windows Search settings");
        results.Should().Contain(x => x.Description == "Search web toggle");
    }

    [Fact]
    public void Filter_ByFeatureId_MatchesSubstring()
    {
        var service = new FeatureFilterService();

        var results = service.Filter(_testFeatures, "61344081", "All Tracks").ToList();

        results.Should().ContainSingle().Which.Description.Should().Be("Search web toggle");
    }

    [Fact]
    public void Filter_ByGroupTrack_FiltersToSpecificTrack()
    {
        var service = new FeatureFilterService();

        var results = service.Filter(_testFeatures, "", "GA 2026").ToList();

        results.Should().HaveCount(2);
        results.All(x => x.Group == "GA 2026").Should().BeTrue();
    }

    [Fact]
    public void Filter_CombinedSearchAndGroup_AppliesConjunction()
    {
        var service = new FeatureFilterService();

        var results = service.Filter(_testFeatures, "Search", "GA 2026").ToList();

        results.Should().ContainSingle().Which.Description.Should().Be("Windows Search settings");
    }

    [Fact]
    public void GetDistinctSelectedFeatureIds_ReturnsSortedDistinctIds()
    {
        var service = new FeatureFilterService();

        var selectedIds = service.GetDistinctSelectedFeatureIds(_testFeatures);

        // GA 2026 item 1 (61754985), 26H2 (61267302, 61344081), Canary (61121285)
        selectedIds.Should().Equal(61121285, 61267302, 61344081, 61754985);
    }

    [Fact]
    public void GetDistinctGroups_ReturnsSortedUniqueGroups()
    {
        var service = new FeatureFilterService();

        var groups = service.GetDistinctGroups(_testFeatures);

        groups.Should().Equal("26H2 Insider", "Canary / Feature Platforms", "GA 2026");
    }

    [Fact]
    public void SetSelection_SetsAllFeaturesToTargetState()
    {
        var service = new FeatureFilterService();

        service.SetSelection(_testFeatures, false);
        _testFeatures.Should().OnlyContain(f => f.IsSelected == false);

        service.SetSelection(_testFeatures, true);
        _testFeatures.Should().OnlyContain(f => f.IsSelected == true);
    }

    [Fact]
    public void SetGroupSelection_SetsOnlyMatchingGroupFeatures()
    {
        var service = new FeatureFilterService();
        service.SetSelection(_testFeatures, false);

        service.SetGroupSelection(_testFeatures, "GA 2026", true);

        _testFeatures.Where(f => f.Group == "GA 2026").Should().OnlyContain(f => f.IsSelected == true);
        _testFeatures.Where(f => f.Group != "GA 2026").Should().OnlyContain(f => f.IsSelected == false);
    }
}
