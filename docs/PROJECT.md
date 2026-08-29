# Project: ViVeToolApp Full-Stack QA, Automated Testing, and UI Layout Polish

## Architecture
- **Framework & Runtime**: .NET 9 (`net9.0-windows10.0.19041.0`), WinUI 3 (Windows App SDK `1.8.251003001`), Unpackaged (`WindowsPackageType=None`).
- **Solution Layout**:
  - `ViVeToolApp.sln`: Master solution linking both application and test projects.
  - `ViVeToolApp/`: WinUI 3 desktop application with MVVM/Service-oriented architecture.
    - `Models/`: Data transfer objects, feature models, selection state models, execution results.
    - `Services/`: Decoupled pure C# services (Scraping, Process Launching, Filtering, ViVeTool CLI Management, Downloading).
    - `Views/` & UI Layer: `MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml`, styles and resources.
  - `ViVeToolApp.Tests/`: xUnit automated test project targeting `net9.0-windows10.0.19041.0` with xUnit, FluentAssertions, and Moq for headless testing.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Service Decoupling & Extraction | Extract non-UI logic (Scraper, ViVeToolRunner, ProcessLauncher, FilterService, Models) from MainWindow.xaml.cs into testable services | M1 | ORIGINAL_REQUEST §R1 |
| 2 | Pureinfotech Scraper & Edge Cases | HTML scraper parsing across page fragments, build groups (GA 2026, GA 2025, 26H2, 25H2, Canary), malformed tags, missing descriptions, multi-ID codes, deduplication | M1 | ORIGINAL_REQUEST §R1 |
| 3 | Offline Fallback Catalog | Fallback feature catalog containing valid features across all tracks when network is unavailable | M1 | ORIGINAL_REQUEST §R1 |
| 4 | ViVeTool CLI Runner & Exit Codes | ViVeTool execution, command line formatting (enable/disable/whatif), and exit-code classification (Success=0, Unsupported/Skip=not found/unknown, Error=other) | M1 | ORIGINAL_REQUEST §R1 |
| 5 | Feature Filter & Selection Calculations | Search filtering (by description, ID, build), track filtering, selection arithmetic, and distinct ID extraction | M1 | ORIGINAL_REQUEST §R1 |
| 6 | Headless xUnit Test Suite | Comprehensive .NET 9 test project (ViVeToolApp.Tests) executing via `dotnet test` with 100% pass rate | M1 | ORIGINAL_REQUEST §R1 |
| 7 | Sidebar Layout Optimization | Consolidate sidebar to 3 compact cards (~440px height), add scroll padding, prevent clipping from 1200x840 down to minimum bounds | M2 | ORIGINAL_REQUEST §R2 |
| 8 | Column Proportions & Alignment | Rebalance ListView columns (Track: 130px, Build: 135px, IDs: 170px, Description: *) and align header border padding to compensate for scrollbar gutter | M2 | ORIGINAL_REQUEST §R2 |
| 9 | Output Log Expander Refactoring | Eliminate nested double borders, embed ProgressBar and Clear button in Expander.Header for persistent visibility without text collision | M2 | ORIGINAL_REQUEST §R2 |
| 10 | Interactive Row Selection | Enable row click toggling on ListView items for smoother usability | M2 | ORIGINAL_REQUEST §R2 |
| 11 | Native Mica Backdrop & Fallback | Declare native XAML MicaBackdrop in MainWindow.xaml with automatic fallback for Windows 10 / non-DWM | M3 | ORIGINAL_REQUEST §R3 |
| 12 | Theme Contrast & Readability | Upgrade text brushes, audit contrast in Light and Dark modes meeting WCAG AA standards | M3 | ORIGINAL_REQUEST §R3 |
| 13 | Async Resilience & Cancellation | Graceful error handling for network timeouts, disconnections, cancellation tokens, and background processes with user InfoBar feedback | M3 | ORIGINAL_REQUEST §R3 |
| 14 | 0-Warning Compilation & Publishing | Enforce TreatWarningsAsErrors on all projects, verify clean `dotnet build` and `dotnet publish` | M4 | ORIGINAL_REQUEST §Criteria |
| 15 | Clean Runtime Startup & Verification | Verify application launches cleanly with 0 exceptions logged to Windows Event Log | M4 | ORIGINAL_REQUEST §Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Core Logic Extraction & xUnit Test Suite | Extract non-UI services & models, create ViVeToolApp.sln & ViVeToolApp.Tests, implement thorough test coverage for scraper, CLI runner, filter service, and models | none | DONE |
| 2 | UI Layout Polish & Responsiveness | Refactor MainWindow.xaml layout, sidebar cards & scrolling, column proportions/alignment, log expander header toolbar | M1 | DONE |
| 3 | Theme & Runtime Stability Hardening | Mica backdrop integration, light/dark contrast tuning, async error handling & cancellation resilience | M2 | DONE |
| 4 | Final Verification & Publish Hardening | End-to-end dotnet test, dotnet build, dotnet publish (0 errors, 0 warnings), clean startup validation | M3 | DONE |

