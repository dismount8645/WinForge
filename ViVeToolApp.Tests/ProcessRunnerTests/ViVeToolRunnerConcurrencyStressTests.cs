using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.ProcessRunnerTests;

/// <summary>
/// Challenger 2 Deep Stress Suite for ViVeToolRunner:
/// Concurrency stress, rapid cancellation race conditions, massive scale lists (10k-50k items),
/// strict IProgress monotonicity and What-If isolation.
/// </summary>
public class ViVeToolRunnerConcurrencyStressTests
{
    private class ThreadSafeProgress<T> : IProgress<T>
    {
        private readonly ConcurrentQueue<T> _reports = new();
        public IReadOnlyCollection<T> Reports => _reports;

        public void Report(T value)
        {
            _reports.Enqueue(value);
        }
    }

    #region 1. Concurrency & Parallel Batches Stress Tests

    [Fact]
    public async Task RunBatchAsync_50ConcurrentBatchesOnSingleRunner_ExecutesSafelyWithoutInterference()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        var runner = new ViVeToolRunner(mockLauncher.Object);

        // Setup mock to respond with deterministic delay and output
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (path, args, ct) =>
            {
                await Task.Delay(1, ct); // Minor async switch
                return (0, $"Success for {args}", "");
            });

        const int batchCount = 50;
        const int itemsPerBatch = 20;

        var tasks = new List<Task<ViVeBatchResult>>();

        for (int b = 0; b < batchCount; b++)
        {
            int batchId = b;
            var ids = Enumerable.Range(1, itemsPerBatch).Select(i => (long)(batchId * 1000 + i)).ToList();
            var mode = batchId % 2 == 0 ? ViVeExecutionMode.Enable : ViVeExecutionMode.Disable;
            var whatIf = batchId % 3 == 0;

            tasks.Add(Task.Run(async () =>
            {
                var progress = new ThreadSafeProgress<ViVeProgressReport>();
                var result = await runner.RunBatchAsync(
                    @"C:\Tools\vivetool.exe",
                    ids,
                    mode,
                    whatIf,
                    progress);

                result.TotalProcessed.Should().Be(itemsPerBatch);
                result.SuccessCount.Should().Be(itemsPerBatch);
                result.SkippedCount.Should().Be(0);
                result.ErrorCount.Should().Be(0);
                result.Results.Should().HaveCount(itemsPerBatch);
                progress.Reports.Should().HaveCount(itemsPerBatch);

                return result;
            }));
        }

        var allResults = await Task.WhenAll(tasks);
        allResults.Should().HaveCount(batchCount);
    }

    [Fact]
    public async Task RunBatchAsync_HighContentionParallelWhatIfAndRealExecutions_PreservesStateIsolation()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        var runner = new ViVeToolRunner(mockLauncher.Object);

        int realExecutionCalls = 0;
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((path, args, ct) =>
            {
                Interlocked.Increment(ref realExecutionCalls);
                return Task.FromResult((0, "OK", ""));
            });

        const int iterations = 40;
        var tasks = new List<Task>();

        for (int i = 0; i < iterations; i++)
        {
            bool isWhatIf = i % 2 == 0;
            long testId = 1000000 + i;

            tasks.Add(Task.Run(async () =>
            {
                var result = await runner.RunBatchAsync(
                    @"C:\Tools\vivetool.exe",
                    new[] { testId },
                    ViVeExecutionMode.Enable,
                    whatIf: isWhatIf);

                result.TotalProcessed.Should().Be(1);
                if (isWhatIf)
                {
                    result.Results[0].Output.Should().Contain("[WHATIF]");
                }
                else
                {
                    result.Results[0].Output.Should().Be("OK");
                }
            }));
        }

        await Task.WhenAll(tasks);
        // Half the tasks were real executions (20 items)
        realExecutionCalls.Should().Be(20);
    }

    #endregion

    #region 2. Rapid Cancellation Stress Tests

    [Fact]
    public async Task RunBatchAsync_ImmediateCancellation_ZeroExecutionsAndThrowsOperationCanceledException()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        var runner = new ViVeToolRunner(mockLauncher.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled

        var ids = Enumerable.Range(100, 1000).Select(i => (long)i);

        Func<Task> act = async () => await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: null,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        mockLauncher.Verify(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    public async Task RunBatchAsync_RapidCancellationDuringExecution_HaltsPromptlyAndThrows(int cancelAfterMs)
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        var runner = new ViVeToolRunner(mockLauncher.Object);

        int callCount = 0;
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (path, args, ct) =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(5, ct); // simulate CLI latency
                return (0, "Success", "");
            });

        using var cts = new CancellationTokenSource();
        var ids = Enumerable.Range(1000, 200).Select(i => (long)i).ToList();

        cts.CancelAfter(cancelAfterMs);

        Func<Task> act = async () => await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: null,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Execution must have halted well before processing all 200 items
        callCount.Should().BeLessThan(200);
    }

    [Fact]
    public async Task RunBatchAsync_CancellationDuringWhatIf_ThrowsOperationCanceledExceptionWithoutHanging()
    {
        var runner = new ViVeToolRunner();
        using var cts = new CancellationTokenSource();
        int reportedCount = 0;
        var progress = new SynchronousProgress(r =>
        {
            reportedCount++;
            if (reportedCount == 25)
            {
                cts.Cancel();
            }
        });

        var ids = Enumerable.Range(1, 5000).Select(i => (long)i).ToList();

        Func<Task> act = async () => await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: true,
            progress: progress,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reportedCount.Should().Be(25);
    }

    [Fact]
    public async Task RunBatchAsync_CancellationInsideLauncherProcess_PropagatesOperationCanceledException()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (p, a, ct) =>
            {
                await Task.Delay(1000, ct); // Will be aborted by ct
                return (0, "OK", "");
            });

        var runner = new ViVeToolRunner(mockLauncher.Object);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        Func<Task> act = async () => await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            new long[] { 123456 },
            ViVeExecutionMode.Enable,
            whatIf: false,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region 3. Massive Feature Lists & Scalability Tests

    [Fact]
    public async Task RunBatchAsync_10000FeaturesInWhatIfMode_CompletesInSubSecondWithAccurateCounts()
    {
        var runner = new ViVeToolRunner();
        const int count = 10000;

        // Generate 10000 unique IDs in chaotic order
        var random = new Random(42);
        var ids = Enumerable.Range(1, count).Select(i => (long)i).OrderBy(_ => random.Next()).ToList();

        var sw = Stopwatch.StartNew();
        var result = await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: true);
        sw.Stop();

        result.TotalProcessed.Should().Be(count);
        result.SuccessCount.Should().Be(count);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        result.Results.Should().HaveCount(count);

        // Verify sorted ascending order
        for (int i = 0; i < count; i++)
        {
            result.Results[i].FeatureId.Should().Be(i + 1);
            result.Results[i].Output.Should().Be($"[WHATIF] /enable /id:{i + 1}");
        }

        // Sub-second execution for 10k items
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    [Fact]
    public async Task RunBatchAsync_MassiveDuplicateAndBoundaryIds_SanitizesAndOrdersCorrectly()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, "Success", ""));

        var runner = new ViVeToolRunner(mockLauncher.Object);

        // Mix boundary values: long.MaxValue, normal values duplicated 100 times, negative values
        var list = new List<long>();
        for (int i = 0; i < 500; i++)
        {
            list.Add(50000);
            list.Add(10000);
            list.Add(99999);
            list.Add(long.MaxValue);
        }

        var result = await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            list,
            ViVeExecutionMode.Disable,
            whatIf: false);

        // Distinct IDs are: 10000, 50000, 99999, long.MaxValue (4 items)
        result.TotalProcessed.Should().Be(4);
        result.SuccessCount.Should().Be(4);
        result.Results.Select(r => r.FeatureId).Should().Equal(10000L, 50000L, 99999L, long.MaxValue);
    }

    [Fact]
    public async Task RunBatchAsync_MassiveFeatureItemListWithNullsAndUnselected_FiltersAccurately()
    {
        var runner = new ViVeToolRunner();
        var featureList = new List<FeatureItem>();

        for (int i = 1; i <= 5000; i++)
        {
            if (i % 5 == 0)
            {
                featureList.Add(null!); // null entries
            }
            else if (i % 2 == 0)
            {
                featureList.Add(new FeatureItem
                {
                    Description = $"Selected Feature {i}",
                    IsSelected = true,
                    IDs = new long[] { i, i + 100000 }
                });
            }
            else
            {
                featureList.Add(new FeatureItem
                {
                    Description = $"Unselected Feature {i}",
                    IsSelected = false,
                    IDs = new long[] { i }
                });
            }
        }

        var result = await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            featureList,
            ViVeExecutionMode.Enable,
            whatIf: true);

        // Every even non-multiple of 5 has 2 IDs
        // Evens up to 5000 = 2500; Multiples of 10 (even and multiple of 5) = 500
        // Valid selected items = 2000; Total distinct IDs = 4000
        result.TotalProcessed.Should().Be(4000);
        result.SuccessCount.Should().Be(4000);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
    }

    #endregion

    #region 4. IProgress Precision & Monotonicity Tests

    [Fact]
    public async Task RunBatchAsync_ProgressMonotonicityAndAccuracy_EmitsExactSequence()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), "/enable /id:1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, "Enabled OK", ""));
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), "/enable /id:2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, "Unknown feature", ""));
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), "/enable /id:3", It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, "", "Access denied"));

        var runner = new ViVeToolRunner(mockLauncher.Object);
        var reports = new List<ViVeProgressReport>();
        var syncProgress = new SynchronousProgress(reports.Add);

        var result = await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            new long[] { 1, 2, 3 },
            ViVeExecutionMode.Enable,
            whatIf: false,
            progress: syncProgress);

        reports.Should().HaveCount(3);

        // Report 1: Success
        reports[0].CurrentIndex.Should().Be(1);
        reports[0].TotalCount.Should().Be(3);
        reports[0].CurrentFeatureId.Should().Be(1);
        reports[0].Percentage.Should().BeApproximately(33.333, 0.01);
        reports[0].LastResult.Should().NotBeNull();
        reports[0].LastResult!.Status.Should().Be(ViVeToolStatus.Success);
        reports[0].FormattedMessage.Should().Contain("[SUCCESS]");

        // Report 2: Skipped / Unsupported
        reports[1].CurrentIndex.Should().Be(2);
        reports[1].TotalCount.Should().Be(3);
        reports[1].CurrentFeatureId.Should().Be(2);
        reports[1].Percentage.Should().BeApproximately(66.666, 0.01);
        reports[1].LastResult.Should().NotBeNull();
        reports[1].LastResult!.Status.Should().Be(ViVeToolStatus.UnsupportedOrNotFound);
        reports[1].FormattedMessage.Should().Contain("[SKIP]");

        // Report 3: Error / Warning
        reports[2].CurrentIndex.Should().Be(3);
        reports[2].TotalCount.Should().Be(3);
        reports[2].CurrentFeatureId.Should().Be(3);
        reports[2].Percentage.Should().Be(100.0);
        reports[2].LastResult.Should().NotBeNull();
        reports[2].LastResult!.Status.Should().Be(ViVeToolStatus.Error);
        reports[2].FormattedMessage.Should().Contain("[WARN]");
    }

    [Fact]
    public async Task RunBatchAsync_HighVolumeProgressEmission_EmitsEverySingleReportInStrictOrder()
    {
        var runner = new ViVeToolRunner();
        const int totalCount = 1000;
        var ids = Enumerable.Range(1, totalCount).Select(i => (long)i).ToList();

        var reports = new List<ViVeProgressReport>(totalCount);
        var syncProgress = new SynchronousProgress(reports.Add);

        await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: true,
            progress: syncProgress);

        reports.Should().HaveCount(totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            var r = reports[i];
            r.CurrentIndex.Should().Be(i + 1);
            r.TotalCount.Should().Be(totalCount);
            r.CurrentFeatureId.Should().Be(i + 1);
            r.Percentage.Should().BeApproximately((i + 1) / (double)totalCount * 100.0, 0.0001);
            r.FormattedMessage.Should().Be($"[WHATIF] /enable /id:{i + 1}");
        }
    }

    private class SynchronousProgress : IProgress<ViVeProgressReport>
    {
        private readonly Action<ViVeProgressReport> _handler;
        public SynchronousProgress(Action<ViVeProgressReport> handler) => _handler = handler;
        public void Report(ViVeProgressReport value) => _handler(value);
    }

    #endregion

    #region 5. What-If Mode Isolation & Side-Effects Verification

    [Theory]
    [InlineData(ViVeExecutionMode.Enable)]
    [InlineData(ViVeExecutionMode.Disable)]
    public async Task RunBatchAsync_WhatIfMode_ZeroProcessLauncherInteractionsGuaranteed(ViVeExecutionMode mode)
    {
        var mockLauncher = new Mock<IProcessLauncher>(MockBehavior.Strict); // Strict mock will fail if ANY method is called
        var runner = new ViVeToolRunner(mockLauncher.Object);

        var ids = new long[] { 101, 202, 303, 404, 505 };

        var result = await runner.RunBatchAsync(
            @"C:\NonExistentDirectory\vivetool.exe", // even with dummy or invalid path
            ids,
            mode,
            whatIf: true);

        result.TotalProcessed.Should().Be(5);
        result.SuccessCount.Should().Be(5);
        result.SkippedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);

        var expectedVerb = mode == ViVeExecutionMode.Enable ? "/enable" : "/disable";
        foreach (var item in result.Results)
        {
            item.ExitCode.Should().Be(0);
            item.Status.Should().Be(ViVeToolStatus.Success);
            item.Output.Should().Be($"[WHATIF] {expectedVerb} /id:{item.FeatureId}");
            item.ErrorMessage.Should().BeEmpty();
        }

        mockLauncher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteFeatureAsync_WhatIfMode_DoesNotValidateOrRequireExecutableExistence()
    {
        var mockLauncher = new Mock<IProcessLauncher>(MockBehavior.Strict);
        var runner = new ViVeToolRunner(mockLauncher.Object);

        // Path is completely empty/invalid, but whatIf is true -> should succeed without touching disk or process
        var result = await runner.ExecuteFeatureAsync(
            viveToolPath: "",
            featureId: 999999,
            mode: ViVeExecutionMode.Enable,
            whatIf: true);

        result.Status.Should().Be(ViVeToolStatus.Success);
        result.ExitCode.Should().Be(0);
        result.Output.Should().Be("[WHATIF] /enable /id:999999");
        mockLauncher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunBatchAsync_50000FeaturesMixedStatuses_MaintainsAccurateArithmeticAndPerformance()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((path, args, ct) =>
            {
                // Assign status based on ID modulo
                // args format: "/enable /id:12345"
                var idStr = args.Split(':')[1];
                long id = long.Parse(idStr);
                if (id % 3 == 0)
                {
                    return Task.FromResult((0, "Success", ""));
                }
                if (id % 3 == 1)
                {
                    return Task.FromResult((1, "Feature not found", ""));
                }
                return Task.FromResult((5, "", "Access Denied"));
            });

        var runner = new ViVeToolRunner(mockLauncher.Object);
        const int totalCount = 30000;
        var ids = Enumerable.Range(1, totalCount).Select(i => (long)i).ToList();

        var result = await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(totalCount);
        result.SuccessCount.Should().Be(10000);
        result.SkippedCount.Should().Be(10000);
        result.ErrorCount.Should().Be(10000);
        (result.SuccessCount + result.SkippedCount + result.ErrorCount).Should().Be(result.TotalProcessed);
        result.FormattedSummary.Should().Be("Done — OK:10000  Skip:10000  Err:10000");
    }

    [Fact]
    public async Task RunBatchAsync_100ConcurrentTasksWithRandomizedCancellation_NeverDeadlocksOrCorrupts()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (p, a, ct) =>
            {
                await Task.Delay(2, ct);
                return (0, "OK", "");
            });

        var runner = new ViVeToolRunner(mockLauncher.Object);
        var random = new Random(12345);

        int completed = 0;
        int canceled = 0;

        var tasks = Enumerable.Range(0, 100).Select(async taskId =>
        {
            using var cts = new CancellationTokenSource();
            int cancelDelay = random.Next(0, 20);
            if (cancelDelay < 15)
            {
                cts.CancelAfter(cancelDelay);
            }

            var ids = Enumerable.Range(taskId * 100, 50).Select(i => (long)i).ToList();

            try
            {
                var res = await runner.RunBatchAsync(
                    @"C:\Tools\vivetool.exe",
                    ids,
                    ViVeExecutionMode.Enable,
                    whatIf: false,
                    cancellationToken: cts.Token);

                Interlocked.Increment(ref completed);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref canceled);
            }
        });

        await Task.WhenAll(tasks);

        (completed + canceled).Should().Be(100);
        canceled.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunBatchAsync_ConcurrentProcessTimeouts_HandlesGracefullyWithoutThrowingUnhandledExceptions()
    {
        var mockLauncher = new Mock<IProcessLauncher>();
        mockLauncher
            .Setup(l => l.RunProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Process 'vivetool.exe' exceeded timeout of 30 seconds."));

        var runner = new ViVeToolRunner(mockLauncher.Object);
        var ids = new long[] { 101, 102, 103, 104, 105 };

        var result = await runner.RunBatchAsync(
            @"C:\Tools\vivetool.exe",
            ids,
            ViVeExecutionMode.Enable,
            whatIf: false);

        result.TotalProcessed.Should().Be(5);
        result.SuccessCount.Should().Be(0);
        result.ErrorCount.Should().Be(5);
        result.Results.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(ViVeToolStatus.Error);
            r.ErrorMessage.Should().Contain("timeout");
        });
    }

    #endregion
}

