# Project: WingetStore Test Coverage Enhancement

## Architecture
WingetStore is a WinUI 3 desktop application targeting net10.0-windows10.0.26100.0.
The solution comprises:
- `WingetStore`: Main Application (XAML pages, ViewModels, Services, Helpers)
- `WingetStore.Tests`: xUnit v3 unit test project (Microsoft.Testing.Platform runner)
- `WingetStore.UITests`: WinUI Unit Test App

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | ViewModels Extraction & Tests | HomeViewModel, InstalledViewModel, UpdatesViewModel, SearchViewModel, FilterableViewModel | Baseline build passing | DONE |
| 2 | Services & Helpers Extraction & Tests | WingetParser, IconService, CachingWingetService, Helpers | M1 | DONE |
| 3 | Code-Behind Pages Extraction & Tests | HomePage.xaml.cs, InstalledPage.xaml.cs, UpdatesPage.xaml.cs, DetailsPage.xaml.cs | M2 | IN_PROGRESS |
| 4 | Final Verification & Hardening | Full build, 309+ baseline tests, executable test runner verification | M3 | PLANNED |

## Interface Contracts
- Pure logic is extracted into `public static` or `internal static` methods in original class files.
- Original event handlers and ViewModel methods delegate to extracted static helpers.
- New unit tests are added to `WingetStore.Tests/Tests.cs`.

## Code Layout
- Main App: `WingetStore/`
  - ViewModels: `WingetStore/ViewModels/`
  - Services: `WingetStore/Services/`
  - Helpers: `WingetStore/Helpers/`
  - Pages: `WingetStore/Pages/`
- Test Suite: `WingetStore.Tests/`
  - Tests: `WingetStore.Tests/Tests.cs`
