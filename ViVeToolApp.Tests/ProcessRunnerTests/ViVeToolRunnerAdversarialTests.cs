using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.ProcessRunnerTests;

/// <summary>
/// Adversarial stress tests for ViVeToolRunner:
/// Chaos exit codes, huge stdout buffers, binary junk, null safety, and concurrency/cancellation.
/// </summary>
public class ViVeToolRunnerAdversarialTests
{
    private readonly Mock<IProcessLauncher> _mockLauncher;
    private readonly ViVeToolRunner _runner;

    public ViVeToolRunnerAdversarialTests()
    {
        _mockLauncher = new Mock<IProcessLauncher>();
        _runner = new ViVeToolRunner(_mockLauncher.Object);
    }

    #region Category 1: Chaos Exit Codes & Output Classification

    [Theory]
    [InlineData(0, "Successfully enabled feature (61754985)", "", ViVeToolStatus.Success)]
    [InlineData(0, "", "", ViVeToolStatus.Success)]
    [InlineData(0, "Feature not found", "", ViVeToolStatus.Success)] // ExitCode 0 is authoritative success
    [InlineData(0, "error occurred in sub-module", "", ViVeToolStatus.Success)]
    [InlineData(1, "Feature 61754985 not found", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "FEATURE NOT FOUND", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "Unknown feature: 61754985", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "Feature is unsupported on this OS", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(1, "No feature with id 61754985 exists", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(2, "", "Unknown feature ID specified", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(2, "Feature unsupported on build 26100", "", ViVeToolStatus.UnsupportedOrNotFound)]
    [InlineData(5, "", "Access is denied. Run as administrator.", ViVeToolStatus.Error)]
    [InlineData(255, "General failure occurred", "", ViVeToolStatus.Error)]
    [InlineData(137, "Killed by SIGKILL", "", ViVeToolStatus.Error)]
    [InlineData(-1, "Process failed to launch", "", ViVeToolStatus.Error)]
    [InlineData(-1073741515, "DLL initialization failed (0xC0000135)", "", ViVeToolStatus.Error)]
    [InlineData(int.MaxValue, "Maximum integer exit code", "", ViVeToolStatus.Error)]
    [InlineData(int.MinValue, "Minimum integer exit code", "", ViVeToolStatus.Error)]
    public void ClassifyResult_ChaosExitCodes_ClassifiesAccurately(
        int exitCode,
        string stdOut,
        string stdErr,
        ViVeToolStatus expectedStatus)
    {
        var result = _runner.ClassifyResult(61754985, ViVeExecutionMode.Enable, exitCode, stdOut, stdErr);

        result.Status.Should().Be(expectedStatus);
        result.ExitCode.Should().Be(exitCode);
        result.FeatureId.Should().Be(61754985);
        result.Mode.Should().Be(ViVeExecutionMode.Enable);

        if (expectedStatus == ViVeToolStatus.Success)
        {
            result.IsSuccess.Should().BeTrue();
            result.IsSkipped.Should().BeFalse();
            result.IsError.Should().BeFalse();
        }
        else if (expectedStatus == ViVeToolStatus.UnsupportedOrNotFound)
        {
            result.IsSuccess.Should().BeFalse();
            result.IsSkipped.Should().BeTrue();
            result.IsError.Should().BeFalse();
        }
        else
        {
            result.IsSuccess.Should().BeFalse();
            result.IsSkipped.Should().BeFalse();
            result.IsError.Should().BeTrue();
        }
    }

    [Fact]
    public void ClassifyStatus_NullOrEmptyCombinedOutput_ReturnsExpectedStatus()
    {
        _runner.ClassifyStatus(0, "").Should().Be(ViVeToolStatus.Success);
        _runner.ClassifyStatus(0, null!).Should().Be(ViVeToolStatus.Success);
        _runner.ClassifyStatus(1, "").Should().Be(ViVeToolStatus.Error);
        _runner.ClassifyStatus(1, null!).Should().Be(ViVeToolStatus.Error);
        _runner.ClassifyStatus(-1, "   ").Should().Be(ViVeToolStatus.Error);
    }

    [Fact]
    public void ClassifyResult_KeywordSplitAcrossStdoutAndStderr_DetectsUnsupported()
    {
        // "not" in stdout, "found" in stderr -> combined is "not found"
        var result = _runner.ClassifyResult(12345, ViVeExecutionMode.Enable, 1, "Feature", "not found");
        result.Status.Should().Be(ViVeToolStatus.UnsupportedOrNotFound);
        result.IsSkipped.Should().BeTrue();
    }

    [Fact]
    public void ClassifyResult_NullStdoutAndStderr_DoesNotThrowNullReferenceException()
    {
        var act1 = () => _runner.ClassifyResult(12345, ViVeExecutionMode.Enable, 0, null!, null!);
        act1.Should().NotThrow();

        var act2 = () => _runner.ClassifyResult(12345, ViVeExecutionMode.Enable, 1, "", null!);
        act2.Should().NotThrow();

        var act3 = () => _runner.ClassifyResult(12345, ViVeExecutionMode.Enable, 1, null!, "error");
        act3.Should().NotThrow();
    }

    #endregion

    #region Category 2: Massive Buffers & Binary Junk

    [Fact]
    public void ClassifyResult_MassiveStdoutBuffer_ProcessesQuicklyWithoutMemoryOrRegexIssues()
    {
        // 5 MB stdout buffer ending with unsupported message
        var sb = new StringBuilder(5 * 1024 * 1024);
        for (int i = 0; i < 50000; i++)
        {
            sb.AppendLine($"[LOG {i:D6}] Initializing ViVeTool internal telemetry and subsystems...");
        }
        sb.AppendLine("Feature 61754985 not found on this system.");
        var massiveStdout = sb.ToString();

        var result = _runner.ClassifyResult(61754985, ViVeExecutionMode.Enable, 1, massiveStdout, "");

        result.Status.Should().Be(ViVeToolStatus.UnsupportedOrNotFound);
        result.Output.Should().NotBeEmpty();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public void ClassifyResult_BinaryJunkAndEscapeSequences_HandlesSafely()
    {
        var binaryJunk = "\0\x01\x02\x1B[31m\x1B[0m\uD83D\uDE00\uFEFF\u200B\r\n\tFeature unknown\0\xFF";
        var result = _runner.ClassifyResult(61754985, ViVeExecutionMode.Enable, 1, binaryJunk, "");

        result.Status.Should().Be(ViVeToolStatus.UnsupportedOrNotFound);
        result.Output.Should().Be(binaryJunk.Trim());
    }

    [Fact]
    public void ClassifyResult_SingleExtremelyLongLineWithoutNewlines_HandlesSafely()
    {
        var longLine = new string('A', 100000) + " unknown feature " + new string('B', 100000);
        var result = _runner.ClassifyResult(61754985, ViVeExecutionMode.Enable, 1, longLine, "");

        result.Status.Should().Be(ViVeToolStatus.UnsupportedOrNotFound);
        result.Output.Length.Should().Be(200017);
    }

    #endregion

    #region Category 3: Concurrency, Batch Execution & Cancellation

    [Fact]
    public async Task RunBatchAsync_PreCanceledToken_ThrowsImmediatelyWithoutLaunchingProcesses()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var featureIds = new long[] { 61754985, 62762248, 63789123 };

        var act = async () => await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: null,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLauncher.Verify(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunBatchAsync_CancellationTokenTriggeredMidBatch_HaltsBatchExecution()
    {
        using var cts = new CancellationTokenSource();
        int executionCount = 0;

        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((path, args, ct) =>
            {
                executionCount++;
                if (executionCount == 2)
                {
                    cts.Cancel();
                }
                return Task.FromResult((0, "Success", ""));
            });

        var featureIds = new long[] { 10000001, 10000002, 10000003, 10000004, 10000005 };

        var act = async () => await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: null,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        executionCount.Should().Be(2);
    }

    [Fact]
    public async Task RunBatchAsync_DuplicateAndOutOfOrderIds_DeduplicatesAndExecutesInAscendingOrder()
    {
        var executedArgs = new List<string>();
        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((path, args, ct) =>
            {
                executedArgs.Add(args);
                return Task.FromResult((0, "Success", ""));
            });

        var featureIds = new long[] { 63000000, 61000000, 62000000, 61000000, 63000000 };

        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(3);
        result.SuccessCount.Should().Be(3);
        executedArgs.Should().Equal(
            "/enable /id:61000000",
            "/enable /id:62000000",
            "/enable /id:63000000");
    }

    [Fact]
    public async Task RunBatchAsync_ProgressReportingAccuracy_ReportsIncrementalStepForEveryItem()
    {
        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), "/enable /id:10000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, "Success 1", ""));
        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), "/enable /id:10000002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, "Feature not found", ""));
        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), "/enable /id:10000003", It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, "", "Access denied"));

        var progressReports = new List<ViVeProgressReport>();
        var progress = new Progress<ViVeProgressReport>(progressReports.Add);

        var featureIds = new long[] { 10000001, 10000002, 10000003 };

        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: progress);

