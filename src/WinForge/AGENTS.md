# WingetStore Test Coverage — Session Summary

## Objective
- Raise automated test coverage of WingetStore.dll by extracting testable pure logic from XAML code-behind into `internal static` methods (tested via xUnit), and exercising WinUI-bound event handlers/constructors in the real WinUI runtime via integration tests (UITestRunner).

## Important Details
- Test runner: `dotnet test` (xUnit v3, Microsoft.Testing.Platform) — 609 tests. Coverage via `dotnet coverage collect` then `reportgenerator`.
- **Toolchain now installed on this machine**: .NET SDK 10.0.302 + Windows SDK 10.0.26100 (via winget). `dotnet build` and test runs work locally. Add `C:\Program Files\dotnet` to PATH if `dotnet` isn't found (persist via `$env:PATH = "C:\Program Files\dotnet;$env:PATH"`).
- **xUnit run command (working on .NET 10 SDK)**: `dotnet build WingetStore.Tests/WingetStore.Tests.csproj -c Debug -p:Platform=x64`, then run the MTP exe directly: `WingetStore.Tests\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\WingetStore.Tests.exe -class- WingetStore.Tests.WinUIPageCreationTests`. The old `dotnet test WingetStore.Tests --filter-not-class ... --xunit-info` fails with `MSB1001: Unknown switch` on .NET 10 (those are adapter switches, not MTP exe switches; the exe uses `-class- "Name"` to exclude a class).
- **Culture fix (en-DK machines)**: this machine's culture is en-DK (time separator `.`). `LogService.FormatLogEntry`, `App.GetCrashLogContent`, and the `h:mm tt` refresh timestamps in `InstalledViewModel`/`UpdatesViewModel` now use `CultureInfo.InvariantCulture` so log/timestamp format is deterministic (`18:00:00`, not `18.00.00`). All 3 previously-failing `LogServiceStaticTests` now pass.
- **Proven pattern**: extract pure logic into `internal static` methods → test via xUnit (no WinUI needed). For WinUI-bound code, run integration tests via `WingetStore.exe --run-ui-tests` (59 tests, all pass).
- **WinUI-bound tests**: `WingetStore.Tests.WinUIPageCreationTests` cannot run under `dotnet test` (no WinUI/XAML message pump in testhost.exe) — must be excluded via `-class- WingetStore.Tests.WinUIPageCreationTests` when running the MTP exe. WinUI-bound code is instead exercised at runtime via `WingetStore.exe --run-ui-tests`.
- `OverrideServiceProvider` and `DelegatingWingetService` / `MockWingetService` in UITestRunner enable mock-based integration tests by wrapping `App.Services` with a DI override for `IWingetService`.
- `IconService.NormalizePackageName` made `internal` for testing.
- `WingetPackage.GetPlaceholderColorForName` extracted as `internal static` returning `Windows.UI.Color` (pure struct, no WinUI dependency). `GetPlaceholderBrushForName` wraps it in `SolidColorBrush`.
- `WingetPackage.GetPlaceholderBrushForName` and `PlaceholderBackground` no longer `[ExcludeFromCodeCoverage]` — covered by integration tests (HomePage displays packages with placeholders).
- All **static methods** across all page classes (HomePage, InstalledPage, UpdatesPage, DetailsPage, MainWindow, App) are tested via xUnit with no WinUI dependency.

## Work State
### Completed (Round 4 — structure/duplication cleanup)
- **Round 3 + 4**: `FilterableViewModel` hosts `SourceFilter`+`OnSourceFilterChanged`; `HomeViewModel`/`SearchViewModel` search-filter statics delegate to `PackageFilteringHelper`; deleted `MockInnerService.cs`; `ThrowingWingetService` now extends `StubWingetService`; removed `WingetStore.UITests` project (round 2).
- **`PackageFilteringHelper.FilterAndCountPackages`**: new shared static (query+source+optional-extra filter → apps/redist/total counts → category filter → sort). `InstalledViewModel.FilterInstalledPackages` and `UpdatesViewModel.FilterUpgradablePackages` are now thin delegating statics (same signatures, existing tests unchanged). `MatchesCategoryFilter` moved to the helper; `FilterableViewModel.MatchesCategoryFilter` delegates.
- **`PackageFilteringHelper.GetEligiblePackagesForAction`**: new shared "filter null + !IsInstalling" static; `InstalledPage.GetEligibleBulkUninstallPackages`, `UpdatesViewModel.GetEligiblePackagesForUpgrade`, `UpdatesPage.FilterPackagesForBulkUpdate` now delegate (identical semantics).
- **`Services/AppPaths.cs`**: centralizes `LocalAppData\WingetStore` path. Consumed by `App.GetCrashLogDirectory`/`GetCrashLogPath`, `LogService` (`LogsDir`/`AppLogFile`), `IconService` (`CacheDir`/`CacheFile`/`IconsDir`), `SettingsService.SettingsFilePath`. Tests reconstruct identical literal paths and reflect on the same field names — unaffected.
- **`IconService.DownloadToFileAsync`**: extracted from 3 duplicated download→write→notify blocks (`DownloadIconAsync`, `ResolveIconOnlineAsync` hunter/favicon paths).
- **`Pages/SortGlyphUpdater.cs`**: static helper; `InstalledPage.UpdateSortGlyphs` + `UpdatesPage.UpdateSortGlyphs` are now one-liners delegating to it. Private handler names preserved (UITestRunner reflection intact).
- **`WingetPackage.PlaceholderColors`**: 10-color array hoisted to `private static readonly` field (was allocated per call).
- **`HomePage`**: removed dead empty `SaveDiscoveryState()` + call; removed dead `if (!queued)` branch in `OnTextScaleFactorChanged`.

