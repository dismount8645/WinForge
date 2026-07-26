## 2026-07-23T16:27:39Z
You are Worker M3 for Milestone 3 (Code-behind pages logic extraction & unit tests).
Working Directory: c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\worker_m3\

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Task:
Extract non-UI pure logic from code-behind files (`HomePage.xaml.cs`, `InstalledPage.xaml.cs`, `UpdatesPage.xaml.cs`, `DetailsPage.xaml.cs`, `App.xaml.cs`, `MainWindow.xaml.cs`, `NoWingetPage.xaml.cs`, `SettingsPage.xaml.cs`) into public static / internal static helper methods, update code-behind files to delegate to these helpers, and add unit tests to `WingetStore.Tests/Tests.cs`.

Refer to the Explorer analysis reports for detailed signatures, implementations, and test cases:
- `c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\explorer_m3_1\analysis.md`
- `c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\explorer_m3_2\analysis.md`
- `c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\explorer_m3_3\analysis.md`

Extraction Targets to implement:
1. `HomePage.xaml.cs`:
   - `ExtractSearchQuery(object? parameter)`
   - `DetermineSearchViewState(bool isSearchActive, int itemCount, bool isLoading, string searchQuery)`
   - `ShouldUpdateGridLayout(bool gridRecreated, int newColumns, int lastColumns, double newSlotWidth, double lastSlotWidth, double newItemHeight, double lastItemHeight, double newCardHeight, double lastCardHeight, double newGap, double lastGap)`
   - `FormatSearchResultsTitle(string query)`

2. `InstalledPage.xaml.cs`:
   - `GetInstalledViewState(bool isLoading, int itemCount)`
   - `GetEligibleBulkUninstallPackages(IEnumerable<WingetPackage?>? selectedPackages)`
   - `GetImportStatusMessage(bool isSuccess, Exception? exception)` & `GetExportStatusMessage(bool isSuccess, string? filePath, Exception? exception)`
   - Add unit tests for existing static methods `GetUpdateVisibility` and `GetSortGlyph`.

3. `UpdatesPage.xaml.cs`:
   - `CanUpdateAll(bool hasItems, IEnumerable<WingetPackage>? packages)`
   - `FilterPackagesForBulkUpdate(IEnumerable<WingetPackage>? selectedPackages)`

4. `DetailsPage.xaml.cs`:
   - `FormatPublisher(string? publisher)`
   - `FormatVersionText(string? version, string? availableVersion)`
   - `FormatDescription(string? description)`
   - `FindActiveTaskForPackage(string? packageId, IEnumerable<InstallTask>? activeTasks)`
   - `GetTextSectionVisibility(string? text)` & `GetCollectionVisibility<T>(IReadOnlyCollection<T>? collection)`
   - `GetTagNavigationParameter(string tag)`

5. Other pages (`App.xaml.cs`, `MainWindow.xaml.cs`, `NoWingetPage.xaml.cs`, `SettingsPage.xaml.cs`):
   - `FormatLogDialogTitle`, `FormatActivityLogStatus` in `App.xaml.cs`
   - `IsBackButtonVisible` in `MainWindow.xaml.cs`
   - `GetPowershellInstallArguments`, `GetTempInstallerPath` in `NoWingetPage.xaml.cs`
   - `GetStatusBrushResourceKey` in `SettingsPage.xaml.cs`

Unit Testing Requirements:
- Add test classes / theory test methods in `WingetStore.Tests/Tests.cs` for all extracted static methods.
- Verify that `dotnet build WingetStore.Tests/WingetStore.Tests.csproj -p:Platform=x64` succeeds with 0 errors.
- Verify that `.\WingetStore.Tests\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\WingetStore.Tests.exe -class- WingetStore.Tests.WinUIPageCreationTests` runs all tests and exits with code 0.
- Ensure all 309 baseline tests + all existing M1 and M2 tests + all new M3 tests pass (zero failures, zero regressions).

Document your changes and verification results in `c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\worker_m3\handoff.md`.
