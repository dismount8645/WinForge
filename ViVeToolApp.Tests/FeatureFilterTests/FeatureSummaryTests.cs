using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.FeatureFilterTests;

public class FeatureSummaryTests
{
    [Fact]
    public void CalculateSummary_ReturnsAccurateMetrics()
    {
        var service = new FeatureFilterService();
        var all = new List<FeatureItem>
        {
            new() { IsSelected = true, IDs = new long[] { 1, 2 } },
            new() { IsSelected = false, IDs = new long[] { 3 } },
            new() { IsSelected = true, IDs = new long[] { 4 } },
            new() { IsSelected = false, IDs = new long[] { 5 } }
        };
        var visible = all.Take(3).ToList();

        var summary = service.CalculateSummary(visible, all);

        summary.TotalCount.Should().Be(4);
        summary.VisibleCount.Should().Be(3);
        summary.SelectedCount.Should().Be(2);
        summary.CheckedCount.Should().Be(2);
        summary.SelectedPercentage.Should().Be(50.0);
        summary.SelectionPercentage.Should().Be(50.0);
        summary.UniqueSelectedIdsCount.Should().Be(3); // IDs 1, 2, 4
        summary.UniqueSelectedIdCount.Should().Be(3);
        summary.FormattedSummary.Should().Be("Visible 3 of 4  ·  Checked: 2");
    }

    [Fact]
    public void CalculateSummary_EmptyCollection_ReturnsZeroWithoutThrowing()
    {
        var service = new FeatureFilterService();

        var summary = service.CalculateSummary(Enumerable.Empty<FeatureItem>(), Enumerable.Empty<FeatureItem>());

        summary.TotalCount.Should().Be(0);
        summary.VisibleCount.Should().Be(0);
        summary.SelectedCount.Should().Be(0);
        summary.CheckedCount.Should().Be(0);
        summary.SelectedPercentage.Should().Be(0.0);
        summary.SelectionPercentage.Should().Be(0.0);
        summary.FormattedSummary.Should().Be("Visible 0 of 0  ·  Checked: 0");
    }
}