### Completed (Round 4 culture fix)
- **Culture-invariant timestamps**: `LogService.FormatLogEntry` (`Services/LogService.cs:16`), `App.GetCrashLogContent` (`App.xaml.cs:49`), `InstalledViewModel.LastRefreshTimeText`, `UpdatesViewModel.LastRefreshTimeText` now use `CultureInfo.InvariantCulture` (added `using System.Globalization;`). Previously the `:` in `DateTime` custom format strings was replaced by the culture's time separator — on en-DK machines logs/timestamps rendered `18.00.00`. Fixed 3 `LogServiceStaticTests` failures; verified on this en-DK machine.

### Completed (Round 4 XAML — shared styles + PackageProgressControl)
- **`App.xaml`**: added `SortHeaderButtonStyle` (Padding=0, Background=Transparent, BorderThickness=0, HorizontalAlignment=Left) and `PackageListRowItemStyle` (Margin=0, Padding=0, HorizontalContentAlignment=Stretch, BorderThickness=0) resources at the end of the main `ResourceDictionary`.
- **`Pages/InstalledPage.xaml` / `Pages/UpdatesPage.xaml`**: three sort-header `Button`s (Name, Version/Version-Available, Publisher) now use `Style="{StaticResource SortHeaderButtonStyle}"`; both ListViews' `ItemContainerStyle` now `BasedOn="{StaticResource PackageListRowItemStyle}"`. `AutomationProperties.Name` values preserved (UITestRunner uses `AutomationPeer.Invoke`).
- **`Controls/PackageProgressControl.xaml` + `.xaml.cs` (new)**: dedups the 3 duplicated "status text + progress bar + view-log button" snippets (InstalledPage row, UpdatesPage card + row). DPs: `StatusText`, `Progress`, `IsInstalling` (callback toggles `RootGrid.Visibility`), `StatusPanelMinWidth` (default 0; card variant uses 100), `LogButtonMargin` (default `8,0,0,0`; card variant uses `4,0,0,0`). `LogRequested` event re-raises `LogButton_Click` with the inner `Button` as sender so existing page handlers (`ViewTaskLog_Click`, `sender is Button { DataContext: WingetPackage }`) match unchanged.
- **x:Bind mode resolution (IMPORTANT)**: `{x:Bind}` defaults to **OneTime** for ALL bindings, including function bindings (`App.VisibleIf(...)` without `Mode` gets NO change tracking — per MS function-bindings doc "If you set the mode to OneWay or TwoWay, the function path will have change detection"). Therefore the original InstalledPage/UpdatesPage progress-panel visibility was OneTime (only HomePage.xaml:70 uses explicit `Mode=OneWay` for `VisibleIf(Package.IsInstalling)`); the replacement `IsInstalling="{x:Bind IsInstalling}"` (OneTime) preserves that exactly. Do NOT "fix" it to OneWay without a behavior-change decision.
- **Internal control bindings**: `MinWidth="{x:Bind StatusPanelMinWidth, Mode=OneWay}"` and `Margin="{x:Bind LogButtonMargin, Mode=OneWay}"` MUST stay `Mode=OneWay` — the parent page sets those attributes *after* the control constructor runs, so a OneTime binding would capture the DP defaults (0 / 8,0,0,0) and the UpdatesPage card overrides would silently never apply.