        result.TotalProcessed.Should().Be(3);
        result.SuccessCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.ErrorCount.Should().Be(1);
        result.FormattedSummary.Should().Be("Done — OK:1  Skip:1  Err:1");

        progressReports.Should().HaveCount(3);
        progressReports[0].CurrentIndex.Should().Be(1);
        progressReports[0].TotalCount.Should().Be(3);
        progressReports[0].Percentage.Should().BeApproximately(33.33, 0.1);
        progressReports[0].FormattedMessage.Should().Contain("[SUCCESS]");

        progressReports[1].CurrentIndex.Should().Be(2);
        progressReports[1].TotalCount.Should().Be(3);
        progressReports[1].Percentage.Should().BeApproximately(66.66, 0.1);
        progressReports[1].FormattedMessage.Should().Contain("[SKIP]");

        progressReports[2].CurrentIndex.Should().Be(3);
        progressReports[2].TotalCount.Should().Be(3);
        progressReports[2].Percentage.Should().Be(100.0);
        progressReports[2].FormattedMessage.Should().Contain("[WARN]");
    }

    [Fact]
    public async Task RunBatchAsync_WhatIfMode_FormatsAccurateSummaryWithoutInvokingLauncher()
    {
        var progressReports = new List<ViVeProgressReport>();
        var progress = new Progress<ViVeProgressReport>(progressReports.Add);

        var featureIds = new long[] { 10000001, 10000002 };

        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Disable,
            whatIf: true,
            progress: progress);

        result.TotalProcessed.Should().Be(2);
        result.SuccessCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        result.FormattedSummary.Should().Be("Done — OK:2  Skip:0  Err:0");

        _mockLauncher.Verify(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        progressReports.Should().HaveCount(2);
        progressReports[0].FormattedMessage.Should().Be("[WHATIF] /disable /id:10000001");
        progressReports[1].FormattedMessage.Should().Be("[WHATIF] /disable /id:10000002");
    }

    [Fact]
    public async Task RunBatchAsync_EmptyCollection_ReturnsEmptyBatchResultImmediately()
    {
        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            Enumerable.Empty<long>(),
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        result.Results.Should().BeEmpty();
        result.FormattedSummary.Should().Be("Done — OK:0  Skip:0  Err:0");
    }

    [Fact]
    public async Task RunBatchAsync_FeatureItemsWithNullOrEmptyIds_HandlesWithoutThrowing()
    {
        var features = new List<FeatureItem>
        {
            new() { IsSelected = true, IDs = null! },
            new() { IsSelected = false, IDs = new long[] { 61754985 } },
            new() { IsSelected = true, IDs = new long[] { 0, -1 } }
        };

        var act = async () => await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            features,
            ViVeExecutionMode.Enable,
            whatIf: true);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunBatchAsync_NullFeaturesCollection_HandlesWithoutThrowingAndReturnsEmptyResult()
    {
        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            (IEnumerable<FeatureItem>)null!,
            ViVeExecutionMode.Enable,
            whatIf: true);

        result.TotalProcessed.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task RunBatchAsync_NullFeatureIdsCollection_HandlesWithoutThrowingAndReturnsEmptyResult()
    {
        var result = await _runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            (IEnumerable<long>)null!,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task RunBatchAsync_ProcessLauncherThrowsException_CatchesAndClassifiesAsErrorResult()
    {
        _mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.ComponentModel.Win32Exception("The system cannot find the file specified"));

        var featureIds = new long[] { 61754985, 62762248 };

        var result = await _runner.RunBatchAsync(
            @"C:\NonExistent\vivetool.exe",
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(2);
        result.SuccessCount.Should().Be(0);
        result.ErrorCount.Should().Be(2);
        result.Results.Should().OnlyContain(r => r.Status == ViVeToolStatus.Error);
        result.Results[0].ErrorMessage.Should().Contain("The system cannot find the file specified");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RunBatchAsync_BlankViVeToolPath_ReturnsErrorResultsWithoutInvokingLauncher(string? blankPath)
    {
        var featureIds = new long[] { 61754985 };

        var result = await _runner.RunBatchAsync(
            blankPath!,
            featureIds,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(1);
        result.ErrorCount.Should().Be(1);
        result.Results[0].Status.Should().Be(ViVeToolStatus.Error);
        result.Results[0].ErrorMessage.Should().Contain("ViVeTool executable path is not specified");
        _mockLauncher.Verify(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
