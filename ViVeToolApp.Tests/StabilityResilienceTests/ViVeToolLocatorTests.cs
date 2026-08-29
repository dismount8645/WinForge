using System;
using System.IO;
using FluentAssertions;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.StabilityResilienceTests;

/// <summary>
/// Unit and edge case tests for ViVeToolLocator probing order, custom directories, and PATH parsing.
/// </summary>
public class ViVeToolLocatorTests : IDisposable
{
    private readonly ViVeToolLocator _locator = new();
    private readonly string _tempDirectory;

    public ViVeToolLocatorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ViVeToolLocatorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Fact]
    public void LocateViVeTool_DirectValidCustomPath_ReturnsCustomPath()
    {
        var dummyExe = Path.Combine(_tempDirectory, "my_vivetool.exe");
        File.WriteAllText(dummyExe, "dummy");

        var result = _locator.LocateViVeTool(customPath: dummyExe);

        result.Should().Be(dummyExe);
    }

    [Fact]
    public void LocateViVeTool_NonExistentCustomPath_FallsBack()
    {
        var nonExistentPath = Path.Combine(_tempDirectory, "non_existent.exe");

        var result = _locator.LocateViVeTool(
            customBaseDirectory: _tempDirectory,
            customPath: nonExistentPath,
            pathEnvironment: string.Empty);

        result.Should().BeNull();
    }

    [Fact]
    public void LocateViVeTool_CustomBaseDirectoryWithViVeTool_ReturnsBaseDirectoryPath()
    {
        var localExe = Path.Combine(_tempDirectory, "vivetool.exe");
        File.WriteAllText(localExe, "dummy");

        var result = _locator.LocateViVeTool(
            customBaseDirectory: _tempDirectory,
            pathEnvironment: string.Empty);

        result.Should().Be(localExe);
    }

    [Fact]
    public void LocateViVeTool_PathEnvironmentWithMultipleEntries_FindsBinaryInValidDirectory()
    {
        var dir1 = Path.Combine(_tempDirectory, "dir1");
        var dir2 = Path.Combine(_tempDirectory, "dir2");
        var dir3 = Path.Combine(_tempDirectory, "dir3");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        Directory.CreateDirectory(dir3);

        var targetExe = Path.Combine(dir2, "vivetool.exe");
        File.WriteAllText(targetExe, "dummy");

        var fakePathEnv = $"C:\\NonExistentPath;{dir1};;;{dir2};{dir3};  ;\"invalid|path\"";

        var result = _locator.LocateViVeTool(
            customBaseDirectory: Path.Combine(_tempDirectory, "emptyBase"),
            pathEnvironment: fakePathEnv);

        result.Should().Be(targetExe);
    }

    [Fact]
    public void LocateViVeTool_NoMatchingLocations_ReturnsNull()
    {
        var emptyBase = Path.Combine(_tempDirectory, "emptyBase");
        Directory.CreateDirectory(emptyBase);

        var result = _locator.LocateViVeTool(
            customBaseDirectory: emptyBase,
            customPath: null,
            pathEnvironment: "C:\\NonExistent1;C:\\NonExistent2");

        result.Should().BeNull();
    }

    [Fact]
    public void LocateViVeTool_NullAndEmptyInputs_DoesNotThrow()
    {
        var act = () => _locator.LocateViVeTool(null, null, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void LocateViVeTool_BaseDirectoryTakesPrecedenceOverPath()
    {
        var baseDir = Path.Combine(_tempDirectory, "baseDir");
        var pathDir = Path.Combine(_tempDirectory, "pathDir");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(pathDir);

        var baseExe = Path.Combine(baseDir, "vivetool.exe");
        var pathExe = Path.Combine(pathDir, "vivetool.exe");
        File.WriteAllText(baseExe, "base");
        File.WriteAllText(pathExe, "path");

        var result = _locator.LocateViVeTool(customBaseDirectory: baseDir, pathEnvironment: pathDir);

        result.Should().Be(baseExe);
    }
}