## Interface Contracts

### 1. `IFeatureScraper`
```csharp
namespace ViVeToolApp.Services
{
    public interface IFeatureScraper
    {
        Task<List<FeatureItem>> FetchAndParseAsync(string? customUrl = null, CancellationToken cancellationToken = default);
        List<FeatureItem> ParseHtml(string html);
        List<FeatureItem> GetOfflineFallback();
    }
}
```

### 2. `IViVeToolRunner` & `IProcessLauncher`
```csharp
namespace ViVeToolApp.Services
{
    public interface IProcessLauncher
    {
        Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken = default);
    }

    public interface IViVeToolRunner
    {
        string LocateViVeTool();
        Task<ViVeBatchResult> RunBatchAsync(
            string viveToolPath,
            IEnumerable<FeatureItem> features,
            ViVeExecutionMode mode,
            bool whatIf,
            IProgress<ViVeProgressReport>? progress = null,
            CancellationToken cancellationToken = default);
        ViVeToolResult ClassifyResult(int exitCode, string stdOut, string stdErr);
    }
}
```

### 3. `IFeatureFilterService`
```csharp
namespace ViVeToolApp.Services
{
    public interface IFeatureFilterService
    {
        IEnumerable<FeatureItem> Filter(IEnumerable<FeatureItem> allFeatures, string? searchQuery, string? groupFilter);
        SelectionSummary CalculateSummary(IEnumerable<FeatureItem> visibleFeatures, IEnumerable<FeatureItem> allFeatures);
        List<int> GetDistinctSelectedFeatureIds(IEnumerable<FeatureItem> features);
    }
}
```

### 4. `IViVeToolDownloader`
```csharp
namespace ViVeToolApp.Services
{
    public interface IViVeToolDownloader
    {
        Task<string> DownloadAndExtractViVeToolAsync(string targetDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
        string? ExtractZipUrlFromReleaseJson(string json);
    }
}
```

## Code Layout
```
C:\Tools\ViVeToolApp\
├── ViVeToolApp.sln
├── ViVeToolApp.csproj
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── app.manifest
├── Models/
│   ├── FeatureItem.cs
│   ├── SelectionSummary.cs
│   ├── ViVeToolResult.cs
│   ├── ViVeExecutionMode.cs
│   ├── ViVeProgressReport.cs
│   └── ViVeBatchResult.cs
├── Services/
│   ├── IFeatureScraper.cs
│   ├── PureinfotechScraper.cs
│   ├── OfflineCatalog.cs
│   ├── IProcessLauncher.cs
│   ├── SystemProcessLauncher.cs
│   ├── IViVeToolRunner.cs
│   ├── ViVeToolRunner.cs
│   ├── IViVeToolLocator.cs
│   ├── ViVeToolLocator.cs
│   ├── IViVeToolDownloader.cs
│   ├── ViVeToolDownloader.cs
│   ├── IFeatureFilterService.cs
│   └── FeatureFilterService.cs
└── ViVeToolApp.Tests/
    ├── ViVeToolApp.Tests.csproj
    ├── ScraperTests/
    │   ├── PureinfotechScraperTests.cs
    │   └── OfflineCatalogTests.cs
    ├── ProcessRunnerTests/
    │   └── ViVeToolRunnerTests.cs
    ├── FeatureFilterTests/
    │   ├── FeatureFilterServiceTests.cs
    │   └── FeatureSummaryTests.cs
    ├── ModelTests/
    │   └── FeatureItemTests.cs
    └── StabilityResilienceTests/
        ├── ScraperResilienceTests.cs
        └── ViVeToolDownloaderTests.cs
```
