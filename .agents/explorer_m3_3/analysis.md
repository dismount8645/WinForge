# Milestone 3 — Code-Behind Pages Logic Extraction & Unit Test Analysis

## 1. Observation

### Target Files Analyzed
- `WingetStore/App.xaml.cs` (174 lines)
- `WingetStore/MainWindow.xaml.cs` (234 lines)
- `WingetStore/Pages/NoWingetPage.xaml.cs` (117 lines)
- `WingetStore/Pages/SettingsPage.xaml.cs` (79 lines)
- `WingetStore/Pages/AboutPage.xaml.cs` (9 lines)
- `WingetStore/Pages/DetailsPage.xaml.cs` (243 lines)
- `WingetStore/Pages/HomePage.xaml.cs` (349 lines)
- `WingetStore/Pages/InstalledPage.xaml.cs` (307 lines)
- `WingetStore/Pages/UpdatesPage.xaml.cs` (212 lines)
- *Note regarding `SearchPage.xaml.cs`*: A separate `SearchPage.xaml.cs` file does not exist in the codebase. Search functionality is hosted within `HomePage.xaml.cs` (search box `HomeSearchBox`, event handlers `HomeSearchBox_KeyDown`, `SearchButton_Click`, `ClearSearchButton_Click`) and backed by `SearchViewModel.cs`.

### Existing Unit Test Suite Verification
- Test runner: `dotnet test` (xUnit v3 with `Microsoft.Testing.Platform`).
- Execution command: `.\WingetStore.Tests\bin\Debug\net10.0-windows10.0.26100.0\win-x64\WingetStore.Tests.exe -class- WingetStore.Tests.WinUIPageCreationTests`
- Result: **Total: 496, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 5.603s**.

### Current Extracted Helper Methods & Test Coverage Inventory

| File | Extracted Static Method | Line(s) | Test Class in `Tests.cs` | Test Line Range |
|---|---|---|---|---|
| `App.xaml.cs` | `VisibleIf(bool)` | 34 | `AppHelperTests` | 2611–2617 |
| `App.xaml.cs` | `CollapsedIf(bool)` | 36 | `AppHelperTests` | 2619–2625 |
| `App.xaml.cs` | `Not(bool)` | 35 | `AppHelperTests` | 2627–2634 |
| `App.xaml.cs` | `ToImageSource(string)` | 38 | *(Untested)* | N/A |
| `App.xaml.cs` | `ParseTheme(string)` | 44 | `ThemeAndSortingTests` | 2459–2461 |
| `App.xaml.cs` | `GetCrashLogDirectory()` | 46 | `AppCrashLogTests` | 3398–3402 |
| `App.xaml.cs` | `GetCrashLogPath()` | 47 | `AppCrashLogTests` | 3404–3408 |
| `App.xaml.cs` | `GetCrashLogContent(string)` | 48 | `AppCrashLogTests` | 3412–3416 |
| `App.xaml.cs` | `FormatErrorDetails(Exception?, string)` | 49 | `AppCrashLogTests` | 3420–3435 |
| `MainWindow.xaml.cs` | `GetMinimumWindowSize(double, double, double)` | 51 | `MainWindowStaticTests` | 3440–3452 |
| `MainWindow.xaml.cs` | `IsTopLevelPage(Type)` | 126 | `MainWindowHelperTests` | 3054–3071 |
| `MainWindow.xaml.cs` | `GetNextTheme(string, ElementTheme)` | 142 | `MainWindowStaticTests` | 3454–3476 |
| `MainWindow.xaml.cs` | `GetBadgeData(int)` | 184 | `MainWindowHelperTests` | 3017–3040 |
| `MainWindow.xaml.cs` | `GetThemeToggleData(ElementTheme)` | 198 | `MainWindowHelperTests` | 3042–3050 |
| `NoWingetPage.xaml.cs` | `CalculateDownloadProgress(long, long)` | 19 | `NoWingetPageTests` | 3378–3393 |
| `SettingsPage.xaml.cs` | `GetDiagnosticsData(bool, DateTime)` | 49 | `SettingsPageDiagnosticsTests` | 2986–3015 |
| `DetailsPage.xaml.cs` | `GetActionButtonData(WingetPackage)` | 170 | `DetailsPageHelperTests` / `DetailsPageStaticTests` | 3150, 3629 |
| `DetailsPage.xaml.cs` | `GetProgressData(WingetPackage)` | 180 | `DetailsPageHelperTests` / `DetailsPageStaticTests` | 3169, 3647 |
| `DetailsPage.xaml.cs` | `GetViewLogsVisibility(WingetPackage?, ObservableCollection)` | 213 | `DetailsPageHelperTests` | 3190–3223 |
| `HomePage.xaml.cs` | `GetTextScaleData(double)` | 117 | `HomePageHelperTests` | 3338–3364 |
| `HomePage.xaml.cs` | `NormalizeQuery(string?)` | 237 | *(Private static helper)* | N/A |
| `HomePage.xaml.cs` | `GetSearchInputData(string)` | 239 | `HomePageHelperTests` | 3365–3376 |
| `InstalledPage.xaml.cs` | `GetUpdateVisibility(PackageStatus)` | 103 | `LogAndNotificationTests` (misnamed section) | 2097–2101 |
| `InstalledPage.xaml.cs` | `ToggleColumnSort(string, string, string)` | 225 | `InstalledPageStaticTests` | 3562–3575 |
| `InstalledPage.xaml.cs` | `GetSortGlyph(string, string, string)` | 251 | `PageSortGlyphTests` | 3120–3133 |
| `UpdatesPage.xaml.cs` | `GetUpdatesViewState(int)` | 90 | `UpdatesPageViewStateTests` / `UpdatesPageStaticTests` | 3074, 3577 |
| `UpdatesPage.xaml.cs` | `GetSortGlyph(string, string, string)` | 157 | `PageSortGlyphTests` / `UpdatesPageStaticTests` | 3135, 3615 |

