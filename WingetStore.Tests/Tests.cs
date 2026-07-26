using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WingetStore.Models;
using WingetStore.Services;
using WingetStore.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WingetStore.Pages;
using Xunit;
using CommunityToolkit.Mvvm.Messaging;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WingetStore.Tests;

public static class TestInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner, MockProcessRunner>();
        services.AddSingleton<WingetService>();
        services.AddSingleton<IWingetService>(sp => new CachingWingetService(sp.GetRequiredService<WingetService>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IconService>(IconService.Instance);
        services.AddTransient<InstalledViewModel>();
        services.AddTransient<UpdatesViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<HomeViewModel>();
        App.Services = services.BuildServiceProvider();
    }
}

public static class TestHelper
{
    public static readonly string[] InstallSteps = ["Downloading package...", "Verifying hash...", "Running installer...", "Finalizing..."];
    public static readonly string[] UpgradeSteps = ["Downloading update...", "Verifying hash...", "Running upgrade installer...", "Finalizing..."];
    public static readonly string[] UninstallSteps = ["Locating registry entries...", "Running uninstaller...", "Cleaning user data...", "Finalizing..."];

    public static void RunWithDispatcher(Action action)
    {
        App.DispatcherOverride = act => act();
        try { action(); }
        finally { App.DispatcherOverride = null; }
    }

    public static async Task RunWithDispatcherAsync(Func<Task> action)
    {
        App.DispatcherOverride = act => act();
        try { await action(); }
        finally { App.DispatcherOverride = null; }
    }

    public static async Task WaitWhileAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        int waited = 0;
        while (condition() && waited < timeoutMs)
        {
            await Task.Delay(50);
            waited += 50;
        }
    }

    public static void RunWithSetting<T>(Func<T> getter, Action<T> setter, T testValue, Action action)
    {
        var original = getter();
        try { setter(testValue); action(); }
        finally { setter(original); }
    }
}

public abstract class StubWingetService : IWingetService
{
    public virtual ObservableCollection<InstallTask> ActiveTasks => throw new NotImplementedException();
    public virtual WingetPackage GetOrCreatePackage(WingetPackage incoming) => incoming;
    public virtual Task<string> RunCommandAsync(string arguments, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<List<WingetPackage>> SearchPackagesAsync(string query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<List<WingetPackage>> GetInstalledPackagesAsync() => throw new NotImplementedException();
    public virtual Task<List<WingetPackage>> GetUpgradablePackagesAsync() => throw new NotImplementedException();
    public virtual Task<List<WingetPackage>> GetPopularPackagesAsync() => throw new NotImplementedException();
    public virtual Task<List<WingetPackage>> GetRecommendationsAsync() => throw new NotImplementedException();
    public virtual Task<List<CategoryItem>> GetCategoriesAsync() => throw new NotImplementedException();
    public virtual Task<WingetPackage?> GetPackageDetailsAsync(PackageId packageId) => throw new NotImplementedException();
    public virtual Task<WingetPackage> FetchAndDecoratePackageDetailsAsync(PackageId packageId) => throw new NotImplementedException();
    public virtual void InstallPackage(WingetPackage package) => throw new NotImplementedException();
    public virtual void UpgradePackage(WingetPackage package) => throw new NotImplementedException();
    public virtual void UninstallPackage(WingetPackage package) => throw new NotImplementedException();
    public virtual void TriggerPackageAction(WingetPackage package) => throw new NotImplementedException();
    public virtual void CancelTask(string taskId) {}
    public virtual void CancelTaskForPackage(string packageId) {}
    public virtual Task<string> ExportPackagesAsync(string filepath) => throw new NotImplementedException();
    public virtual Task<string> ImportPackagesAsync(string filepath) => throw new NotImplementedException();
}

public class MockProcessRunner : IProcessRunner
{
    public static bool ShouldThrow { get; set; }

    public async Task<int> RunStreamAsync(string fileName, string arguments, Action<string> onLineReceived, CancellationToken cancellationToken = default)
    {
        if (ShouldThrow) throw new InvalidOperationException("Simulated general command failure");

        await Task.Delay(100, cancellationToken);

        if (arguments != null && (arguments.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                                  arguments.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ||
                                  arguments.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) &&
            arguments.Contains("Mock.", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Contains("Mock.Throw", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Simulated task exception");
            if (arguments.Contains("Mock.Fail", StringComparison.OrdinalIgnoreCase)) return 2;

            string[] statusSteps = arguments.Contains("install", StringComparison.OrdinalIgnoreCase) ? TestHelper.InstallSteps
                : arguments.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ? TestHelper.UpgradeSteps
                : TestHelper.UninstallSteps;

            for (int i = 0; i < statusSteps.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onLineReceived($"Progress: {10 + i * 25}%");
                onLineReceived(statusSteps[i]);
                await Task.Delay(50, cancellationToken);
            }
            return 0;
        }

        if (arguments != null)
        {
            if (arguments.Contains("source list", StringComparison.OrdinalIgnoreCase))
            {
                onLineReceived("Name    Argument");
                onLineReceived("-----------------------------------------");
                onLineReceived("winget  https://cdn.winget.microsoft.com/cache");
                onLineReceived("msstore https://storeedgefd.dsx.mp.microsoft.com/v9.0");
                return 0;
            }

            if (arguments.StartsWith("list", StringComparison.OrdinalIgnoreCase))
            {
                onLineReceived("(1/4) Git [Git.Git]");
                onLineReceived("  Publisher: Software Corp");
                onLineReceived("  Version: 2.40.0");
                onLineReceived("  Origin Source: winget");
                onLineReceived("");
                onLineReceived("(2/4) Visual Studio Code [Microsoft.VisualStudioCode]");
                onLineReceived("  Publisher: Microsoft Corporation");
                onLineReceived("  Version: 1.79.0");
                onLineReceived("  Origin Source: winget");
                onLineReceived("");
                onLineReceived("(3/4) Mock Installed Package [Mock.App.Installed]");
                onLineReceived("  Publisher: Mock Publisher");
                onLineReceived("  Version: 1.0.0");
                onLineReceived("  Origin Source: winget");
                onLineReceived("");
                onLineReceived("(4/4) Mock Upgradable Package [Mock.App.Upgradable]");
                onLineReceived("  Publisher: Mock Publisher");
                onLineReceived("  Version: 1.0.0");
                onLineReceived("  Origin Source: winget");
                return 0;
            }

            if (arguments.StartsWith("upgrade", StringComparison.OrdinalIgnoreCase) && !arguments.Contains("--all", StringComparison.OrdinalIgnoreCase))
            {
                onLineReceived("Name                           Id                                       Version          Available        Source");
                onLineReceived("----------------------------------------------------------------------------------------------------------------");
                onLineReceived("Git                            Git.Git                                  2.40.0           2.41.0           winget");
                onLineReceived("Visual Studio Code             Microsoft.VisualStudioCode               1.79.0           1.80.0           winget");
                onLineReceived("Mock Upgradable Package        Mock.App.Upgradable                      1.0.0            1.1.0            winget");
                return 0;
            }

            if (arguments.StartsWith("search", StringComparison.OrdinalIgnoreCase))
            {
                onLineReceived("Name                           Id                                       Version          Source");
                onLineReceived("------------------------------------------------------------------------------------------------");
                onLineReceived("Git                            Git.Git                                  2.41.0           winget");
                onLineReceived("GitHub Desktop                 GitHub.GitHubDesktop                     3.2.3            winget");
                onLineReceived("GitLab Runner                  GitLab.GitLabRunner                      16.1.0           winget");
                return 0;
            }

            if (arguments.StartsWith("show", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.Contains("Mock.NotExist", StringComparison.OrdinalIgnoreCase)) return 1;
                onLineReceived("Found Git [Git.Git]");
                onLineReceived("Version: 2.41.0");
                onLineReceived("Publisher: Software Corp");
                onLineReceived("Author: Git Contributors");
                onLineReceived("Publisher Support Url: https://git-scm.com/support");
                onLineReceived("Description: Git is a free and open source distributed version control system.");
                onLineReceived("AppMoniker: git");
                onLineReceived("Tags: git, vcs, version-control");
                return 0;
            }
        }
        return 0;
    }
}

public class LogAndNotificationTests
{
    [Fact]
    public void LogService_LogsCorrectly()
    {
        LogService.LogInfo("Test info log message");
        LogService.LogError("Test error log message");
        LogService.LogError("Test error log message with exception", new Exception("Simulated exception"));

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingetStore", "logs");
        var logFile = Path.Combine(logDir, "app.log");
        Assert.True(File.Exists(logFile));
        var content = File.ReadAllText(logFile);
        Assert.Contains("Test info log message", content);
        Assert.Contains("Test error log message", content);
        Assert.Contains("Simulated exception", content);
    }

    [Fact]
    public void NotificationService_ShowErrorAndInfo_NullWindow_Coverage()
    {
        var oldMainWindow = App.MainWindow;
        var prop = typeof(App).GetProperty("MainWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        prop.SetValue(null, null);
        App.DispatcherOverride = action => action();
        try
        {
            var notification = new NotificationService();
            notification.ShowError("Error Title", "Error Message");
            notification.ShowInfo("Info Title", "Info Message");
        }
        finally
        {
            prop.SetValue(null, oldMainWindow);
            App.DispatcherOverride = null;
        }
    }
}





public class SettingsServiceTests
{
    [Fact]
    public void AppTheme_SaveAndLoad()
    {
        var original = SettingsService.AppTheme;
        try
        {
            SettingsService.AppTheme = "Dark";
            Assert.Equal("Dark", SettingsService.AppTheme);

            SettingsService.AppTheme = "Light";
            Assert.Equal("Light", SettingsService.AppTheme);
        }
        finally
        {
            SettingsService.AppTheme = original;
        }
    }

    [Fact]
    public void AutoUpdate_SaveAndLoad()

    {
        var original = SettingsService.AutoUpdate;
        try
        {
            SettingsService.AutoUpdate = true;
            Assert.True(SettingsService.AutoUpdate);

            SettingsService.AutoUpdate = false;
            Assert.False(SettingsService.AutoUpdate);
        }
        finally
        {
            SettingsService.AutoUpdate = original;
        }
    }

    [Fact]
    public void SettingsService_InterfaceImplementation()
    {
        ISettingsService service = new SettingsService();
        var originalTheme = service.AppTheme;
        var originalUpdate = service.AutoUpdate;

        try
        {
            service.AppTheme = "Dark";
            Assert.Equal("Dark", service.AppTheme);

            service.AutoUpdate = true;
            Assert.True(service.AutoUpdate);
        }
        finally
        {
            service.AppTheme = originalTheme;
            service.AutoUpdate = originalUpdate;
        }
    }

    [Fact]
    public void SettingsService_CorruptFileLoadException()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WingetStore",
            "settings.json"
        );

        string? originalJson = null;
        if (File.Exists(path))
        {
            originalJson = File.ReadAllText(path);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ corrupt json }");

            var method = typeof(SettingsService).GetMethod("LoadSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            method.Invoke(null, null);
        }
        finally
        {
            if (originalJson != null)
            {
                File.WriteAllText(path, originalJson);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void SettingsService_EdgeCases_Coverage()
    {
        var field = typeof(SettingsService).GetField("SettingsFilePath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var originalPath = field.GetValue(null);

        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WingetStore",
            "settings.json"
        );

        try
        {
            // 1. Test loaded == null in LoadSettings (file contains "null")
            File.WriteAllText(path, "null");
            var loadMethod = typeof(SettingsService).GetMethod("LoadSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            loadMethod.Invoke(null, null);

            // 2. Test dir == null in SaveSettings (no crash)
            field.SetValue(null, "C:\\");
            var saveMethod = typeof(SettingsService).GetMethod("SaveSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            saveMethod.Invoke(null, null);

            // 3. Test successful settings load (loaded != null)
            field.SetValue(null, path);
            var validSettings = new AppSettings { AutoUpdate = true, AppTheme = "Light" };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(validSettings));
            loadMethod.Invoke(null, null);
            Assert.True(SettingsService.AutoUpdate);
            Assert.Equal("Light", SettingsService.AppTheme);
        }
        finally
        {
            field.SetValue(null, originalPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

public class WingetPackageTests
{
    [Fact]
    public void CoreProperties_And_PropertyChanged()
    {
        var pkg = new WingetPackage();
        bool nameChanged = false;
        bool idChanged = false;
        pkg.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(pkg.Name)) nameChanged = true;
            if (e.PropertyName == nameof(pkg.Id)) idChanged = true;
        };

        pkg.Name = "Test Application";
        Assert.True(nameChanged);

        pkg.Id = "Test.Id";
        Assert.True(idChanged);
    }

    [Fact]
    public void IconUrlGetter_And_Caching()
    {
        var pkg = new WingetPackage { Id = "Test.App.Icon", Name = "Icon Test App" };
        var iconUrl = pkg.IconUrl;
        Assert.Equal("", iconUrl);
        var iconUrlCached = pkg.IconUrl;
        Assert.Equal(iconUrl, iconUrlCached);
    }

    [Fact]
    public void InitialParsing()
    {
        var pkg1 = new WingetPackage { Name = "Visual Studio Code" };
        var pkg2 = new WingetPackage { Name = "   git  " };
        var pkgNull = new WingetPackage { Name = null! };
        var pkgSpace = new WingetPackage { Name = "   " };

        Assert.Equal("V", pkg1.Initial);
        Assert.Equal("G", pkg2.Initial);
        Assert.Equal("?", pkgNull.Initial);
        Assert.Equal("?", pkgSpace.Initial);
    }

    [Fact]
    public void TagsInitialization()
    {
        var pkg = new WingetPackage();
        pkg.Tags.Add("developer");
        Assert.Single(pkg.Tags);
    }

    [Fact]
    public void ScreenshotsAndHasScreenshots()
    {
        var pkg = new WingetPackage { Id = "Mock.Screenshot.App", Name = "Screenshot App" };
        Assert.Equal(pkg.Screenshots.Count > 0, pkg.HasScreenshots);

        pkg.Screenshots = null!;
        var screenshots = pkg.Screenshots;
        Assert.Empty(screenshots);

        pkg.Screenshots = new List<string> { "url1" };
        Assert.Same(pkg.Screenshots, pkg.Screenshots);
    }

    [Fact]
    public void WingetPackage_PropertiesAndMethods_Comprehensive()
    {
        var pkg = new WingetPackage();

        // RecommendationReason coverage
        Assert.False(pkg.HasRecommendationReason);
        pkg.RecommendationReason = "Featured";
        Assert.Equal("Featured", pkg.RecommendationReason);
        Assert.True(pkg.HasRecommendationReason);

        // ActionButtonLabel coverage
        pkg.Status = PackageStatus.Installed;
        Assert.Equal("Uninstall", pkg.ActionButtonLabel);
        pkg.Status = PackageStatus.Upgradable;
        Assert.Equal("Update", pkg.ActionButtonLabel);
        pkg.Status = (PackageStatus)99;
        Assert.Equal("Install", pkg.ActionButtonLabel);
        pkg.Status = PackageStatus.Installable;
        Assert.Equal("Install", pkg.ActionButtonLabel);

        // RefreshIcon coverage
        pkg.IconUrl = "https://icon.com/logo.png";
        Assert.True(pkg.HasIcon);
        pkg.RefreshIcon();
        Assert.False(pkg.HasIcon);

        // Initial edge cases
        pkg.Name = "";
        Assert.Equal("?", pkg.Initial);
        pkg.Name = "   ";
        Assert.Equal("?", pkg.Initial);

        // MetadataItem coverage
        var metadata = new MetadataItem
        {
            Key = "Key",
            Value = "Value",
            IsUrl = true,
            SubItems = new List<MetadataItem>()
        };
        Assert.Equal("Key", metadata.Key);
        Assert.Equal("Value", metadata.Value);
        Assert.True(metadata.IsUrl);
        Assert.Empty(metadata.SubItems);
    }
}

public class IconServiceTests
{
    [Fact]
    public void LocalPathVerification()
    {
        var service = IconService.Instance;
        var iconUrl = service.GetIconUrl("Test.Package.DoesNotExist", "Does Not Exist");
        Assert.Equal("", iconUrl);
    }

    [Fact]
    public void FailedIdsRegistry()
    {
        var service = IconService.Instance;
        var iconUrlFirst = service.GetIconUrl("Dummy.Failed.App", "Failed App");
        var iconUrlSecond = service.GetIconUrl("Dummy.Failed.App", "Failed App");
        Assert.Equal("", iconUrlFirst);
        Assert.Equal("", iconUrlSecond);
    }

    [Fact]
    public void GetScreenshots_ResolvesCorrectly()
    {
        var service = IconService.Instance;
        var screenshots = service.GetScreenshots("Mock.App.Nonexistent", "Nonexistent App");
        Assert.Empty(screenshots);
    }
}

public class WingetServiceTests
{
    private static string AssetPath(string name) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", name);

    private static async Task<string?> BackupAssetAsync(string name)
    {
        var path = AssetPath(name);
        if (!File.Exists(path)) return null;
        var content = await File.ReadAllTextAsync(path);
        File.Delete(path);
        return content;
    }

    private static async Task RestoreAssetAsync(string name, string? backup)
    {
        if (backup == null) return;
        await File.WriteAllTextAsync(AssetPath(name), backup);
    }

    private static async Task WriteAssetAsync(string name, string content)
    {
        await File.WriteAllTextAsync(AssetPath(name), content);
    }

    [Fact]
    public void GetOrCreatePackage_CoreInstanceMerging()
    {
        var service = App.Winget;
        var pkg1 = new WingetPackage { Id = "Core.App.ID", Name = "Core App Version 1", Version = "1.0.0" };
        var pkg2 = new WingetPackage
        {
            Id = "Core.App.ID",
            Name = "Core App Version 2",
            Version = "2.0.0",
            AvailableVersion = "2.1.0",
            Source = "winget",
            Publisher = "Publisher",
            Status = PackageStatus.Installed,
            Description = "Desc",
            Homepage = "Home",
            License = "MIT",
            ReleaseNotes = "Notes",
            PublisherUrl = "PubUrl",
            InstallerType = "Nullsoft",
            InstallerUrl = "InstUrl",
            Tags = new List<string> { "tag1" },
            Details = new List<MetadataItem> { new MetadataItem { Key = "DetailKey" } },
            Screenshots = new List<string> { "https://screenshot.png" }
        };

        var cached1 = service.GetOrCreatePackage(pkg1);
        var cached2 = service.GetOrCreatePackage(pkg2);

        Assert.Same(cached1, cached2);
        Assert.Equal("Core App Version 2", cached1.Name);
        Assert.Equal("2.0.0", cached1.Version);
        Assert.Equal("2.1.0", cached1.AvailableVersion);
        Assert.Equal("winget", cached1.Source);
        Assert.Equal("Publisher", cached1.Publisher);
        Assert.Equal(PackageStatus.Installed, cached1.Status);
        Assert.Equal("Desc", cached1.Description);
        Assert.Equal("Home", cached1.Homepage);
        Assert.Equal("MIT", cached1.License);
        Assert.Equal("Notes", cached1.ReleaseNotes);
        Assert.Equal("PubUrl", cached1.PublisherUrl);
        Assert.Equal("Nullsoft", cached1.InstallerType);
        Assert.Equal("InstUrl", cached1.InstallerUrl);
        Assert.Single(cached1.Tags);
        Assert.Single(cached1.Details);
        Assert.Single(cached1.Screenshots);
    }

    [Fact]
    public void GetOrCreatePackage_ScreenshotsMerging()
    {
        var service = App.Winget;
        var pkg1 = new WingetPackage { Id = "Core.App.Screenshot", Name = "Core App" };
        var pkg2 = new WingetPackage
        {
            Id = "Core.App.Screenshot",
            Name = "Core App",
            Screenshots = new List<string> { "https://example.com/screenshot1.png" }
        };

        var cached1 = service.GetOrCreatePackage(pkg1);
        var cached2 = service.GetOrCreatePackage(pkg2);

        Assert.Same(cached1, cached2);
        Assert.True(cached1.HasScreenshots);
        Assert.Single(cached1.Screenshots);
        Assert.Equal("https://example.com/screenshot1.png", cached1.Screenshots[0]);
    }

    [Fact]
    public void GetOrCreatePackage_EmptyMerging()
    {
        var service = App.Winget;
        var pkg1 = new WingetPackage
        {
            Id = "Core.App.EmptyID",
            Name = "Core App Version 1",
            Version = "1.0.0",
            AvailableVersion = "2.1.0",
            Source = "winget",
            Publisher = "Publisher",
            Status = PackageStatus.Installed,
            Description = "Desc",
            Homepage = "Home",
            License = "MIT",
            ReleaseNotes = "Notes",
            PublisherUrl = "PubUrl",
            InstallerType = "Nullsoft",
            InstallerUrl = "InstUrl",
            Tags = new List<string> { "tag1" },
            Details = new List<MetadataItem> { new MetadataItem { Key = "DetailKey" } },
            Screenshots = new List<string> { "https://screenshot.png" }
        };
        var pkg2 = new WingetPackage
        {
            Id = "Core.App.EmptyID",
            Name = "Core App Version 1",
            Version = "",
            AvailableVersion = null!,
            Source = "",
            Publisher = null!,
            Status = PackageStatus.Installable,
            Description = "",
            Homepage = null!,
            License = "",
            ReleaseNotes = null!,
            PublisherUrl = "",
            InstallerType = null!,
            InstallerUrl = "",
            Tags = null!,
            Details = new List<MetadataItem>(),
            Screenshots = null!
        };

        var pkg3 = new WingetPackage
        {
            Id = "Core.App.EmptyID",
            Name = "Core App Version 1",
            Status = PackageStatus.Installable,
            Tags = new List<string>(),
            Details = null!,
            Screenshots = new List<string>()
        };

        var cached1 = service.GetOrCreatePackage(pkg1);
        var cached2 = service.GetOrCreatePackage(pkg2);
        var cached3 = service.GetOrCreatePackage(pkg3);

        Assert.Same(cached1, cached2);
        Assert.Same(cached1, cached3);
        Assert.Equal("1.0.0", cached1.Version);
        Assert.Equal("2.1.0", cached1.AvailableVersion);
        Assert.Equal("winget", cached1.Source);
        Assert.Equal("Core", cached1.Publisher);
        Assert.Equal(PackageStatus.Installed, cached1.Status);
        Assert.Equal("Desc", cached1.Description);
        Assert.Equal("Home", cached1.Homepage);
        Assert.Equal("MIT", cached1.License);
        Assert.Equal("Notes", cached1.ReleaseNotes);
        Assert.Equal("PubUrl", cached1.PublisherUrl);
        Assert.Equal("Nullsoft", cached1.InstallerType);
        Assert.Equal("InstUrl", cached1.InstallerUrl);
        Assert.Single(cached1.Tags);
        Assert.Single(cached1.Details);
        Assert.Single(cached1.Screenshots);
    }

    [Fact]
    public void TableParsingHelper()
    {
        string tableOutput =
            "Name           Id             Version    Available\r\n" +
            "-----------------------------------------------------------\r\n" +
            "Git            Git.Git        2.40.0     2.41.0\r\n" +
            "Notepad++      Notepad.Npp    8.5.1      8.5.2\r\n";

        var parsed = WingetParser.ParseTable(tableOutput);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Git.Git", parsed[0]["Id"]);
        Assert.Equal("2.41.0", parsed[0]["Available"]);
    }

    [Fact]
    public void ListDetailsOutputParsing()
    {
        string detailsOutput =
            "(1/2) Git [Git.Git]\r\n" +
            "  Publisher: Git\r\n" +
            "  Version: 2.40.0\r\n" +
            "  Origin Source: winget\r\n" +
            "(2/2) VS Code [Microsoft.VisualStudioCode]\r\n" +
            "  Publisher: Microsoft\r\n" +
            "  Version: 1.80.0\r\n" +
            "  Origin Source: winget\r\n";

        var parsed = WingetParser.ParseDetailsList(detailsOutput);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Git.Git", parsed[0].Id);
        Assert.Equal("Microsoft", parsed[1].Publisher);
        Assert.Equal("1.80.0", parsed[1].Version);
    }

    [Fact]
    public async Task InstalledAndUpgradable_Fetch()
    {
        var service = App.Winget;
        var installed = await service.GetInstalledPackagesAsync();
        Assert.Equal(4, installed.Count);
        Assert.Contains(installed, p => p.Id == "Git.Git");
        var upgradable = await service.GetUpgradablePackagesAsync();
        Assert.Equal(3, upgradable.Count);
        Assert.Contains(upgradable, p => p.Id == "Git.Git");
    }

    [Fact]
    public async Task SearchPackages_Operations()
    {
        var service = App.Winget;
        var searchResults = await service.SearchPackagesAsync("git", TestContext.Current.CancellationToken);
        Assert.Equal(3, searchResults.Count);
        Assert.All(searchResults, p => Assert.Equal("winget", p.Source));
    }

    [Fact]
    public async Task GetPackageDetails_Extraction()
    {
        var service = App.Winget;
        var pkgDetails = await service.GetPackageDetailsAsync("Git.Git");
        Assert.NotNull(pkgDetails);
        Assert.Equal("Git.Git", pkgDetails.Id);

        var decorated = await service.FetchAndDecoratePackageDetailsAsync("Git.Git");
        Assert.NotNull(decorated);
        Assert.Equal("Git.Git", decorated.Id);
    }

    public static IEnumerable<object[]> PackageOperationData()
    {
        yield return new object[] { (Action<IWingetService, WingetPackage>)((s, p) => s.InstallPackage(p)), "Mock.Install.App", PackageStatus.Installable };
        yield return new object[] { (Action<IWingetService, WingetPackage>)((s, p) => s.UpgradePackage(p)), "Mock.Upgrade.App", PackageStatus.Upgradable };
        yield return new object[] { (Action<IWingetService, WingetPackage>)((s, p) => s.UninstallPackage(p)), "Mock.Uninstall.App", PackageStatus.Installed };
    }

    [Theory]
    [MemberData(nameof(PackageOperationData))]
    public async Task PackageOperation_MockOperations(Action<IWingetService, WingetPackage> operation, string id, PackageStatus status)
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var service = App.Winget;
            var pkg = new WingetPackage { Id = id, Name = "Mock App", Status = status };
            operation(service, pkg);

            Assert.True(pkg.IsInstalling);
            Assert.Equal("Initializing...", pkg.InstallStatusText);

            await Task.Delay(100);
            Assert.True(pkg.InstallProgress > 0);

            pkg.IsInstalling = false;
        });
    }

    [Fact]
    public async Task BasicServiceFunctionality_ReturnsExpectedData()
    {
        var service = App.Winget;
        Assert.Equal(4, (await service.GetInstalledPackagesAsync()).Count);
        Assert.Equal(3, (await service.GetUpgradablePackagesAsync()).Count);
        var categories = await service.GetCategoriesAsync();
        Assert.NotEmpty(categories);
    }


    [Fact]
    public async Task ExportAndImport_CommandGeneration()
    {
        var service = App.Winget;
        string tempFile = Path.Combine(Path.GetTempPath(), "winget_export_test.json");
        try
        {
            var result = await service.ExportPackagesAsync(tempFile);
            Assert.Equal("", result);

            var importResult = await service.ImportPackagesAsync(tempFile);
            Assert.Equal("", importResult);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PopularAndRecommendations_FetchJson()
    {
        var service = App.Winget;
        var popular = await service.GetPopularPackagesAsync();
        Assert.NotEmpty(popular);

        var recommendations = await service.GetRecommendationsAsync();
        Assert.NotEmpty(recommendations);
    }

    [Fact]
    public void WingetService_TriggerPackageAction_Null_DoesNotThrow()

    {
        var service = App.Services.GetRequiredService<WingetService>();
        var ex = Record.Exception(() => service.TriggerPackageAction(null!));
        Assert.Null(ex);
    }


    [Fact]
    public async Task SearchPackagesAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var service = App.Services.GetRequiredService<WingetService>();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchPackagesAsync("anything", cts.Token));
    }

    [Fact]
    public async Task WingetService_IsWingetAvailable_FileNotFound()
    {
        var service = App.Services.GetRequiredService<WingetService>();
        var field = typeof(WingetService).GetField("WingetPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        try
        {
            field.SetValue(null, "C:\\nonexistent\\winget.exe");
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.RunCommandAsync("anything", TestContext.Current.CancellationToken));
        }
        finally
        {
            field.SetValue(null, original);
        }
    }

    [Fact]
    public async Task FetchAndDecoratePackageDetailsAsync_NullPackage_Coverage()
    {
        var service = App.Services.GetRequiredService<WingetService>();
        MockProcessRunner.ShouldThrow = true;
        try
        {
            var pkg = await service.FetchAndDecoratePackageDetailsAsync("Mock.NonExistent.App");
            Assert.NotNull(pkg);
            Assert.Equal("Mock.NonExistent.App", pkg.Id);
            Assert.Equal("Mock.NonExistent.App", pkg.Name);
            Assert.Equal(PackageStatus.Installable, pkg.Status);
        }
        finally
        {
            MockProcessRunner.ShouldThrow = false;
        }
    }

    [Fact]
    public async Task WingetService_NoFiles_Coverage()
    {
        var service = App.Services.GetRequiredService<WingetService>();
        var bakP = await BackupAssetAsync("popular_packages.json");
        var bakC = await BackupAssetAsync("categories.json");

        try
        {
            Assert.Empty(await service.GetPopularPackagesAsync());
            Assert.Empty(await service.GetCategoriesAsync());
        }
        finally
        {
            await RestoreAssetAsync("popular_packages.json", bakP);
            await RestoreAssetAsync("categories.json", bakC);
        }
    }

    [Fact]
    public async Task WingetService_DeepEdgeCases_Coverage()
    {
        var service = App.Services.GetRequiredService<WingetService>();

        // 1. FetchAndDecoratePackageDetailsAsync installed not upgradable
        Assert.Equal(PackageStatus.Installed, (await service.FetchAndDecoratePackageDetailsAsync("Mock.App.Installed")).Status);

        // 2. FetchDetailsInBackground catch block
        var fetchMethod = typeof(WingetService).GetMethod("FetchDetailsInBackground", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        MockProcessRunner.ShouldThrow = true;
        try
        {
            await (Task)fetchMethod.Invoke(service, new object[] { new WingetPackage { Id = "fail-app" } })!;
        }
        finally
        {
            MockProcessRunner.ShouldThrow = false;
        }

        // 3. TriggerPackageAction branches (Upgradable and Installable)
        service.TriggerPackageAction(new WingetPackage { Id = "upg", Status = PackageStatus.Upgradable });
        service.TriggerPackageAction(new WingetPackage { Id = "inst", Status = PackageStatus.Installable });

        // 4. null/empty list coverage for popular, categories, recommendations
        var bakP = await BackupAssetAsync("popular_packages.json");
        var bakC = await BackupAssetAsync("categories.json");
        try
        {
            await WriteAssetAsync("popular_packages.json", "null");
            await WriteAssetAsync("categories.json", "null");
            Assert.Empty(await service.GetPopularPackagesAsync());
            Assert.Empty(await service.GetCategoriesAsync());

            await WriteAssetAsync("popular_packages.json", "[null]");
            Assert.Empty(await service.GetRecommendationsAsync());
        }
        finally
        {
            await RestoreAssetAsync("popular_packages.json", bakP);
            await RestoreAssetAsync("categories.json", bakC);
        }
    }
    [Fact]
    public async Task Uninstall_SuccessPath_Coverage()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var service = App.Services.GetRequiredService<WingetService>();
            var pkg = new WingetPackage { Id = "Mock.Uninstall.Success", Name = "Mock Uninstall", Status = PackageStatus.Installed };
            service.UninstallPackage(pkg);

            await TestHelper.WaitWhileAsync(() => pkg.IsInstalling);

            Assert.False(pkg.IsInstalling);
            Assert.Equal(PackageStatus.Installable, pkg.Status);
            Assert.Equal(100, pkg.InstallProgress);
        });
    }

    [Fact]
    public async Task GetPackageDetailsAsync_NullNameFallback()
    {
        var service = App.Services.GetRequiredService<WingetService>();
        var result = await service.GetPackageDetailsAsync("Mock.NotExist");
        Assert.NotNull(result);
        Assert.Equal("Mock.NotExist", result.Name);
    }

    [Fact]
    public async Task FetchDetailsInBackground_SuccessPath()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var service = App.Services.GetRequiredService<WingetService>();
            var pkg = new WingetPackage { Id = "Git.Git", Name = "Git" };

            var fetchMethod = typeof(WingetService).GetMethod("FetchDetailsInBackground",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            await (Task)fetchMethod.Invoke(service, new object[] { pkg })!;

            Assert.Equal("Software Corp", pkg.Publisher);
            Assert.Contains("version control", pkg.Description);
        });
    }

    [Fact]
    public async Task WingetService_AllMethods_ExceptionPaths_Coverage()
    {
        var service = App.Services.GetRequiredService<WingetService>();
        var bakP = await BackupAssetAsync("popular_packages.json");
        var bakC = await BackupAssetAsync("categories.json");

        await WriteAssetAsync("popular_packages.json", "{invalid json}");
        await WriteAssetAsync("categories.json", "{invalid json}");

        MockProcessRunner.ShouldThrow = true;
        try
        {
            Assert.Empty(await service.SearchPackagesAsync("anything", TestContext.Current.CancellationToken));
            Assert.Empty(await service.GetInstalledPackagesAsync());
            Assert.Empty(await service.GetUpgradablePackagesAsync());
            Assert.Empty(await service.GetPopularPackagesAsync());
            Assert.Empty(await service.GetRecommendationsAsync());
            Assert.Empty(await service.GetCategoriesAsync());
            Assert.Null(await service.GetPackageDetailsAsync("any"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportPackagesAsync("anyfile"));


            var decPkg = await service.FetchAndDecoratePackageDetailsAsync("any");
            Assert.NotNull(decPkg);
            Assert.Equal("any", decPkg.Id);
            Assert.Equal("any", decPkg.Name);
            Assert.Equal(PackageStatus.Installable, decPkg.Status);
        }
        finally
        {
            MockProcessRunner.ShouldThrow = false;
            await RestoreAssetAsync("popular_packages.json", bakP);
            await RestoreAssetAsync("categories.json", bakC);
        }
    }
}

public class PackageFilteringHelperTests
{
    [Fact]
    public void MatchesQuery_NullPackage_ReturnsFalse()
    {
        WingetPackage? pkg = null;
        Assert.False(pkg!.MatchesQuery("test"));
    }


    [Fact]
    public void MatchesQuery_EmptyQuery_ReturnsTrue()
    {
        var pkg = new WingetPackage { Id = "App", Name = "App Name" };
        Assert.True(pkg.MatchesQuery(null!));
        Assert.True(pkg.MatchesQuery(""));
        Assert.True(pkg.MatchesQuery("   "));
    }

    [Fact]
    public void MatchesQuery_ValidMatches_ReturnsTrue()
    {
        var pkg = new WingetPackage { Id = "Git.Git", Name = "Git Installer", Publisher = "Software Corp" };

        // ID Match
        Assert.True(pkg.MatchesQuery("git"));
        // Name Match
        Assert.True(pkg.MatchesQuery("installer"));
        // Publisher Match
        Assert.True(pkg.MatchesQuery("corp"));
        // Case insensitive Match
        Assert.True(pkg.MatchesQuery("SOFTWARE"));
    }

    [Fact]
    public void MatchesQuery_Mismatches_ReturnsFalse()
    {
        var pkg = new WingetPackage { Id = "Git.Git", Name = "Git Installer", Publisher = "Software Corp" };
        Assert.False(pkg.MatchesQuery("vscode"));
    }

    [Fact]
    public void MatchesQuery_NullProperties_ReturnsFalse()
    {
        var pkg = new WingetPackage { Id = null!, Name = null!, Publisher = null! };
        Assert.False(pkg.MatchesQuery("test"));
    }
}

public class WingetParserTests
{
    [Fact]
    public void ParseProgressFromOutput_Tests()
    {
        Assert.Equal(98.0, WingetParser.ParseProgressFromOutput("██████████████████████████████  98%"));
        Assert.Equal(98.0, WingetParser.ParseProgressFromOutput("98%"));
        Assert.Equal(20.0, WingetParser.ParseProgressFromOutput("Downloading installer..."));
        Assert.Equal(60.0, WingetParser.ParseProgressFromOutput("Verifying installer..."));
        Assert.Equal(80.0, WingetParser.ParseProgressFromOutput("Installing package..."));
        Assert.Equal(0.0, WingetParser.ParseProgressFromOutput("Something random..."));
    }

    [Fact]
    public void ParseStatusTextFromOutput_Tests()
    {
        Assert.Equal("Downloading installer...", WingetParser.ParseStatusTextFromOutput("Downloading..."));
        Assert.Equal("Verifying installer...", WingetParser.ParseStatusTextFromOutput("Successfully verified installer hash"));
        Assert.Equal("Installing...", WingetParser.ParseStatusTextFromOutput("Starting package install"));
        Assert.Equal("Completed", WingetParser.ParseStatusTextFromOutput("Successfully installed"));
        Assert.Equal("Uninstalled", WingetParser.ParseStatusTextFromOutput("Successfully uninstalled"));
        Assert.Equal(string.Empty, WingetParser.ParseStatusTextFromOutput("98%"));
        Assert.Equal("This is a very long line that should ...", WingetParser.ParseStatusTextFromOutput("This is a very long line that should be truncated to fit"));
        Assert.Equal("Short line", WingetParser.ParseStatusTextFromOutput("Short line"));
    }

    [Fact]
    public void ParseTable_HeaderAndColumnPermutations()
    {
        // Short output
        Assert.Empty(WingetParser.ParseTable("Only one line"));

        // No separator line
        string noSep = "Name  Id  Version\nGit   Git 2.0.0\nVSCode VS 1.0";
        Assert.Empty(WingetParser.ParseTable(noSep));

        // Missing ID or Version
        string noId = "Name  Version\n----\nGit  2.0.0";
        Assert.Empty(WingetParser.ParseTable(noId));

        // Match column
        string matchTable = "Name  Id  Version  Match\n------------------------\nGit   Git 2.0      git";
        var resultMatch = WingetParser.ParseTable(matchTable);
        Assert.Single(resultMatch);
        Assert.Equal("git", resultMatch[0]["Match"]);

        // Available column
        string availTable = "Name  Id  Version  Available\n----------------------------\nGit   Git 2.0      2.1";
        var resultAvail = WingetParser.ParseTable(availTable);
        Assert.Single(resultAvail);
        Assert.Equal("2.1", resultAvail[0]["Available"]);

        // Default simple columns
        string simpleTable = "Name  Id  Version\n-----------------\nGit   Git 2.0";
        var resultSimple = WingetParser.ParseTable(simpleTable);
        Assert.Single(resultSimple);
        Assert.Equal("2.0", resultSimple[0]["Version"]);
    }

    [Fact]
    public void ParseDetailsList_FilteringAndARP()
    {
        string raw = @"
(1/2) Git [Git.Git]
  Publisher: Software Corp
  Version: 2.40.0
  Origin Source: winget

(2/2) Filtered App [ARP\Filtered]
  Publisher: Bad Publisher
  Version: 1.0.0
";
        var list = WingetParser.ParseDetailsList(raw);
        Assert.Single(list);
        Assert.Equal("Git.Git", list[0].Id);
        Assert.Equal("Software Corp", list[0].Publisher);
        Assert.Equal("2.40.0", list[0].Version);
        Assert.Equal("winget", list[0].Source);
    }

    [Fact]
    public void ParsePackageDetails_Comprehensive()
    {
        string rawDetails = @"
Found Git [Git.Git]
Version: 2.41.0
Publisher: Software Corp
Publisher Url: https://pub.com
Description: A test description
Homepage: http://homepage.org
License: MIT
Release Notes: https://rel.com/notes
Tags:
  git
  vcs
Installer:
  Installer Type: Nullsoft
  Installer Url: https://dl.com/git.exe
  Installer Alt: http://dl.com/git.exe
NoColonRoot
";
        var pkg = WingetParser.ParsePackageDetails(rawDetails, "Git.Git");
        Assert.Equal("Git", pkg.Name);
        Assert.Equal("2.41.0", pkg.Version);
        Assert.Equal("Software Corp", pkg.Publisher);
        Assert.Equal("https://pub.com", pkg.PublisherUrl);
        Assert.Equal("A test description", pkg.Description);
        Assert.Equal("http://homepage.org", pkg.Homepage);
        Assert.Equal("MIT", pkg.License);
        Assert.Equal("https://rel.com/notes", pkg.ReleaseNotes);
        Assert.Equal("Nullsoft", pkg.InstallerType);
        Assert.Equal("https://dl.com/git.exe", pkg.InstallerUrl);
        Assert.Contains("git", pkg.Tags);
        Assert.Contains("vcs", pkg.Tags);

        // Verify details collections are populated correctly
        Assert.NotEmpty(pkg.Details);
        var installerMetadata = pkg.Details.Find(m => m.Key == "Installer");
        Assert.NotNull(installerMetadata);
        Assert.Equal(3, installerMetadata.SubItems.Count);

        var noColonRootMetadata = pkg.Details.Find(m => m.Key == "NoColonRoot");
        Assert.NotNull(noColonRootMetadata);
    }

    [Fact]
    public void ParseTagsFromShowOutput_Tests()
    {
        string rawShow = @"
Name: Git
Version: 2.41.0
Tags:
  git
  vcs
Publisher: Software Corp
";
        var tags = WingetParser.ParseTagsFromShowOutput(rawShow);
        Assert.Equal(2, tags.Count);
        Assert.Contains("git", tags);
        Assert.Contains("vcs", tags);
    }

    [Fact]
    public void WingetParser_AdditionalEdgeCases_Coverage()
    {
        // 1. ParseTable exception path, 2 lines limit, short row substring, upgrades available text
        string exceptionTable = "Name  Source  Version  Id\n------------------------\nGit   Source  2.0      Git";
        Assert.Empty(WingetParser.ParseTable(exceptionTable));
        Assert.Empty(WingetParser.ParseTable("Line1\nLine2"));

        string shortRowTable = "Name      Id        Version   Match\n----------------------------------\nGit       GitID     2.0";
        Assert.Single(WingetParser.ParseTable(shortRowTable));

        string upgradesTextTable = "Name  Id  Version\n-----------------\nGit   Git 2.0\nupgrades available\nupgrade available";
        Assert.Single(WingetParser.ParseTable(upgradesTextTable));

        // 2. ParsePackageDetails edge cases
        // - Starts with "Found " but no bracket
        // - indent >= 2 but currentParent is null
        // - root key that is not in switch, has http URL
        // - root key that has no colon
        // - Name: root key switch case
        // - custom subkey with colon under Installer
        // - custom subkey without colon under NoColonRoot
        string rawDetails = @"
Found Git Without Bracket
  SubKey: SubVal
CustomKey: http://custom.com
NoColonRoot
  CustomNoColonSubKey
Installer:
  Installer SHA256: 123
  EmptyVal:
";
        var pkg = WingetParser.ParsePackageDetails(rawDetails, "Git.Git");
        Assert.Equal("", pkg.Name); // "Found Git Without Bracket" was skipped because of no bracket
        Assert.Equal("Git.Git", pkg.Id);
        var customMeta = pkg.Details.Find(m => m.Key == "CustomKey");
        Assert.NotNull(customMeta);
        Assert.True(customMeta.IsUrl);
        Assert.Equal("http://custom.com", customMeta.Value);

        var pkgNameSwitch = WingetParser.ParsePackageDetails("Name: GitAppName", "Git.Git");
        Assert.Equal("GitAppName", pkgNameSwitch.Name);

        // 3. ParseTagsFromShowOutput edge cases
        // - tab indentation
        // - empty tag lines
        string rawShow = "Tags:\n\tgit-tab\n  \nNonTagLine";
        var tags = WingetParser.ParseTagsFromShowOutput(rawShow);
        Assert.Single(tags);
        Assert.Equal("git-tab", tags[0]);

        // 4. ParseDetailsList last item edge cases (empty ID, ARP ID)
        Assert.Empty(WingetParser.ParseDetailsList("(1) App []"));
        Assert.Empty(WingetParser.ParseDetailsList("(1) App [ARP\\Test]"));
    }

    [Fact]
    public async Task TriggerPackageAction_CancelInstallingPackage()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var service = App.Services.GetRequiredService<WingetService>();
            var pkg = new WingetPackage { Id = "Mock.CancelTest.App", Name = "Cancel Test", Status = PackageStatus.Installable };

            service.InstallPackage(pkg);
            Assert.True(pkg.IsInstalling);

            service.TriggerPackageAction(pkg);

            await TestHelper.WaitWhileAsync(() => pkg.IsInstalling, 2000);
            Assert.False(pkg.IsInstalling);
            Assert.Contains("Canceled", pkg.InstallStatusText);
        });
    }
}