### Completed
- **xUnit tests**: 609 tests pass, covering:
  - All static methods: GetTextScaleData, ShouldUpdateGridLayout, FormatSearchResultsTitle, DetermineSearchViewState, ExtractSearchQuery, NormalizeQuery, GetSearchInputData (HomePage)
  - GetUpdateVisibility, GetSortGlyph, GetInstalledViewState, GetEligibleBulkUninstallPackages, GetImportStatusMessage, GetExportStatusMessage, ToggleColumnSort (InstalledPage)
  - GetSortGlyph, GetUpdatesViewState, CanUpdateAll, FilterPackagesForBulkUpdate (UpdatesPage)
  - FormatPublisher, FormatVersionText, FormatDescription, FindActiveTaskForPackage, GetTextSectionVisibility, GetCollectionVisibility, GetTagNavigationParameter, GetActionButtonData, GetProgressData, GetViewLogsVisibility (DetailsPage)
  - GetBadgeData, GetThemeToggleData, IsTopLevelPage, IsBackButtonVisible, ResolveCurrentTheme, GetMinimumWindowSize, GetNextTheme (MainWindow)
  - VisibleIf, CollapsedIf, ToImageSource, ParseTheme, FormatLogDialogTitle, FormatActivityLogStatus (App)
  - All ViewModels (100%), WingetService (96%), CachingWingetService (100%)

- **Integration tests (UITestRunner)**: 59/59 pass, covering:
  - All 8 pages (HomePage, InstalledPage, UpdatesPage, SettingsPage, AboutPage, DetailsPage, NoWingetPage)
  - ErrorWindow (construct/Activate/Close + close button AutomationPeer.Invoke)
  - NavigationHelper.CanGoBack (after navigating to DetailsPage)
  - SettingsPage toggle switches (AutoUpdateToggle, NotificationsToggle, TestStatusButton)
  - SettingsService I/O error paths (read-only file → SaveSettings catch, replace file with dir → LoadSettings catch)
  - DetailsPage event handlers (Screenshot_Click, CloseLightbox_Click, LightboxOverlay_Tapped, TagButton_Click, ViewLogsButton_Click, Package_PropertyChanged with 4 properties, ActionButton_Click)
  - DetailsPage BackButton_Click
  - **DetailsPage rich data via mock** (covers icon URL try path, ReleaseNotes, Tags, Screenshots)
  - **DetailsPage icon catch block** (invalid URL → BitmapImage constructor throws)
  - InstalledPage/UpdatesPage sort headers (Name, Version, Publisher) and category buttons
  - InstalledPage bulk select with injected items (Toggle, SelectAll, DeselectAll, Cancel via BulkSelectionHelperUI)
  - **HomePage SearchButton_Click** (covers ProcessSearchInput path)
  - **HomePage DetailsButton_Click** (covers navigation via Button.DataContext)
  - **HomePage ActionButton_Click** (covers both RecommendationCardViewModel and WingetPackage DataContext branches)
  - **InstalledPage ViewTaskLog_Click / UninstallSingle_Click / UpdateSingle_Click** (covers ShowLogDialogForPackage, Uninstall, Upgrade paths via Button.DataContext)
  - **InstalledPage BulkUninstallButton_Click** (early return with no selected items)
  - **UpdatesPage ViewTaskLog_Click / UpdateSingle_Click** (covers ShowLogDialogForPackage, Upgrade paths)
  - **UpdatesPage BulkUpdateButton_Click** (early return with no selected items)
  - HomePage clear search and see-all
  - **HomeViewModel search cancellation** (fire-and-forget → cancel mid-flight → OperationCanceledException)
  - **HomeViewModel search exception** (mock throws → Exception catch)
  - MainWindow theme toggle, UpdateThemeToggleIcon, UpdateUpdatesBadge(0/5/150), SizeChanged (via AppWindow API), TitleBarBackButton_Click
  - NavView full navigation (Home→Installed→Updates→Settings→Home)
  - NoWingetPage install click & cancel (via `_installCts`)
  - IconService NotifyIconsUpdated, LoadDatabaseAsync (temp JSON), DownloadIconAsync error path (fire-and-forget via GetIconUrl)
  - PackageDetailHelper.PopulateMetadata (StackPanel + 6 cards with URLs with sub-items)
  - **InstalledPage ViewModel_PropertyChanged** (IsLoading/FilteredPackages/LastRefreshTimeText via synchronous DispatcherOverride)
  - **InstalledPage IconService_IconsUpdated** (simulates icon refresh event)
  - **UpdatesPage ViewModel_PropertyChanged** (IsLoading/FilteredUpgrades/4 global progress properties)
  - **UpdatesPage IconService_IconsUpdated** (simulates icon refresh event)
  - **HomePage ViewModel_PropertyChanged** (IsLoading/IsSearchActive/FilteredSearchResults/FilteredRecommendations)
  - **HomePage IconService_IconsUpdated** (simulates icon refresh event)