---

## 2. Logic Chain

1. **Proven WinUI Testing Pattern**: In WinUI 3 applications, instantiating `Page` or `Window` UI controls directly in unit tests without a WinUI Application host thread causes `InvalidOperationException` or hangs. Extracting pure calculation, string formatting, status classification, and visibility derivation into `public static` methods allows 100% unit test coverage of that logic via xUnit tests running in standard console `dotnet test`.
2. **Analysis of Remaining Unextracted Pure Logic**: By auditing all 9 code-behind files line-by-line, 14 concrete unextracted logic targets were identified:
   - **`App.xaml.cs`**:
     - `ShowLogDialogForPackage` (lines 103, 112, 126): String formatting for dialog title (`$"Activity Log: {packageName} ({operation})"`) and status progress (`$"Status: {statusText} | Progress: {(int)progress}%"`). Extract as `FormatLogDialogTitle` and `FormatActivityLogStatus`.
     - `ToImageSource` (line 38): Existing `public static` method lacks explicit edge-case unit tests (null, empty string, invalid URI).
   - **`MainWindow.xaml.cs`**:
     - `NavFrame_Navigated` (line 131): Calculation `(!isTopLevelPage && canGoBack) ? Visibility.Visible : Visibility.Collapsed`. Extract as `public static Visibility IsBackButtonVisible(bool isTopLevelPage, bool canGoBack)`.
   - **`NoWingetPage.xaml.cs`**:
     - `InstallButton_Click` (line 75): Construction of PowerShell command string `$"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{tempPath}'\""`. Extract as `public static string GetPowershellInstallArguments(string tempPath)`.
     - Temp path resolution (line 40): `Path.Combine(tempDir, "Microsoft.DesktopAppInstaller.msixbundle")`. Extract as `public static string GetTempInstallerPath(string tempDir)`.
   - **`SettingsPage.xaml.cs`**:
     - `UpdateDiagnostics` (lines 63–66): Resource key selection for status brush (`isWingetAvailable ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"`). Extract as `public static string GetStatusBrushResourceKey(bool isWingetAvailable)`.
   - **`DetailsPage.xaml.cs`**:
     - `LoadDetailsAsync` (lines 64–66): Default fallback string formatting for publisher (`string.IsNullOrEmpty(publisher) ? "Unknown Publisher" : publisher`), version (`$"Version: {version}" + (string.IsNullOrEmpty(availableVersion) ? "" : $" (Latest: {availableVersion})")`), and description (`string.IsNullOrEmpty(description) ? "No description available for this package." : description`). Extract as `FormatPublisher`, `FormatVersionText`, `FormatDescription`.
     - `TagButton_Click` (line 126): Parameter construction `$"tag:{tag}"`. Extract as `public static string GetTagNavigationParameter(string tag)`.
   - **`HomePage.xaml.cs`**:
     - `OnNavigatedTo` (line 64): Parameter query extraction `queryParam.StartsWith("category:") ? queryParam["category:".Length..] : queryParam`. Extract as `public static string ExtractSearchQueryFromParameter(string queryParam)`.
     - `ViewModel_PropertyChanged` (line 198): Search title string formatting `$"Search Results for \"{query}\""`. Extract as `public static string FormatSearchResultsTitle(string query)`.
     - `ViewModel_PropertyChanged` (lines 189–197): Result panel visibility calculations based on `isSearching`, `hasItems`, and `isLoading`. Extract as `public static (Visibility SearchResultsListVis, Visibility EmptyStateVis, Visibility SearchResultsPanelVis, Visibility DiscoverPanelVis) GetSearchResultsVisibilities(bool isSearching, int itemCount, bool isLoading)`.
     - `NormalizeQuery` (line 237): Make `private static` method `public static` and add direct boundary unit tests.
   - **`InstalledPage.xaml.cs`**:
     - `BulkUninstallButton_Click` (line 290): Filtering packages eligible for bulk uninstall `selectedPackages.Where(p => p != null && !p.IsInstalling).ToList()`. Extract as `public static List<WingetPackage> GetEligiblePackagesForBulkUninstall(IEnumerable<WingetPackage> selected)`.
   - **`UpdatesPage.xaml.cs`**:
     - `UpdateViewForResultCount` (line 117): Update All button enabled condition `hasItems && filteredUpgrades.Any(p => !p.IsInstalling)`. Extract as `public static bool GetUpdateAllButtonEnabledState(bool hasItems, IEnumerable<WingetPackage> upgrades)`.
     - `BulkUpdateButton_Click` (line 195): Filtering packages eligible for bulk upgrade `selectedPackages.Where(p => p != null && !p.IsInstalling).ToList()`. Extract as `public static List<WingetPackage> GetEligiblePackagesForBulkUpgrade(IEnumerable<WingetPackage> selected)`.