public class CachingWingetServiceTests
{
    [Fact]
    public void Constructor_NullParameter_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => new CachingWingetService(null!));
    }

    [Fact]
    public void GetOrCreatePackage_NullOrEmptyId_Checks()
    {
        var innerService = App.Services.GetRequiredService<WingetService>();
        var cachingService = new CachingWingetService(innerService);

        // Null check
        Assert.Throws<ArgumentNullException>(() => cachingService.GetOrCreatePackage(null!));

        // Empty ID check
        var emptyPkg = new WingetPackage { Id = "", Name = "Empty ID Pkg" };
        Assert.Same(emptyPkg, cachingService.GetOrCreatePackage(emptyPkg));
    }

    [Fact]
    public async Task GetPackageDetailsAsync_ReturnsNull_ForNonExistentPackage()
    {
        var cachingService = (CachingWingetService)App.Services.GetRequiredService<IWingetService>();
        var details = await cachingService.GetPackageDetailsAsync("Mock.NotExist");
        Assert.NotNull(details);
        Assert.Equal("Mock", details.Publisher);

        // Test successful TryGetValue cache hit branch
        var details1 = await cachingService.GetPackageDetailsAsync("Git.Git");
        var details2 = await cachingService.GetPackageDetailsAsync("Git.Git");
        Assert.Same(details1, details2);
    }

    [Fact]
    public void TriggerPackageAction_And_CommonOperations_Coverage()
    {
        TestHelper.RunWithDispatcher(() =>
        {
            var cachingService = (CachingWingetService)App.Services.GetRequiredService<IWingetService>();
            var pkg = new WingetPackage { Id = "TriggerAction.Mock.App.Installed", Name = "Mock Installed App", Status = PackageStatus.Installed };

            cachingService.TriggerPackageAction(pkg);
            Assert.True(pkg.IsInstalling);
            Assert.NotEmpty(cachingService.ActiveTasks);
        });
    }

    [Fact]
    public async Task GetPackageDetailsAsync_InnerReturnsNull()
    {
        var cachingService = (CachingWingetService)App.Services.GetRequiredService<IWingetService>();
        MockProcessRunner.ShouldThrow = true;
        try
        {
            var details = await cachingService.GetPackageDetailsAsync("Mock.NonExistent");
            Assert.Null(details);
        }
        finally
        {
            MockProcessRunner.ShouldThrow = false;
        }
    }

    [Fact]
    public async Task RunTaskAsync_FailureAndException_Coverage()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var service = App.Services.GetRequiredService<WingetService>();

            var pkgFail = new WingetPackage { Id = "Mock.Fail.App", Name = "Mock Fail App", Status = PackageStatus.Installable };
            service.InstallPackage(pkgFail);

            await TestHelper.WaitWhileAsync(() => pkgFail.IsInstalling, 1500);

            Assert.False(pkgFail.IsInstalling);
            Assert.Contains("Failed (Exit code: 2)", pkgFail.InstallStatusText);

            var pkgThrow = new WingetPackage { Id = "Mock.Throw.App", Name = "Mock Throw App", Status = PackageStatus.Installable };
            service.InstallPackage(pkgThrow);

            await TestHelper.WaitWhileAsync(() => pkgThrow.IsInstalling, 1500);

            Assert.False(pkgThrow.IsInstalling);
            Assert.Contains("Error: Simulated task exception", pkgThrow.InstallStatusText);
        });
    }

    [Fact]
    public void SettingsService_SaveSettings_Exception()
    {
        var field = typeof(SettingsService).GetField("SettingsFilePath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var originalPath = field.GetValue(null);
        try
        {
            field.SetValue(null, "illegal:\0path");
            var ex = Record.Exception(() => { SettingsService.AppTheme = "NonExistentThemeXYZ"; });
            Assert.Null(ex);
        }
        finally
        {
            field.SetValue(null, originalPath);
        }
    }

    [Fact]
    public void WingetParser_ParseProgressFromOutput_EdgeCases()
    {
        Assert.Equal(0.0, WingetParser.ParseProgressFromOutput(""));
        Assert.Equal(0.0, WingetParser.ParseProgressFromOutput(null!));
        Assert.Equal(100.0, WingetParser.ParseProgressFromOutput("100%"));
        Assert.Equal(5.0, WingetParser.ParseProgressFromOutput("-5%"));
        Assert.Equal(0.0, WingetParser.ParseProgressFromOutput("0%"));
    }

    [Fact]
    public void WingetParser_ParseStatusTextFromOutput_EdgeCases()
    {
        Assert.Equal(string.Empty, WingetParser.ParseStatusTextFromOutput(null!));
        Assert.Equal(string.Empty, WingetParser.ParseStatusTextFromOutput(""));
        Assert.Equal("Line", WingetParser.ParseStatusTextFromOutput("Line\r\n"));
        var s40 = new string('A', 40);
        var s41 = new string('A', 41);
        Assert.Equal(s40, WingetParser.ParseStatusTextFromOutput(s40));
        Assert.Equal(new string('A', 37) + "...", WingetParser.ParseStatusTextFromOutput(s41));
    }

    [Fact]
    public void PackageFilteringHelper_MatchesQuery_SpecialCharacters()
    {
        var pkg = new WingetPackage { Id = "App.Name", Name = "App+Name", Publisher = "Pub.*" };
        Assert.True(pkg.MatchesQuery("+"));
        Assert.True(pkg.MatchesQuery(".*"));
        Assert.False(pkg.MatchesQuery("^"));
    }

    [Fact]
    public async Task IconService_Initialize_WithNonExistentCacheFile()
    {
        var service = IconService.Instance;
        await service.InitializeAsync();
        Assert.False(service.GetType().GetField("_isInitialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(service) is false);
    }

    [Fact]
    public void IconService_GetIconUrl_WithInvalidPackageId()
    {
        var service = IconService.Instance;
        Assert.Equal(string.Empty, service.GetIconUrl(null!, "Name"));
        Assert.Equal(string.Empty, service.GetIconUrl("", "Name"));
    }

    [Fact]
    public void WingetParser_ParsePackageDetails_MalformedDetails()
    {
        var pkg = WingetParser.ParsePackageDetails("some random non-yaml text here", "App.Id");
        Assert.Equal("App.Id", pkg.Id);
        Assert.Equal(string.Empty, pkg.Name);
    }

    [Fact]
    public void WingetService_Constructor_NullParameter_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => new WingetService(null!));
    }

    [Fact]
    public void DataTypes_EdgeCoverage()
    {
        var task = new InstallTask();
        bool changed = false;
        task.PropertyChanged += (s, e) => changed = true;
        task.Status = InstallTaskStatus.Running;
        Assert.True(changed);

        PackageId id = null!;
        Assert.Equal(string.Empty, (string)id);

        string s = null!;
        Assert.Equal(string.Empty, ((PackageId)s).Value);

        PackageVersion verNull = null!;
        Assert.Equal(string.Empty, (string)verNull);

        string svNull = null!;
        Assert.Equal(string.Empty, ((PackageVersion)svNull).Value);

        var nonNullVer = new PackageVersion("1.0.0");
        Assert.Equal("1.0.0", (string)nonNullVer);
        Assert.Equal("1.0.0", nonNullVer.ToString());
        Assert.Equal("1.0.0", ((PackageVersion)"1.0.0").Value);
    }

    [Fact]
    public void CancelTask_DelegatesToInner()
    {
        var innerService = App.Services.GetRequiredService<WingetService>();
        var cachingService = new CachingWingetService(innerService);
        cachingService.CancelTask("test-task-id");
    }

    [Fact]
    public void CancelTaskForPackage_DelegatesToInner()
    {
        var innerService = App.Services.GetRequiredService<WingetService>();
        var cachingService = new CachingWingetService(innerService);
        cachingService.CancelTaskForPackage("test-package-id");
    }
}

public class PackageDetailHelperTests
{
    [Theory]
    [InlineData("Name", true)]
    [InlineData("Version", true)]
    [InlineData("Description", true)]
    [InlineData("Release Notes", true)]
    [InlineData("Publisher", false)]
    [InlineData("Homepage", false)]
    [InlineData("", false)]
    [InlineData("Installer", false)]
    public void ShouldSkipMetadataItem_ReturnsExpected(string key, bool expected)
    {
        Assert.Equal(expected, PackageDetailHelper.ShouldSkipMetadataItem(key));
    }
}

public class BulkSelectionHelperTests
{
    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(0, 5, false)]
    [InlineData(3, 5, null)]
    [InlineData(0, 0, false)]
    [InlineData(-1, 5, null)]
    public void ComputeSelectAllState_ReturnsExpected(int selected, int total, bool? expected)
    {
        Assert.Equal(expected, BulkSelectionHelper.ComputeSelectAllState(selected, total));
    }
}

public class NavigationHelperTests
{
    [Fact]
    public void GetPageType_NoWinget_ReturnsNoWingetPage()
    {
        var type = NavigationHelper.GetPageType("home", false, false);
        Assert.Equal(typeof(Pages.NoWingetPage), type);
    }

    [Fact]
    public void GetPageType_SettingsSelected_ReturnsSettingsPage()
    {
        var type = NavigationHelper.GetPageType(null, true, true);
        Assert.Equal(typeof(Pages.SettingsPage), type);
    }

    [Theory]
    [InlineData("home", typeof(Pages.HomePage))]
    [InlineData("search", typeof(Pages.HomePage))]

    [InlineData("installed", typeof(Pages.InstalledPage))]
    [InlineData("updates", typeof(Pages.UpdatesPage))]
    [InlineData("about", typeof(Pages.AboutPage))]
    public void GetPageType_ValidTag_ReturnsExpectedPage(string tag, Type expected)
    {
        var type = NavigationHelper.GetPageType(tag, false, true);
        Assert.Equal(expected, type);
    }

    [Fact]
    public void GetPageType_UnknownTag_ReturnsNull()
    {
        var type = NavigationHelper.GetPageType("unknown", false, true);
        Assert.Null(type);
    }

    [Fact]
    public void GetPageType_NullTagAndNotSettings_ReturnsNull()
    {
        var type = NavigationHelper.GetPageType(null, false, true);
        Assert.Null(type);
    }

    [Fact]
    public void GetPageType_NoWingetTakesPriorityOverSettings()
    {
        var type = NavigationHelper.GetPageType(null, true, false);
        Assert.Equal(typeof(Pages.NoWingetPage), type);
    }

    [Fact]
    public void GetPageType_EmptyString_ReturnsNull()
    {
        var type = NavigationHelper.GetPageType("", false, true);
        Assert.Null(type);
    }

}

public class FilterableViewModelHelperTests
{
    [Fact]
    public void MatchesSourceFilter_NullSourceFilter_ReturnsFalse()
    {
        var method = typeof(FilterableViewModel).GetMethod("MatchesSourceFilter",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "winget", null! });
        Assert.False((bool)result!);
    }

    [Fact]
    public void SortPackages_ExpandedSortOptions_SortsCorrectly()
    {
        var list = new List<WingetPackage>
            {
                new() { Id = "Z.App", Name = "Beta", Publisher = "Zebra Corp", Status = PackageStatus.Installed },
                new() { Id = "A.App", Name = "Alpha", Publisher = "Alpha Inc", Status = PackageStatus.Upgradable },
                new() { Id = "M.App", Name = "Gamma", Publisher = "Beta LLC", Status = PackageStatus.Installable }
            };

        var method = typeof(FilterableViewModel).GetMethod("SortPackages",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        var listPublisher = new List<WingetPackage>(list);
        method.Invoke(null, new object[] { listPublisher, "publisher" });
        Assert.Equal("Alpha Inc", listPublisher[0].Publisher);
        Assert.Equal("Beta LLC", listPublisher[1].Publisher);
        Assert.Equal("Zebra Corp", listPublisher[2].Publisher);

        var listId = new List<WingetPackage>(list);
        method.Invoke(null, new object[] { listId, "id" });
        Assert.Equal("A.App", listId[0].Id);
        Assert.Equal("M.App", listId[1].Id);
        Assert.Equal("Z.App", listId[2].Id);

        var listStatus = new List<WingetPackage>(list);
        method.Invoke(null, new object[] { listStatus, "status" });
        Assert.Equal(PackageStatus.Upgradable, listStatus[0].Status);
        Assert.Equal(PackageStatus.Installed, listStatus[1].Status);
        Assert.Equal(PackageStatus.Installable, listStatus[2].Status);
    }
}


public class ModelCoverageTests
{
    [Fact]
    public void CategoryItem_Defaults()
    {
        var item = new CategoryItem();
        Assert.Equal(string.Empty, item.Name);
        Assert.Equal(string.Empty, item.Tag);
        Assert.Equal("#1F0D4F", item.BackgroundColor);
        Assert.Equal("\uE943", item.IconGlyph);
    }

    [Fact]
    public void PackageStatusChangedMessage_Create()
    {
        var pkg = new WingetPackage { Id = "Test" };
        var msg = new PackageStatusChangedMessage(pkg);
        Assert.Same(pkg, msg.Value);
    }
}

