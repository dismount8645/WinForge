using FluentAssertions;
using ViVeToolApp.Models;
using Xunit;

namespace ViVeToolApp.Tests.ModelTests;

public class FeatureItemTests
{
    [Fact]
    public void IsSelected_Change_RaisesPropertyChangedNotification()
    {
        var item = new FeatureItem { IsSelected = true };
        var propertyChangedFired = false;
        string? propertyName = null;

        item.PropertyChanged += (_, e) =>
        {
            propertyChangedFired = true;
            propertyName = e.PropertyName;
        };

        item.IsSelected = false;

        propertyChangedFired.Should().BeTrue();
        propertyName.Should().Be(nameof(FeatureItem.IsSelected));
    }

    [Fact]
    public void PropertyAssignments_PersistAccurately()
    {
        var item = new FeatureItem
        {
            Group = "GA 2026",
            BuildLabel = "Build 26300",
            Description = "Test Feature",
            IDsDisplay = "1234567, 7654321",
            IDs = new long[] { 1234567, 7654321 },
            IsSelected = true
        };

        item.Group.Should().Be("GA 2026");
        item.BuildLabel.Should().Be("Build 26300");
        item.Description.Should().Be("Test Feature");
        item.IDsDisplay.Should().Be("1234567, 7654321");
        item.IDs.Should().Equal(1234567, 7654321);
        item.IsSelected.Should().BeTrue();
    }
}
