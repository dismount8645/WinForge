# Project: WingetStore

## Architecture
WinUI 3 Desktop Application using MVVM pattern with C# / .NET.
- **Pages**: DiscoverPage, InstalledPage, UpdatesPage, DetailsPage, SettingsPage
- **ViewModels**: DiscoverViewModel, InstalledViewModel, UpdatesViewModel, DetailsViewModel, SettingsViewModel, MainViewModel
- **Services**: PackageService, WingetCliService, SettingsService, NavigationService, UpdateService, TelemetryService, etc.
- **Controls**: Custom controls / components in Controls/
- **Tests**: `WingetStore.Tests` project containing 170+ MSTest / xUnit / NUnit unit tests.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | M0: Exploration & Baseline Audit | Audit WinUI 3 page layouts, async code quality, and test baseline (170/170 tests passing) | none | DONE |
| 2 | M1: Visual & Layout Refinement | Fluent Design, 16 DIP container margins, header column alignment, theme resource fixes, accessibility attributes | M0 | IN_PROGRESS |
| 3 | M2: Code Quality & Performance | Eliminate `async void` delegates, fix null checks on `DetailsPage`, resolve `IconService` file lock race, dispose CTS handles, safe Uri parsing | M1 | PLANNED |
| 4 | M3: Automated Test Verification | 100% test pass rate across all 170+ unit tests with 0 build errors | M2 | PLANNED |

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
- Controls: `Controls/*.xaml`, `Controls/*.xaml.cs`
- Unit Tests: `WingetStore.Tests/`