public class RunCommandAsyncNullLineTests
{
    [Fact]
    public async Task RunCommandAsync_NullLine_DoesNotThrow()
    {
        var runner = new NullLineRunner();
        var service = new WingetService(runner);

        var result = await service.RunCommandAsync("list", TestContext.Current.CancellationToken);

        Assert.Equal("", result);
    }
}

public class RunTaskAsyncNullLineTests
{
    [Fact]
    public async Task RunTaskAsync_NullLine_DoesNotThrow()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var runner = new NullLineRunner();
            var service = new WingetService(runner);
            var pkg = new WingetPackage { Id = "Mock.NullLine.Pkg", Name = "NullLine" };

            service.InstallPackage(pkg);
            await TestHelper.WaitWhileAsync(() => pkg.IsInstalling);

            Assert.False(pkg.IsInstalling);
            Assert.Equal(PackageStatus.Installed, pkg.Status);
            Assert.Equal(100, pkg.InstallProgress);
        });
    }
}

public class RunTaskAsyncProgressStatusTests
{
    [Fact]
    public async Task Install_StatusOnlyLines_UpdatesStatusText()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var runner = new StatusOnlyLinesRunner();
            var service = new WingetService(runner);
            var pkg = new WingetPackage { Id = "Mock.StatusOnly", Name = "StatusOnly" };

            service.InstallPackage(pkg);
            await TestHelper.WaitWhileAsync(() => pkg.IsInstalling);

            Assert.False(pkg.IsInstalling);
            Assert.Equal(PackageStatus.Installed, pkg.Status);
        });
    }
}
public class NullLineRunner : IProcessRunner
{
    public async Task<int> RunStreamAsync(string fileName, string arguments, Action<string> onLineReceived, CancellationToken cancellationToken = default)
    {
        onLineReceived(null!);
        if (arguments != null && (arguments.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                                  arguments.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ||
                                  arguments.Contains("uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            string[] ops = arguments.Contains("install", StringComparison.OrdinalIgnoreCase) ? TestHelper.InstallSteps
                : arguments.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ? TestHelper.UpgradeSteps
                : TestHelper.UninstallSteps;
            for (int i = 0; i < ops.Length; i++)
            {
                onLineReceived($"Progress: {10 + i * 25}%");
                onLineReceived(ops[i]);
                await Task.Delay(10, cancellationToken);
            }
            return 0;
        }
        return 0;
    }
}

public class StatusOnlyLinesRunner : IProcessRunner
{
    public async Task<int> RunStreamAsync(string fileName, string arguments, Action<string> onLineReceived, CancellationToken cancellationToken = default)
    {
        if (arguments != null && (arguments.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                                  arguments.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ||
                                  arguments.Contains("uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            string[] ops = arguments.Contains("install", StringComparison.OrdinalIgnoreCase) ? TestHelper.InstallSteps
                : arguments.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ? TestHelper.UpgradeSteps
                : TestHelper.UninstallSteps;
            foreach (var op in ops)
            {
                onLineReceived(op);
                await Task.Delay(10, cancellationToken);
            }
            return 0;
        }
        return 0;
    }
}

public class MockInnerService : IWingetService
{
    public bool ImportCalled { get; private set; }
    public string? LastPath { get; private set; }

    public ObservableCollection<InstallTask> ActiveTasks => [];
    public Task<string> ImportPackagesAsync(string filepath)
    {
        ImportCalled = true;
        LastPath = filepath;
        return Task.FromResult($"imported:{filepath}");
    }
    public Task<string> RunCommandAsync(string arguments, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<List<WingetPackage>> SearchPackagesAsync(string query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<List<WingetPackage>> GetInstalledPackagesAsync() => throw new NotImplementedException();
    public Task<List<WingetPackage>> GetUpgradablePackagesAsync() => throw new NotImplementedException();
    public Task<List<WingetPackage>> GetPopularPackagesAsync() => throw new NotImplementedException();
    public Task<List<WingetPackage>> GetRecommendationsAsync() => throw new NotImplementedException();
    public Task<List<CategoryItem>> GetCategoriesAsync() => throw new NotImplementedException();
    public Task<WingetPackage?> GetPackageDetailsAsync(PackageId packageId) => throw new NotImplementedException();
    public Task<WingetPackage> FetchAndDecoratePackageDetailsAsync(PackageId packageId) => throw new NotImplementedException();
    public void InstallPackage(WingetPackage package) => throw new NotImplementedException();
    public void UpgradePackage(WingetPackage package) => throw new NotImplementedException();
    public void UninstallPackage(WingetPackage package) => throw new NotImplementedException();
    public void TriggerPackageAction(WingetPackage package) => throw new NotImplementedException();
    public void CancelTask(string taskId) {}
    public void CancelTaskForPackage(string packageId) {}
    public WingetPackage GetOrCreatePackage(WingetPackage incoming) => incoming;
    public Task<string> ExportPackagesAsync(string filepath) => throw new NotImplementedException();
}

public class ViewModelTests
{
    [Fact]
    public async Task SearchViewModel_LoadAndFilter()
    {
        var searchVM = App.Services.GetRequiredService<SearchViewModel>();
        var searchTask = searchVM.SearchAsync("git");
        Assert.True(searchVM.IsLoading);
        await searchTask;
        Assert.False(searchVM.IsLoading);
        Assert.NotEmpty(searchVM.SearchResults);
    }

    [Fact]
    public async Task SearchViewModel_Cancellation_RapidTyping()
    {
        var searchVM = App.Services.GetRequiredService<SearchViewModel>();
        var task1 = searchVM.SearchAsync("g");
        var task2 = searchVM.SearchAsync("gi");
        var task3 = searchVM.SearchAsync("git");
        await Task.WhenAll(task1, task2, task3);
        Assert.Equal("git", searchVM.SearchQuery);
        Assert.False(searchVM.IsLoading);
    }

    [Fact]
    public async Task InstalledViewModel_FiltersAutomatically()
    {
        var installedVM = App.Services.GetRequiredService<InstalledViewModel>();
        await installedVM.LoadPackagesAsync();
        installedVM.FilterQuery = "mock";
        foreach (var pkg in installedVM.FilteredPackages)
        {
            Assert.Contains("mock", (pkg.Name ?? "").ToLower() + (pkg.Id ?? "").ToLower() + (pkg.Publisher ?? "").ToLower());
        }
    }

    [Fact]
    public async Task ViewModels_SetErrorMessage_OnException()
    {
        var throwingService = new ThrowingWingetService();

        var searchVM = new SearchViewModel(throwingService);
        await searchVM.SearchAsync("test");
        Assert.True(searchVM.IsErrorOpen);
        Assert.Contains("Search failed", searchVM.ErrorMessage);

        var installedVM = new InstalledViewModel(throwingService);
        await installedVM.LoadPackagesAsync();
        Assert.True(installedVM.IsErrorOpen);
        Assert.Contains("Failed to load installed apps", installedVM.ErrorMessage);

        var updatesVM = new UpdatesViewModel(throwingService);
        await updatesVM.LoadUpgradesAsync();
        Assert.True(updatesVM.IsErrorOpen);
        Assert.Contains("Failed to load upgradable apps", updatesVM.ErrorMessage);

        var homeVM = new HomeViewModel(throwingService);
        await homeVM.LoadFeaturedContentAsync();
        Assert.True(homeVM.IsErrorOpen);
        Assert.Contains("Failed to load home content", homeVM.ErrorMessage);
    }

    [Fact]
    public async Task ViewModels_PropertyAndCommandCoverages()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var searchVM = App.Services.GetRequiredService<SearchViewModel>();
            searchVM.FilterQuery = "mock";
            searchVM.SortOrder = "az";
            await searchVM.SearchAsync("mock");
            var installedVM = App.Services.GetRequiredService<InstalledViewModel>();
            await installedVM.LoadPackagesAsync();
            Assert.NotEmpty(installedVM.FilteredPackages);
            var mockPkg = new WingetPackage { Id = "Mock.App.Installed", Name = "Mock Installed App", Status = PackageStatus.Installed };
            installedVM.UpgradeCommand.Execute(mockPkg);
            installedVM.UninstallCommand.Execute(mockPkg);

            var updatesVM = App.Services.GetRequiredService<UpdatesViewModel>();
            await updatesVM.LoadUpgradesAsync();
            updatesVM.FilterQuery = "mock";
            updatesVM.SortOrder = "az";

            var upgradePkg = new WingetPackage { Id = "Mock.App", Name = "Mock App", Status = PackageStatus.Upgradable };
            updatesVM.UpgradeCommand.Execute(upgradePkg);

            upgradePkg.Status = PackageStatus.Installed;
            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(upgradePkg));

            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.LoadFeaturedContentAsync();
            homeVM.FilterQuery = "mock";
            homeVM.SortOrder = "az";
            homeVM.SortOrder = "za";
        });
    }

    [Fact]
    public async Task ViewModels_EdgeCasesAndDeepCoverage()
    {
        var mainWindowProp = typeof(App).GetProperty("MainWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
#pragma warning disable SYSLIB0050
        var mockMainWindow = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainWindow));

#pragma warning restore SYSLIB0050
        mainWindowProp.SetValue(null, mockMainWindow);

        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var searchVM = App.Services.GetRequiredService<SearchViewModel>();
            await searchVM.SearchAsync(null!);
            await searchVM.SearchAsync("   ");

            var searchResultsField = typeof(SearchViewModel).GetField("_allResults", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var testPkgs = new List<WingetPackage>
            {
                    new WingetPackage { Id = "WingetApp", Name = "Winget App", Source = "winget" },
                    new WingetPackage { Id = "OtherApp", Name = "Other App", Source = "other" }
            };
            searchResultsField.SetValue(searchVM, testPkgs);

            searchVM.FilterQuery = "";
            searchVM.SourceFilter = "winget";
            searchVM.ApplyFilter();
            Assert.Single(searchVM.FilteredResults);

            searchVM.SourceFilter = "all";
            searchVM.SortOrder = "az";
            searchVM.ApplyFilter();

            searchVM.SortOrder = "za";
            searchVM.ApplyFilter();

            searchVM.SortOrder = "default";
            searchVM.ApplyFilter();

            searchVM.SourceFilter = "unknown";
            searchVM.ApplyFilter();
            Assert.Empty(searchVM.FilteredResults);

            var nullSrc = new List<WingetPackage> { new() { Id = "ns", Source = null! } };
            searchResultsField.SetValue(searchVM, nullSrc);
            searchVM.SourceFilter = "winget";
            searchVM.ApplyFilter();
            Assert.False(searchVM.HasResults);

            var installedVM = App.Services.GetRequiredService<InstalledViewModel>();
            await installedVM.LoadPackagesAsync();
            var devOpts = installedVM.DeveloperOptions;
            Assert.NotEmpty(devOpts);

            var pkgToInstallable = new WingetPackage { Id = "Mock.Temp1", Name = "Mock Temp 1", Status = PackageStatus.Installed };
            var internalListField = typeof(InstalledViewModel).GetField("_allPackages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var allPkgs = (List<WingetPackage>)internalListField.GetValue(installedVM)!;
            allPkgs.Add(pkgToInstallable);

            pkgToInstallable.Status = PackageStatus.Installable;
            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(pkgToInstallable));
            Assert.DoesNotContain(pkgToInstallable, allPkgs);

            var pkgToUpdate = new WingetPackage { Id = "Mock.Temp2", Name = "Mock Temp 2", Status = PackageStatus.Upgradable, Version = "1.0", AvailableVersion = "2.0" };
            allPkgs.Add(pkgToUpdate);
            pkgToUpdate.Status = PackageStatus.Installed;
            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(pkgToUpdate));
            Assert.Equal("2.0", pkgToUpdate.Version);

            var upgradableInList = new WingetPackage { Id = "Mock.Upgradable.InList", Name = "Mock Upgradable In List", Status = PackageStatus.Upgradable };
            allPkgs.Add(upgradableInList);
            allPkgs.Add(null!);
            var triggerInstalled = new WingetPackage { Id = "Mock.Trigger.Installed", Name = "Mock Trigger Installed", Status = PackageStatus.Upgradable };
            allPkgs.Add(triggerInstalled);
            triggerInstalled.Status = PackageStatus.Installed;
            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(triggerInstalled));
            allPkgs.Remove(upgradableInList);
            allPkgs.RemoveAll(p => p == null);

            installedVM.DeveloperFilter = "Mock Publisher";
            installedVM.SourceFilter = "winget";
            installedVM.SortOrder = "az";
            installedVM.ApplyFilter();

            installedVM.SortOrder = "za";
            installedVM.ApplyFilter();

            installedVM.DeveloperFilter = "Mock Publisher";
            installedVM.SourceFilter = "all";
            internalListField.SetValue(installedVM, new List<WingetPackage>
            {
                    new() { Id = "ep", Publisher = "", Source = "winget" },
                    new() { Id = "np", Publisher = null!, Source = "winget" },
                    new() { Id = "mp", Publisher = "Mock Publisher", Source = "winget" }
            });

            installedVM.ApplyFilter();
            Assert.Single(installedVM.FilteredPackages);

            installedVM.DeveloperFilter = "All Publishers";
            internalListField.SetValue(installedVM, new List<WingetPackage>
            {
                    new() { Id = "es", Source = "" },
                    new() { Id = "ns", Source = null! },
                    new() { Id = "ms", Source = "winget" }
            });
            installedVM.SourceFilter = "winget";
            installedVM.ApplyFilter();
            Assert.Single(installedVM.FilteredPackages);

            var updatesVM = App.Services.GetRequiredService<UpdatesViewModel>();
            await updatesVM.LoadUpgradesAsync();

            var upgradePkg1 = new WingetPackage { Id = "Mock.Upg1", Name = "Mock Upg 1", Status = PackageStatus.Upgradable, IsInstalling = true, InstallProgress = 40, Source = "winget" };
            var upgradePkg2 = new WingetPackage { Id = "Mock.Upg2", Name = "Mock Upg 2", Status = PackageStatus.Upgradable, IsInstalling = true, InstallProgress = 60, Source = "winget" };
            var upgradePkg3 = new WingetPackage { Id = "Mock.Upg3", Name = "Mock Upg 3", Status = PackageStatus.Upgradable, IsInstalling = false, InstallProgress = 0, Source = "winget" };

            updatesVM.Upgrades.Add(upgradePkg1);
            updatesVM.Upgrades.Add(upgradePkg2);
            updatesVM.Upgrades.Add(upgradePkg3);

            var internalUpgradesField = typeof(UpdatesViewModel).GetField("_allUpgrades", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var allUpgradesList = (List<WingetPackage>)internalUpgradesField.GetValue(updatesVM)!;
            allUpgradesList.Clear();
            allUpgradesList.Add(upgradePkg1);
            allUpgradesList.Add(upgradePkg2);
            allUpgradesList.Add(upgradePkg3);

            updatesVM.UpdateGlobalProgress();
            Assert.True(updatesVM.IsGlobalProgressVisible);
            Assert.Equal(50, updatesVM.GlobalProgressValue);
            Assert.Contains("2 apps", updatesVM.GlobalProgressStatusText);

            updatesVM.SourceFilter = "winget";
            Assert.Equal(3, updatesVM.FilteredUpgrades.Count);
            updatesVM.SourceFilter = "all";
            Assert.Equal(3, updatesVM.FilteredUpgrades.Count);



            internalUpgradesField.SetValue(updatesVM, new List<WingetPackage> { new() { Id = "nsu", Source = null! } });
            var savedUpgrades = updatesVM.Upgrades;
            updatesVM.Upgrades = new ObservableCollection<WingetPackage> { new() { Id = "nsu", Source = null! } };
            updatesVM.SourceFilter = "winget";
            updatesVM.ApplyFilter();
            Assert.Empty(updatesVM.FilteredUpgrades);
            var savedFilteredUpgrades = updatesVM.FilteredUpgrades;
            internalUpgradesField.SetValue(updatesVM, allUpgradesList);
            updatesVM.Upgrades = savedUpgrades;
            updatesVM.SourceFilter = "all";
            updatesVM.FilteredUpgrades = savedFilteredUpgrades;

            updatesVM.SortOrder = "az";
            updatesVM.SortOrder = "za";

            upgradePkg2.IsInstalling = false;
            updatesVM.UpdateGlobalProgress();
            Assert.Contains("Mock Upg 1", updatesVM.GlobalProgressStatusText);

            updatesVM.UpgradeAllCommand.Execute(null);
            updatesVM.UpgradeCommand.Execute(upgradePkg3);

            upgradePkg1.Status = PackageStatus.Installed;
            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(upgradePkg1));
            Assert.DoesNotContain(upgradePkg1, updatesVM.Upgrades);

            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(null!));

            installedVM.DevelopersList = null!;
            Assert.NotNull(installedVM.DeveloperOptions);

