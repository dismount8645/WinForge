# ViewModel Logic Extraction & Testability Analysis Report

**Milestone 1 — ViewModel Logic Extraction**  
**Explorer**: Explorer 3  
**Working Directory**: `c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\teamwork_preview_explorer_m1_3\`  
**Target Files**:
- `WingetStore/ViewModels/SearchViewModel.cs`
- `WingetStore/ViewModels/HomeViewModel.cs`
- `WingetStore/ViewModels/InstalledViewModel.cs`
- `WingetStore/ViewModels/UpdatesViewModel.cs`
- `WingetStore/ViewModels/FilterableViewModel.cs`
- `WingetStore/ViewModels/RecommendationCardViewModel.cs`
- `WingetStore/ViewModels/UITestRunner.cs`
- `WingetStore.Tests/Tests.cs` (Baseline tests)

---

## Executive Summary

An investigation of the `WingetStore/ViewModels/` directory was conducted to identify non-UI pure logic (search query normalization, filter criteria evaluation, multi-field package sorting, state transitions, count calculations, and sort order mapping) that can be extracted from ViewModels into `public static` methods.

### Key Observations:
1. **Existing Baseline**:
   - `InstalledViewModel.cs` already contains `public static List<string> ExtractDevelopersList(IEnumerable<WingetPackage>? packages)`.
   - `UpdatesViewModel.cs` already contains `public static (bool IsVisible, double ProgressValue, string PercentText, string StatusText) CalculateGlobalProgress(IEnumerable<WingetPackage>? packages)`.
   - Existing unit tests in `WingetStore.Tests/Tests.cs` (~3700 lines) test ViewModels via reflection, field manipulation (`_allResults`, `_allPackages`), and dispatcher overrides (`App.DispatcherOverride`).
2. **Untested / Hard-to-Test Logic in ViewModels**:
   - **`SearchViewModel.cs`**: Search result filtering by query and source ("winget", "msstore", "all") and custom sort ordering placing `"winget"` source packages first is tightly coupled inside `ApplyFilter()`.
   - **`HomeViewModel.cs`**: Recommendation filtering and search result filtering are coupled inside `ApplyFilter()`. Search query display formatting (`"All Applications"` fallback) and search execution rules are embedded in `SearchInternalAsync()`.
   - **`InstalledViewModel.cs`**: Multi-criteria filtering (query, developer/publisher, source, redistributable category), count metrics (`AppsCount`, `RedistCount`, `TotalCount`), and package list status transition state updates (handling `Installable` vs `Installed` status changes from messaging) are inside instance methods.
   - **`UpdatesViewModel.cs`**: Upgradable package filtering, category splitting, count metrics, and removing completed upgrades on package status messages are inside instance methods.
   - **`FilterableViewModel.cs`**: Mapping sort order presets (`"az"`, `"za"`, `"publisher"`, `"id"`, `"status"`) to `SortBy`/`SortDirection` properties and count text formatting (`"Applications (5)"`) are done via instance property setters and partial methods.

Extracting these into `public static` helper methods in their respective files allows **100% direct unit test coverage** without needing UI dispatchers, reflection, or mock service wrappers.

---

## Detailed Proposals for Pure Method Extraction

### 1. `SearchViewModel.cs` Proposals

#### Proposal 1.1: `SearchViewModel.FilterSearchResults`
- **Location**: `WingetStore/ViewModels/SearchViewModel.cs:43-48`
- **Original Logic**:
  ```csharp
  public override void ApplyFilter()
  {
      var filtered = _allResults.FindAll(p => p.MatchesQuery(FilterQuery) && MatchesSourceFilter(p.Source, SourceFilter));
      if (SortOrder == SortOrders.Default) filtered = [.. filtered.OrderBy(p => (p.Source ?? "").Equals(SourceFilters.Winget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)]; else SortPackages(filtered, SortOrder);
      FilteredResults = [.. filtered]; HasResults = FilteredResults.Count > 0;
  }
  ```
- **Proposed Signature**:
  ```csharp
  public static List<WingetPackage> FilterSearchResults(
      IEnumerable<WingetPackage>? packages,
      string? filterQuery,
      string? sourceFilter,
      string? sortOrder)
  ```
- **Input Specifications**:
  - `packages`: Source collection of `WingetPackage` items (may be `null` or empty).
  - `filterQuery`: Query string to match against Package ID, Name, or Publisher.
  - `sourceFilter`: Source filter string (`"all"`, `"winget"`, `"msstore"`, etc.).
  - `sortOrder`: Sort order preset (`"default"`, `"az"`, `"za"`, `"publisher"`, `"id"`, `"status"`).
- **Output Specifications**:
  - Returns `List<WingetPackage>` filtered by query and source, and ordered according to `sortOrder`.
  - When `sortOrder` is `"default"`, packages with `Source == "winget"` are prioritized first.
- **Refactored `ApplyFilter()`**:
  ```csharp
  public override void ApplyFilter()
  {
      var filtered = FilterSearchResults(_allResults, FilterQuery, SourceFilter, SortOrder);
      FilteredResults = [.. filtered];
      HasResults = FilteredResults.Count > 0;
  }
  ```
- **xUnit Test Specifications** (to add to `WingetStore.Tests/Tests.cs`):
  - `FilterSearchResults_NullOrEmptyPackages_ReturnsEmptyList`: Passes `null` and empty list; verifies empty list returned.
  - `FilterSearchResults_FilterQuery_FiltersMatchingPackages`: Verifies query filtering by ID and Name.
  - `FilterSearchResults_SourceFilter_FiltersWingetAndMsstore`: Verifies filtering by source string.
  - `FilterSearchResults_DefaultSortOrder_PrioritizesWingetSource`: Verifies packages with `"winget"` source appear before other sources under default sort.
  - `FilterSearchResults_AzSortOrder_SortsByNameAscending`: Verifies alphabetical sorting by package name.

---

### 2. `HomeViewModel.cs` Proposals

#### Proposal 2.1: `HomeViewModel.FilterRecommendations` & `HomeViewModel.FilterHomeSearchResults`
- **Location**: `WingetStore/ViewModels/HomeViewModel.cs:84-96`
- **Original Logic**:
  ```csharp
  public override void ApplyFilter()
  {
      var filteredRecs = (_allRecommendations ?? []).FindAll(p => p.MatchesQuery(FilterQuery));
      SortPackages(filteredRecs, SortOrder);
      FilteredRecommendations = new ObservableCollection<WingetPackage>(filteredRecs);

      var filteredResults = (_allSearchResults ?? []).FindAll(p => p.MatchesQuery(FilterQuery) && MatchesSourceFilter(p.Source, SourceFilter));
      if (SortOrder == SortOrders.Default) filteredResults = [.. filteredResults.OrderBy(p => (p.Source ?? "").Equals(SourceFilters.Winget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];
      else SortPackages(filteredResults, SortOrder);

      FilteredSearchResults = [.. filteredResults];
      HasSearchResults = FilteredSearchResults.Count > 0;
  }
  ```
- **Proposed Signatures**:
  ```csharp
  public static List<WingetPackage> FilterRecommendations(
      IEnumerable<WingetPackage>? recommendations,
      string? filterQuery,
      string? sortOrder)

  public static List<WingetPackage> FilterHomeSearchResults(
      IEnumerable<WingetPackage>? searchResults,
      string? filterQuery,
      string? sourceFilter,
      string? sortOrder)
  ```
- **Input & Output Specifications**:
  - `FilterRecommendations`: Accepts `recommendations` list, filters by query, sorts by `sortOrder`, returns `List<WingetPackage>`.
  - `FilterHomeSearchResults`: Accepts `searchResults` list, filters by query & source, sorts (default prioritizes winget source), returns `List<WingetPackage>`.
- **Refactored `ApplyFilter()`**:
  ```csharp
  public override void ApplyFilter()
  {
      var filteredRecs = FilterRecommendations(_allRecommendations, FilterQuery, SortOrder);
      FilteredRecommendations = [.. filteredRecs];

      var filteredResults = FilterHomeSearchResults(_allSearchResults, FilterQuery, SourceFilter, SortOrder);
      FilteredSearchResults = [.. filteredResults];
      HasSearchResults = FilteredSearchResults.Count > 0;
  }
  ```

#### Proposal 2.2: `HomeViewModel.FormatSearchQueryDisplay` & `HomeViewModel.ShouldExecuteSearch`
- **Location**: `WingetStore/ViewModels/HomeViewModel.cs:57-67`
- **Original Logic**:
  ```csharp
  string searchKey = query?.Trim() ?? "";
  if (string.IsNullOrWhiteSpace(searchKey) && !forceSearchAll) { ... }
  ... SearchQuery = string.IsNullOrWhiteSpace(searchKey) ? "All Applications" : searchKey;
  ```
- **Proposed Signatures**:
  ```csharp
  public static string FormatSearchQueryDisplay(string? query)
  public static bool ShouldExecuteSearch(string? query, bool forceSearchAll)
  ```
- **Specifications**:
  - `FormatSearchQueryDisplay(query)`: returns `"All Applications"` when query is `null` or whitespace; otherwise returns `query.Trim()`.
  - `ShouldExecuteSearch(query, forceSearchAll)`: returns `true` if `query` is non-whitespace OR `forceSearchAll` is `true`.
- **xUnit Test Specifications**:
  - `FormatSearchQueryDisplay_NullOrWhitespace_ReturnsAllApplications`
  - `FormatSearchQueryDisplay_ValidQuery_ReturnsTrimmed`
  - `ShouldExecuteSearch_WhitespaceNotForced_ReturnsFalse`
  - `ShouldExecuteSearch_WhitespaceForced_ReturnsTrue`

---

### 3. `InstalledViewModel.cs` Proposals

#### Proposal 3.1: `InstalledViewModel.FilterInstalledPackages`
- **Location**: `WingetStore/ViewModels/InstalledViewModel.cs:90-105`
- **Original Logic**:
  ```csharp
  public override void ApplyFilter()
  {
      var baseList = _allPackages.FindAll(p => { ... });
      AppsCount = baseList.Count(p => !p.IsRedistributable);
      RedistCount = baseList.Count(p => p.IsRedistributable);
      TotalCount = baseList.Count;
      var filtered = baseList.FindAll(p => CategoryFilter switch { "Apps" => !p.IsRedistributable, "Redist" => p.IsRedistributable, _ => true });
      PackageFilteringHelper.SortPackages(filtered, SortBy, SortDirection);
      FilteredPackages = [.. filtered];
  }
  ```
- **Proposed Signature**:
  ```csharp
  public static (List<WingetPackage> FilteredPackages, int AppsCount, int RedistCount, int TotalCount) FilterInstalledPackages(
      IEnumerable<WingetPackage>? packages,
      string? filterQuery,
      string? developerFilter,
      string? sourceFilter,
      string? categoryFilter,
      string? sortBy,
      string? sortDirection)
  ```
- **Input & Output Specifications**:
  - Input: List of installed packages, query string, developer filter, source filter, category filter ("Apps", "Redist", "All"), sort criteria (`SortBy`, `SortDirection`).
  - Output: Value tuple `(FilteredPackages, AppsCount, RedistCount, TotalCount)`.
- **Refactored `ApplyFilter()`**:
  ```csharp
  public override void ApplyFilter()
  {
      var (filtered, appsCount, redistCount, totalCount) = FilterInstalledPackages(
          _allPackages, FilterQuery, DeveloperFilter, SourceFilter, CategoryFilter, SortBy, SortDirection);
      AppsCount = appsCount;
      RedistCount = redistCount;
      TotalCount = totalCount;
      FilteredPackages = [.. filtered];
  }
  ```

#### Proposal 3.2: `InstalledViewModel.UpdatePackageStatusInList`
- **Location**: `WingetStore/ViewModels/InstalledViewModel.cs:28-47` (messenger callback)
- **Original Logic**:
  Removes package if status becomes `Installable`, or updates `Version`/`Status` if status becomes `Installed`.
- **Proposed Signature**:
  ```csharp
  public static bool UpdatePackageStatusInList(
      List<WingetPackage> packages,
      WingetPackage statusUpdatePackage)
  ```
- **Specifications**:
  - Modifies `packages` collection based on `statusUpdatePackage.Status` and `Id`.
  - Returns `true` if any package in the list was modified or removed.
- **xUnit Test Specifications**:
  - `FilterInstalledPackages_CalculatesCountsCorrectly`: Verifies `AppsCount`, `RedistCount`, `TotalCount`.
  - `FilterInstalledPackages_FiltersByCategory`: Verifies "Apps" vs "Redist" filtering.
  - `UpdatePackageStatusInList_Installable_RemovesFromList`: Verifies package removal when status changes to `Installable`.
  - `UpdatePackageStatusInList_Installed_UpdatesVersion`: Verifies package version and status update when status changes to `Installed`.

---

### 4. `UpdatesViewModel.cs` Proposals

#### Proposal 4.1: `UpdatesViewModel.FilterUpgradablePackages`
- **Location**: `WingetStore/ViewModels/UpdatesViewModel.cs:71-86`
- **Original Logic**:
  ```csharp
  public override void ApplyFilter()
  {
      var baseList = _allUpgrades.FindAll(p => p.MatchesQuery(FilterQuery) && MatchesSourceFilter(p.Source, SourceFilter));
      AppsCount = baseList.Count(p => !p.IsRedistributable);
      RedistCount = baseList.Count(p => p.IsRedistributable);
      TotalCount = baseList.Count;
      var filtered = baseList.FindAll(p => CategoryFilter switch { "Apps" => !p.IsRedistributable, "Redist" => p.IsRedistributable, _ => true });
      PackageFilteringHelper.SortPackages(filtered, SortBy, SortDirection);
      FilteredUpgrades = new ObservableCollection<WingetPackage>(filtered);
  }
  ```
- **Proposed Signature**:
  ```csharp
  public static (List<WingetPackage> FilteredUpgrades, int AppsCount, int RedistCount, int TotalCount) FilterUpgradablePackages(
      IEnumerable<WingetPackage>? packages,
      string? filterQuery,
      string? sourceFilter,
      string? categoryFilter,
      string? sortBy,
      string? sortDirection)
  ```
- **Input & Output Specifications**:
  - Input: List of upgradable packages, query, source, category, sort parameters.
  - Output: Value tuple `(FilteredUpgrades, AppsCount, RedistCount, TotalCount)`.

#### Proposal 4.2: `UpdatesViewModel.ProcessPackageInstalled`
- **Location**: `WingetStore/ViewModels/UpdatesViewModel.cs:31-38`
- **Proposed Signature**:
  ```csharp
  public static bool ProcessPackageInstalled(
      List<WingetPackage> upgradesList,
      string packageId)
  ```
- **Specifications**:
  - Removes package matching `packageId` (case-insensitive) from `upgradesList`. Returns `true` if removed.
- **xUnit Test Specifications**:
  - `FilterUpgradablePackages_CalculatesCountsAndFiltersCategory`
  - `ProcessPackageInstalled_RemovesInstalledPackageById`

---

### 5. `FilterableViewModel.cs` Proposals

#### Proposal 5.1: `FilterableViewModel.MapSortOrder` & Count Formatting Static Methods
- **Location**: `WingetStore/ViewModels/FilterableViewModel.cs:21-23` & `53-61`
- **Original Logic**:
  ```csharp
  partial void OnSortOrderChanged(string value)
  {
      if (value == SortOrders.Az) { SortBy = "Name"; SortDirection = "Ascending"; }
      else if (value == SortOrders.Za) { SortBy = "Name"; SortDirection = "Descending"; }
      else if (value == SortOrders.Publisher) { SortBy = "Publisher"; SortDirection = "Ascending"; }
      else if (value == SortOrders.Id) { SortBy = "Id"; SortDirection = "Ascending"; }
      else if (value == SortOrders.Status) { SortBy = "Version"; SortDirection = "Descending"; }
      ApplyFilter();
  }
  ```
- **Proposed Signatures**:
  ```csharp
  public static (string SortBy, string SortDirection) MapSortOrder(string? sortOrder)
  public static string FormatAppsCountText(int count)
  public static string FormatRedistCountText(int count)
  public static string FormatAllCountText(int count)
  ```
- **Input & Output Specifications**:
  - `MapSortOrder("az")` => `("Name", "Ascending")`
  - `MapSortOrder("za")` => `("Name", "Descending")`
  - `MapSortOrder("publisher")` => `("Publisher", "Ascending")`
  - `MapSortOrder("id")` => `("Id", "Ascending")`
  - `MapSortOrder("status")` => `("Version", "Descending")`
  - `FormatAppsCountText(5)` => `"Applications (5)"`
  - `FormatRedistCountText(2)` => `"Redistributables (2)"`
  - `FormatAllCountText(7)` => `"All (7)"`
- **xUnit Test Specifications**:
  - `MapSortOrder_AllPresets_ReturnsExpectedSortByAndDirection`
  - `FormatCountTexts_FormatsExpectedStrings`

---

## Synthesis of Coverage Impact

| ViewModel File | New Static Methods Proposed | Estimated New Unit Tests | Coverage Impact |
|---|---|---|---|
| `SearchViewModel.cs` | 1 method (`FilterSearchResults`) | 5 tests | Direct coverage of search filtering & default sorting |
| `HomeViewModel.cs` | 4 methods (`FilterRecommendations`, `FilterHomeSearchResults`, `FormatSearchQueryDisplay`, `ShouldExecuteSearch`) | 8 tests | Direct coverage of home filtering & search query formatting |
| `InstalledViewModel.cs` | 2 methods (`FilterInstalledPackages`, `UpdatePackageStatusInList`) | 6 tests | Direct coverage of installed filtering, categories, & status transitions |
| `UpdatesViewModel.cs` | 2 methods (`FilterUpgradablePackages`, `ProcessPackageInstalled`) | 4 tests | Direct coverage of upgradable package filtering & installed removal |
| `FilterableViewModel.cs` | 4 methods (`MapSortOrder`, `FormatAppsCountText`, `FormatRedistCountText`, `FormatAllCountText`) | 6 tests | Direct coverage of sort presets & count label formatting |
| **Total** | **13 static methods** | **29 unit tests** | High signal test coverage increase without WinUI UI thread dependencies |

---

## Conclusion & Actionable Recommendation

By extracting pure static logic from ViewModel class files:
1. MVVM state management and XAML bindings remain completely unaffected, as instance methods (`ApplyFilter()`, messenger callbacks, property changed handlers) delegate directly to these static helper methods.
2. 29 high-value unit tests can be added to `WingetStore.Tests/Tests.cs` to test edge cases (null collections, case insensitivity, boundary counts, status changes).
3. The test suite avoids relying on fragile reflection, mock UI windows, or private field access.
