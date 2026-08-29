# Project: WingetStore

## Architecture
WinUI 3 Desktop Application using MVVM pattern with C# / .NET.
- **Pages**: HomePage, InstalledPage, UpdatesPage, DetailsPage, SettingsPage, AboutPage, NoWingetPage
- **ViewModels**: HomeViewModel, InstalledViewModel, UpdatesViewModel, SearchViewModel, RecommendationCardViewModel, FilterableViewModel
- **Services**: WingetService, CachingWingetService, IconService, SettingsService, CliProcessRunner, WingetParser, LogService, NotificationService, PackageFilteringHelper, AppPaths
- **Controls**: `Controls/ResponsivePageContainer.cs`, `Controls/PackageProgressControl.xaml`/`.xaml.cs`
- **Tests**: `WingetStore.Tests` (609 xUnit tests) and `WingetStore.exe --run-ui-tests` (59 WinUI integration tests).

## Test Strategy
- **Pure logic** is extracted into `internal static` methods and tested via xUnit (`dotnet test`, no WinUI needed).
- **WinUI-bound code** (event handlers, constructors) is exercised in the real WinUI runtime via the UITestRunner integration harness (`WingetStore.exe --run-ui-tests`).
- WinUI-bound code (`WingetStore.Tests.WinUIPageCreationTests`) fails under `dotnet test` (no WinUI message pump); exclude via `--filter-not-class WingetStore.Tests.WinUIPageCreationTests --xunit-info` and run `WingetStore.exe --run-ui-tests` instead.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | M0: Exploration & Baseline Audit | Audit WinUI 3 page layouts, async code quality, and test baseline | none | DONE |
| 2 | M1: Visual & Layout Refinement | Fluent Design, 16 DIP container margins, header column alignment, theme resource fixes, accessibility attributes | M0 | DONE |
| 3 | M2: Code Quality & Performance | Eliminate `async void` delegates, fix null checks on `DetailsPage`, resolve `IconService` file lock race, dispose CTS handles, safe Uri parsing | M1 | DONE |
| 4 | M3: Automated Test Verification | 100% test pass rate across all unit tests with 0 build errors | M2 | DONE |

## Interface Contracts
### View ↔ ViewModel
- ViewModels expose ObservableProperties, RelayCommands, and clean async Task methods.
- Views bind cleanly without XamlParse exceptions or unhandled binding errors.

### Service ↔ ViewModel
- Services provide thread-safe, async API surface returning Result or catching exceptions gracefully.

## Code Layout
- Root project: `WingetStore.csproj`
- Pages: `Pages/*.xaml`, `Pages/*.xaml.cs`
- ViewModels: `ViewModels/*.cs`
- Services: `Services/*.cs`
- Controls: `Controls/*.cs`
- Testing: `Testing/UITestRunner.cs`
- Unit Tests: `WingetStore.Tests/` (one file per test class)
- Integration Tests: `WingetStore.exe --run-ui-tests` via `Testing/UITestRunner.cs`