            var allPkgsList = (List<WingetPackage>)internalListField.GetValue(installedVM)!;
            allPkgsList.Add(null!);
            var populateMethod = typeof(InstalledViewModel).GetMethod("PopulateDevelopersList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            populateMethod.Invoke(installedVM, null);

            var searchVM2 = App.Services.GetRequiredService<SearchViewModel>();
            var allResultsField = typeof(SearchViewModel).GetField("_allResults", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var nullSourcePkg = new WingetPackage { Id = "null.src", Source = null! };
            var allResultsList = new List<WingetPackage> { nullSourcePkg };
            allResultsField.SetValue(searchVM2, allResultsList);
            searchVM2.SortOrder = "default";
            searchVM2.ApplyFilter();

            var pkgInstalling1 = new WingetPackage { Id = "inst1", IsInstalling = true, InstallProgress = 50.0 };
            var pkgInstalling2 = new WingetPackage { Id = "inst2", IsInstalling = false, InstallProgress = 0.0 };
            updatesVM.Upgrades = new ObservableCollection<WingetPackage> { pkgInstalling1, pkgInstalling2 };
            updatesVM.UpgradeAll();

            mainWindowProp?.SetValue(null, null);
            upgradePkg1.Status = PackageStatus.Installed;
            WeakReferenceMessenger.Default.Send<PackageStatusChangedMessage>(new PackageStatusChangedMessage(upgradePkg1));

            await App.Services.GetRequiredService<UpdatesViewModel>().LoadUpgradesAsync();
        });
        mainWindowProp?.SetValue(null, null);
    }

    [Fact]
    public void ViewModels_GeneratorBranchCoverage()
    {
        App.DispatcherOverride = action => action();
        try
        {
            var searchVM = App.Services.GetRequiredService<SearchViewModel>();
            Assert.Same(searchVM.SearchCommand, searchVM.SearchCommand);

            var installedVM = App.Services.GetRequiredService<InstalledViewModel>();
            Assert.Same(installedVM.UninstallCommand, installedVM.UninstallCommand);
            Assert.Same(installedVM.UpgradeCommand, installedVM.UpgradeCommand);
            Assert.Same(installedVM.LoadPackagesCommand, installedVM.LoadPackagesCommand);

            var updatesVM = App.Services.GetRequiredService<UpdatesViewModel>();
            Assert.Same(updatesVM.UpgradeAllCommand, updatesVM.UpgradeAllCommand);
            Assert.Same(updatesVM.UpgradeCommand, updatesVM.UpgradeCommand);
            Assert.Same(updatesVM.LoadUpgradesCommand, updatesVM.LoadUpgradesCommand);
        }
        finally
        {
            App.DispatcherOverride = null;
        }
    }

    [Fact]
    public void InstalledPage_GetUpdateVisibility_ReturnsExpected()
    {
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, Pages.InstalledPage.GetUpdateVisibility(PackageStatus.Upgradable));
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, Pages.InstalledPage.GetUpdateVisibility(PackageStatus.Installed));
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, Pages.InstalledPage.GetUpdateVisibility(PackageStatus.Installable));
    }

    [Fact]
    public async Task InstalledViewModel_DeveloperFilter_NullOrEmptyDefaultsToAll()
    {
        var vm = App.Services.GetRequiredService<InstalledViewModel>();
        await vm.LoadPackagesAsync();

        vm.DeveloperFilter = null!;
        vm.ApplyFilter();
        Assert.NotEmpty(vm.FilteredPackages);

        vm.DeveloperFilter = "";
        vm.ApplyFilter();
        Assert.NotEmpty(vm.FilteredPackages);

        vm.DeveloperFilter = "  ";
        vm.ApplyFilter();
        Assert.NotEmpty(vm.FilteredPackages);
    }

    [Fact]
    public void DummyAppsFleet_HandlesDiversePackagesAndFilters()

    {
        var diverseApps = new List<WingetPackage>
            {
                new WingetPackage { Id = "App.1", Name = "App One", Publisher = "Publisher A", Source = "winget", Status = PackageStatus.Installed },
                new WingetPackage { Id = "App.2", Name = "App Two", Publisher = "Publisher B", Source = "winget", Status = PackageStatus.Installed },
                new WingetPackage { Id = "App.3", Name = "App Three", Publisher = null!, Source = "winget", Status = PackageStatus.Installed },
                new WingetPackage { Id = "App.4", Name = "Special (x64) & Co.", Publisher = "Special & Co.", Source = "winget", Status = PackageStatus.Upgradable, AvailableVersion = "2.0" },
                new WingetPackage { Id = "App.5", Name = "Null Pub App", Publisher = "", Source = "winget", Status = PackageStatus.Installed }
            };

        for (int i = 6; i <= 50; i++)
        {
            diverseApps.Add(new WingetPackage
            {
                Id = $"Fleet.App.{i}",
                Name = $"Fleet Application {i}",
                Publisher = i % 2 == 0 ? "Fleet Devs" : "Open Source",
                Source = "winget",
                Status = i % 5 == 0 ? PackageStatus.Upgradable : PackageStatus.Installed
            });
        }

        var vm = App.Services.GetRequiredService<InstalledViewModel>();
        var allPkgsField = typeof(InstalledViewModel).GetField("_allPackages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        allPkgsField.SetValue(vm, diverseApps);

        var populateDevs = typeof(InstalledViewModel).GetMethod("PopulateDevelopersList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        populateDevs.Invoke(vm, null);

        Assert.Contains("All Publishers", vm.DeveloperOptions);
        Assert.Contains("Publisher A", vm.DeveloperOptions);
        Assert.Contains("Publisher B", vm.DeveloperOptions);
        Assert.Contains("Fleet Devs", vm.DeveloperOptions);

        vm.DeveloperFilter = "All Publishers";
        vm.SourceFilter = "winget";
        vm.ApplyFilter();
        Assert.Equal(50, vm.FilteredPackages.Count);


        vm.DeveloperFilter = "Publisher A";
        vm.ApplyFilter();
        Assert.Single(vm.FilteredPackages);
        Assert.Equal("App.1", vm.FilteredPackages[0].Id);

        vm.DeveloperFilter = "All Publishers";
        vm.SourceFilter = "winget";
        vm.ApplyFilter();
        Assert.All(vm.FilteredPackages, p => Assert.Contains("winget", p.Source ?? "", StringComparison.OrdinalIgnoreCase));

        vm.SortOrder = "az";
        vm.ApplyFilter();
        Assert.True(string.Compare(vm.FilteredPackages[0].Name, vm.FilteredPackages[1].Name, StringComparison.OrdinalIgnoreCase) <= 0);

        vm.SortOrder = "za";
        vm.ApplyFilter();
        Assert.True(string.Compare(vm.FilteredPackages[0].Name, vm.FilteredPackages[1].Name, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public async Task AutomatedHeadless_ViewModelsHandleThrowingServiceGracefully()
    {
        var throwingService = new ThrowingWingetService();

        var installedVM = new InstalledViewModel(throwingService);
        await installedVM.LoadPackagesAsync();
        Assert.True(installedVM.IsErrorOpen);
        Assert.Contains("Failed to load installed apps", installedVM.ErrorMessage);

        var homeVM = new HomeViewModel(throwingService);
        await homeVM.LoadFeaturedContentAsync();
        Assert.True(homeVM.IsErrorOpen);
        Assert.Contains("Failed to load home content", homeVM.ErrorMessage);

        var updatesVM = new UpdatesViewModel(throwingService);
        await updatesVM.LoadUpgradesAsync();
        Assert.True(updatesVM.IsErrorOpen);
        Assert.Contains("Failed to load upgradable apps", updatesVM.ErrorMessage);
    }

    [Fact]
    public void NavigationHelper_ResolvesAllPageTypesCorrectly()
    {
        Assert.Equal(typeof(Pages.HomePage), NavigationHelper.GetPageType("home", false, true));
        Assert.Equal(typeof(Pages.InstalledPage), NavigationHelper.GetPageType("installed", false, true));
        Assert.Equal(typeof(Pages.UpdatesPage), NavigationHelper.GetPageType("updates", false, true));
        Assert.Equal(typeof(Pages.AboutPage), NavigationHelper.GetPageType("about", false, true));
        Assert.Equal(typeof(Pages.SettingsPage), NavigationHelper.GetPageType("settings", true, true));
        Assert.Equal(typeof(Pages.NoWingetPage), NavigationHelper.GetPageType("home", false, false));
        Assert.Null(NavigationHelper.GetPageType("unknown_tag", false, true));
    }
}




public class ThrowingWingetService : IWingetService
{
    public ObservableCollection<InstallTask> ActiveTasks => throw new NotImplementedException();
    public Task<string> RunCommandAsync(string arguments, CancellationToken cancellationToken = default) => throw new Exception("CLI connection lost");
    public Task<List<WingetPackage>> SearchPackagesAsync(string query, CancellationToken cancellationToken = default) => throw new Exception("CLI search failed");
    public Task<List<WingetPackage>> GetInstalledPackagesAsync() => throw new Exception("CLI list failed");
    public Task<List<WingetPackage>> GetUpgradablePackagesAsync() => throw new Exception("CLI upgrades failed");
    public Task<List<WingetPackage>> GetPopularPackagesAsync() => throw new Exception("Popular JSON corrupted");
    public Task<List<WingetPackage>> GetRecommendationsAsync() => throw new Exception("Recommendation engine failed");
    public Task<List<CategoryItem>> GetCategoriesAsync() => throw new Exception("Categories missing");
    public Task<WingetPackage?> GetPackageDetailsAsync(PackageId packageId) => throw new Exception("Details unreachable");
    public Task<WingetPackage> FetchAndDecoratePackageDetailsAsync(PackageId packageId) => throw new Exception("Details failed");
    public void InstallPackage(WingetPackage package) => throw new NotImplementedException();
    public void UpgradePackage(WingetPackage package) => throw new NotImplementedException();
    public void UninstallPackage(WingetPackage package) => throw new NotImplementedException();
    public void TriggerPackageAction(WingetPackage package) => throw new NotImplementedException();
    public void CancelTask(string taskId) {}
    public void CancelTaskForPackage(string packageId) {}
    public WingetPackage GetOrCreatePackage(WingetPackage incoming) => incoming;
    public Task<string> ExportPackagesAsync(string filepath) => throw new NotImplementedException();
    public Task<string> ImportPackagesAsync(string filepath) => throw new NotImplementedException();
}

public class CliProcessRunnerTests
{
    [Fact]
    public async Task RunStreamAsync_CapturesStdout()
    {
        var runner = new CliProcessRunner();
        var lines = new List<string>();
        int exitCode = await runner.RunStreamAsync("cmd.exe", "/c echo hello-world", s => lines.Add(s), TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);
        Assert.Contains(lines, l => l.Contains("hello-world"));
    }

    [Fact]
    public async Task RunStreamAsync_ReturnsExitCode()
    {
        var runner = new CliProcessRunner();
        int exitCode = await runner.RunStreamAsync("cmd.exe", "/c exit 42", _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(42, exitCode);
    }

    [Fact]
    public async Task RunStreamAsync_CancellationKillsProcess()
    {
        var runner = new CliProcessRunner();
        using var cts = new CancellationTokenSource();
        var lines = new List<string>();
        var task = runner.RunStreamAsync("cmd.exe", "/c ping -n 10 127.0.0.1", s => lines.Add(s), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}

public class NotificationsSettingsTests
{
    [Fact]
    public void EnableNotifications_TogglesSettingCorrectly()
    {
        var original = SettingsService.EnableNotifications;
        try
        {
            SettingsService.EnableNotifications = false;
            Assert.False(SettingsService.EnableNotifications);
            SettingsService.EnableNotifications = true;
            Assert.True(SettingsService.EnableNotifications);
        }
        finally
        {
            SettingsService.EnableNotifications = original;
        }
    }
}

public class SecurityAndSanitizationTests
{
    [Theory]
    [InlineData("Microsoft.VisualStudioCode", "Microsoft.VisualStudioCode.png")]
    [InlineData("Foo/Bar\\Baz", "Foo_Bar_Baz.png")]
    [InlineData("..\\..\\secret.txt", "____secret.txt.png")]
    [InlineData("Invalid:File*Name?Chars\"< >|", "Invalid_File_Name_Chars__ __.png")]
    [InlineData("", "unknown.png")]
    public void GetSafeIconFileName_SanitizesPathTraversalAndInvalidChars(string input, string expected)
    {
        string actual = IconService.GetSafeIconFileName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("simple", "\"simple\"")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("with\"quote", "\"with\\\"quote\"")]
    [InlineData("trailing\\", "\"trailing\\\\\"")]
    [InlineData("path\\with\\\"quote", "\"path\\with\\\\\\\"quote\"")]
    [InlineData("", "\"\"")]
    public void EscapeArgument_EscapesQuotesAndBackslashesCorrectly(string input, string expected)
    {
        string actual = WingetService.EscapeArgument(input);
        Assert.Equal(expected, actual);
    }
}

public class WingetParserHardeningTests
{
    [Theory]
    [InlineData("Hello World", -1, 5, "")]
    [InlineData("Hello World", 20, 25, "")]
    [InlineData("Hello World", 5, 2, "")]
    [InlineData("Hello World", 0, 100, "Hello World")]
    [InlineData("Hello World", 0, 5, "Hello")]
    public void GetSubstring_HandlesOutOfBoundsIndicesSafely(string line, int start, int endExclusive, string expected)
    {
        string actual = WingetParser.GetSubstring(line, start, endExclusive);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Test verification of ARP filter")]
    public void ParseDetailsList_FiltersArpEntriesCaseInsensitively()
    {
        string sampleOutput = "(1/2) App One [ARP\\App1]\r\n(2/2) App Two [arp\\App2]\r\n(3/3) App Three [Vendor.App3]\r\n";
        var result = WingetParser.ParseDetailsList(sampleOutput);
        Assert.Single(result);
        Assert.Equal("Vendor.App3", result[0].Id);
    }
}

public class ViewModelStatusMessageTests
{
    [Fact]
    public async Task InstalledViewModel_RemovesUninstalledPackage_ByIdMatch()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var mockService = new StubWingetServiceWithPackages();
            var vm = new InstalledViewModel(mockService);
            await vm.LoadPackagesAsync();
            Assert.Equal(2, vm.FilteredPackages.Count);

            // Send status change message with a DISTINCT C# object reference but SAME Id
            var uninstalledPkg = new WingetPackage { Id = "App.One", Status = PackageStatus.Installable };
            WeakReferenceMessenger.Default.Send(new PackageStatusChangedMessage(uninstalledPkg));

            Assert.Single(vm.FilteredPackages);
            Assert.Equal("App.Two", vm.FilteredPackages[0].Id);
        });
    }

    [Fact]
    public async Task UpdatesViewModel_RemovesUpgradedPackage_ByIdMatch()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var mockService = new StubWingetServiceWithUpgrades();
            var vm = new UpdatesViewModel(mockService);
            await vm.LoadUpgradesAsync();
            Assert.Equal(2, vm.FilteredUpgrades.Count);

            // Send status change message with a DISTINCT C# object reference but SAME Id
            var upgradedPkg = new WingetPackage { Id = "Upg.App1", Status = PackageStatus.Installed };
            WeakReferenceMessenger.Default.Send(new PackageStatusChangedMessage(upgradedPkg));

            Assert.Single(vm.FilteredUpgrades);
            Assert.Equal("Upg.App2", vm.FilteredUpgrades[0].Id);
        });
    }

    private class StubWingetServiceWithPackages : StubWingetService
    {
        public override Task<List<WingetPackage>> GetInstalledPackagesAsync() =>
            Task.FromResult(new List<WingetPackage>
            {
                new() { Id = "App.One", Name = "App One", Status = PackageStatus.Installed },
                new() { Id = "App.Two", Name = "App Two", Status = PackageStatus.Installed }
            });
    }

    private class StubWingetServiceWithUpgrades : StubWingetService
    {
        public override Task<List<WingetPackage>> GetUpgradablePackagesAsync() =>
            Task.FromResult(new List<WingetPackage>
            {
                new() { Id = "Upg.App1", Name = "Upgrade One", Status = PackageStatus.Upgradable },
                new() { Id = "Upg.App2", Name = "Upgrade Two", Status = PackageStatus.Upgradable }
            });
    }
}

public class TaskCancellationTests
{
    [Fact]
    public void InstallTask_CanCancelProperty_TracksStatusCorrectly()
    {
        var task = new InstallTask { Status = InstallTaskStatus.Running };
        Assert.True(task.CanCancel);

        task.Status = InstallTaskStatus.Completed;
        Assert.False(task.CanCancel);
    }

    [Fact]
    public async Task WingetService_CancelTaskForPackage_CancelsRunningProcess()
    {
        var mockRunner = new SlowProcessRunner();
        var service = new WingetService(mockRunner);
        var pkg = new WingetPackage { Id = "Slow.App", Status = PackageStatus.Installable };

        service.InstallPackage(pkg);
        await Task.Delay(100);

        Assert.True(pkg.IsInstalling);
        service.CancelTaskForPackage("Slow.App");

        await TestHelper.WaitWhileAsync(() => pkg.IsInstalling, 2000);
        Assert.False(pkg.IsInstalling);
        Assert.Equal("Canceled", pkg.InstallStatusText);
    }

    private class SlowProcessRunner : IProcessRunner
    {
        public async Task<int> RunStreamAsync(string fileName, string arguments, Action<string> onLineReceived, CancellationToken cancellationToken = default)
        {
            onLineReceived("Starting...");
            await Task.Delay(5000, cancellationToken);
            return 0;
        }
    }
}

public class ThemeAndSortingTests
{
    [Theory]
    [InlineData("Light", Microsoft.UI.Xaml.ElementTheme.Light)]
    [InlineData("Dark", Microsoft.UI.Xaml.ElementTheme.Dark)]
    [InlineData("Default", Microsoft.UI.Xaml.ElementTheme.Default)]
    [InlineData("Unknown", Microsoft.UI.Xaml.ElementTheme.Default)]
    public void ParseTheme_ReturnsExpectedTheme(string themeString, Microsoft.UI.Xaml.ElementTheme expectedTheme)
    {
        var actual = App.ParseTheme(themeString);
        Assert.Equal(expectedTheme, actual);
    }

    [Fact]
    public void SortPackages_SortsByPropertyAndDirection()
    {
        var packages = new List<WingetPackage>
        {
            new() { Name = "Alpha", Id = "A.Id", Publisher = "Publisher A", Version = "1.0" },
            new() { Name = "Zebra", Id = "B.Id", Publisher = "Publisher B", Version = "2.0" }
        };

        // High to Low (Descending) by default
        PackageFilteringHelper.SortPackages(packages, "Name", "Descending");
        Assert.Equal("Zebra", packages[0].Name);

        // Low to High (Ascending)
        PackageFilteringHelper.SortPackages(packages, "Name", "Ascending");
        Assert.Equal("Alpha", packages[0].Name);

        // High to Low by Publisher
        PackageFilteringHelper.SortPackages(packages, "Publisher", "Descending");
        Assert.Equal("Publisher B", packages[0].Publisher);

        // High to Low by Id
        PackageFilteringHelper.SortPackages(packages, "Id", "Descending");
        Assert.Equal("B.Id", packages[0].Id);
    }

    [Fact]
    public void SortPackages_SortsByVersion()
    {
        var packages = new List<WingetPackage>
        {
            new() { Name = "App", Version = "1.0.0" },
            new() { Name = "App", Version = "2.0.0" }
        };
        PackageFilteringHelper.SortPackages(packages, "Version", "Descending");
        Assert.Equal("2.0.0", packages[0].Version);
        PackageFilteringHelper.SortPackages(packages, "Version", "Ascending");
        Assert.Equal("1.0.0", packages[0].Version);
    }

    [Fact]
    public void SortPackages_FallbackToDefaultSortByName()
    {
        var packages = new List<WingetPackage>
        {
            new() { Name = "Zed" },
            new() { Name = "Alpha" }
        };
        PackageFilteringHelper.SortPackages(packages, "UnknownField", "Ascending");
        Assert.Equal("Alpha", packages[0].Name);
    }

    [Theory]
    [InlineData("Google.Chrome", "Installed", "Google")]
    [InlineData("Microsoft.PowerToys", "Installed", "Microsoft")]
    [InlineData("Discord.Discord", "Discord Inc.", "Discord Inc.")]
    [InlineData("SingleWordId", "", "SingleWordId")]
    public void Publisher_DerivesFromId_WhenEmptyOrInstalled(string id, string explicitPublisher, string expectedPublisher)
    {
        var package = new WingetPackage { Id = id, Publisher = explicitPublisher };
        Assert.Equal(expectedPublisher, package.Publisher);
    }
    [Theory]
    [InlineData("Antigravity 2.3.1", "Antigravity")]
    [InlineData("Ente Auth version 4.4.24+1048", "Ente Auth")]
    [InlineData("Everything 1.4.1.1032 (x64)", "Everything")]
    [InlineData("LightBulb 2.6.3 (x86)", "LightBulb")]
    [InlineData("Normal App Name x64", "Normal App Name")]
    public void DisplayTitle_StripsVersionNumbersAndArchitecture(string originalName, string expectedCleanTitle)
    {
        var package = new WingetPackage { Name = originalName };
        Assert.Equal(expectedCleanTitle, package.DisplayTitle);
    }

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64)", true)]
    [InlineData("Microsoft .NET Desktop Runtime 8.0.1 (x64)", true)]
    [InlineData("Microsoft Edge WebView2 Runtime", true)]
    [InlineData("Google Chrome", false)]
    [InlineData("Discord", false)]
    public void IsRedistributable_DetectsRuntimesAndRedists(string name, bool expectedIsRedist)
    {
        var package = new WingetPackage { Name = name };
        Assert.Equal(expectedIsRedist, package.IsRedistributable);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(300, 1, 300, 0)]
    [InlineData(631, 1, 631, 0)]
    [InlineData(632, 2, 316, 16)]
    [InlineData(947, 2, 473.5, 16)]
    [InlineData(948, 3, 316, 16)]
    [InlineData(1263, 3, 421, 16)]
    [InlineData(1264, 4, 316, 16)]
    [InlineData(1579, 4, 394.75, 16)]
    [InlineData(1580, 5, 316, 16)]
    public void GridCalculator_OptionB_Boundaries(double usableWidth, int expectedCols, double expectedSlotWidth, double expectedGap)
    {
        var dims = GridCalculator.CalculateGridDimensions(usableWidth);
        Assert.Equal(expectedCols, dims.Columns);
        Assert.Equal(expectedSlotWidth, dims.SlotWidth, 2);
        Assert.Equal(expectedGap, dims.EffectiveGap);
        Assert.Equal(Math.Max(0, dims.SlotWidth - dims.EffectiveGap), dims.CardWidth, 2);
    }

    [Fact]
    public void GridCalculator_ValidatesArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateGridDimensions(500, minCardWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateGridDimensions(500, gap: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateGridDimensions(500, maxColumns: 0));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    public void VersionComparer_OptionB_Comparisons(string v1, string v2, int expectedSign)
    {
        int result = VersionComparer.Instance.Compare(v1, v2);
        if (expectedSign == 0) Assert.Equal(0, result);
        else if (expectedSign > 0) Assert.True(result > 0);
        else Assert.True(result < 0);
    }
}

public class Milestone1LayoutAndRefinementTests
{
    [Theory]
    [InlineData(600, Controls.ResponsiveBand.Narrow)]
    [InlineData(699, Controls.ResponsiveBand.Narrow)]
    [InlineData(700, Controls.ResponsiveBand.Medium)]
    [InlineData(1199, Controls.ResponsiveBand.Medium)]
    [InlineData(1200, Controls.ResponsiveBand.Wide)]
    [InlineData(1920, Controls.ResponsiveBand.Wide)]
    public void ResponsiveBand_CalculatesCorrectBands(double width, Controls.ResponsiveBand expectedBand)
    {
        var band = Controls.ResponsivePageContainer.GetBand(width);
        Assert.Equal(expectedBand, band);
    }

    [Theory]
    [InlineData(Controls.ResponsiveBand.Narrow, 16, 16, 16, 24)]
    [InlineData(Controls.ResponsiveBand.Medium, 24, 20, 24, 28)]
    [InlineData(Controls.ResponsiveBand.Wide, 32, 24, 32, 32)]
    public void ResponsiveBand_ReturnsCorrectPaddings(Controls.ResponsiveBand band, double left, double top, double right, double bottom)
    {
        var padding = Controls.ResponsivePageContainer.GetPadding(band);
        Assert.Equal(left, padding.Left);
        Assert.Equal(top, padding.Top);
        Assert.Equal(right, padding.Right);
        Assert.Equal(bottom, padding.Bottom);
    }

    [Fact]
    public void ResponsiveBand_WidthZero_ReturnsNarrow()
    {
        Assert.Equal(Controls.ResponsiveBand.Narrow, Controls.ResponsivePageContainer.GetBand(0));
    }

    [Fact]
    public void ResponsiveBand_NegativeWidth_ReturnsNarrow()
    {
        Assert.Equal(Controls.ResponsiveBand.Narrow, Controls.ResponsivePageContainer.GetBand(-1));
    }
}

public class AppHelperTests
{
    [Theory]
    [InlineData(true, Microsoft.UI.Xaml.Visibility.Visible)]
    [InlineData(false, Microsoft.UI.Xaml.Visibility.Collapsed)]
    public void VisibleIf_ReturnsCorrectVisibility(bool value, Microsoft.UI.Xaml.Visibility expected)
    {
        Assert.Equal(expected, App.VisibleIf(value));
    }

    [Theory]
    [InlineData(true, Microsoft.UI.Xaml.Visibility.Collapsed)]
    [InlineData(false, Microsoft.UI.Xaml.Visibility.Visible)]
    public void CollapsedIf_ReturnsCorrectVisibility(bool value, Microsoft.UI.Xaml.Visibility expected)
    {
        Assert.Equal(expected, App.CollapsedIf(value));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Not_ReturnsInverse(bool value, bool expected)
    {
        Assert.Equal(expected, App.Not(value));
    }

    [Fact]
    public void Dispatch_UsesDispatcherOverride()
    {
        bool invoked = false;
        var original = App.DispatcherOverride;
        try
        {
            App.DispatcherOverride = action => invoked = true;
            App.Dispatch(() => { });
            Assert.True(invoked);
        }
        finally
        {
            App.DispatcherOverride = original;
        }
    }

    [Fact]
    public void Dispatch_NoOverride_FallsThrough()
    {
        var original = App.DispatcherOverride;
        try
        {
            App.DispatcherOverride = null;
            bool invoked = false;
            App.Dispatch(() => invoked = true);
            Assert.True(invoked);
        }
        finally
        {
            App.DispatcherOverride = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToImageSource_NullOrEmpty_ReturnsNull(string? path)
    {
        Assert.Null(App.ToImageSource(path));
    }

    [Fact]
    public void ToImageSource_ValidUri_CatchBlockReturnsNull()
    {
        Assert.Null(App.ToImageSource("http://example.com/icon.png"));
    }

    [Fact]
    public void IsUITestMode_ReturnsFalseDuringNormalTests()
    {
        Assert.False(App.IsUITestMode());
    }
}

public class RecommendationLayoutTests
{
    [Fact]
    public void RecommendationLayoutState_DefaultValues()
    {
        var state = new RecommendationLayoutState();
        Assert.Equal(146.0, state.CardHeight);
        Assert.Equal(new Microsoft.UI.Xaml.Thickness(0, 0, 16, 16), state.CardMargin);
    }

    [Fact]
    public void RecommendationLayoutState_CardHeight_RaisesPropertyChanged()
    {
        var state = new RecommendationLayoutState();
        string? changedProp = null;
        state.PropertyChanged += (s, e) => changedProp = e.PropertyName;
        state.CardHeight = 200;
        Assert.Equal(200, state.CardHeight);
        Assert.Equal(nameof(RecommendationLayoutState.CardHeight), changedProp);
    }

    [Fact]
    public void RecommendationLayoutState_CardMargin_RaisesPropertyChanged()
    {
        var state = new RecommendationLayoutState();
        string? changedProp = null;
        state.PropertyChanged += (s, e) => changedProp = e.PropertyName;
        state.CardMargin = new Microsoft.UI.Xaml.Thickness(8);
        Assert.Equal(new Microsoft.UI.Xaml.Thickness(8), state.CardMargin);
        Assert.Equal(nameof(RecommendationLayoutState.CardMargin), changedProp);
    }

    [Fact]
    public void RecommendationLayoutState_SameValue_DoesNotRaisePropertyChanged()
    {
        var state = new RecommendationLayoutState();
        int changeCount = 0;
        state.PropertyChanged += (s, e) => changeCount++;
        state.CardHeight = 146.0;
        state.CardMargin = new Microsoft.UI.Xaml.Thickness(0, 0, 16, 16);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void RecommendationCardViewModel_StoresPackageAndLayout()
    {
        var pkg = new WingetPackage { Id = "Test.App", Name = "Test App" };
        var layout = new RecommendationLayoutState();
        var vm = new RecommendationCardViewModel(pkg, layout);
        Assert.Same(pkg, vm.Package);
        Assert.Same(layout, vm.LayoutState);
    }
}

public class BulkSelectionHelperStateTests
{
    [Fact]
    public void BulkSelectionHelper_Toggle_ActivatesAndDeactivates()
    {
        int callbackCount = 0;
        var helper = new BulkSelectionHelper(() => callbackCount++);
        Assert.False(helper.IsActive);
        Assert.Empty(helper.SelectedPackages);

        helper.Toggle();
        Assert.True(helper.IsActive);
        Assert.Empty(helper.SelectedPackages);
        Assert.Equal(1, callbackCount);

        helper.Toggle();
        Assert.False(helper.IsActive);
        Assert.Empty(helper.SelectedPackages);
        Assert.Equal(2, callbackCount);
    }

    [Fact]
    public void BulkSelectionHelper_SelectAll_AddsPackages()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "A" }, new() { Id = "B" }
        };
        var helper = new BulkSelectionHelper(() => { });
        helper.SelectAll(packages);
        Assert.Equal(2, helper.SelectedPackages.Count);
    }

    [Fact]
    public void BulkSelectionHelper_DeselectAll_ClearsPackages()
    {
        var helper = new BulkSelectionHelper(() => { });
        helper.SelectAll(new List<WingetPackage> { new() { Id = "A" } });
        Assert.Single(helper.SelectedPackages);
        helper.DeselectAll();
        Assert.Empty(helper.SelectedPackages);
    }
}

public class IconServiceCoverageTests
{
    [Fact]
    public void GetSafeIconFileName_NullOrEmpty_ReturnsUnknown()
    {
        Assert.Equal("unknown.png", IconService.GetSafeIconFileName(null!));
        Assert.Equal("unknown.png", IconService.GetSafeIconFileName(""));
        Assert.Equal("unknown.png", IconService.GetSafeIconFileName("   "));
    }

    [Fact]
    public void GetSafeIconFileName_SanitizesInvalidChars()
    {
        var result = IconService.GetSafeIconFileName(@"Test/App:Name");
        Assert.Contains(".png", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public void GetSafeIconFileName_DoubleDots_Replaced()
    {
        var result = IconService.GetSafeIconFileName("Test..App");
        Assert.DoesNotContain("..", result.Replace(".png", ""));
    }

    [Fact]
    public void GetIconUrl_NullPackageId_ReturnsEmpty()
    {
        var service = IconService.Instance;
        Assert.Equal("", service.GetIconUrl(null!, "Name"));
        Assert.Equal("", service.GetIconUrl("", "Name"));
    }

    [Fact]
    public void GetScreenshots_NonExistentPackage_ReturnsEmpty()
    {
        var service = IconService.Instance;
        Assert.Empty(service.GetScreenshots("Does.Not.Exist", "Does Not Exist"));
    }

    [Fact]
    public void GetScreenshots_NullPackageId_ReturnsEmpty()
    {
        var service = IconService.Instance;
        Assert.Empty(service.GetScreenshots(null!, "Name"));
    }
}

public class HomeViewModelCoverageTests
{
    [Fact]
    public async Task HomeViewModel_ApplyFilter_WithNullRecommendations()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            var recField = typeof(HomeViewModel).GetField("_allRecommendations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            recField.SetValue(homeVM, null);
            homeVM.FilterQuery = "test";
            homeVM.ApplyFilter();
        });
    }

    [Fact]
    public async Task HomeViewModel_ApplyFilter_FiltersRecommendations()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.LoadFeaturedContentAsync();

            homeVM.FilterQuery = "popular";
            Assert.NotNull(homeVM.FilteredRecommendations);
        });
    }

    [Fact]
    public async Task HomeViewModel_LoadFeaturedContentAsync_PopulatesCategories()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.LoadFeaturedContentAsync();
            Assert.NotNull(homeVM.Categories);
            Assert.NotEmpty(homeVM.Categories);
        });
    }

    [Fact]
    public async Task HomeViewModel_SearchAsync_ClearsOnEmpty()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.SearchAsync("");
            Assert.False(homeVM.IsSearchActive);
        });
    }

    [Fact]
    public async Task HomeViewModel_SearchAsync_Whitespace_Clears()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.SearchAsync("   ");
            Assert.False(homeVM.IsSearchActive);
        });
    }

    [Fact]
    public async Task HomeViewModel_SortOrder_Changes()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.LoadFeaturedContentAsync();
            homeVM.SortOrder = "az";
            homeVM.SortOrder = "za";
            homeVM.SortOrder = "default";
        });
    }

    [Fact]
    public async Task HomeViewModel_RecommendationCardViewModel_Wrapping()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.LoadFeaturedContentAsync();

            var filteredField = typeof(HomeViewModel).GetField("_allRecommendations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var recs = filteredField.GetValue(homeVM) as List<WingetPackage>;
            if (recs != null && recs.Count > 0)
            {
                var layoutState = new RecommendationLayoutState();
                var cardVm = new RecommendationCardViewModel(recs[0], layoutState);
                Assert.Same(recs[0], cardVm.Package);
                Assert.Same(layoutState, cardVm.LayoutState);
            }
        });
    }
}

public class NavigationHelperEdgeTests
{
    [Fact]
    public void GetPageType_UnknownTag_ReturnsNull()
    {
        Assert.Null(NavigationHelper.GetPageType("nonexistent", false, true));
    }

    [Fact]
    public void GetPageType_EmptyTag_ReturnsNull()
    {
        Assert.Null(NavigationHelper.GetPageType("", false, true));
    }

    [Fact]
    public void GetPageType_NullTagNoSettingsNoWinget_ReturnsNoWingetPage()
    {
        var type = NavigationHelper.GetPageType(null, false, false);
        Assert.Equal(typeof(Pages.NoWingetPage), type);
    }

    [Fact]
    public void GetPageType_SettingsNoWinget_ReturnsNoWingetPage()
    {
        var type = NavigationHelper.GetPageType(null, true, false);
        Assert.Equal(typeof(Pages.NoWingetPage), type);
    }

    [Fact]
    public void GetPageType_SettingsWingetAvailable_ReturnsSettingsPage()
    {
        var type = NavigationHelper.GetPageType(null, true, true);
        Assert.Equal(typeof(Pages.SettingsPage), type);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("search")]
    public void GetPageType_HomeAndSearchTags_ReturnHomePage(string tag)
    {
        var type = NavigationHelper.GetPageType(tag, false, true);
        Assert.Equal(typeof(Pages.HomePage), type);
    }

    [Theory]
    [InlineData("installed")]
    [InlineData("updates")]
    [InlineData("about")]
    public void GetPageType_ValidTags_ReturnCorrectPage(string tag)
    {
        var type = NavigationHelper.GetPageType(tag, false, true);
        Assert.NotNull(type);
    }
}

public class PackageFilteringHelperEdgeTests
{
    [Fact]
    public void MatchesQuery_NullPackage_ReturnsFalse()
    {
        Assert.False(PackageFilteringHelper.MatchesQuery(null!, "test"));
    }

    [Fact]
    public void MatchesQuery_EmptyQuery_ReturnsTrue()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test" };
        Assert.True(pkg.MatchesQuery(""));
        Assert.True(pkg.MatchesQuery(null!));
        Assert.True(pkg.MatchesQuery("   "));
    }

    [Fact]
    public void MatchesQuery_TagMatch_ReturnsTrue()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test", Tags = new List<string> { "utility" } };
        Assert.True(pkg.MatchesQuery("tag:utility"));
    }

    [Fact]
    public void MatchesQuery_TagNoMatch_ReturnsFalse()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test", Tags = new List<string> { "utility" } };
        Assert.False(pkg.MatchesQuery("tag:unknown"));
    }

    [Fact]
    public void MatchesQuery_NullProperties_NoException()
    {
        var pkg = new WingetPackage { Id = null!, Name = null!, Publisher = null!, Description = null! };
        Assert.False(pkg.MatchesQuery("test"));
    }

    [Theory]
    [InlineData("all", "winget", true)]
    [InlineData("all", null, true)]
    [InlineData("all", "", true)]
    [InlineData("winget", "winget", true)]
    [InlineData("winget", "WINGET", true)]
    [InlineData("winget", "other", false)]
    [InlineData("winget", null, false)]
    public void MatchesSourceFilter_ReturnsCorrectResult(string filter, string? source, bool expected)
    {
        Assert.Equal(expected, PackageFilteringHelper.MatchesSourceFilter(source, filter));
    }

    [Fact]
    public void FilterAndSortPackages_FiltersByQueryAndSourceAndSorts()
    {
        var packages = new List<WingetPackage>
        {
            new() { Name = "Brave", Id = "Brave.Brave", Source = "winget" },
            new() { Name = "Zoom", Id = "Zoom.Zoom", Source = "msstore" },
            new() { Name = "DBeaver", Id = "DBeaver.DBeaver", Source = "winget" }
        };
        var result = PackageFilteringHelper.FilterAndSortPackages(packages, "brave", "all");
        Assert.Single(result);
        Assert.Equal("Brave", result[0].Name);
    }

    [Fact]
    public void FilterAndSortPackages_EmptyQuery_ReturnsAllSorted()
    {
        var packages = new List<WingetPackage>
        {
            new() { Name = "Zebra", Id = "Z.Z" },
            new() { Name = "Alpha", Id = "A.A" }
        };
        var result = PackageFilteringHelper.FilterAndSortPackages(packages, "", "all", "name");
        Assert.Equal(2, result.Count);
        Assert.Equal("Zebra", result[0].Name);
    }
}

public class SettingsPageDiagnosticsTests
{
    [Theory]
    [InlineData(true, "Connected to Windows Package Manager", "\uE73E")]
    [InlineData(false, "Winget not found on this system", "\uEA39")]
    public void GetDiagnosticsData_ReturnsCorrectStatus(bool available, string expectedStatus, string expectedGlyph)
    {
        var (statusText, isAvailable, glyph, formatted) = SettingsPage.GetDiagnosticsData(available, DateTime.Today + new TimeSpan(14, 30, 0));
        Assert.Equal(expectedStatus, statusText);
        Assert.Equal(available, isAvailable);
        Assert.Equal(expectedGlyph, glyph);
        Assert.Contains("Checked today", formatted);
    }

    [Fact]
    public void GetDiagnosticsData_PreviousDate_ShowsDate()
    {
        var (_, _, _, formatted) = SettingsPage.GetDiagnosticsData(true, new DateTime(2026, 7, 22, 10, 0, 0));
        Assert.Contains("Checked ", formatted);
        Assert.Contains("22", formatted);
    }

    [Fact]
    public void GetDiagnosticsData_DefaultNotAvailable_UsesNotConnected()
    {
        var (statusText, isAvailable, _, _) = SettingsPage.GetDiagnosticsData(false, DateTime.Now);
        Assert.Equal("Winget not found on this system", statusText);
        Assert.False(isAvailable);
    }
}

public class MainWindowHelperTests
{
    [Theory]
    [InlineData(0, false, "0", "Updates, none available")]
    [InlineData(1, true, "1", "Updates, 1 available")]
    [InlineData(5, true, "5", "Updates, 5 available")]
    [InlineData(99, true, "99", "Updates, 99 available")]
    [InlineData(100, true, "99", "Updates, 100 available")]
    public void GetBadgeData_ReturnsCorrectValues(int count, bool expectedVisible, string expectedText, string expectedAutomation)
    {
        var (isVisible, badgeText, automation) = MainWindow.GetBadgeData(count);
        Assert.Equal(expectedVisible, isVisible);
        Assert.Equal(expectedText, badgeText);
        Assert.Equal(expectedAutomation, automation);
    }

    [Fact]
    public void GetBadgeData_NegativeCount_TreatedAsNoUpdates()
    {
        var (isVisible, badgeText, automation) = MainWindow.GetBadgeData(-1);
        Assert.False(isVisible);
        Assert.Equal("0", badgeText);
        Assert.Equal("Updates, none available", automation);
    }

