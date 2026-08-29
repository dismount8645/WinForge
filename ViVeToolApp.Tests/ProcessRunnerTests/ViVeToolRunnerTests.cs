using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.ProcessRunnerTests;

public class ViVeToolRunnerTests
{
    private readonly Mock<IProcessLauncher> _mockLauncher;
    private readonly ViVeToolRunner _runner;

    public ViVeToolRunnerTests()
    {
        _mockLauncher = new Mock<IProcessLauncher>();
        _runner = new ViVeToolRunner(_mockLauncher.Object);
    }

    [Theory]
    [InlineData(ViVeExecutionMode.Enable, 12345678, "/enable /id:12345678")]
    [InlineData(ViVeExecutionMode.Disable, 12345678, "/disable /id:12345678")]
    [InlineData(ViVeExecutionMode.Enable, 44470355, "/enable /id:44470355")]
    [InlineData(ViVeExecutionMode.Disable, 61267302, "/disable /id:61267302")]
    public void FormatArguments_GeneratesCorrectCliSyntax(ViVeExecutionMode mode, long featureId, string expected)
    {
        var result = _runner.FormatArguments(mode, featureId);
        result.Should().Be(expected);

        var resultAlias = _runner.BuildArguments(mode, featureId);
        resultAlias.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "Successfully enabled feature (12345678)", "", ViVeToolStatus.Success)]
    [InlineData(0, "", "", ViVeToolStatus.Success)]
    [InlineData(1, "Feature 12345678 not found", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "", "Error: Unknown feature id: 12345678", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(2, "Feature is unsupported on this Windows build.", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "No feature matches ID 12345678", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "FEATURE NOT FOUND", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(5, "", "Access is denied. Run as administrator.", ViVeToolStatus.Error)]
    [InlineData(-1073741515, "", "DLL initialization failed", ViVeToolStatus.Error)]
    [InlineData(255, "General failure occurred", "", ViVeToolStatus.Error)]
    public void ClassifyResult_CorrectlyClassifiesStatuses(int exitCode, string stdout, string stderr, ViVeToolStatus expectedStatus)
    {
        var result = _runner.ClassifyResult(12345678, ViVeExecutionMode.Enable, exitCode, stdout, stderr);
        result.Status.Should().Be(expectedStatus);
        result.ExitCode.Should().Be(exitCode);
        if (exitCode == 0)
        {
            result.IsSuccess.Should().BeTrue();
        }
        else if (expectedStatus == ViVeToolStatus.UnsupportedOrNotFound)
        {
            result.IsSkipped.Should().BeTrue();
        }
        else
        {
            result.IsError.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ExecuteFeatureAsync_WhenWhatIfMode_SkipsProcessLaunchAndReturnsSuccess()
    {
        var result = await _runner.ExecuteFeatureAsync(
            viveToolPath: @"C:\Tools\vivetool.exe",
            featureId: 12345678,
            mode: ViVeExecutionMode.Enable,
            whatIf: true);

        result.Status.Should().Be(ViVeToolStatus.Success);
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("[WHATIF] /enable /id:12345678");
        _mockLauncher.Verify(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteFeatureAsync_WhenWhatIfDisable_SkipsProcessLaunchAndReturnsSuccess()
    {
        var result = await _runner.ExecuteFeatureAsync(
            viveToolPath: @"C:\Tools\vivetool.exe",
            featureId: 87654321,
            mode: ViVeExecutionMode.Disable,
            whatIf: true);

        result.Status.Should().Be(ViVeToolStatus.Success);
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("[WHATIF] /disable /id:87654321");
        _mockLauncher.Verify(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteFeatureAsync_WhenViVeToolPathEmpty_ReturnsErrorResult()
    {
        var result = await _runner.ExecuteFeatureAsync(
            viveToolPath: "",
            featureId: 12345678,
            mode: ViVeExecutionMode.Enable,
            whatIf: false);

        result.Status.Should().Be(ViVeToolStatus.Error);
        result.ErrorMessage.Should().Contain("path is not specified");
    }

    [Fact]
    public async Task ExecuteFeatureAsync_WhenLauncherThrowsException_ReturnsErrorResult()
    {
        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Binary corrupt"));

        var result = await _runner.ExecuteFeatureAsync(
            viveToolPath: @"C:\Tools\vivetool.exe",
            featureId: 12345678,
            mode: ViVeExecutionMode.Enable,
            whatIf: false);

        result.Status.Should().Be(ViVeToolStatus.Error);
        result.ErrorMessage.Should().Be("Binary corrupt");
    }

    [Fact]
    public async Task RunBatchAsync_RealExecution_InvokesLauncherAndAggregatesCounts()
    {
        _mockLauncher
            .Setup(m => m.RunProcessAsync(@"C:\Tools\vivetool.exe", "/enable /id:61754985", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, "Successfully enabled", ""));
        _mockLauncher
            .Setup(m => m.RunProcessAsync(@"C:\Tools\vivetool.exe", "/enable /id:62762248", It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, "Feature not found", ""));

        var featureIds = new long[] { 61754985, 62762248 };
        var reports = new List<ViVeProgressReport>();
        var progress = new Progress<ViVeProgressReport>(reports.Add);

        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress);

        result.TotalProcessed.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.ErrorCount.Should().Be(0);
        result.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunBatchAsync_WhatIfMode_DoesNotInvokeLauncher()
    {
        var featureIds = new long[] { 61754985, 62762248 };
        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: true);

        _mockLauncher.Verify(m => m.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        result.TotalProcessed.Should().Be(2);
        result.SuccessCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task RunBatchAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var featureIds = new long[] { 61754985, 62762248 };

        Func<Task> act = async () => await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: null,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunBatchAsync_FeatureItemOverload_ExtractsOnlySelectedIds()
    {
        _mockLauncher
            .Setup(m => m.RunProcessAsync(@"C:\Tools\vivetool.exe", "/enable /id:1111111", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, "OK", ""));

        var features = new List<FeatureItem>
        {
            new() { IsSelected = true, IDs = new long[] { 1111111 } },
            new() { IsSelected = false, IDs = new long[] { 2222222 } }
        };

        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            features,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(1);
        result.SuccessCount.Should().Be(1);
        _mockLauncher.Verify(m => m.RunProcessAsync(@"C:\Tools\vivetool.exe", "/enable /id:1111111", It.IsAny<CancellationToken>()), Times.Once);
        _mockLauncher.Verify(m => m.RunProcessAsync(@"C:\Tools\vivetool.exe", "/enable /id:2222222", It.IsAny<CancellationToken>()), Times.Never);
    }
}
