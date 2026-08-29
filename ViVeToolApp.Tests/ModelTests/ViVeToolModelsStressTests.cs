using System;
using System.Collections.Generic;
using System.ComponentModel;
using FluentAssertions;
using ViVeToolApp.Models;
using Xunit;

namespace ViVeToolApp.Tests.ModelTests;

/// <summary>
/// Stress and edge case tests for ViVeTool models: ViVeToolResult, ViVeBatchResult, ViVeProgressReport, FeatureItem.
/// </summary>
public class ViVeToolModelsStressTests
{
    [Fact]
    public void ViVeToolResult_PropertiesAndMessageAliases_WorkCorrectly()
    {
        var res = new ViVeToolResult();
        res.FeatureId = 12345678;
        res.Mode = ViVeExecutionMode.Disable;
        res.Status = ViVeToolStatus.Error;
        res.ExitCode = 5;
        res.Message = "Access Denied";

        res.ErrorMessage.Should().Be("Access Denied");
        res.Message.Should().Be("Access Denied");
        res.RawOutput.Should().Be("Access Denied");
        res.IsError.Should().BeTrue();
        res.IsSuccess.Should().BeFalse();
        res.IsSkipped.Should().BeFalse();

        res.Status = ViVeToolStatus.Success;
        res.Message = "Enabled successfully";
        res.Output.Should().Be("Enabled successfully");
        res.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ViVeBatchResult_SummaryFormatting_CalculatesAccurateStrings()
    {
        var batch = new ViVeBatchResult
        {
            TotalProcessed = 10,
            SuccessCount = 7,
            SkippedCount = 2,
            ErrorCount = 1
        };

        batch.SkipCount.Should().Be(2);
        batch.FormattedSummary.Should().Be("Done — OK:7  Skip:2  Err:1");
        batch.SummaryMessage.Should().Be("Done — OK:7  Skip:2  Err:1");

        batch.SkipCount = 3;
        batch.SkippedCount.Should().Be(3);
    }

    [Fact]
    public void ViVeProgressReport_DefaultsAndAliases_WorkCorrectly()
    {
        var report = new ViVeProgressReport();
        report.LogMessage = "Processing item 1 of 5";
        report.FormattedMessage.Should().Be("Processing item 1 of 5");
        report.LogMessage.Should().Be("Processing item 1 of 5");
    }

    [Fact]
    public void FeatureItem_PropertyChanged_FiresOnSelectionChange()
    {
        var item = new FeatureItem { IsSelected = true };
        var changedProps = new List<string?>();
        item.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName);

        item.IsSelected = false;
        item.IsSelected = false; // no change, should not fire again
        item.IsSelected = true;

        changedProps.Should().Equal("IsSelected", "IsSelected");
    }
}