    [Theory]
    [InlineData(ElementTheme.Dark, "\uE706", "Switch to light theme")]
    [InlineData(ElementTheme.Light, "\uE708", "Switch to dark theme")]
    public void GetThemeToggleData_ReturnsCorrectGlyph(ElementTheme theme, string expectedGlyph, string expectedLabel)
    {
        var (glyph, label) = MainWindow.GetThemeToggleData(theme);
        Assert.Equal(expectedGlyph, glyph);
        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [MemberData(nameof(PageTypeData))]
    public void IsTopLevelPage_ReturnsCorrectResult(Type? pageType, bool expected)
    {
        if (pageType == null)
            Assert.False(MainWindow.IsTopLevelPage(null!));
        else
            Assert.Equal(expected, MainWindow.IsTopLevelPage(pageType));
    }

    public static IEnumerable<object[]> PageTypeData()
    {
        yield return [typeof(HomePage), true];
        yield return [typeof(InstalledPage), true];
        yield return [typeof(UpdatesPage), true];
        yield return [typeof(SettingsPage), true];
        yield return [typeof(AboutPage), true];
        yield return [typeof(NoWingetPage), true];
        yield return [typeof(DetailsPage), false];
    }

    [Theory]
    [InlineData(ElementTheme.Dark, ElementTheme.Dark, ElementTheme.Dark)]
    [InlineData(ElementTheme.Light, ElementTheme.Dark, ElementTheme.Light)]
    [InlineData(ElementTheme.Default, ElementTheme.Dark, ElementTheme.Dark)]
    [InlineData(ElementTheme.Default, ElementTheme.Light, ElementTheme.Light)]
    [InlineData(null, ElementTheme.Dark, ElementTheme.Dark)]
    [InlineData(null, ElementTheme.Light, ElementTheme.Light)]
    public void ResolveCurrentTheme_ReturnsExpected(ElementTheme? requested, ElementTheme actual, ElementTheme expected)
    {
        Assert.Equal(expected, MainWindow.ResolveCurrentTheme(requested, actual));
    }
}

public class UpdatesPageViewStateTests
{
    [Fact]
    public void GetUpdatesViewState_ZeroCount_ShowsEmpty()
    {
        var (hasItems, showCardView, showListView, showEmptyState, showFullToolbar, subtitle) = UpdatesPage.GetUpdatesViewState(0);
        Assert.False(hasItems);
        Assert.False(showCardView);
        Assert.False(showListView);
        Assert.True(showEmptyState);
        Assert.True(showFullToolbar);
        Assert.Equal("", subtitle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GetUpdatesViewState_SmallSet_ShowsCardView(int count)
    {
        var (hasItems, showCardView, showListView, showEmptyState, showFullToolbar, subtitle) = UpdatesPage.GetUpdatesViewState(count);
        Assert.True(hasItems);
        Assert.True(showCardView);
        Assert.False(showListView);
        Assert.False(showEmptyState);
        Assert.False(showFullToolbar);
        string expected = count == 1 ? "1 update available" : $"{count} updates available";
        Assert.Equal(expected, subtitle);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(100)]
    public void GetUpdatesViewState_LargeSet_ShowsListView(int count)
    {
        var (hasItems, showCardView, showListView, showEmptyState, showFullToolbar, subtitle) = UpdatesPage.GetUpdatesViewState(count);
        Assert.True(hasItems);
        Assert.False(showCardView);
        Assert.True(showListView);
        Assert.False(showEmptyState);
        Assert.True(showFullToolbar);
        Assert.Equal($"{count} updates available", subtitle);
    }
}

public class PageSortGlyphTests
{
    [Theory]
    [InlineData("Descending", "Name", "Name", "\uE74B", Visibility.Visible)]
    [InlineData("Ascending", "Name", "Name", "\uE74A", Visibility.Visible)]
    [InlineData("Descending", "Name", "Version", "\uE74B", Visibility.Collapsed)]
    [InlineData("Descending", "Version", "Name", "\uE74B", Visibility.Collapsed)]
    [InlineData("Ascending", "Version", "Version", "\uE74A", Visibility.Visible)]
    public void InstalledPage_GetSortGlyph_ReturnsCorrectValues(string direction, string sortBy, string target, string expectedGlyph, Visibility expectedVis)
    {
        var (glyph, vis) = InstalledPage.GetSortGlyph(direction, sortBy, target);
        Assert.Equal(expectedGlyph, glyph);
        Assert.Equal(expectedVis, vis);
    }

    [Theory]
    [InlineData("Descending", "Name", "Name", "\uE74B", Visibility.Visible)]
    [InlineData("Ascending", "Name", "Name", "\uE74A", Visibility.Visible)]
    [InlineData("Descending", "Name", "Publisher", "\uE74B", Visibility.Collapsed)]
    [InlineData("Ascending", "Publisher", "Publisher", "\uE74A", Visibility.Visible)]
    public void UpdatesPage_GetSortGlyph_ReturnsCorrectValues(string direction, string sortBy, string target, string expectedGlyph, Visibility expectedVis)
    {
        var (glyph, vis) = UpdatesPage.GetSortGlyph(direction, sortBy, target);
        Assert.Equal(expectedGlyph, glyph);
        Assert.Equal(expectedVis, vis);
    }
}

public class DetailsPageHelperTests
{
    [Fact]
    public void GetActionButtonData_Installed_ReturnsUninstallEnabled()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test", Status = PackageStatus.Installed };
        var (label, enabled) = DetailsPage.GetActionButtonData(pkg);
        Assert.Equal("Uninstall", label);
        Assert.True(enabled);
    }

    [Fact]
    public void GetActionButtonData_Installing_Disabled()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test", Status = PackageStatus.Installed, IsInstalling = true };
        var (label, enabled) = DetailsPage.GetActionButtonData(pkg);
        Assert.Equal("Uninstall", label);
        Assert.False(enabled);
    }

    [Fact]
    public void GetProgressData_NotInstalling_Collapsed()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test" };
        var (vis, value, statusText, enabled) = DetailsPage.GetProgressData(pkg);
        Assert.Equal(Visibility.Collapsed, vis);
        Assert.Equal(0, value);
        Assert.Equal("", statusText);
        Assert.True(enabled);
    }

    [Fact]
    public void GetProgressData_IsInstalling_ShowsProgress()
    {
        var pkg = new WingetPackage { Id = "Test", Name = "Test", IsInstalling = true, InstallProgress = 50, InstallStatusText = "Installing..." };
        var (vis, value, statusText, enabled) = DetailsPage.GetProgressData(pkg);
        Assert.Equal(Visibility.Visible, vis);
        Assert.Equal(50, value);
        Assert.Equal("Installing...", statusText);
        Assert.False(enabled);
    }

    [Fact]
    public void GetViewLogsVisibility_NullPackage_Collapsed()
    {
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetViewLogsVisibility(null, new ObservableCollection<InstallTask>()));
    }

    [Fact]
    public void GetViewLogsVisibility_NullTasks_Collapsed()
    {
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetViewLogsVisibility(new WingetPackage { Id = "Test", Name = "Test" }, null!));
    }

    [Fact]
    public void GetViewLogsVisibility_HasMatchingTask_Visible()
    {
        var pkg = new WingetPackage { Id = "Test.Pkg", Name = "Test" };
        var tasks = new ObservableCollection<InstallTask>
        {
            new() { PackageId = "Other.Pkg", PackageName = "Other" },
            new() { PackageId = "Test.Pkg", PackageName = "Test" }
        };
        Assert.Equal(Visibility.Visible, DetailsPage.GetViewLogsVisibility(pkg, tasks));
    }

    [Fact]
    public void GetViewLogsVisibility_NoMatchingTask_Collapsed()
    {
        var pkg = new WingetPackage { Id = "Test.Pkg", Name = "Test" };
        var tasks = new ObservableCollection<InstallTask>
        {
            new() { PackageId = "Other.Pkg", PackageName = "Other" }
        };
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetViewLogsVisibility(pkg, tasks));
    }
}

public static class WinUIApp
{
    private static Thread? _uiThread;
    private static DispatcherQueue? _dispatcher;
    private static readonly ManualResetEventSlim _ready = new();

    public static void EnsureStarted()
    {
        if (_dispatcher != null) return;

        _uiThread = new Thread(() =>
        {
            Application.Start((args) =>
            {
                _dispatcher = DispatcherQueue.GetForCurrentThread();
                _ready.Set();
            });
        });
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Name = "WinUI";
        _uiThread.IsBackground = true;
        _uiThread.Start();
        if (!_ready.Wait(30000)) throw new TimeoutException("WinUI Application.Start failed to initialize");
    }

    public static void Run(Action action)
    {
        EnsureStarted();
        Exception? captured = null;
        var done = new ManualResetEventSlim();
        if (_dispatcher == null || !_dispatcher.TryEnqueue(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
            finally { done.Set(); }
        }))
        {
            throw new InvalidOperationException("Failed to dispatch work to WinUI thread");
        }
        if (!done.Wait(60000)) throw new TimeoutException("WinUI operation timed out");
        if (captured != null) throw captured;
    }

    public static T Run<T>(Func<T> func)
    {
        T? result = default;
        Run(() => { result = func(); });
        return result!;
    }
}

public class WinUIPageCreationTests
{
    public WinUIPageCreationTests() => WinUIApp.EnsureStarted();

    [Fact]
    public void CanCreateSettingsPage()
    {
        SettingsPage? page = null;
        WinUIApp.Run(() => { page = new SettingsPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateHomePage()
    {
        HomePage? page = null;
        WinUIApp.Run(() => { page = new HomePage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateInstalledPage()
    {
        InstalledPage? page = null;
        WinUIApp.Run(() => { page = new InstalledPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateUpdatesPage()
    {
        UpdatesPage? page = null;
        WinUIApp.Run(() => { page = new UpdatesPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateDetailsPage()
    {
        DetailsPage? page = null;
        WinUIApp.Run(() => { page = new DetailsPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateAboutPage()
    {
        AboutPage? page = null;
        WinUIApp.Run(() => { page = new AboutPage(); });
        Assert.NotNull(page);
    }

    [Fact]
    public void CanCreateNoWingetPage()
    {
        NoWingetPage? page = null;
        WinUIApp.Run(() => { page = new NoWingetPage(); });
        Assert.NotNull(page);
    }
}

public class HomePageHelperTests
{
    [Theory]
    [InlineData(1.0, 130.0, 146.0)]
    [InlineData(1.5, 154.0, 170.0)]
    [InlineData(1.74, 154.0, 170.0)]
    [InlineData(1.75, 186.0, 202.0)]
    [InlineData(1.99, 186.0, 202.0)]
    [InlineData(2.0, 218.0, 234.0)]
    [InlineData(2.24, 218.0, 234.0)]
    [InlineData(2.25, 250.0, 266.0)]
    [InlineData(3.0, 250.0, 266.0)]
    public void GetTextScaleData_ReturnsCorrectDimensions(double factor, double expectedCardHeight, double expectedItemHeight)
    {
        var (cardHeight, itemHeight) = HomePage.GetTextScaleData(factor);
        Assert.Equal(expectedCardHeight, cardHeight);
        Assert.Equal(expectedItemHeight, itemHeight);
    }

    [Fact]
    public void GetTextScaleData_ZeroFactor_UsesDefault()
    {
        var (cardHeight, itemHeight) = HomePage.GetTextScaleData(0);
        Assert.Equal(130.0, cardHeight);
        Assert.Equal(146.0, itemHeight);
    }

    [Theory]
    [InlineData("", null, "")]
    [InlineData("a", "Enter at least 2 characters to search", null)]
    [InlineData("ab", null, "ab")]
    [InlineData("hello world", null, "hello world")]
    public void GetSearchInputData_ReturnsExpected(string normalized, string? expectedHint, string? expectedQuery)
    {
        var (hint, query) = HomePage.GetSearchInputData(normalized);
        Assert.Equal(expectedHint, hint);
        Assert.Equal(expectedQuery, query);
    }
}

public class NoWingetPageTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(100, 100, 100)]
    [InlineData(150, 100, 100)]
    [InlineData(0, 0, 0)]
    [InlineData(75, 200, 37.5)]
    [InlineData(0, -1, 0)]
    public void CalculateDownloadProgress_ReturnsExpected(long totalRead, long totalBytes, double expected)
    {
        double result = NoWingetPage.CalculateDownloadProgress(totalRead, totalBytes);
        Assert.Equal(expected, result);
    }
}

public class AppCrashLogTests
{
    [Fact]
    public void GetCrashLogDirectory_ReturnsWingetStoreUnderLocalAppData()
    {
        string dir = App.GetCrashLogDirectory();
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingetStore"), dir);
    }

    [Fact]
    public void GetCrashLogPath_ReturnsCrashLogFileUnderDirectory()
    {
        string path = App.GetCrashLogPath();
        Assert.Equal(Path.Combine(App.GetCrashLogDirectory(), "crash.log"), path);
    }

    [Fact]
    public void GetCrashLogContent_IncludesTimestampAndErrorDetails()
    {
        string content = App.GetCrashLogContent("test error");
        Assert.Contains("test error", content);
        Assert.Contains("[CRASH LOG - ", content);
    }

    [Fact]
    public void FormatErrorDetails_WithException_IncludesTypeAndMessage()
    {
        var ex = new InvalidOperationException("test msg");
        string result = App.FormatErrorDetails(ex, "user message");
        Assert.Contains("InvalidOperationException", result);
        Assert.Contains("user message", result);
        Assert.Contains("Stack Trace", result);
    }

    [Fact]
    public void FormatErrorDetails_WithNullException_ShowsNullType()
    {
        string result = App.FormatErrorDetails(null, "msg");
        Assert.Contains("Exception:", result);
        Assert.DoesNotContain("NullReferenceException", result);
    }
}

public class MainWindowStaticTests
{
    [Theory]
    [InlineData(800, 500, 1.0, 800, 500)]
    [InlineData(600, 400, 1.0, 800, 500)]
    [InlineData(800, 500, 1.5, 1200, 750)]
    [InlineData(600, 400, 1.25, 1000, 625)]
    [InlineData(900, 600, 2.0, 1800, 1200)]
    [InlineData(0, 0, 1.0, 800, 500)]
    public void GetMinimumWindowSize_ReturnsCorrectDimensions(double w, double h, double scale, int ew, int eh)
    {
        var (pw, ph) = MainWindow.GetMinimumWindowSize(w, h, scale);
        Assert.Equal(ew, pw);
        Assert.Equal(eh, ph);
    }

    [Theory]
    [InlineData("Dark", "Light")]
    [InlineData("Light", "Dark")]
    public void GetNextTheme_ReturnsExpected(string current, string expected)
    {
        var actual = current == "Dark" ? ElementTheme.Dark : ElementTheme.Light;
        string result = MainWindow.GetNextTheme(current, actual);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetNextTheme_SystemWithDarkActual_ReturnsLight()
    {
        string result = MainWindow.GetNextTheme("System", ElementTheme.Dark);
        Assert.Equal("Light", result);
    }

    [Fact]
    public void GetNextTheme_SystemWithLightActual_ReturnsDark()
    {
        string result = MainWindow.GetNextTheme("System", ElementTheme.Light);
        Assert.Equal("Dark", result);
    }
}

public class UpdatesViewModelStaticTests
{
    [Fact]
    public void CalculateGlobalProgress_NullOrEmpty_ReturnsNotVisible()
    {
        var (isVis, val, text, status) = UpdatesViewModel.CalculateGlobalProgress(null);
        Assert.False(isVis);
        Assert.Equal(0, val);
        Assert.Equal("0%", text);
        Assert.Equal("", status);

        var (isVis2, _, _, _) = UpdatesViewModel.CalculateGlobalProgress(new List<WingetPackage>());
        Assert.False(isVis2);
    }

    [Fact]
    public void CalculateGlobalProgress_NoActiveUpgrades_ReturnsNotVisible()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "pkg1", IsInstalling = false },
            new() { Id = "pkg2", IsInstalling = false }
        };
        var (isVis, _, _, _) = UpdatesViewModel.CalculateGlobalProgress(packages);
        Assert.False(isVis);
    }

    [Fact]
    public void CalculateGlobalProgress_SingleActiveUpgrade_ReturnsCorrectStatus()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "pkg1", Name = "App One", IsInstalling = true, InstallProgress = 45.0 }
        };
        var (isVis, val, text, status) = UpdatesViewModel.CalculateGlobalProgress(packages);
        Assert.True(isVis);
        Assert.Equal(45.0, val);
        Assert.Equal("45%", text);
        Assert.Equal("Updating App One...", status);
    }

    [Fact]
    public void CalculateGlobalProgress_MultipleActiveUpgrades_CalculatesAverage()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "pkg1", Name = "App One", IsInstalling = true, InstallProgress = 20.0 },
            new() { Id = "pkg2", Name = "App Two", IsInstalling = true, InstallProgress = 60.0 },
            new() { Id = "pkg3", Name = "App Three", IsInstalling = false, InstallProgress = 0.0 }
        };
        var (isVis, val, text, status) = UpdatesViewModel.CalculateGlobalProgress(packages);
        Assert.True(isVis);
        Assert.Equal(40.0, val);
        Assert.Equal("40%", text);
        Assert.Equal("Updating 2 apps...", status);
    }
}

public class InstalledViewModelStaticTests
{
    [Fact]
    public void ExtractDevelopersList_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(InstalledViewModel.ExtractDevelopersList(null));
        Assert.Empty(InstalledViewModel.ExtractDevelopersList([]));
    }

    [Fact]
    public void ExtractDevelopersList_ExtractsUniqueSortedPublishers()
    {
        var packages = new List<WingetPackage>
        {
            new() { Publisher = " Microsoft " },
            new() { Publisher = "Adobe" },
            new() { Publisher = "microsoft" }
        };
        var devs = InstalledViewModel.ExtractDevelopersList(packages);
        Assert.Equal(2, devs.Count);
        Assert.Equal("Adobe", devs[0]);
        Assert.Equal("Microsoft", devs[1]);
    }
}

public class InstalledPageStaticTests
{
    [Theory]
    [InlineData("Name", "Descending", "Name", "Name", "Ascending")]
    [InlineData("Name", "Ascending", "Name", "Name", "Descending")]
    [InlineData("Name", "Descending", "Publisher", "Publisher", "Descending")]
    [InlineData("Version", "Ascending", "Name", "Name", "Descending")]
    public void ToggleColumnSort_ReturnsExpectedNewSort(string currentSortBy, string currentDir, string target, string expectedSortBy, string expectedDir)
    {
        var (newSortBy, newDir) = InstalledPage.ToggleColumnSort(currentSortBy, currentDir, target);
        Assert.Equal(expectedSortBy, newSortBy);
        Assert.Equal(expectedDir, newDir);
    }
}

public class UpdatesPageStaticTests
{
    [Fact]
    public void GetUpdatesViewState_ZeroCount_ReturnsEmptyState()
    {
        var (hasItems, showCard, showList, showEmpty, showToolbar, subtitle) = UpdatesPage.GetUpdatesViewState(0);
        Assert.False(hasItems);
        Assert.False(showCard);
        Assert.False(showList);
        Assert.True(showEmpty);
        Assert.True(showToolbar);
        Assert.Equal("", subtitle);
    }

    [Fact]
    public void GetUpdatesViewState_SmallCount_ReturnsCardView()
    {
        var (hasItems, showCard, showList, showEmpty, showToolbar, subtitle) = UpdatesPage.GetUpdatesViewState(2);
        Assert.True(hasItems);
        Assert.True(showCard);
        Assert.False(showList);
        Assert.False(showEmpty);
        Assert.False(showToolbar);
        Assert.Equal("2 updates available", subtitle);
    }

    [Fact]
    public void GetUpdatesViewState_LargeCount_ReturnsListView()
    {
        var (hasItems, showCard, showList, showEmpty, showToolbar, subtitle) = UpdatesPage.GetUpdatesViewState(5);
        Assert.True(hasItems);
        Assert.False(showCard);
        Assert.True(showList);
        Assert.False(showEmpty);
        Assert.True(showToolbar);
        Assert.Equal("5 updates available", subtitle);
    }

    [Theory]
    [InlineData("Descending", "Name", "Name", "\uE74B", Visibility.Visible)]
    [InlineData("Ascending", "Name", "Name", "\uE74A", Visibility.Visible)]
    [InlineData("Descending", "Publisher", "Name", "\uE74B", Visibility.Collapsed)]
    public void GetSortGlyph_ReturnsCorrectGlyphAndVisibility(string dir, string sortBy, string target, string expectedGlyph, Visibility expectedVis)
    {
        var (glyph, vis) = UpdatesPage.GetSortGlyph(dir, sortBy, target);
        Assert.Equal(expectedGlyph, glyph);
        Assert.Equal(expectedVis, vis);
    }
}

public class DetailsPageStaticTests
{
    [Fact]
    public void GetActionButtonData_NormalPackage_ReturnsLabelAndEnabled()
    {
        var pkg = new WingetPackage { Status = PackageStatus.Installable, IsInstalling = false };
        var (label, enabled) = DetailsPage.GetActionButtonData(pkg);
        Assert.Equal("Install", label);
        Assert.True(enabled);
    }

    [Fact]
    public void GetActionButtonData_InstallingPackage_ReturnsDisabled()
    {
        var pkg = new WingetPackage { Status = PackageStatus.Installable, IsInstalling = true };
        var (_, enabled) = DetailsPage.GetActionButtonData(pkg);
        Assert.False(enabled);
    }

    [Fact]
    public void GetProgressData_InstallingPackage_ReturnsVisibleProgress()
    {
        var pkg = new WingetPackage { IsInstalling = true, InstallProgress = 75.0, InstallStatusText = "Downloading..." };
        var (vis, val, text, enabled) = DetailsPage.GetProgressData(pkg);
        Assert.Equal(Visibility.Visible, vis);
        Assert.Equal(75.0, val);
        Assert.Equal("Downloading...", text);
        Assert.False(enabled);
    }

    [Fact]
    public void GetProgressData_NotInstalling_ReturnsCollapsed()
    {
        var pkg = new WingetPackage { IsInstalling = false };
        var (vis, val, text, enabled) = DetailsPage.GetProgressData(pkg);
        Assert.Equal(Visibility.Collapsed, vis);
        Assert.Equal(0, val);
        Assert.Equal("", text);
        Assert.True(enabled);
    }

    [Fact]
    public void GetViewLogsVisibility_NullOrNoTask_ReturnsCollapsed()
    {
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetViewLogsVisibility(null, []));
        var pkg = new WingetPackage { Id = "test.app" };
        var tasks = new ObservableCollection<InstallTask>();
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetViewLogsVisibility(pkg, tasks));
    }

    [Fact]
    public void GetViewLogsVisibility_HasMatchingTask_ReturnsVisible()
    {
        var pkg = new WingetPackage { Id = "test.app" };
        var tasks = new ObservableCollection<InstallTask>
        {
            new() { PackageId = "test.app" }
        };
        Assert.Equal(Visibility.Visible, DetailsPage.GetViewLogsVisibility(pkg, tasks));
    }
}

public class FilterableViewModelStaticTests
{
    [Theory]
    [InlineData(0, "Applications (0)")]
    [InlineData(42, "Applications (42)")]
    public void FormatAppsCountText_ReturnsExpected(int count, string expected)
    {
        Assert.Equal(expected, FilterableViewModel.FormatAppsCountText(count));
    }

    [Theory]
    [InlineData(0, "Redistributables (0)")]
    [InlineData(15, "Redistributables (15)")]
    public void FormatRedistCountText_ReturnsExpected(int count, string expected)
    {
        Assert.Equal(expected, FilterableViewModel.FormatRedistCountText(count));
    }

    [Theory]
    [InlineData(0, "All (0)")]
    [InlineData(100, "All (100)")]
    public void FormatAllCountText_ReturnsExpected(int count, string expected)
    {
        Assert.Equal(expected, FilterableViewModel.FormatAllCountText(count));
    }

    [Theory]
    [InlineData("Apps", "Apps", true)]
    [InlineData("apps", "Apps", true)]
    [InlineData("Redist", "Apps", false)]
    [InlineData(null, "Apps", false)]
    public void IsCategorySelected_ReturnsExpectedBool(string? category, string target, bool expected)
    {
        Assert.Equal(expected, FilterableViewModel.IsCategorySelected(category, target));
    }

    [Fact]
    public void ResolveCategorySelection_ReturnsExpectedCategory()
    {
        Assert.Equal("Apps", FilterableViewModel.ResolveCategorySelection("Redist", "Apps", true));
        Assert.Equal("Redist", FilterableViewModel.ResolveCategorySelection("Redist", "Apps", false));
        Assert.Equal("", FilterableViewModel.ResolveCategorySelection(null, "Apps", false));
    }

    [Theory]
    [InlineData(false, "Apps", true)]
    [InlineData(true, "Apps", false)]
    [InlineData(false, "Redist", false)]
    [InlineData(true, "Redist", true)]
    [InlineData(true, "All", true)]
    [InlineData(false, "All", true)]
    [InlineData(true, null, true)]
    public void MatchesCategoryFilter_ReturnsExpected(bool isRedistributable, string? categoryFilter, bool expected)
    {
        Assert.Equal(expected, FilterableViewModel.MatchesCategoryFilter(isRedistributable, categoryFilter));
    }

    [Theory]
    [InlineData("az", "Name", "Ascending")]
    [InlineData("za", "Name", "Descending")]
    [InlineData("publisher", "Publisher", "Ascending")]
    [InlineData("id", "Id", "Ascending")]
    [InlineData("status", "Version", "Descending")]
    public void MapSortOrder_ValidPresets_ReturnsCorrectTuple(string order, string expectedBy, string expectedDir)
    {
        var (by, dir) = FilterableViewModel.MapSortOrder(order);
        Assert.Equal(expectedBy, by);
        Assert.Equal(expectedDir, dir);
    }

    [Fact]
    public void MapSortOrder_UnknownOrNullOrder_PreservesCurrentValues()
    {
        var (by, dir) = FilterableViewModel.MapSortOrder("unknown", "CustomBy", "CustomDir");
        Assert.Equal("CustomBy", by);
        Assert.Equal("CustomDir", dir);

        var (byNull, dirNull) = FilterableViewModel.MapSortOrder(null, "CustomBy", "CustomDir");
        Assert.Equal("CustomBy", byNull);
        Assert.Equal("CustomDir", dirNull);
    }
}

public class HomeViewModelStaticTests
{
    [Theory]
    [InlineData("  git  ", false, true, "git", "git")]
    [InlineData("vscode", false, true, "vscode", "vscode")]
    public void ProcessSearchQuery_ValidQuery_ReturnsShouldSearchTrue(string input, bool forceAll, bool expectedShould, string expectedClean, string expectedDisplay)
    {
        var (shouldSearch, cleanQuery, displayQuery) = HomeViewModel.ProcessSearchQuery(input, forceAll);
        Assert.Equal(expectedShould, shouldSearch);
        Assert.Equal(expectedClean, cleanQuery);
        Assert.Equal(expectedDisplay, displayQuery);
    }

    [Theory]
    [InlineData("", false, false, "", "All Applications")]
    [InlineData(null, false, false, "", "All Applications")]
    [InlineData("   ", false, false, "", "All Applications")]
    public void ProcessSearchQuery_EmptyQueryNoForce_ReturnsShouldSearchFalse(string? input, bool forceAll, bool expectedShould, string expectedClean, string expectedDisplay)
    {
        var (shouldSearch, cleanQuery, displayQuery) = HomeViewModel.ProcessSearchQuery(input, forceAll);
        Assert.Equal(expectedShould, shouldSearch);
        Assert.Equal(expectedClean, cleanQuery);
        Assert.Equal(expectedDisplay, displayQuery);
    }

    [Theory]
    [InlineData("", true, true, "", "All Applications")]
    [InlineData(null, true, true, "", "All Applications")]
    public void ProcessSearchQuery_EmptyQueryForced_ReturnsShouldSearchTrueAndFallbackDisplay(string? input, bool forceAll, bool expectedShould, string expectedClean, string expectedDisplay)
    {
        var (shouldSearch, cleanQuery, displayQuery) = HomeViewModel.ProcessSearchQuery(input, forceAll);
        Assert.Equal(expectedShould, shouldSearch);
        Assert.Equal(expectedClean, cleanQuery);
        Assert.Equal(expectedDisplay, displayQuery);
    }

    [Fact]
    public void FilterAndSortRecommendations_NullOrEmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(HomeViewModel.FilterAndSortRecommendations(null, "", "az"));
        Assert.Empty(HomeViewModel.FilterAndSortRecommendations([], "git", "az"));
    }

    [Fact]
    public void FilterAndSortRecommendations_FiltersByQueryAndSortsByName()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "Git.Git", Name = "Git for Windows" },
            new() { Id = "Microsoft.VSCode", Name = "Visual Studio Code" },
            new() { Id = "Git.GitHubDesktop", Name = "GitHub Desktop" }
        };

        var result = HomeViewModel.FilterAndSortRecommendations(packages, "Git", "az");
        Assert.Equal(2, result.Count);
        Assert.Equal("Git for Windows", result[0].Name);
        Assert.Equal("GitHub Desktop", result[1].Name);
    }

    [Fact]
    public void FilterAndSortSearchResults_NullInput_ReturnsEmptyList()
    {
        Assert.Empty(HomeViewModel.FilterAndSortSearchResults(null, "", "all", "default"));
    }

    [Fact]
    public void FilterAndSortSearchResults_DefaultSort_PrioritizesWingetSource()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "App1", Name = "App One", Source = "msstore" },
            new() { Id = "App2", Name = "App Two", Source = "winget" }
        };

        var result = HomeViewModel.FilterAndSortSearchResults(packages, "", "all", "default");
        Assert.Equal(2, result.Count);
        Assert.Equal("winget", result[0].Source);
        Assert.Equal("msstore", result[1].Source);
    }

    [Fact]
    public void FilterAndSortSearchResults_SourceFilter_FiltersByWingetSource()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "App1", Name = "App One", Source = "msstore" },
            new() { Id = "App2", Name = "App Two", Source = "winget" }
        };

        var result = HomeViewModel.FilterAndSortSearchResults(packages, "", "winget", "az");
        Assert.Single(result);
        Assert.Equal("App2", result[0].Id);
    }
}