- **`App.DispatcherOverride`**: Allows integration tests to force synchronous execution of `App.Dispatch` callbacks, enabling direct reflection-based invocation of `ViewModel_PropertyChanged` and `IconService_IconsUpdated` handlers without waiting for the DispatcherQueue message pump.
- **App.xaml**: Added fallback XAML resources (HeaderCommandStyle, HeaderCommandToggleStyle, CategoryToggleStyle, AccentButtonStyle, SystemFillColorAlertBrush) for profiler compatibility
- **App.xaml.cs** (line 149-161): `--run-ui-tests` dispatches UITestRunner, then exits process
- **`NavigationMode` enum + responsive sidebar** (`MainWindow.xaml.cs`): `GetNavigationMode(double width)` returns `Desktop` (≥900), `Tablet` (600–899), or `Phone` (<600). `ApplyNavigationMode` switches `NavigationView.PaneDisplayMode` — `Left`/`LeftCompact`/`LeftMinimal` — hides PaneFooter in compact/minimal, toggles hamburger button, adjusts Settings margin. 12 new xUnit tests (609 total).
- **WingetPackage.cs**: `GetPlaceholderColorForName` extracted (`internal static`, returns `Windows.UI.Color`); `GetPlaceholderBrushForName` delegates to it; `IsNullOrWhiteSpace` fix (was `IsNullOrEmpty`, missed whitespace); no `[ExcludeFromCodeCoverage]` on either
- **ViewModels/UITestRunner.cs**: Resilient integration test runner (59 tests, independent try/catch per test) with `OverrideServiceProvider` + `DelegatingWingetService`/`MockWingetService` for DI-aware mock integration tests. Covers ViewModel_PropertyChanged and IconService_IconsUpdated handlers across all 3 main pages (InstalledPage, UpdatesPage, HomePage) via synchronous `App.DispatcherOverride`.
- **DetailsPage rich-data integration test**: Uses `OverrideServiceProvider` to swap `App.Winget` with `MockWingetService` which returns a package with IconUrl, ReleaseNotes, Tags, and Screenshots — covers all previously-unreachable code paths in `LoadDetailsAsync`. Provider restored in `finally` block.
- **HomeViewModel search tests**: Use `DelegatingWingetService` to override SearchPackagesAsync for cancellation + exception coverage.

### Measured Coverage (class-level, profiler-dependent)
| Class | Coverage |
|---|---|
| **ErrorWindow** | **100%** |
| **SettingsPage** | **100%** |
| **AboutPage** | **100%** |
| **CliProcessRunner** | **100%** |
| **NavigationHelper** | **100%** |
| **PackageDetailHelper** | **100%** |
| **CachingWingetService** | **100%** |
| **LogService** | **100%** |
| **NotificationService** | **100%** |
| **AppSettings** | **100%** |
| **BulkSelectionHelper** | **100%** |
| **GridCalculator** | **100%** |
| **GridDimensions** | **100%** |
| **CategoryItem** | **100%** |
| **MetadataItem** | **100%** |
| **PackageId** | **100%** |
| **PackageVersion** | **100%** |
| **PackageStatusChangedMessage** | **100%** |
| **FilterableViewModel** | **100%** |
| **RecommendationCardViewModel** | **100%** |
| **RecommendationLayoutState** | **100%** |
| **ResponsivePageContainer** | **100%** |
| **MainWindow** | **93.9%** |
| **SettingsService** | **92.3%** |
| **DetailsPage** | **91.4%** |
| **HomeViewModel** | **91.6%** |
| **InstalledViewModel** | **90.5%** |
| **UpdatesViewModel** | **90%** |
| **IconService** | **85%** |
| **WingetService** | **83.5%** |
| **UpdatesPage** | **79.3%** |
| **HomePage** | **78.8%** |
| **VersionComparer** | **76.9%** |
| **SearchViewModel** | **73.3%** |
| **BulkSelectionHelperUI** | **70%** |
| **WingetPackage** | **95.8%** |
| **WingetParser** | **97.2%** |
| **App** | **66.6%** |
| **PackageFilteringHelper** | **96.8%** |
| **NoWingetPage** | **32.9%** |
| **InstalledPage** | **51.3%** |
| **UITestRunner** | **80.3%** |

