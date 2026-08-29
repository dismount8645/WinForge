using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.ProcessRunnerTests;

/// <summary>
/// Empirical integration stress tests for SystemProcessLauncher executing real OS processes.
/// Tests timeouts, cancellation, concurrent execution, process tree killing, and error handling.
/// </summary>
public class SystemProcessLauncherStressTests
{
    private readonly SystemProcessLauncher _launcher = new();

    [Fact]
    public async Task RunProcessAsync_StandardCommand_ReturnsExpectedExitCodeAndOutput()
    {
        var (exitCode, output, error) = await _launcher.RunProcessAsync("cmd.exe", "/c echo Hello ProcessLauncher");

        exitCode.Should().Be(0);
        output.Trim().Should().Be("Hello ProcessLauncher");
        error.Should().BeEmpty();
    }

    [Fact]
    public async Task RunProcessAsync_NonZeroExitCodeAndStderr_CapturesBothCorrectly()
    {
        var (exitCode, output, error) = await _launcher.RunProcessAsync("cmd.exe", "/c echo StandardOutMsg & echo ErrorOutMsg 1>&2 & exit 42");

        exitCode.Should().Be(42);
        output.Trim().Should().Be("StandardOutMsg");
        error.Trim().Should().Be("ErrorOutMsg");
    }

    [Fact]
    public async Task RunProcessAsync_TimeoutExceeded_KillsProcessAndThrowsTimeoutException()
    {
        // Ping runs for ~4 seconds; timeout set to 200ms
        var sw = Stopwatch.StartNew();
        var act = async () => await _launcher.RunProcessAsync(
            "cmd.exe",
            "/c ping 127.0.0.1 -n 5 > nul",
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000, "Timeout should trigger promptly and kill the process");
    }

    [Fact]
    public async Task RunProcessAsync_CancellationTokenTriggered_KillsProcessAndThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(250);

        var sw = Stopwatch.StartNew();
        var act = async () => await _launcher.RunProcessAsync(
            "cmd.exe",
            "/c ping 127.0.0.1 -n 6 > nul",
            TimeSpan.FromSeconds(30),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000, "Cancellation should terminate process immediately");
    }

    [Fact]
    public async Task RunProcessAsync_NonExistentExecutable_ThrowsWin32Exception()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid():N}.exe");

        var act = async () => await _launcher.RunProcessAsync(nonExistentPath, "--version");

        await act.Should().ThrowAsync<Win32Exception>();
    }

    [Fact]
    public async Task RunProcessAsync_HighVolumeOutput_CapturesFullStreamWithoutDeadlock()
    {
        // Produce 500 lines of output through cmd.exe
        var (exitCode, output, error) = await _launcher.RunProcessAsync(
            "cmd.exe",
            "/c for /L %i in (1,1,500) do @echo OutputLine_%i",
            TimeSpan.FromSeconds(15));

        exitCode.Should().Be(0);
        output.Should().Contain("OutputLine_1");
        output.Should().Contain("OutputLine_500");
    }

    [Fact]
    public async Task RunProcessAsync_ConcurrentExecution_ExecutesMultipleProcessesWithoutInterference()
    {
        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            var result = await _launcher.RunProcessAsync("cmd.exe", $"/c echo Task_{i} & exit {i}");
            return (Index: i, Result: result);
        });

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(10);
        foreach (var r in results)
        {
            r.Result.ExitCode.Should().Be(r.Index);
            r.Result.Output.Trim().Should().Be($"Task_{r.Index}");
        }
    }
}