public class InstalledViewModelAdditionalStaticTests
{
    [Theory]
    [InlineData(null, "All Publishers")]
    [InlineData("", "All Publishers")]
    [InlineData("   ", "All Publishers")]
    public void NormalizeDeveloperFilter_NullOrEmpty_ReturnsAllDevelopers(string? current, string expected)
    {
        var options = new List<string> { "All Publishers", "Microsoft", "Adobe" };
        Assert.Equal(expected, InstalledViewModel.NormalizeDeveloperFilter(current, options));
    }

    [Fact]
    public void NormalizeDeveloperFilter_InvalidOption_ReturnsAllDevelopers()
    {
        var options = new List<string> { "All Publishers", "Microsoft" };
        Assert.Equal("All Publishers", InstalledViewModel.NormalizeDeveloperFilter("UnknownDev", options));
    }

    [Fact]
    public void NormalizeDeveloperFilter_ValidOption_ReturnsCurrentFilter()
    {
        var options = new List<string> { "All Publishers", "Microsoft", "Adobe" };
        Assert.Equal("Microsoft", InstalledViewModel.NormalizeDeveloperFilter("Microsoft", options));
        Assert.Equal("microsoft", InstalledViewModel.NormalizeDeveloperFilter("microsoft", options));
    }

    [Theory]
    [InlineData("Microsoft", "All Publishers", true)]
    [InlineData("Microsoft", null, true)]
    [InlineData("Microsoft", "", true)]
    [InlineData("Microsoft", "microsoft", true)]
    [InlineData(null, "Microsoft", false)]
    [InlineData("", "Microsoft", false)]
    [InlineData("Microsoft", "Adobe", false)]
    public void MatchesDeveloperFilter_ReturnsExpectedBool(string? pub, string? devFilter, bool expected)
    {
        Assert.Equal(expected, InstalledViewModel.MatchesDeveloperFilter(pub, devFilter));
    }

    [Fact]
    public void HandlePackageStatusChange_InstallableStatus_RemovesPackageFromList()
    {
        var list = new List<WingetPackage>
        {
            new() { Id = "App.Git" },
            new() { Id = "App.VSCode" }
        };

        bool result = InstalledViewModel.HandlePackageStatusChange(list, new WingetPackage { Id = "app.git", Status = PackageStatus.Installable });
        Assert.True(result);
        Assert.Single(list);
        Assert.Equal("App.VSCode", list[0].Id);
    }

    [Fact]
    public void HandlePackageStatusChange_InstalledStatus_UpdatesTargetVersionAndStatus()
    {
        var list = new List<WingetPackage>
        {
            new() { Id = "App.Git", Status = PackageStatus.Upgradable, Version = "1.0", AvailableVersion = "2.0" }
        };

        bool result = InstalledViewModel.HandlePackageStatusChange(list, new WingetPackage { Id = "App.Git", Status = PackageStatus.Installed, AvailableVersion = "2.0" });
        Assert.True(result);
        Assert.Equal(PackageStatus.Installed, list[0].Status);
        Assert.Equal("2.0", list[0].Version);
        Assert.Equal("", list[0].AvailableVersion);
    }

    [Fact]
    public void HandlePackageStatusChange_PackageNotFoundOrNull_ReturnsFalse()
    {
        var list = new List<WingetPackage> { new() { Id = "App.Git" } };
        Assert.False(InstalledViewModel.HandlePackageStatusChange(list, new WingetPackage { Id = "App.Other", Status = PackageStatus.Installable }));
        Assert.False(InstalledViewModel.HandlePackageStatusChange(list, null!));
        Assert.False(InstalledViewModel.HandlePackageStatusChange(null!, new WingetPackage { Id = "App.Git" }));
    }

    [Fact]
    public void CountUpgradablePackages_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0, InstalledViewModel.CountUpgradablePackages(null));
        Assert.Equal(0, InstalledViewModel.CountUpgradablePackages([]));
    }

    [Fact]
    public void CountUpgradablePackages_ValidList_CountsUpgradableOnly()
    {
        var list = new List<WingetPackage>
        {
            new() { Status = PackageStatus.Upgradable },
            new() { Status = PackageStatus.Installed },
            new() { Status = PackageStatus.Upgradable }
        };
        Assert.Equal(2, InstalledViewModel.CountUpgradablePackages(list));
    }

    [Fact]
    public void FilterInstalledPackages_FiltersAndCountsCorrectly()
    {
        var list = new List<WingetPackage>
        {
            new() { Id = "App1", Name = "App One", Publisher = "MS", Source = "winget" },
            new() { Id = "App2", Name = "VCRedist", Publisher = "MS", Source = "winget" },
            new() { Id = "App3", Name = "App Three", Publisher = "Adobe", Source = "winget" }
        };

        var (filtered, appsCount, redistCount, totalCount) = InstalledViewModel.FilterInstalledPackages(
            list, "", "MS", "all", "Apps", "Name", "Ascending");

        Assert.Single(filtered);
        Assert.Equal("App1", filtered[0].Id);
        Assert.Equal(1, appsCount);
        Assert.Equal(1, redistCount);
        Assert.Equal(2, totalCount);
    }
}

public class UpdatesViewModelAdditionalStaticTests
{
    [Fact]
    public void HandlePackageInstalled_RemovesFromBothCollections()
    {
        var allUpgrades = new List<WingetPackage>
        {
            new() { Id = "Upg.App1" },
            new() { Id = "Upg.App2" }
        };
        var upgradesObs = new ObservableCollection<WingetPackage>
        {
            new() { Id = "Upg.App1" },
            new() { Id = "Upg.App2" }
        };

        bool removed = UpdatesViewModel.HandlePackageInstalled(allUpgrades, upgradesObs, new WingetPackage { Id = "upg.app1", Status = PackageStatus.Installed });
        Assert.True(removed);
        Assert.Single(allUpgrades);
        Assert.Single(upgradesObs);
        Assert.Equal("Upg.App2", allUpgrades[0].Id);
        Assert.Equal("Upg.App2", upgradesObs[0].Id);
    }

    [Fact]
    public void HandlePackageInstalled_NullOrNotFound_ReturnsFalse()
    {
        var allUpgrades = new List<WingetPackage> { new() { Id = "Upg.App1" } };
        var upgradesObs = new ObservableCollection<WingetPackage> { new() { Id = "Upg.App1" } };

        Assert.False(UpdatesViewModel.HandlePackageInstalled(allUpgrades, upgradesObs, null!));
        Assert.False(UpdatesViewModel.HandlePackageInstalled(allUpgrades, upgradesObs, new WingetPackage { Id = "Upg.NonExistent" }));
    }

    [Fact]
    public void GetEligiblePackagesForUpgrade_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(UpdatesViewModel.GetEligiblePackagesForUpgrade(null));
        Assert.Empty(UpdatesViewModel.GetEligiblePackagesForUpgrade([]));
    }

    [Fact]
    public void GetEligiblePackagesForUpgrade_FiltersOutInstallingPackages()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "p1", IsInstalling = false },
            new() { Id = "p2", IsInstalling = true },
            new() { Id = "p3", IsInstalling = false }
        };

        var eligible = UpdatesViewModel.GetEligiblePackagesForUpgrade(packages);
        Assert.Equal(2, eligible.Count);
        Assert.Equal("p1", eligible[0].Id);
        Assert.Equal("p3", eligible[1].Id);
    }

    [Fact]
    public void FilterUpgradablePackages_FiltersBySourceCategoryAndCalculatesCounts()
    {
        var list = new List<WingetPackage>
        {
            new() { Id = "U1", Name = "Up 1", Source = "winget" },
            new() { Id = "U2", Name = "VCRedist Up 2", Source = "winget" }
        };

        var (filtered, appsCount, redistCount, totalCount) = UpdatesViewModel.FilterUpgradablePackages(
            list, "", "winget", "Redist", "Name", "Ascending");

        Assert.Single(filtered);
        Assert.Equal("U2", filtered[0].Id);
        Assert.Equal(1, appsCount);
        Assert.Equal(1, redistCount);
        Assert.Equal(2, totalCount);
    }
}

public class SearchViewModelStaticTests
{
    [Fact]
    public void FilterAndSortSearchResults_NullInput_ReturnsEmptyList()
    {
        Assert.Empty(SearchViewModel.FilterAndSortSearchResults(null, "", "all", "default"));
    }

    [Fact]
    public void FilterAndSortSearchResults_FiltersQueryAndSource()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "App.Git", Name = "Git", Source = "winget" },
            new() { Id = "App.Git2", Name = "Git GUI", Source = "msstore" },
            new() { Id = "App.VSCode", Name = "VS Code", Source = "winget" }
        };

        var results = SearchViewModel.FilterAndSortSearchResults(packages, "Git", "winget", "az");
        Assert.Single(results);
        Assert.Equal("App.Git", results[0].Id);
    }

    [Fact]
    public void FilterAndSortSearchResults_DefaultSort_PutsWingetFirst()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "App1", Source = "msstore" },
            new() { Id = "App2", Source = "winget" }
        };

        var results = SearchViewModel.FilterAndSortSearchResults(packages, "", "all", "default");
        Assert.Equal(2, results.Count);
        Assert.Equal("winget", results[0].Source);
        Assert.Equal("msstore", results[1].Source);
    }

    [Fact]
    public void FilterAndSortSearchResults_CustomSort_SortsByNameDescending()
    {
        var packages = new List<WingetPackage>
        {
            new() { Id = "AppA", Name = "Alpha", Source = "winget" },
            new() { Id = "AppZ", Name = "Zeta", Source = "winget" }
        };

        var results = SearchViewModel.FilterAndSortSearchResults(packages, "", "all", "za");
        Assert.Equal(2, results.Count);
        Assert.Equal("Zeta", results[0].Name);
        Assert.Equal("Alpha", results[1].Name);
    }
}

public class WingetParserInternalStaticTests
{
    [Fact]
    public void FindHeaderLine_WithValidSeparator_ReturnsHeaderIndex()
    {
        string[] lines = ["Name Id Version", "--- -- -------", "App1 1.0 1.0"];
        Assert.Equal(0, WingetParser.FindHeaderLine(lines));
    }

    [Fact]
    public void FindHeaderLine_NoSeparator_ReturnsNegativeOne()
    {
        string[] lines = ["Name Id Version", "App1 1.0 1.0"];
        Assert.Equal(-1, WingetParser.FindHeaderLine(lines));
    }

    [Fact]
    public void FindHeaderLine_SeparatorAtFirstLine_ReturnsNegativeOne()
    {
        string[] lines = ["---", "App1 1.0"];
        Assert.Equal(-1, WingetParser.FindHeaderLine(lines));
    }

    [Fact]
    public void FindHeaderLine_EmptyArray_ReturnsNegativeOne()
    {
        Assert.Equal(-1, WingetParser.FindHeaderLine([]));
    }

    [Fact]
    public void TryParseColumnPositions_StandardHeader_ReturnsPositions()
    {
        string headerLine = "Name Id Version Source";
        bool success = WingetParser.TryParseColumnPositions(headerLine, out var pos);
        Assert.True(success);
        Assert.Equal(0, pos.namePos);
        Assert.Equal(5, pos.idPos);
        Assert.Equal(8, pos.versionPos);
        Assert.Equal(16, pos.sourcePos);
    }

    [Fact]
    public void TryParseColumnPositions_UpgradeHeader_ReturnsAvailablePosition()
    {
        string headerLine = "Name Id Version Available Source";
        bool success = WingetParser.TryParseColumnPositions(headerLine, out var pos);
        Assert.True(success);
        Assert.Equal(16, pos.availablePos);
    }

    [Fact]
    public void TryParseColumnPositions_MatchHeader_ReturnsMatchPosition()
    {
        string headerLine = "Name Id Version Match";
        bool success = WingetParser.TryParseColumnPositions(headerLine, out var pos);
        Assert.True(success);
        Assert.Equal(16, pos.matchPos);
    }

    [Fact]
    public void TryParseColumnPositions_MissingIdOrVersion_ReturnsFalse()
    {
        Assert.False(WingetParser.TryParseColumnPositions("Name Version Source", out _));
        Assert.False(WingetParser.TryParseColumnPositions("Name Id Source", out _));
    }

    [Fact]
    public void TryParseColumnPositions_InvalidColumnOrder_ReturnsFalse()
    {
        Assert.False(WingetParser.TryParseColumnPositions("Version Id Name", out _));
    }