### Blocked / Hard to Test
- `dotnet test` cannot execute `[UITestMethod]` or WinUIPageCreationTests — no WinUI host.
- 8 WingetService lines (129-132, 148-151) are unreachable catch blocks (inside GetRecommendationsAsync try/catch — exception doesn't propagate from nested calls).
- InstalledPage ExportButton_Click / ImportButton_Click (0% for their meaningful bodies — ~35% entry): use FileSavePicker/FileOpenPicker requiring user interaction — cannot automatedly test.
- SettingsService 2 lines (file I/O catch) — fragile to test deterministically; exercised via read-only/dir-swap integration test.
- IconService `ResolveIconOnlineAsync` HTTP success path needs real HTTP response (~48.5%).
- HomeViewModel ~5 lines — lambdas inside async state machine (profiler can't distinguish branches from `catch`/`finally`).
- NavigationHelper.GetPageType fallthrough cases — `_ => null`, `string.IsNullOrEmpty(tag) => null` — trivial single-line returns.
- NoWingetPage `InstallButton_Click` download/install body (~74% of method) — requires actual winget download/install.
- DetailsPage `LoadDetailsAsync` icon catch block (lines 105-112) requires `new BitmapImage` to throw synchronously; now covered via mock with invalid URI.
- InstalledPage/UpdatesPage/HomePage `ViewModel_PropertyChanged` and `IconService_IconsUpdated` — now covered via reflection with `App.DispatcherOverride` for synchronous dispatch.
- `InstalledAppsList_ItemClick` / `UpdatesList_ItemClick` / `PopularAppsGrid_ItemClick` / `SearchResultsList_ItemClick` / `CategoriesGrid_ItemClick`: require `ItemClickEventArgs` with settable `ClickedItem` — WinRT property is get-only, blocking test of navigate body.
- HomePage remaining untested event handlers: `Page_Loaded/Unloaded`, `HomeSearchBox_KeyDown` — require XAML load events / KeyRoutedEventArgs construction.
- `InstalledAppsList_ItemClick` / `UpdatesList_ItemClick` / `PopularAppsGrid_ItemClick` / `SearchResultsList_ItemClick` / `CategoriesGrid_ItemClick`: require `ItemClickEventArgs` with settable `ClickedItem` — WinRT property is get-only, blocking test of navigate body

## Relevant Files
- `WingetStore/WingetStore.Tests/`: 609 tests, one file per class (split from the former `Tests.cs` monolith). Shared usings live in `GlobalUsings.cs`.
- `WingetStore/Models/WingetPackage.cs` (line 97-109): `PlaceholderBackground`, `GetPlaceholderColorForName` (pure, static `PlaceholderColors` array), `GetPlaceholderBrushForName` (wraps in SolidColorBrush).
- `WingetStore/Services/AppPaths.cs`: shared `LocalAppData\WingetStore` path constants (`Root`, `LogsDir`, `AppLogFile`, `SettingsFile`, `IconsCacheDir`, `ScreenshotDbFile`, `CrashLogFile`).
- `WingetStore/Services/IconService.cs`: `NormalizePackageName` made `internal`; `DownloadToFileAsync` shared download helper.
- `WingetStore/Services/WingetService.cs`: `TriggerPackageAction` Cancel branch covered.
- `WingetStore/Services/CachingWingetService.cs`: `CancelTask`/`CancelTaskForPackage` covered (100%).
- `WingetStore/Services/PackageDetailHelper.cs`: `PopulateMetadata`.
- `WingetStore/Services/BulkSelectionHelperUI.cs`: bulk selection UI helper.
- `WingetStore/ViewModels/FilterableViewModel.cs`: All partial methods + computed properties covered (100%).
- `WingetStore/ViewModels/HomeViewModel.cs`: `OnSourceFilterChanged` covered.
- `WingetStore/Testing/UITestRunner.cs`: 59 integration tests, resilient per-page try/catch. Contains `OverrideServiceProvider` + `DelegatingWingetService`/`MockWingetService` for mock-based testing.
- `WingetStore/App.xaml`: Fallback XAML resources for profiler compatibility + `SortHeaderButtonStyle`/`PackageListRowItemStyle` (round 4 XAML).
- `WingetStore/Controls/PackageProgressControl.xaml` + `.xaml.cs` (new): shared progress-status + view-log button; DPs `StatusText`, `Progress`, `IsInstalling`, `StatusPanelMinWidth`, `LogButtonMargin`; `LogRequested` event. Internal `MinWidth`/`Margin` bindings are `Mode=OneWay` (parent sets them post-construction).
- `WingetStore/Pages/InstalledPage.xaml` / `Pages/UpdatesPage.xaml`: shared sort-header + row styles; 3 progress/log snippets replaced with `<controls:PackageProgressControl>`.