---

## 3. Caveats

- **WinUI Host Incompatibility**: Do NOT attempt to write unit tests that instantiate `Page` or `Window` classes directly without the WinUI host process (e.g. `WinUIPageCreationTests`), as `testhost.exe` lacks the necessary WinUI message pump and will hang or fail under console `dotnet test`.
- **SearchPage File Scope**: The task specification listed `SearchPage.xaml.cs`. Be aware that `SearchPage.xaml.cs` does not exist as an independent file; search recommendations and search results are handled within `HomePage.xaml.cs` and `SearchViewModel.cs`.

---

## 4. Conclusion

The code-behind logic extraction strategy for Milestone 3 is in a strong state with 26 existing static methods already extracted and tested. Implementing the 14 newly identified static helper methods across the 7 code-behind files will complete logic extraction for all XAML pages in `WingetStore`, bringing overall unit test coverage of page logic to maximum reachable levels without WinUI UI thread dependency.

### Extraction Target & Test Recommendation Summary Table

| Source File | Proposed Method Signature | Purpose / Logic Extracted | Recommended Unit Tests |
|---|---|---|---|
| `App.xaml.cs` | `public static string FormatLogDialogTitle(string packageName, string operation)` | Formats activity log dialog title | Valid inputs, null/empty package name |
| `App.xaml.cs` | `public static string FormatActivityLogStatus(string statusText, double progress)` | Formats log status line with integer percentage | Valid status/progress, boundary progress (0, 100, 45.7) |
| `App.xaml.cs` | `public static ImageSource? ToImageSource(string path)` | Existing method | Test null, empty, whitespace, invalid URI return null |
| `MainWindow.xaml.cs` | `public static Visibility IsBackButtonVisible(bool isTopLevelPage, bool canGoBack)` | Derives title bar back button visibility | `(false, true) => Visible`, `(true, true) => Collapsed`, `(false, false) => Collapsed` |
| `NoWingetPage.xaml.cs` | `public static string GetPowershellInstallArguments(string tempPath)` | Constructs powershell `Add-AppxPackage` command | Correct quotation and path inclusion |
| `NoWingetPage.xaml.cs` | `public static string GetTempInstallerPath(string tempDir)` | Combines directory with installer filename | Standard path, trailing slash handling |
| `SettingsPage.xaml.cs` | `public static string GetStatusBrushResourceKey(bool isWingetAvailable)` | Returns resource key for status indicator brush | `true => "SystemFillColorSuccessBrush"`, `false => "SystemFillColorCriticalBrush"` |
| `DetailsPage.xaml.cs` | `public static string FormatPublisher(string? publisher)` | Fallback publisher text | Null/empty => `"Unknown Publisher"`, valid => original string |
| `DetailsPage.xaml.cs` | `public static string FormatVersionText(string version, string? availableVersion)` | Formats version label with optional latest version | Single version, version with latest available, null available |
| `DetailsPage.xaml.cs` | `public static string FormatDescription(string? description)` | Fallback description text | Null/empty => `"No description available..."`, valid => original |
| `DetailsPage.xaml.cs` | `public static string GetTagNavigationParameter(string tag)` | Constructs tag navigation parameter | `"git"` => `"tag:git"`, empty => `"tag:"` |
| `HomePage.xaml.cs` | `public static string ExtractSearchQueryFromParameter(string queryParam)` | Strips `"category:"` prefix from nav parameter | `"category:Tools"` => `"Tools"`, `"vscode"` => `"vscode"` |
| `HomePage.xaml.cs` | `public static string FormatSearchResultsTitle(string query)` | Formats search header title | `"git"` => `"Search Results for \"git\""` |
| `HomePage.xaml.cs` | `public static (Visibility SearchListVis, Visibility EmptyStateVis, Visibility SearchPanelVis, Visibility DiscoverPanelVis) GetSearchResultsVisibilities(bool isSearching, int itemCount, bool isLoading)` | Derives visibility states for home page panels | Searching with items, searching with no items (loading vs not loading), not searching |
| `HomePage.xaml.cs` | `public static string NormalizeQuery(string? value)` | Change access modifier from `private static` to `public static` | Null, whitespace, leading/trailing spaces |
| `InstalledPage.xaml.cs` | `public static List<WingetPackage> GetEligiblePackagesForBulkUninstall(IEnumerable<WingetPackage> selected)` | Filters out installing or null packages for bulk uninstall | List with non-installing, installing, null packages |
| `UpdatesPage.xaml.cs` | `public static bool GetUpdateAllButtonEnabledState(bool hasItems, IEnumerable<WingetPackage> upgrades)` | Derives Update All button enabled state | Empty list, list with all installing, list with upgradable packages |
| `UpdatesPage.xaml.cs` | `public static List<WingetPackage> GetEligiblePackagesForBulkUpgrade(IEnumerable<WingetPackage> selected)` | Filters out installing or null packages for bulk upgrade | List with non-installing, installing, null packages |

---

## 5. Verification Method

To independently verify the test suite and confirm that all extracted static methods pass unit testing:

1. **Run Console Test Runner**:
   ```pwsh
   .\WingetStore.Tests\bin\Debug\net10.0-windows10.0.26100.0\win-x64\WingetStore.Tests.exe -class- WingetStore.Tests.WinUIPageCreationTests
   ```
2. **Run via dotnet test (excluding WinUI app creation tests)**:
   ```pwsh
   dotnet test WingetStore.Tests/WingetStore.Tests.csproj --no-build -- -class- WingetStore.Tests.WinUIPageCreationTests
   ```
3. **Verify Pass Condition**: Output must report zero failed tests (`Failed: 0, Errors: 0`).