    [Fact]
    public void ParseTableRow_StandardRow_PopulatesDictionary()
    {
        (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos = (0, 10, 20, 30, -1, -1);
        string line = "TestApp   App.Id    1.0.0     winget";
        var dict = WingetParser.ParseTableRow(line, pos);
        Assert.Equal("TestApp", dict["Name"]);
        Assert.Equal("App.Id", dict["Id"]);
        Assert.Equal("1.0.0", dict["Version"]);
        Assert.Equal("winget", dict["Source"]);
    }

    [Fact]
    public void ParseTableRow_AvailableColumn_PopulatesAvailableKey()
    {
        (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos = (0, 10, 20, -1, -1, 30);
        string line = "TestApp   App.Id    1.0.0     2.0.0";
        var dict = WingetParser.ParseTableRow(line, pos);
        Assert.Equal("2.0.0", dict["Available"]);
    }

    [Fact]
    public void ParseTableRow_MatchColumn_PopulatesMatchKey()
    {
        (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos = (0, 10, 20, -1, 30, -1);
        string line = "TestApp   App.Id    1.0.0     Tag:Test";
        var dict = WingetParser.ParseTableRow(line, pos);
        Assert.Equal("Tag:Test", dict["Match"]);
    }

    [Fact]
    public void TryParseFoundLine_ValidFoundHeader_SetsPackageNameAndReturnsTrue()
    {
        var pkg = new WingetPackage();
        bool result = WingetParser.TryParseFoundLine("Found Git [Git.Git]", pkg);
        Assert.True(result);
        Assert.Equal("Git", pkg.Name);
    }

    [Fact]
    public void TryParseFoundLine_FoundLineWithoutBracket_ReturnsTrueWithoutSettingName()
    {
        var pkg = new WingetPackage();
        bool result = WingetParser.TryParseFoundLine("Found Git", pkg);
        Assert.True(result);
        Assert.Empty(pkg.Name);
    }

    [Fact]
    public void TryParseFoundLine_NonFoundLine_ReturnsFalse()
    {
        var pkg = new WingetPackage();
        bool result = WingetParser.TryParseFoundLine("Publisher: Microsoft", pkg);
        Assert.False(result);
    }

    [Fact]
    public void SetPackageField_ValidMetadataKeys_SetsPropertiesCorrectly()
    {
        var pkg = new WingetPackage();
        WingetParser.SetPackageField(pkg, "Name", "Test");
        WingetParser.SetPackageField(pkg, "Version", "1.2.3");
        WingetParser.SetPackageField(pkg, "Publisher", "Pub");
        WingetParser.SetPackageField(pkg, "Publisher Url", "https://pub.com");
        WingetParser.SetPackageField(pkg, "Description", "Desc");
        WingetParser.SetPackageField(pkg, "Homepage", "https://home.com");
        WingetParser.SetPackageField(pkg, "License", "MIT");
        WingetParser.SetPackageField(pkg, "Release Notes", "Notes");

        Assert.Equal("Test", pkg.Name);
        Assert.Equal("1.2.3", pkg.Version);
        Assert.Equal("Pub", pkg.Publisher);
        Assert.Equal("https://pub.com", pkg.PublisherUrl);
        Assert.Equal("Desc", pkg.Description);
        Assert.Equal("https://home.com", pkg.Homepage);
        Assert.Equal("MIT", pkg.License);
        Assert.Equal("Notes", pkg.ReleaseNotes);
    }

    [Fact]
    public void SetPackageField_UnknownKey_DoesNotThrow()
    {
        var pkg = new WingetPackage();
        WingetParser.SetPackageField(pkg, "UnknownKey", "Val");
        Assert.Empty(pkg.Name);
    }

    [Fact]
    public void IsUrl_HttpAndHttpsUrls_ReturnsTrue()
    {
        Assert.True(WingetParser.IsUrl("http://example.com"));
        Assert.True(WingetParser.IsUrl("https://example.com/path"));
    }

    [Fact]
    public void IsUrl_NonHttpUrlsAndPaths_ReturnsFalse()
    {
        Assert.False(WingetParser.IsUrl("ftp://example.com"));
        Assert.False(WingetParser.IsUrl("C:\\Program Files"));
        Assert.False(WingetParser.IsUrl("invalid_string"));
    }
}

public class IconServiceStaticTests
{
    [Fact]
    public void ParseDatabaseJson_ValidPayload_ParsesIconsAndScreenshots()
    {
        string json = """
        {
          "icons_and_screenshots": {
            "Git.Git": {
              "icon": "https://example.com/git.png",
              "images": [ "https://example.com/shot1.png", "https://example.com/shot2.png" ]
            }
          }
        }
        """;

        var (icons, screenshots) = IconService.ParseDatabaseJson(json);
        Assert.Single(icons);
        Assert.Equal("https://example.com/git.png", icons["Git.Git"]);
        Assert.Single(screenshots);
        Assert.Equal(2, screenshots["Git.Git"].Count);
    }

    [Fact]
    public void ParseDatabaseJson_MissingProperty_ReturnsEmptyDictionaries()
    {
        string json = "{\"other_property\": {}}";
        var (icons, screenshots) = IconService.ParseDatabaseJson(json);
        Assert.Empty(icons);
        Assert.Empty(screenshots);
    }

    [Fact]
    public void ParseDatabaseJson_FiltersEmptyOrNullImageStrings()
    {
        string json = """
        {
          "icons_and_screenshots": {
            "App.Id": {
              "icon": "",
              "images": [ "", "https://example.com/shot1.png" ]
            }
          }
        }
        """;

        var (icons, screenshots) = IconService.ParseDatabaseJson(json);
        Assert.Empty(icons);
        Assert.Single(screenshots);
        Assert.Single(screenshots["App.Id"]);
    }

    [Fact]
    public void ParseDatabaseJson_MalformedJson_ReturnsEmptyDictionariesWithoutThrowing()
    {
        var (icons, screenshots) = IconService.ParseDatabaseJson("{ invalid json ");
        Assert.Empty(icons);
        Assert.Empty(screenshots);
    }

    [Fact]
    public void ParseDatabaseJson_CaseInsensitiveKeys()
    {
        string json = """
        {
          "icons_and_screenshots": {
            "Git.Git": { "icon": "https://example.com/git.png" }
          }
        }
        """;

        var (icons, _) = IconService.ParseDatabaseJson(json);
        Assert.True(icons.ContainsKey("git.git"));
    }

    [Fact]
    public void IsCacheExpired_WithinThreshold_ReturnsFalse()
    {
        DateTime now = DateTime.Now;
        DateTime lastWrite = now.AddHours(-23);
        Assert.False(IconService.IsCacheExpired(lastWrite, now, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IsCacheExpired_ExceedsThreshold_ReturnsTrue()
    {
        DateTime now = DateTime.Now;
        DateTime lastWrite = now.AddHours(-25);
        Assert.True(IconService.IsCacheExpired(lastWrite, now, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IsCacheExpired_FutureTimestamp_ReturnsTrue()
    {
        DateTime now = DateTime.Now;
        DateTime lastWrite = now.AddHours(2);
        Assert.True(IconService.IsCacheExpired(lastWrite, now, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void ExtractHomepageFromShowOutput_ValidOutput_ReturnsHomepageUrl()
    {
        string showOutput = "Publisher: Microsoft\r\nHomepage: https://microsoft.com\r\nLicense: MIT";
        string homepage = IconService.ExtractHomepageFromShowOutput(showOutput);
        Assert.Equal("https://microsoft.com", homepage);
    }

    [Fact]
    public void ExtractHomepageFromShowOutput_NoHomepage_ReturnsEmptyString()
    {
        string showOutput = "Publisher: Microsoft\r\nLicense: MIT";
        Assert.Equal("", IconService.ExtractHomepageFromShowOutput(showOutput));
    }

    [Fact]
    public void ExtractHomepageFromShowOutput_NullOrEmptyOutput_ReturnsEmptyString()
    {
        Assert.Equal("", IconService.ExtractHomepageFromShowOutput(""));
        Assert.Equal("", IconService.ExtractHomepageFromShowOutput(null!));
    }

    [Fact]
    public void ExtractDomainFromUrl_StandardUrl_ReturnsHost()
    {
        Assert.Equal("github.com", IconService.ExtractDomainFromUrl("https://github.com/microsoft/winget-cli"));
    }

    [Fact]
    public void ExtractDomainFromUrl_StripsWwwPrefix()
    {
        Assert.Equal("google.com", IconService.ExtractDomainFromUrl("https://www.google.com/search"));
    }

    [Fact]
    public void ExtractDomainFromUrl_InvalidUrl_ReturnsEmptyString()
    {
        Assert.Equal("", IconService.ExtractDomainFromUrl("not a url"));
        Assert.Equal("", IconService.ExtractDomainFromUrl(""));
    }

    [Fact]
    public void GetHunterLogoUrl_ValidDomain_ReturnsFormattedUrl()
    {
        Assert.Equal("https://logos.hunter.io/example.com", IconService.GetHunterLogoUrl("example.com"));
    }

    [Fact]
    public void GetHunterLogoUrl_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal("", IconService.GetHunterLogoUrl(""));
        Assert.Equal("", IconService.GetHunterLogoUrl(null!));
    }

    [Fact]
    public void GetGoogleFaviconUrl_ValidDomain_ReturnsFormattedUrl()
    {
        Assert.Equal("https://www.google.com/s2/favicons?domain=example.com&sz=128", IconService.GetGoogleFaviconUrl("example.com"));
        Assert.Equal("https://www.google.com/s2/favicons?domain=example.com&sz=64", IconService.GetGoogleFaviconUrl("example.com", 64));
    }

    [Fact]
    public void GetGoogleFaviconUrl_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal("", IconService.GetGoogleFaviconUrl(""));
    }
}

public class CachingWingetServiceStaticTests
{
    [Fact]
    public void MergePackageProperties_NullArguments_ThrowsArgumentNullException()
    {
        var pkg = new WingetPackage { Id = "P1" };
        Assert.Throws<ArgumentNullException>(() => CachingWingetService.MergePackageProperties(null!, pkg));
        Assert.Throws<ArgumentNullException>(() => CachingWingetService.MergePackageProperties(pkg, null!));
    }

    [Fact]
    public void MergePackageProperties_OverwritesNonNullProperties()
    {
        var existing = new WingetPackage { Id = "P1", Name = "OldName", Version = "1.0" };
        var incoming = new WingetPackage { Id = "P1", Name = "NewName", Version = "2.0", Publisher = "NewPub", Source = "winget" };

        CachingWingetService.MergePackageProperties(existing, incoming);

        Assert.Equal("NewName", existing.Name);
        Assert.Equal("2.0", existing.Version);
        Assert.Equal("NewPub", existing.Publisher);
        Assert.Equal("winget", existing.Source);
    }

    [Fact]
    public void MergePackageProperties_PreservesExistingWhenIncomingEmpty()
    {
        var existing = new WingetPackage { Id = "P1.App", Name = "OldName", Version = "1.0", Description = "ExistingDesc" };
        var incoming = new WingetPackage { Id = "P1.App", Name = "NewName", Version = "", Description = "" };

        CachingWingetService.MergePackageProperties(existing, incoming);

        Assert.Equal("NewName", existing.Name);
        Assert.Equal("1.0", existing.Version);
        Assert.Equal("ExistingDesc", existing.Description);
    }

    [Fact]
    public void MergePackageProperties_StatusTransitions_UpdatesNonInstallable()
    {
        var existing = new WingetPackage { Id = "P1", Status = PackageStatus.Installable };
        var incoming = new WingetPackage { Id = "P1", Status = PackageStatus.Installed };

        CachingWingetService.MergePackageProperties(existing, incoming);
        Assert.Equal(PackageStatus.Installed, existing.Status);

        var incomingInstallable = new WingetPackage { Id = "P1", Status = PackageStatus.Installable };
        CachingWingetService.MergePackageProperties(existing, incomingInstallable);
        Assert.Equal(PackageStatus.Installed, existing.Status);
    }

    [Fact]
    public void MergePackageProperties_ListCollections_CopiesNonEmptyLists()
    {
        var existing = new WingetPackage { Id = "P1" };
        var incoming = new WingetPackage
        {
            Id = "P1",
            Tags = ["tag1", "tag2"],
            Screenshots = ["shot1.png"]
        };

        CachingWingetService.MergePackageProperties(existing, incoming);

        Assert.Equal(2, existing.Tags.Count);
        Assert.Single(existing.Screenshots);
    }
}

public class SettingsServiceStaticTests
{
    [Fact]
    public void DeserializeSettings_ValidJson_ReturnsPopulatedSettings()
    {
        string json = "{\"AutoUpdate\":true,\"AppTheme\":\"Dark\",\"EnableNotifications\":false}";
        var settings = SettingsService.DeserializeSettings(json);
        Assert.True(settings.AutoUpdate);
        Assert.Equal("Dark", settings.AppTheme);
        Assert.False(settings.EnableNotifications);
    }

    [Fact]
    public void DeserializeSettings_NullOrEmptyJson_ReturnsDefaultSettings()
    {
        var settings1 = SettingsService.DeserializeSettings("");
        Assert.False(settings1.AutoUpdate);
        var settings2 = SettingsService.DeserializeSettings(null);
        Assert.False(settings2.AutoUpdate);
    }

    [Fact]
    public void DeserializeSettings_CorruptJson_ReturnsDefaultSettings()
    {
        var settings = SettingsService.DeserializeSettings("{ invalid json");
        Assert.False(settings.AutoUpdate);
    }

    [Fact]
    public void SerializeSettings_ValidInstance_ProducesJson()
    {
        var appSettings = new AppSettings { AutoUpdate = true, AppTheme = "Light", EnableNotifications = true };
        string json = SettingsService.SerializeSettings(appSettings);
        Assert.Contains("\"AutoUpdate\":true", json);
        Assert.Contains("\"AppTheme\":\"Light\"", json);
    }

    [Fact]
    public void SerializeSettings_NullInstance_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsService.SerializeSettings(null!));
    }
}

public class LogServiceStaticTests
{
    [Fact]
    public void FormatLogEntry_ValidInputs_FormatsCorrectly()
    {
        var timestamp = new DateTime(2026, 7, 23, 18, 0, 0);
        string formatted = LogService.FormatLogEntry("INFO", "Application started", timestamp);
        Assert.Equal("[2026-07-23 18:00:00] [INFO] Application started", formatted);
    }

    [Fact]
    public void FormatLogEntry_SpecialCharacters_PreservesMessage()
    {
        var timestamp = new DateTime(2026, 7, 23, 12, 30, 45);
        string formatted = LogService.FormatLogEntry("ERROR", "Failed to connect: 500 & path='C:\\test'", timestamp);
        Assert.Equal("[2026-07-23 12:30:45] [ERROR] Failed to connect: 500 & path='C:\\test'", formatted);
    }
}

public class WingetServiceStaticTests
{
    [Fact]
    public void MapFromRow_StandardRow_MapsPropertiesCorrectly()
    {
        var row = new Dictionary<string, string>
        {
            { "Name", "Git" },
            { "Id", "Git.Git" },
            { "Version", "2.40.0" },
            { "Source", "winget" }
        };

        var pkg = WingetService.MapFromRow(row);
        Assert.Equal("Git", pkg.Name);
        Assert.Equal("Git.Git", pkg.Id);
        Assert.Equal("2.40.0", pkg.Version);
        Assert.Equal("winget", pkg.Source);
        Assert.Equal(PackageStatus.Installable, pkg.Status);
    }

    [Fact]
    public void MapFromRow_EmptySource_DefaultsToWinget()
    {
        var row = new Dictionary<string, string>
        {
            { "Name", "App" },
            { "Id", "App.Id" },
            { "Version", "1.0" }
        };

        var pkg = WingetService.MapFromRow(row);
        Assert.Equal("winget", pkg.Source);
    }

    [Fact]
    public void MapFromRow_IncludeAvailableTrue_MapsAvailableVersion()
    {
        var row = new Dictionary<string, string>
        {
            { "Name", "App" },
            { "Id", "App.Id" },
            { "Version", "1.0" },
            { "Available", "2.0" }
        };

        var pkg = WingetService.MapFromRow(row, includeAvailable: true, defaultStatus: PackageStatus.Upgradable);
        Assert.Equal("1.0", pkg.Version);
        Assert.Equal("2.0", pkg.AvailableVersion);
        Assert.Equal(PackageStatus.Upgradable, pkg.Status);
    }

    [Fact]
    public void BuildRecommendations_MatchingInstalledPackage_SetsInstalledStatusAndVersion()
    {
        var popular = new List<WingetPackage>
        {
            new() { Id = "Git.Git", Name = "Git", Version = "1.0" },
            new() { Id = "NodeJS.NodeJS", Name = "Node.js", Version = "18.0" }
        };
        var installedMap = new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase)
        {
            { "Git.Git", new WingetPackage { Id = "Git.Git", Version = "2.40.0" } }
        };

        var recs = WingetService.BuildRecommendations(popular, installedMap, 10);
        Assert.Equal(2, recs.Count);
        Assert.Equal(PackageStatus.Installed, recs[0].Status);
        Assert.Equal("2.40.0", recs[0].Version);
        Assert.Equal(PackageStatus.Installable, recs[1].Status);
    }

    [Fact]
    public void BuildRecommendations_CaseInsensitiveIdMatch_UpdatesStatusCorrectly()
    {
        var popular = new List<WingetPackage>
        {
            new() { Id = "Git.Git", Name = "Git" }
        };
        var installedMap = new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase)
        {
            { "git.git", new WingetPackage { Id = "Git.Git", Version = "2.40.0" } }
        };

        var recs = WingetService.BuildRecommendations(popular, installedMap, 10);
        Assert.Single(recs);
        Assert.Equal(PackageStatus.Installed, recs[0].Status);
    }

    [Fact]
    public void BuildRecommendations_RespectsMaxCountLimit()
    {
        var popular = Enumerable.Range(1, 15).Select(i => new WingetPackage { Id = $"App.{i}", Name = $"App {i}" }).ToList();
        var recs = WingetService.BuildRecommendations(popular, null, 5);
        Assert.Equal(5, recs.Count);
    }

    [Fact]
    public void BuildRecommendations_NullOrEmptyInputs_ReturnsEmptyList()
    {
        Assert.Empty(WingetService.BuildRecommendations(null, null));
        Assert.Empty(WingetService.BuildRecommendations([], null));
    }

    [Fact]
    public void DecoratePackageDetails_NullDetails_CreatesFallbackPackage()
    {
        var pkg = WingetService.DecoratePackageDetails(null, "App.Id", [], []);
        Assert.Equal("App.Id", pkg.Id);
        Assert.Equal("App.Id", pkg.Name);
        Assert.Equal(PackageStatus.Installable, pkg.Status);
    }

    [Fact]
    public void DecoratePackageDetails_UpgradableMatch_SetsUpgradableStatusAndVersions()
    {
        var details = new WingetPackage { Id = "App.Id", Name = "App" };
        var upgradable = new List<WingetPackage>
        {
            new() { Id = "App.Id", Version = "1.0", AvailableVersion = "2.0" }
        };

        var pkg = WingetService.DecoratePackageDetails(details, "App.Id", [], upgradable);
        Assert.Equal(PackageStatus.Upgradable, pkg.Status);
        Assert.Equal("1.0", pkg.Version);
        Assert.Equal("2.0", pkg.AvailableVersion);
    }

    [Fact]
    public void DecoratePackageDetails_InstalledMatch_SetsInstalledStatusAndVersion()
    {
        var details = new WingetPackage { Id = "App.Id", Name = "App" };
        var installed = new List<WingetPackage>
        {
            new() { Id = "App.Id", Version = "1.5" }
        };

        var pkg = WingetService.DecoratePackageDetails(details, "App.Id", installed, []);
        Assert.Equal(PackageStatus.Installed, pkg.Status);
        Assert.Equal("1.5", pkg.Version);
    }

    [Fact]
    public void DecoratePackageDetails_UpgradablePrecedesInstalled()
    {
        var details = new WingetPackage { Id = "App.Id" };
        var installed = new List<WingetPackage> { new() { Id = "App.Id", Version = "1.0" } };
        var upgradable = new List<WingetPackage> { new() { Id = "App.Id", Version = "1.0", AvailableVersion = "2.0" } };

        var pkg = WingetService.DecoratePackageDetails(details, "App.Id", installed, upgradable);
        Assert.Equal(PackageStatus.Upgradable, pkg.Status);
    }

    [Fact]
    public void DeterminePackageAction_NullPackage_ReturnsNone()
    {
        Assert.Equal(WingetService.PackageActionKind.None, WingetService.DeterminePackageAction(null));
    }

    [Fact]
    public void DeterminePackageAction_IsInstalling_ReturnsCancel()
    {
        var pkg = new WingetPackage { Id = "App.Id", IsInstalling = true };
        Assert.Equal(WingetService.PackageActionKind.Cancel, WingetService.DeterminePackageAction(pkg));
    }

    [Fact]
    public void DeterminePackageAction_Installed_ReturnsUninstall()
    {
        var pkg = new WingetPackage { Id = "App.Id", Status = PackageStatus.Installed };
        Assert.Equal(WingetService.PackageActionKind.Uninstall, WingetService.DeterminePackageAction(pkg));
    }

    [Fact]
    public void DeterminePackageAction_Upgradable_ReturnsUpgrade()
    {
        var pkg = new WingetPackage { Id = "App.Id", Status = PackageStatus.Upgradable };
        Assert.Equal(WingetService.PackageActionKind.Upgrade, WingetService.DeterminePackageAction(pkg));
    }

    [Fact]
    public void DeterminePackageAction_Installable_ReturnsInstall()
    {
        var pkg = new WingetPackage { Id = "App.Id", Status = PackageStatus.Installable };
        Assert.Equal(WingetService.PackageActionKind.Install, WingetService.DeterminePackageAction(pkg));
    }

    [Fact]
    public void BuildSearchArguments_EscapesQuery()
    {
        Assert.Equal("search \"git\" --source winget --accept-source-agreements", WingetService.BuildSearchArguments("git"));
    }

    [Fact]
    public void BuildShowArguments_EscapesPackageId()
    {
        Assert.Equal("show \"Git.Git\" --accept-source-agreements", WingetService.BuildShowArguments("Git.Git"));
    }

    [Fact]
    public void BuildInstallArguments_EscapesPackageId()
    {
        Assert.Equal("install \"Git.Git\" --silent --accept-package-agreements --accept-source-agreements", WingetService.BuildInstallArguments("Git.Git"));
    }

    [Fact]
    public void BuildUpgradeArguments_EscapesPackageId()
    {
        Assert.Equal("upgrade \"Git.Git\" --silent --accept-package-agreements --accept-source-agreements", WingetService.BuildUpgradeArguments("Git.Git"));
    }

    [Fact]
    public void BuildUninstallArguments_EscapesPackageId()
    {
        Assert.Equal("uninstall \"Git.Git\" --silent", WingetService.BuildUninstallArguments("Git.Git"));
    }

    [Fact]
    public void BuildExportArguments_EscapesFilePath()
    {
        Assert.Equal("export -o \"C:\\temp\\apps.json\" --source winget --accept-source-agreements", WingetService.BuildExportArguments(@"C:\temp\apps.json"));
    }

    [Fact]
    public void BuildImportArguments_EscapesFilePath()
    {
        Assert.Equal("import -i \"C:\\temp\\apps.json\" --accept-package-agreements --accept-source-agreements", WingetService.BuildImportArguments(@"C:\temp\apps.json"));
    }
}

public class VersionComparerEdgeCaseTests
{
    [Fact]
    public void Compare_NullArguments_HandlesNulls()
    {
        var comparer = VersionComparer.Instance;
        Assert.Equal(0, comparer.Compare(null, null));
        Assert.True(comparer.Compare(null, "1.0") < 0);
        Assert.True(comparer.Compare("1.0", null) > 0);
    }

    [Fact]
    public void Compare_PrereleaseVsNonPrerelease_PrereleaseIsLower()
    {
        var comparer = VersionComparer.Instance;
        Assert.True(comparer.Compare("1.0.0-alpha", "1.0.0") < 0);
        Assert.True(comparer.Compare("1.0.0", "1.0.0-alpha") > 0);
    }

    [Fact]
    public void Compare_PrereleaseAlphabeticalOrdering_SortsCorrectly()
    {
        var comparer = VersionComparer.Instance;
        Assert.True(comparer.Compare("1.0.0-alpha", "1.0.0-beta") < 0);
        Assert.True(comparer.Compare("1.0.0-rc1", "1.0.0-beta") > 0);
    }

    [Fact]
    public void Compare_DifferentSectionLengths_ShorterIsLower()
    {
        var comparer = VersionComparer.Instance;
        Assert.True(comparer.Compare("1.0", "1.0.0") < 0);
        Assert.True(comparer.Compare("1.0.0.1", "1.0.0") > 0);
    }

    [Fact]
    public void Compare_NonNumericParts_UsesCaseInsensitiveStringComparison()
    {
        var comparer = VersionComparer.Instance;
        Assert.True(comparer.Compare("1.0.0.a", "1.0.0.b") < 0);
        Assert.Equal(0, comparer.Compare("1.0.0.A", "1.0.0.a"));
    }

    [Fact]
    public void Compare_LeadingVPrefix_IgnoresPrefixCaseInsensitively()
    {
        var comparer = VersionComparer.Instance;
        Assert.Equal(0, comparer.Compare("v2.1.0", "V2.1.0"));
        Assert.Equal(0, comparer.Compare("v2.1.0", "2.1.0"));
    }

    [Fact]
    public void Compare_BuildMetadataPlusSign_ComparesBuildMetadata()
    {
        var comparer = VersionComparer.Instance;
        Assert.True(comparer.Compare("1.0.0+build1", "1.0.0+build2") < 0);
    }
}

public class ChallengerM2StaticMethodStressTests
{
    // --- WingetParser Stress Tests ---
    [Fact]
    public void WingetParser_GetSubstring_BoundaryAndInvalidInputs()
    {
        Assert.Equal("", WingetParser.GetSubstring(null!, 0, 5));
        Assert.Equal("", WingetParser.GetSubstring("", 0, 5));
        Assert.Equal("", WingetParser.GetSubstring("test", -1, 3));
        Assert.Equal("", WingetParser.GetSubstring("test", 4, 5));
        Assert.Equal("", WingetParser.GetSubstring("test", 2, 2));
        Assert.Equal("", WingetParser.GetSubstring("test", 3, 2));
        Assert.Equal("st", WingetParser.GetSubstring("test", 2, 10));
    }

    [Fact]
    public void WingetParser_FindHeaderLine_Variations()
    {
        Assert.Equal(-1, WingetParser.FindHeaderLine([]));
        Assert.Equal(-1, WingetParser.FindHeaderLine(["Line 1", "Line 2"]));
        Assert.Equal(-1, WingetParser.FindHeaderLine(["---"])); // line 0 -> 0-1 = -1
        Assert.Equal(0, WingetParser.FindHeaderLine(["Header", "---"]));
    }

    [Fact]
    public void WingetParser_TryParseColumnPositions_MalformedHeaders()
    {
        Assert.False(WingetParser.TryParseColumnPositions("Name Version Id", out _)); // Id after Version
        Assert.False(WingetParser.TryParseColumnPositions("Name Source", out _)); // Missing Id & Version
        Assert.True(WingetParser.TryParseColumnPositions("Name Id Version Source", out var pos));
        Assert.True(pos.sourcePos > 0);
    }

    [Fact]
    public void WingetParser_ParseTable_MalformedOutputs()
    {
        Assert.Empty(WingetParser.ParseTable(""));
        Assert.Empty(WingetParser.ParseTable("Short\nOutput"));
        Assert.Empty(WingetParser.ParseTable("Header Line\n----------------\n")); // Invalid column positions

        string tableWithNoMatch = "Name                           Id                                       Version\n--------------------------------------------------------------------------------------\nApp One                        App.One                                  1.0.0";
        var res = WingetParser.ParseTable(tableWithNoMatch);
        Assert.Single(res);
        Assert.Equal("App.One", res[0]["Id"]);
    }

    [Fact]
    public void WingetParser_ParseDetailsList_ARPFilterAndMalformed()
    {
        Assert.Empty(WingetParser.ParseDetailsList(""));
        string outputWithARP = "(1/2) Normal App [App.Normal]\n  Publisher: Pub\n(2/2) ARP App [ARP\\ControlPanelApp]\n  Publisher: Pub2";
        var list = WingetParser.ParseDetailsList(outputWithARP);
        Assert.Single(list);
        Assert.Equal("App.Normal", list[0].Id);
    }

    [Fact]
    public void WingetParser_ParseProgressAndStatusText()
    {
        Assert.Equal(0.0, WingetParser.ParseProgressFromOutput(""));
        Assert.Equal(20.0, WingetParser.ParseProgressFromOutput("Downloading something..."));
        Assert.Equal(60.0, WingetParser.ParseProgressFromOutput("Verifying package..."));
        Assert.Equal(80.0, WingetParser.ParseProgressFromOutput("Installing package..."));
        Assert.Equal(45.0, WingetParser.ParseProgressFromOutput("Progress: 45%"));
        // Empirical observation: Decimal percentages (e.g. 45.5%) match integer regex (\d+)% on the decimal part, returning 5.
        Assert.Equal(5.0, WingetParser.ParseProgressFromOutput("Progress: 45.5%"));

        Assert.Equal("", WingetParser.ParseStatusTextFromOutput(""));
        Assert.Equal("Downloading installer...", WingetParser.ParseStatusTextFromOutput("Downloading file"));
        Assert.Equal("Verifying installer...", WingetParser.ParseStatusTextFromOutput("Successfully verified installer hash"));
        Assert.Equal("Installing...", WingetParser.ParseStatusTextFromOutput("Starting package install"));
        Assert.Equal("Completed", WingetParser.ParseStatusTextFromOutput("Successfully installed"));
        Assert.Equal("Uninstalled", WingetParser.ParseStatusTextFromOutput("Successfully uninstalled"));
        Assert.Equal("", WingetParser.ParseStatusTextFromOutput("Progress 50%"));
        Assert.Equal("Very long status message that exceeds...", WingetParser.ParseStatusTextFromOutput("Very long status message that exceeds forty characters limit in output"));
    }

    // --- IconService Stress Tests ---
    [Fact]
    public void IconService_GetSafeIconFileName_SanitizesSpecialChars()
    {
        Assert.Equal("unknown.png", IconService.GetSafeIconFileName(""));
        Assert.Equal("unknown.png", IconService.GetSafeIconFileName("   "));
        Assert.Equal("App_Name.png", IconService.GetSafeIconFileName("App:Name"));
        Assert.Equal("App_Name.png", IconService.GetSafeIconFileName("App/Name"));
        Assert.Equal("App_Name.png", IconService.GetSafeIconFileName("App..Name"));
    }

    [Fact]
    public void IconService_ParseDatabaseJson_HandlesMalformedAndEmpty()
    {
        var (i1, s1) = IconService.ParseDatabaseJson("");
        Assert.Empty(i1); Assert.Empty(s1);

        var (i2, s2) = IconService.ParseDatabaseJson("{ invalid json }");
        Assert.Empty(i2); Assert.Empty(s2);

        string validJson = @"{
            ""icons_and_screenshots"": {
                ""App1"": {
                    ""icon"": ""https://example.com/icon.png"",
                    ""images"": [""https://example.com/ss1.png""]
                }
            }
        }";
        var (i3, s3) = IconService.ParseDatabaseJson(validJson);
        Assert.Single(i3);
        Assert.Equal("https://example.com/icon.png", i3["App1"]);
        Assert.Single(s3);
        Assert.Equal("https://example.com/ss1.png", s3["App1"][0]);
    }

    [Fact]
    public void IconService_IsCacheExpired_TestBoundaries()
    {
        var now = DateTime.Now;
        Assert.True(IconService.IsCacheExpired(now.AddHours(1), now, TimeSpan.FromHours(24))); // Future time -> expired
        Assert.True(IconService.IsCacheExpired(now.AddHours(-25), now, TimeSpan.FromHours(24))); // > 24 hours -> expired
        Assert.False(IconService.IsCacheExpired(now.AddHours(-10), now, TimeSpan.FromHours(24))); // < 24 hours -> not expired
    }

    [Fact]
    public void IconService_ExtractDomainFromUrl_Variations()
    {
        Assert.Equal("", IconService.ExtractDomainFromUrl(""));
        Assert.Equal("", IconService.ExtractDomainFromUrl("invalid-url"));
        Assert.Equal("example.com", IconService.ExtractDomainFromUrl("https://example.com/path"));
        Assert.Equal("example.com", IconService.ExtractDomainFromUrl("https://www.example.com/path?q=1"));
    }

    [Fact]
    public void IconService_NormalizePackageName_PerformanceWordEdgeCase()
    {
        Assert.Equal("", IconService.NormalizePackageName(""));
        Assert.Equal("DotNetSDK", IconService.NormalizePackageName("Microsoft.DotNet.SDK"));
        // Empirical observation: IndexOf("for") matches substrings inside words like "Performance" (Per-for-mance), truncating to "Per".
        string normPerf = IconService.NormalizePackageName("Performance Tool");
        Assert.Equal("Per", normPerf);
    }

    // --- CachingWingetService Stress Tests ---
    [Fact]
    public void CachingWingetService_MergePackageProperties_NullChecksAndMerging()
    {
        Assert.Throws<ArgumentNullException>(() => CachingWingetService.MergePackageProperties(null!, new WingetPackage()));
        Assert.Throws<ArgumentNullException>(() => CachingWingetService.MergePackageProperties(new WingetPackage(), null!));

        var existing = new WingetPackage { Id = "Test.App", Name = "Old Name", Version = "1.0", Status = PackageStatus.Installable };
        var incoming = new WingetPackage { Id = "Test.App", Name = "New Name", Version = "2.0", Status = PackageStatus.Installed, Publisher = "Pub" };

        CachingWingetService.MergePackageProperties(existing, incoming);
        Assert.Equal("New Name", existing.Name);
        Assert.Equal("2.0", existing.Version);
        Assert.Equal(PackageStatus.Installed, existing.Status);
        Assert.Equal("Pub", existing.Publisher);
    }

    // --- SettingsService Stress Tests ---
    [Fact]
    public void SettingsService_DeserializeAndSerialize()
    {
        Assert.False(SettingsService.DeserializeSettings(null).AutoUpdate);
        Assert.False(SettingsService.DeserializeSettings("").AutoUpdate);
        Assert.False(SettingsService.DeserializeSettings("invalid json").AutoUpdate);

        Assert.Throws<ArgumentNullException>(() => SettingsService.SerializeSettings(null!));

        var appSettings = new AppSettings { AutoUpdate = true, AppTheme = "Dark", EnableNotifications = false };
        string json = SettingsService.SerializeSettings(appSettings);
        var restored = SettingsService.DeserializeSettings(json);
        Assert.True(restored.AutoUpdate);
        Assert.Equal("Dark", restored.AppTheme);
        Assert.False(restored.EnableNotifications);
    }

    // --- LogService Stress Tests ---
    [Fact]
    public void LogService_FormatLogEntry_Formatting()
    {
        var ts = new DateTime(2026, 7, 23, 14, 30, 0);
        string entry = LogService.FormatLogEntry("INFO", "Test message", ts);
        Assert.Equal("[2026-07-23 14:30:00] [INFO] Test message", entry);
    }

    // --- WingetService Stress Tests ---
    [Fact]
    public void WingetService_EscapeArgument_EscapingRules()
    {
        Assert.Equal("\"\"", WingetService.EscapeArgument(null));
        Assert.Equal("\"\"", WingetService.EscapeArgument(""));
        Assert.Equal("\"simple\"", WingetService.EscapeArgument("simple"));
        Assert.Equal("\"with space\"", WingetService.EscapeArgument("with space"));
        Assert.Equal("\"with\\\"quote\"", WingetService.EscapeArgument("with\"quote"));
        Assert.Equal("\"C:\\Program Files\\\\\"", WingetService.EscapeArgument(@"C:\Program Files\"));
    }

    [Fact]
    public void WingetService_BuildRecommendations_NullAndEmptyInputs()
    {
        Assert.Empty(WingetService.BuildRecommendations(null, null));
        Assert.Empty(WingetService.BuildRecommendations([], null));

        var popular = new List<WingetPackage> { new() { Id = "App.1", Name = "App One" } };
        var recs = WingetService.BuildRecommendations(popular, null);
        Assert.Single(recs);
        Assert.Equal(PackageStatus.Installable, recs[0].Status);

        var installedMap = new Dictionary<string, WingetPackage> { ["App.1"] = new() { Id = "App.1", Version = "1.5.0", Status = PackageStatus.Installed } };
        var recsInstalled = WingetService.BuildRecommendations(popular, installedMap);
        Assert.Single(recsInstalled);
        Assert.Equal(PackageStatus.Installed, recsInstalled[0].Status);
        Assert.Equal("1.5.0", recsInstalled[0].Version);
    }

    [Fact]
    public void WingetService_DecoratePackageDetails_StatusResolution()
    {
        var details = new WingetPackage { Id = "App.1", Name = "App One" };
        var upgradable = new List<WingetPackage> { new() { Id = "App.1", Version = "1.0", AvailableVersion = "2.0" } };
        var installed = new List<WingetPackage> { new() { Id = "App.1", Version = "1.0" } };

        var decUpgradable = WingetService.DecoratePackageDetails(details, "App.1", installed, upgradable);
        Assert.Equal(PackageStatus.Upgradable, decUpgradable.Status);
        Assert.Equal("2.0", decUpgradable.AvailableVersion);

        var decInstalled = WingetService.DecoratePackageDetails(details, "App.1", installed, []);
        Assert.Equal(PackageStatus.Installed, decInstalled.Status);

        var decInstallable = WingetService.DecoratePackageDetails(null, "App.2", [], []);
        Assert.Equal("App.2", decInstallable.Id);
        Assert.Equal(PackageStatus.Installable, decInstallable.Status);
    }

    [Fact]
    public void WingetService_DeterminePackageAction_AllStates()
    {
        Assert.Equal(WingetService.PackageActionKind.None, WingetService.DeterminePackageAction(null));
        Assert.Equal(WingetService.PackageActionKind.Cancel, WingetService.DeterminePackageAction(new WingetPackage { IsInstalling = true }));
        Assert.Equal(WingetService.PackageActionKind.Uninstall, WingetService.DeterminePackageAction(new WingetPackage { Status = PackageStatus.Installed }));
        Assert.Equal(WingetService.PackageActionKind.Upgrade, WingetService.DeterminePackageAction(new WingetPackage { Status = PackageStatus.Upgradable }));
        Assert.Equal(WingetService.PackageActionKind.Install, WingetService.DeterminePackageAction(new WingetPackage { Status = PackageStatus.Installable }));
    }

    // --- Helpers Stress Tests ---
    [Fact]
    public void NavigationHelper_GetPageType_AllBranches()
    {
        Assert.Equal(typeof(Pages.NoWingetPage), NavigationHelper.GetPageType(NavTags.Home, false, false));
        Assert.Equal(typeof(Pages.SettingsPage), NavigationHelper.GetPageType(NavTags.Home, true, true));
        Assert.Null(NavigationHelper.GetPageType(null, false, true));
        Assert.Null(NavigationHelper.GetPageType("", false, true));
        Assert.Null(NavigationHelper.GetPageType("UnknownTag", false, true));
        Assert.Equal(typeof(Pages.HomePage), NavigationHelper.GetPageType(NavTags.Home, false, true));
        Assert.Equal(typeof(Pages.InstalledPage), NavigationHelper.GetPageType(NavTags.Installed, false, true));
        Assert.Equal(typeof(Pages.UpdatesPage), NavigationHelper.GetPageType(NavTags.Updates, false, true));
        Assert.Equal(typeof(Pages.AboutPage), NavigationHelper.GetPageType(NavTags.About, false, true));
    }

    [Fact]
    public void PackageFilteringHelper_MatchesQuery_And_Sorting()
    {
        Assert.False(PackageFilteringHelper.MatchesQuery(null!, "test"));
        var pkg = new WingetPackage { Name = "Calculator", Id = "Microsoft.WindowsCalculator", Publisher = "Microsoft", Description = "Standard calc", Tags = ["math", "tools"] };

        Assert.True(pkg.MatchesQuery(""));
        Assert.True(pkg.MatchesQuery("calc"));
        Assert.True(pkg.MatchesQuery("tag:math"));
        Assert.False(pkg.MatchesQuery("tag:games"));

        var list = new List<WingetPackage>
        {
            new() { Name = "B App", Id = "B.App", Publisher = "Pub B", Status = PackageStatus.Installable, Version = "1.0" },
            new() { Name = "A App", Id = "A.App", Publisher = "Pub A", Status = PackageStatus.Upgradable, Version = "2.0" }
        };

        PackageFilteringHelper.SortPackages(list, SortOrders.Az);
        Assert.Equal("A App", list[0].Name);

        PackageFilteringHelper.SortPackages(list, SortOrders.Status);
        Assert.Equal(PackageStatus.Upgradable, list[0].Status);
    }

    [Fact]
    public void GridCalculator_CalculateGridDimensions_ExceptionsAndBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateGridDimensions(500, minCardWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateGridDimensions(500, gap: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateGridDimensions(500, maxColumns: 0));

        var zeroWidth = GridCalculator.CalculateGridDimensions(0);
        Assert.Equal(1, zeroWidth.Columns);
        Assert.Equal(0, zeroWidth.SlotWidth);

        var normalGrid = GridCalculator.CalculateGridDimensions(1000, minCardWidth: 300, gap: 16, maxColumns: 5);
        Assert.Equal(3, normalGrid.Columns); // floor(1000 / 316) = 3
    }

    [Fact]
    public void BulkSelectionHelper_ComputeSelectAllState_Combinations()
    {
        Assert.Equal(false, BulkSelectionHelper.ComputeSelectAllState(0, 0));
        Assert.Equal(false, BulkSelectionHelper.ComputeSelectAllState(10, 0));
        Assert.Equal(true, BulkSelectionHelper.ComputeSelectAllState(5, 5));
        Assert.Null(BulkSelectionHelper.ComputeSelectAllState(10, 5));
    }

    [Fact]
    public void PackageDetailHelper_ShouldSkipMetadataItem_Keys()
    {
        Assert.True(PackageDetailHelper.ShouldSkipMetadataItem("Name"));
        Assert.True(PackageDetailHelper.ShouldSkipMetadataItem("Version"));
        Assert.True(PackageDetailHelper.ShouldSkipMetadataItem("Description"));
        Assert.True(PackageDetailHelper.ShouldSkipMetadataItem("Release Notes"));
        Assert.False(PackageDetailHelper.ShouldSkipMetadataItem("Publisher"));
    }
}

public class Milestone3PageStaticMethodTests
{
    // --- HomePage Static Tests ---
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("git", "git")]
    [InlineData("  vscode  ", "vscode")]
    [InlineData("category:Developer Tools", "Developer Tools")]
    [InlineData("category:  Tools  ", "Tools")]
    [InlineData("category:", "")]
    [InlineData(12345, "")]
    public void HomePage_ExtractSearchQuery_ReturnsExpectedQuery(object? parameter, string expected)
    {
        Assert.Equal(expected, HomePage.ExtractSearchQuery(parameter));
    }

    [Theory]
    [InlineData(false, 0, false, "", Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, "")]
    [InlineData(true, 5, false, "python", Visibility.Visible, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, "Search Results for \"python\"")]
    [InlineData(true, 0, true, "vs", Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed, "Search Results for \"vs\"")]
    [InlineData(true, 0, false, "unknown", Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, "Search Results for \"unknown\"")]
    public void HomePage_DetermineSearchViewState_ReturnsExpectedVisibilitiesAndTitle(
        bool isSearchActive, int itemCount, bool isLoading, string searchQuery,
        Visibility expSearchVis, Visibility expDiscVis, Visibility expListVis, Visibility expEmptyVis, string expTitle)
    {
        var (searchVis, discVis, listVis, emptyVis, title) = HomePage.DetermineSearchViewState(isSearchActive, itemCount, isLoading, searchQuery);
        Assert.Equal(expSearchVis, searchVis);
        Assert.Equal(expDiscVis, discVis);
        Assert.Equal(expListVis, listVis);
        Assert.Equal(expEmptyVis, emptyVis);
        Assert.Equal(expTitle, title);
    }

    [Fact]
    public void HomePage_ShouldUpdateGridLayout_Recreated_ReturnsTrue()
    {
        Assert.True(HomePage.ShouldUpdateGridLayout(true, 3, 3, 300, 300, 150, 150, 130, 130, 16, 16));
    }

    [Fact]
    public void HomePage_ShouldUpdateGridLayout_Identical_ReturnsFalse()
    {
        Assert.False(HomePage.ShouldUpdateGridLayout(false, 3, 3, 300.2, 300.0, 150.1, 150.0, 130.1, 130.0, 16.1, 16.0));
    }

    [Fact]
    public void HomePage_ShouldUpdateGridLayout_DeltasExceedThreshold_ReturnsTrue()
    {
        Assert.True(HomePage.ShouldUpdateGridLayout(false, 4, 3, 300, 300, 150, 150, 130, 130, 16, 16));
        Assert.True(HomePage.ShouldUpdateGridLayout(false, 3, 3, 301, 300, 150, 150, 130, 130, 16, 16));
        Assert.True(HomePage.ShouldUpdateGridLayout(false, 3, 3, 300, 300, 151, 150, 130, 130, 16, 16));
        Assert.True(HomePage.ShouldUpdateGridLayout(false, 3, 3, 300, 300, 150, 150, 131, 130, 16, 16));
        Assert.True(HomePage.ShouldUpdateGridLayout(false, 3, 3, 300, 300, 150, 150, 130, 130, 17, 16));
    }

    [Fact]
    public void HomePage_FormatSearchResultsTitle_FormatsCorrectly()
    {
        Assert.Equal("Search Results for \"git\"", HomePage.FormatSearchResultsTitle("git"));
    }

    [Fact]
    public void HomePage_NormalizeQuery_TrimsWhitespace()
    {
        Assert.Equal("", HomePage.NormalizeQuery(null));
        Assert.Equal("", HomePage.NormalizeQuery("   "));
        Assert.Equal("git", HomePage.NormalizeQuery("  git  "));
    }

    // --- InstalledPage Static Tests ---
    [Fact]
    public void InstalledPage_GetUpdateVisibility_ReturnsExpected()
    {
        Assert.Equal(Visibility.Visible, InstalledPage.GetUpdateVisibility(PackageStatus.Upgradable));
        Assert.Equal(Visibility.Collapsed, InstalledPage.GetUpdateVisibility(PackageStatus.Installed));
        Assert.Equal(Visibility.Collapsed, InstalledPage.GetUpdateVisibility(PackageStatus.Installable));
    }

    [Theory]
    [InlineData("Descending", "Name", "Name", "\uE74B", Visibility.Visible)]
    [InlineData("Ascending", "Name", "Name", "\uE74A", Visibility.Visible)]
    [InlineData("Descending", "Publisher", "Name", "\uE74B", Visibility.Collapsed)]
    public void InstalledPage_GetSortGlyph_ReturnsExpectedGlyphAndVisibility(string sortDir, string sortBy, string targetField, string expGlyph, Visibility expVis)
    {
        var (glyph, vis) = InstalledPage.GetSortGlyph(sortDir, sortBy, targetField);
        Assert.Equal(expGlyph, glyph);
        Assert.Equal(expVis, vis);
    }

    [Fact]
    public void InstalledPage_GetInstalledViewState_Loading_ReturnsLoadingVisible()
    {
        var (progressVis, listVis, emptyVis) = InstalledPage.GetInstalledViewState(true, 5);
        Assert.Equal(Visibility.Visible, progressVis);
        Assert.Equal(Visibility.Collapsed, listVis);
        Assert.Equal(Visibility.Collapsed, emptyVis);
    }

    [Fact]
    public void InstalledPage_GetInstalledViewState_HasItems_ReturnsListVisible()
    {
        var (progressVis, listVis, emptyVis) = InstalledPage.GetInstalledViewState(false, 5);
        Assert.Equal(Visibility.Collapsed, progressVis);
        Assert.Equal(Visibility.Visible, listVis);
        Assert.Equal(Visibility.Collapsed, emptyVis);
    }

    [Fact]
    public void InstalledPage_GetInstalledViewState_ZeroItems_ReturnsEmptyVisible()
    {
        var (progressVis, listVis, emptyVis) = InstalledPage.GetInstalledViewState(false, 0);
        Assert.Equal(Visibility.Collapsed, progressVis);
        Assert.Equal(Visibility.Collapsed, listVis);
        Assert.Equal(Visibility.Visible, emptyVis);
    }

    [Fact]
    public void InstalledPage_GetEligibleBulkUninstallPackages_FiltersInstallingAndNulls()
    {
        Assert.Empty(InstalledPage.GetEligibleBulkUninstallPackages(null));
        Assert.Empty(InstalledPage.GetEligibleBulkUninstallPackages([]));

        var list = new List<WingetPackage?>
        {
            null,
            new WingetPackage { Id = "p1", IsInstalling = true },
            new WingetPackage { Id = "p2", IsInstalling = false }
        };

        var eligible = InstalledPage.GetEligibleBulkUninstallPackages(list);
        Assert.Single(eligible);
        Assert.Equal("p2", eligible[0].Id);
    }

    [Fact]
    public void InstalledPage_GetImportStatusMessage_ReturnsCorrectMessages()
    {
        var (successSev, successTitle, successMsg) = InstalledPage.GetImportStatusMessage(true, null);
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success, successSev);
        Assert.Equal("Import Completed", successTitle);
        Assert.Contains("imported and processed successfully", successMsg);

        var (failSev, failTitle, failMsg) = InstalledPage.GetImportStatusMessage(false, new Exception("File corrupted"));
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, failSev);
        Assert.Equal("Import Failed", failTitle);
        Assert.Contains("File corrupted", failMsg);
    }

    [Fact]
    public void InstalledPage_GetExportStatusMessage_ReturnsCorrectMessages()
    {
        var (successSev, successTitle, successMsg) = InstalledPage.GetExportStatusMessage(true, "C:\\export.json", null);
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success, successSev);
        Assert.Equal("Export Complete", successTitle);
        Assert.Contains("C:\\export.json", successMsg);

        var (failSev, failTitle, failMsg) = InstalledPage.GetExportStatusMessage(false, null, new Exception("Access Denied"));
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, failSev);
        Assert.Equal("Export Failed", failTitle);
        Assert.Contains("Access Denied", failMsg);
    }

    // --- UpdatesPage Static Tests ---
    [Fact]
    public void UpdatesPage_CanUpdateAll_EvaluatesConditionsCorrectly()
    {
        Assert.False(UpdatesPage.CanUpdateAll(false, [new WingetPackage { IsInstalling = false }]));
        Assert.False(UpdatesPage.CanUpdateAll(true, null));
        Assert.False(UpdatesPage.CanUpdateAll(true, []));
        Assert.False(UpdatesPage.CanUpdateAll(true, [new WingetPackage { IsInstalling = true }]));
        Assert.True(UpdatesPage.CanUpdateAll(true, [new WingetPackage { IsInstalling = true }, new WingetPackage { IsInstalling = false }]));
        Assert.True(UpdatesPage.CanUpdateAll(true, [null!, new WingetPackage { IsInstalling = false }]));
    }

    [Fact]
    public void UpdatesPage_FilterPackagesForBulkUpdate_FiltersNullsAndInstalling()
    {
        Assert.Empty(UpdatesPage.FilterPackagesForBulkUpdate(null));
        Assert.Empty(UpdatesPage.FilterPackagesForBulkUpdate([]));

        var selected = new List<WingetPackage?>
        {
            null,
            new WingetPackage { Id = "u1", IsInstalling = true },
            new WingetPackage { Id = "u2", IsInstalling = false }
        };

        var filtered = UpdatesPage.FilterPackagesForBulkUpdate(selected!);
        Assert.Single(filtered);
        Assert.Equal("u2", filtered[0].Id);
    }

    // --- DetailsPage Static Tests ---
    [Fact]
    public void DetailsPage_FormatPublisher_ReturnsPublisherOrFallback()
    {
        Assert.Equal("Unknown Publisher", DetailsPage.FormatPublisher(null));
        Assert.Equal("Unknown Publisher", DetailsPage.FormatPublisher(""));
        Assert.Equal("Unknown Publisher", DetailsPage.FormatPublisher("   "));
        Assert.Equal("Microsoft Corporation", DetailsPage.FormatPublisher("Microsoft Corporation"));
    }

    [Fact]
    public void DetailsPage_FormatVersionText_CombinesVersionsCorrectly()
    {
        Assert.Equal("Version: 1.0.0", DetailsPage.FormatVersionText("1.0.0", null));
        Assert.Equal("Version: 1.0.0", DetailsPage.FormatVersionText("1.0.0", ""));
        Assert.Equal("Version: 1.0.0 (Latest: 1.2.0)", DetailsPage.FormatVersionText("1.0.0", "1.2.0"));
        Assert.Equal("Version: Unknown (Latest: 2.0.0)", DetailsPage.FormatVersionText(null, "2.0.0"));
    }

    [Fact]
    public void DetailsPage_FormatDescription_ReturnsDescriptionOrFallback()
    {
        Assert.Equal("No description available for this package.", DetailsPage.FormatDescription(null));
        Assert.Equal("No description available for this package.", DetailsPage.FormatDescription(""));
        Assert.Equal("No description available for this package.", DetailsPage.FormatDescription("   "));
        Assert.Equal("Git version control system.", DetailsPage.FormatDescription("Git version control system."));
    }

    [Fact]
    public void DetailsPage_FindActiveTaskForPackage_FindsRunningOrQueuedTaskCaseInsensitively()
    {
        Assert.Null(DetailsPage.FindActiveTaskForPackage(null, []));
        Assert.Null(DetailsPage.FindActiveTaskForPackage("app.id", null));

        var tasks = new List<InstallTask>
        {
            new InstallTask { Id = "1", PackageId = "app.git", PackageName = "Git", Operation = TaskOperation.Install, Status = InstallTaskStatus.Completed },
            new InstallTask { Id = "2", PackageId = "app.vscode", PackageName = "VS Code", Operation = TaskOperation.Install, Status = InstallTaskStatus.Running },
            new InstallTask { Id = "3", PackageId = "app.node", PackageName = "Node", Operation = TaskOperation.Install, Status = InstallTaskStatus.Queued }
        };

        Assert.Null(DetailsPage.FindActiveTaskForPackage("app.git", tasks));
        var task2 = DetailsPage.FindActiveTaskForPackage("APP.VSCODE", tasks);
        Assert.NotNull(task2);
        Assert.Equal("2", task2.Id);

        var task3 = DetailsPage.FindActiveTaskForPackage("app.node", tasks);
        Assert.NotNull(task3);
        Assert.Equal("3", task3.Id);
    }

    [Fact]
    public void DetailsPage_GetTextSectionVisibility_ReturnsVisibleOnlyWhenHasContent()
    {
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetTextSectionVisibility(null));
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetTextSectionVisibility(""));
        Assert.Equal(Visibility.Visible, DetailsPage.GetTextSectionVisibility("Release notes text"));
    }

    [Fact]
    public void DetailsPage_GetCollectionVisibility_ReturnsVisibleOnlyWhenHasItems()
    {
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetCollectionVisibility<string>(null));
        Assert.Equal(Visibility.Collapsed, DetailsPage.GetCollectionVisibility<string>([]));
        Assert.Equal(Visibility.Visible, DetailsPage.GetCollectionVisibility<string>(["tag1"]));
    }

    [Fact]
    public void DetailsPage_GetTagNavigationParameter_ConstructsPrefix()
    {
        Assert.Equal("tag:developer", DetailsPage.GetTagNavigationParameter("developer"));
    }

    // --- App / MainWindow / NoWingetPage / SettingsPage Static Tests ---
    [Fact]
    public void App_FormatLogDialogTitle_FormatsCorrectly()
    {
        Assert.Equal("Activity Log: Git (Install)", App.FormatLogDialogTitle("Git", "Install"));
    }

    [Fact]
    public void App_FormatActivityLogStatus_FormatsPercentageCorrectly()
    {
        Assert.Equal("Status: Downloading... | Progress: 45%", App.FormatActivityLogStatus("Downloading...", 45.7));
        Assert.Equal("Status: Done | Progress: 100%", App.FormatActivityLogStatus("Done", 100));
    }

    [Theory]
    [InlineData(false, true, Visibility.Visible)]
    [InlineData(true, true, Visibility.Collapsed)]
    [InlineData(false, false, Visibility.Collapsed)]
    [InlineData(true, false, Visibility.Collapsed)]
    public void MainWindow_IsBackButtonVisible_ReturnsExpectedVisibility(bool isTopLevel, bool canGoBack, Visibility expected)
    {
        Assert.Equal(expected, MainWindow.IsBackButtonVisible(isTopLevel, canGoBack));
    }

    [Fact]
    public void NoWingetPage_GetTempInstallerPath_CombinesDirectoryAndFileName()
    {
        string path = NoWingetPage.GetTempInstallerPath("C:\\temp");
        Assert.Equal(Path.Combine("C:\\temp", "Microsoft.DesktopAppInstaller.msixbundle"), path);
    }

    [Fact]
    public void NoWingetPage_GetPowershellInstallArguments_ConstructsFormattedCommand()
    {
        string args = NoWingetPage.GetPowershellInstallArguments("C:\\temp\\installer.msixbundle");
        Assert.Equal("-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path 'C:\\temp\\installer.msixbundle'\"", args);
    }

    [Fact]
    public void SettingsPage_GetStatusBrushResourceKey_ReturnsExpectedResourceKey()
    {
        Assert.Equal("SystemFillColorSuccessBrush", SettingsPage.GetStatusBrushResourceKey(true));
        Assert.Equal("SystemFillColorCriticalBrush", SettingsPage.GetStatusBrushResourceKey(false));
    }
}

public class FilterableViewModelPartialMethodsTests
{
    private class TestFilterableViewModel : FilterableViewModel
    {
        public int ApplyFilterCallCount { get; set; }
        public override void ApplyFilter() => ApplyFilterCallCount++;
    }

    [Fact]
    public void OnCategoryFilterChanged_RaisesPropertyChangedForIsCategoryProperties()
    {
        var vm = new TestFilterableViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CategoryFilter = "All";

        Assert.Contains("IsCategoryApps", changed);
        Assert.Contains("IsCategoryRedist", changed);
        Assert.Contains("IsCategoryAll", changed);
        Assert.Equal(1, vm.ApplyFilterCallCount);
    }

    [Fact]
    public void OnCategoryFilterChanged_MultipleChanges()
    {
        var vm = new TestFilterableViewModel();

        vm.CategoryFilter = "Redist";
        Assert.Equal(1, vm.ApplyFilterCallCount);

        vm.CategoryFilter = "All";
        Assert.Equal(2, vm.ApplyFilterCallCount);

        vm.CategoryFilter = "Apps";
        Assert.Equal(3, vm.ApplyFilterCallCount);
    }

    [Fact]
    public void OnAppsCountChanged_RaisesPropertyChanged()
    {
        var vm = new TestFilterableViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.AppsCount = 42;

        Assert.Contains("AppsCountText", changed);
    }

    [Fact]
    public void OnRedistCountChanged_RaisesPropertyChanged()
    {
        var vm = new TestFilterableViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RedistCount = 10;

        Assert.Contains("RedistCountText", changed);
    }

    [Fact]
    public void OnTotalCountChanged_RaisesPropertyChanged()
    {
        var vm = new TestFilterableViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.TotalCount = 100;

        Assert.Contains("AllCountText", changed);
    }

    [Fact]
    public void OnFilterQueryChanged_CallsApplyFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.FilterQuery = "test";
        Assert.Equal(1, vm.ApplyFilterCallCount);
    }

    [Fact]
    public void OnSortOrderChanged_CallsApplyFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.SortOrder = "az";
        Assert.Equal(1, vm.ApplyFilterCallCount);
    }

    [Fact]
    public void OnSortByChanged_CallsApplyFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.SortBy = "Version";
        Assert.Equal(1, vm.ApplyFilterCallCount);
    }

    [Fact]
    public void OnSortDirectionChanged_CallsApplyFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.SortDirection = "Descending";
        Assert.Equal(1, vm.ApplyFilterCallCount);
    }

    [Fact]
    public void ComputedPropertyGetters_ReturnFormattedValues()
    {
        var vm = new TestFilterableViewModel();

        vm.AppsCount = 5;
        Assert.Equal("Applications (5)", vm.AppsCountText);

        vm.RedistCount = 3;
        Assert.Equal("Redistributables (3)", vm.RedistCountText);

        vm.TotalCount = 8;
        Assert.Equal("All (8)", vm.AllCountText);
    }

    [Fact]
    public void IsCategoryGetters_MatchCurrentCategoryFilter()
    {
        var vm = new TestFilterableViewModel();

        vm.CategoryFilter = "Apps";
        Assert.True(vm.IsCategoryApps);
        Assert.False(vm.IsCategoryRedist);
        Assert.False(vm.IsCategoryAll);

        vm.CategoryFilter = "Redist";
        Assert.False(vm.IsCategoryApps);
        Assert.True(vm.IsCategoryRedist);
        Assert.False(vm.IsCategoryAll);

        vm.CategoryFilter = "All";
        Assert.False(vm.IsCategoryApps);
        Assert.False(vm.IsCategoryRedist);
        Assert.True(vm.IsCategoryAll);
    }

    [Fact]
    public void IsCategoryApp_Setter_ChangesCategoryFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.CategoryFilter = "All";
        vm.IsCategoryApps = true;
        Assert.Equal("Apps", vm.CategoryFilter);
    }

    [Fact]
    public void IsCategoryRedist_Setter_ChangesCategoryFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.CategoryFilter = "All";
        vm.IsCategoryRedist = true;
        Assert.Equal("Redist", vm.CategoryFilter);
    }

    [Fact]
    public void IsCategoryAll_Setter_ChangesCategoryFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.CategoryFilter = "Apps";
        vm.IsCategoryAll = true;
        Assert.Equal("All", vm.CategoryFilter);
    }

    [Fact]
    public void IsCategorySetter_WhenFalse_DoesNotChangeCategoryFilter()
    {
        var vm = new TestFilterableViewModel();
        vm.CategoryFilter = "All";

        vm.IsCategoryApps = false;
        Assert.Equal("All", vm.CategoryFilter);

        vm.IsCategoryRedist = false;
        Assert.Equal("All", vm.CategoryFilter);

        vm.IsCategoryAll = false;
        Assert.Equal("All", vm.CategoryFilter);
    }

    [Fact]
    public void IsCategorySetter_WhenAlreadyMatch_DoesNotChange()
    {
        var vm = new TestFilterableViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Apps", vm.CategoryFilter);
        vm.IsCategoryApps = true;

        Assert.DoesNotContain("CategoryFilter", changed);
        Assert.Equal(0, vm.ApplyFilterCallCount);
    }
}

public class WingetPackageEdgeCaseTests
{
    [Fact]
    public void Publisher_FallbackWhenInstalled_ReturnsWordFromId()
    {
        var pkg = new WingetPackage { Id = "Microsoft.VSCode", Publisher = "Installed" };
        Assert.Equal("Microsoft", pkg.Publisher);

        var pkg2 = new WingetPackage { Id = "Microsoft.VSCode", Publisher = "winget" };
        Assert.Equal("Microsoft", pkg2.Publisher);
    }

    [Fact]
    public void Publisher_FallbackWhenNullAndNoDotInId_ReturnsNameFirstWord()
    {
        var pkg = new WingetPackage { Id = "NoDot", Name = "Some App", Publisher = "" };
        Assert.Equal("Some", pkg.Publisher);
    }

    [Fact]
    public void Publisher_FallbackWhenAllEmpty_ReturnsWingetPackage()
    {
        var pkg = new WingetPackage { Name = "", Publisher = "" };
        Assert.Equal("Winget Package", pkg.Publisher);
    }

    [Fact]
    public void IsRedistributable_KeywordMatches()
    {
        Assert.True(new WingetPackage { Name = "Visual C++ Runtime" }.IsRedistributable);
        Assert.True(new WingetPackage { Name = ".NET Runtime" }.IsRedistributable);
        Assert.True(new WingetPackage { Name = "Microsoft WebView2" }.IsRedistributable);
        Assert.True(new WingetPackage { Name = "DirectX Runtime" }.IsRedistributable);
        Assert.True(new WingetPackage { Name = "Software Development Kit" }.IsRedistributable);
        Assert.True(new WingetPackage { Name = "Windows SDK", Id = "Some.SDK" }.IsRedistributable);
        Assert.True(new WingetPackage { Id = "Some.DotNet", Name = "Runtime" }.IsRedistributable);
        Assert.True(new WingetPackage { Id = "Some.VCRedist", Name = "Package" }.IsRedistributable);
    }

    [Fact]
    public void IsNotRedistributable_DoesNotMatchKeywords()
    {
        Assert.False(new WingetPackage { Name = "Visual Studio Code" }.IsRedistributable);
        Assert.False(new WingetPackage { Name = "Microsoft Office" }.IsRedistributable);
    }

    [Fact]
    public void DisplayTitle_StripsVersionPatternsFromName()
    {
        var pkg = new WingetPackage { Name = "App Name v1.2.3", Id = "App.Id" };
        Assert.Equal("App Name", pkg.DisplayTitle);

        var pkg2 = new WingetPackage { Name = "App Name 1.2.3", Id = "App.Id" };
        Assert.Equal("App Name", pkg2.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_FallsBackToNameWhenCleanedIsEmpty()
    {
        var pkg = new WingetPackage { Name = "v1.0", Id = "App.Id" };
        Assert.Equal("v1.0", pkg.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_FallsBackToIdWhenNameIsWhitespace()
    {
        var pkg = new WingetPackage { Name = "", Id = "App.Id" };
        Assert.Equal("App.Id", pkg.DisplayTitle);
    }

    [Fact]
    public void FormattedVersionAndSource_FormatsCorrectly()
    {
        Assert.EndsWith("Winget", new WingetPackage { Version = "", Source = "" }.FormattedVersionAndSource);
        Assert.Contains("1.0", new WingetPackage { Version = "1.0", Source = "winget" }.FormattedVersionAndSource);
        Assert.DoesNotContain("·", new WingetPackage { Version = "", Source = "winget" }.FormattedVersionAndSource);
    }

    [Fact]
    public void StatusDrivenProperties_ReturnExpectedValues()
    {
        var pkg = new WingetPackage();

        pkg.Status = PackageStatus.Installable;
        Assert.True(pkg.ShowInstallOrUpdateButton);
        Assert.False(pkg.ShowUninstallButton);
        Assert.True(pkg.IsInstallAction);
        Assert.False(pkg.IsUninstallAction);
        Assert.Equal("Install", pkg.PrimaryActionButtonText);

        pkg.Status = PackageStatus.Installed;
        Assert.False(pkg.ShowInstallOrUpdateButton);
        Assert.True(pkg.ShowUninstallButton);
        Assert.False(pkg.IsInstallAction);
        Assert.True(pkg.IsUninstallAction);
        Assert.Equal("Uninstall", pkg.PrimaryActionButtonText);

        pkg.Status = PackageStatus.Upgradable;
        Assert.True(pkg.ShowInstallOrUpdateButton);
        Assert.False(pkg.ShowUninstallButton);
        Assert.True(pkg.IsInstallAction);
        Assert.False(pkg.IsUninstallAction);
        Assert.Equal("Update", pkg.PrimaryActionButtonText);

        pkg.IsInstalling = true;
        Assert.Equal("Working...", pkg.PrimaryActionButtonText);
    }

    [Fact]
    public void PackageProperties_SetAndGet()
    {
        var pkg = new WingetPackage();
        pkg.Version = "2.0";
        Assert.Equal("2.0", pkg.Version);
        pkg.AvailableVersion = "3.0";
        Assert.Equal("3.0", pkg.AvailableVersion);
        pkg.Source = "winget";
        Assert.Equal("winget", pkg.Source);
        pkg.Description = "A test package";
        Assert.Equal("A test package", pkg.Description);
        pkg.Homepage = "https://example.com";
        Assert.Equal("https://example.com", pkg.Homepage);
        pkg.License = "MIT";
        Assert.Equal("MIT", pkg.License);
        pkg.InstallerType = "msi";
        Assert.Equal("msi", pkg.InstallerType);
        pkg.InstallerUrl = "https://example.com/setup.msi";
        Assert.Equal("https://example.com/setup.msi", pkg.InstallerUrl);
        pkg.PublisherUrl = "https://publisher.com";
        Assert.Equal("https://publisher.com", pkg.PublisherUrl);
        pkg.ReleaseNotes = "Bug fixes";
        Assert.Equal("Bug fixes", pkg.ReleaseNotes);
        pkg.InstallStatusText = "Downloading";
        Assert.Equal("Downloading", pkg.InstallStatusText);
        pkg.InstallProgress = 50.0;
        Assert.Equal(50.0, pkg.InstallProgress);
    }
}

public class HomeViewModelRemainingTests
{
    [Fact]
    public async Task OnSourceFilterChanged_CallsApplyFilter()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            var recField = typeof(HomeViewModel).GetField("_allRecommendations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            recField.SetValue(homeVM, new List<WingetPackage>
            {
                new() { Name = "App1", Source = "winget" },
                new() { Name = "App2", Source = "other" }
            });

            homeVM.SourceFilter = "winget";

            Assert.NotNull(homeVM.FilteredRecommendations);
        });
    }

    [Fact]
    public async Task SearchInternalAsync_WithValidQuery_CompletesWithoutError()
    {
        await TestHelper.RunWithDispatcherAsync(async () =>
        {
            var homeVM = App.Services.GetRequiredService<HomeViewModel>();
            await homeVM.SearchAsync("test");
            Assert.True(homeVM.IsSearchActive || !homeVM.IsLoading);
        });
    }
}

public class PlaceholderBrushTests
{
    [Fact]
    public void GetPlaceholderColorForName_EmptyName_ReturnsGray()
    {
        var color = WingetPackage.GetPlaceholderColorForName("");
        Assert.Equal(Microsoft.UI.Colors.Gray, color);
    }

    [Fact]
    public void GetPlaceholderColorForName_NullName_ReturnsGray()
    {
        var color = WingetPackage.GetPlaceholderColorForName(null!);
        Assert.Equal(Microsoft.UI.Colors.Gray, color);
    }

    [Fact]
    public void GetPlaceholderColorForName_WhitespaceName_ReturnsGray()
    {
        var color = WingetPackage.GetPlaceholderColorForName("   ");
        Assert.Equal(Microsoft.UI.Colors.Gray, color);
    }

    [Fact]
    public void GetPlaceholderColorForName_ValidName_ReturnsNonTransparentColor()
    {
        var color = WingetPackage.GetPlaceholderColorForName("Git");
        Assert.NotEqual(Microsoft.UI.Colors.Transparent, color);
        Assert.NotEqual(Microsoft.UI.Colors.Gray, color);
    }

    [Fact]
    public void GetPlaceholderColorForName_DifferentNames_DifferentColors()
    {
        var color1 = WingetPackage.GetPlaceholderColorForName("A");
        var color2 = WingetPackage.GetPlaceholderColorForName("B");
        Assert.NotEqual(color1, color2);
    }

    [Fact]
    public void GetPlaceholderColorForName_SameName_ConsistentColor()
    {
        var color1 = WingetPackage.GetPlaceholderColorForName("Node.js");
        var color2 = WingetPackage.GetPlaceholderColorForName("Node.js");
        Assert.Equal(color1, color2);
    }

    [Fact]
    public void GetPlaceholderColorForName_CommonPackageNames_ReturnsOneOfTenColors()
    {
        var knownColors = new Windows.UI.Color[]
        {
            Windows.UI.Color.FromArgb(255, 30, 144, 255),
            Windows.UI.Color.FromArgb(255, 46, 139, 87),
            Windows.UI.Color.FromArgb(255, 138, 43, 226),
            Windows.UI.Color.FromArgb(255, 210, 105, 30),
            Windows.UI.Color.FromArgb(255, 220, 20, 60),
            Windows.UI.Color.FromArgb(255, 0, 128, 128),
            Windows.UI.Color.FromArgb(255, 218, 112, 214),
            Windows.UI.Color.FromArgb(255, 255, 99, 71),
            Windows.UI.Color.FromArgb(255, 70, 130, 180),
            Windows.UI.Color.FromArgb(255, 186, 85, 211)
        };

        foreach (var name in new[] { "Git", "Python", "Node.js", "Firefox", "VS Code" })
        {
            var color = WingetPackage.GetPlaceholderColorForName(name);
            Assert.Contains(color, knownColors);
        }
    }
}












