# ViewModels Logic Extraction Analysis: HomeViewModel & FilterableViewModel

## Executive Summary
This analysis identifies pure, non-UI logic embedded within `FilterableViewModel.cs` and `HomeViewModel.cs` suitable for extraction into static helper methods in the same files.
By extracting this logic into static methods, unit tests in `WingetStore.Tests/Tests.cs` can achieve complete code coverage of property formatting, state mapping, search query processing, and list filtering/sorting without requiring MVVM framework instantiation or WinUI Dispatcher dependencies.

---

## 1. Overview of Target Files & Current Test Coverage

### Target Files Analyzed
1. `WingetStore/ViewModels/FilterableViewModel.cs` (69 lines)
   - Abstract base class for filterable view models (`HomeViewModel`, `InstalledViewModel`, `UpdatesViewModel`, `SearchViewModel`).
   - Contains count text getters, category toggle properties, property change callbacks, sort order property mapping, and static helper wrappers.

2. `WingetStore/ViewModels/HomeViewModel.cs` (98 lines)
   - ViewModel for the home page. Manages categories, recommendations, search results, search query active state, and filtering/sorting of recommendations and search results.

### Existing Test Baseline in `WingetStore.Tests/Tests.cs`
- `FilterableViewModelHelperTests`: Tests `MatchesSourceFilter` and `SortPackages` via static reflection calls.
- `ViewModelTests`: Instantiates ViewModels with mock/throwing services to test overall command execution and exception handling.
- **Coverage Gaps**:
  - Count text formatting properties (`AppsCountText`, `RedistCountText`, `AllCountText`) are un-tested at the static logic level.
  - Category filter selection check and state resolution (`IsCategoryApps`, `IsCategoryRedist`, `IsCategoryAll`) are un-tested.
  - `OnSortOrderChanged` mapping logic from `sortOrder` string to `(SortBy, SortDirection)` is implicit and un-tested for edge cases (null, default, unknown).
  - Search query normalization, validation, and display title formatting in `HomeViewModel.SearchInternalAsync` are un-tested as pure functions.
  - Recommendations filtering and sorting in `HomeViewModel.ApplyFilter` are un-tested directly.
  - Search results filtering and default Winget-source prioritization in `HomeViewModel.ApplyFilter` are un-tested directly.

---

## 2. Detailed Static Method Extraction Proposals

### Proposal 1: Count Text Formatting (`FilterableViewModel.cs`)
- **File & Lines**: `WingetStore/ViewModels/FilterableViewModel.cs` (lines 21–23)
- **Original Code**:
  ```csharp
  public string AppsCountText => $"Applications ({AppsCount})";
  public string RedistCountText => $"Redistributables ({RedistCount})";
  public string AllCountText => $"All ({TotalCount})";
  ```
- **Proposed Static Methods**:
  ```csharp
  public static string FormatAppsCountText(int count) => $"Applications ({count})";
  public static string FormatRedistCountText(int count) => $"Redistributables ({count})";
  public static string FormatAllCountText(int count) => $"All ({count})";
  ```
- **Refactored ViewModel Properties**:
  ```csharp
  public string AppsCountText => FormatAppsCountText(AppsCount);
  public string RedistCountText => FormatRedistCountText(RedistCount);
  public string AllCountText => FormatAllCountText(TotalCount);
  ```
- **Input / Output Specification**:
  - `FormatAppsCountText(0)` -> `"Applications (0)"`
  - `FormatAppsCountText(42)` -> `"Applications (42)"`
  - `FormatRedistCountText(5)` -> `"Redistributables (5)"`
  - `FormatAllCountText(100)` -> `"All (100)"`
- **xUnit Test Specification**:
  - Test class: `FilterableViewModelTests`
  - Methods:
    - `[Theory] FormatAppsCountText_ReturnsExpected(int count, string expected)`
    - `[Theory] FormatRedistCountText_ReturnsExpected(int count, string expected)`
    - `[Theory] FormatAllCountText_ReturnsExpected(int count, string expected)`

---

### Proposal 2: Category Filter Selection & Resolution (`FilterableViewModel.cs`)
- **File & Lines**: `WingetStore/ViewModels/FilterableViewModel.cs` (lines 25–39)
- **Original Code**:
  ```csharp
  public bool IsCategoryApps
  {
      get => CategoryFilter == "Apps";
      set { if (value && CategoryFilter != "Apps") CategoryFilter = "Apps"; }
  }
  public bool IsCategoryRedist
  {
      get => CategoryFilter == "Redist";
      set { if (value && CategoryFilter != "Redist") CategoryFilter = "Redist"; }
  }
  public bool IsCategoryAll
  {
      get => CategoryFilter == "All";
      set { if (value && CategoryFilter != "All") CategoryFilter = "All"; }
  }
  ```
- **Proposed Static Methods**:
  ```csharp
  public static bool IsCategorySelected(string? categoryFilter, string targetCategory)
      => string.Equals(categoryFilter, targetCategory, StringComparison.OrdinalIgnoreCase);

  public static string ResolveCategorySelection(string currentCategoryFilter, string targetCategory, bool isSelected)
      => isSelected ? targetCategory : currentCategoryFilter;
  ```
- **Refactored ViewModel Properties**:
  ```csharp
  public bool IsCategoryApps
  {
      get => IsCategorySelected(CategoryFilter, "Apps");
      set => CategoryFilter = ResolveCategorySelection(CategoryFilter, "Apps", value);
  }
  public bool IsCategoryRedist
  {
      get => IsCategorySelected(CategoryFilter, "Redist");
      set => CategoryFilter = ResolveCategorySelection(CategoryFilter, "Redist", value);
  }
  public bool IsCategoryAll
  {
      get => IsCategorySelected(CategoryFilter, "All");
      set => CategoryFilter = ResolveCategorySelection(CategoryFilter, "All", value);
  }
  ```
- **Input / Output Specification**:
  - `IsCategorySelected("Apps", "Apps")` -> `true`
  - `IsCategorySelected("Redist", "Apps")` -> `false`
  - `IsCategorySelected(null, "Apps")` -> `false`
  - `ResolveCategorySelection("Redist", "Apps", true)` -> `"Apps"`
  - `ResolveCategorySelection("Redist", "Apps", false)` -> `"Redist"`
- **xUnit Test Specification**:
  - `IsCategorySelected_ReturnsExpectedBool(string? category, string target, bool expected)`
  - `ResolveCategorySelection_ReturnsUpdatedOrCurrentCategory(string current, string target, bool selected, string expected)`

---

### Proposal 3: Sort Order Resolution (`FilterableViewModel.cs`)
- **File & Lines**: `WingetStore/ViewModels/FilterableViewModel.cs` (lines 53–61)
- **Original Code**:
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
- **Proposed Static Method**:
  ```csharp
  public static (string SortBy, string SortDirection) MapSortOrder(string? sortOrder, string currentSortBy = "Name", string currentSortDirection = "Ascending")
  {
      if (sortOrder == SortOrders.Az) return ("Name", "Ascending");
      if (sortOrder == SortOrders.Za) return ("Name", "Descending");
      if (sortOrder == SortOrders.Publisher) return ("Publisher", "Ascending");
      if (sortOrder == SortOrders.Id) return ("Id", "Ascending");
      if (sortOrder == SortOrders.Status) return ("Version", "Descending");
      return (currentSortBy, currentSortDirection);
  }
  ```
- **Refactored ViewModel Handler**:
  ```csharp
  partial void OnSortOrderChanged(string value)
  {
      (SortBy, SortDirection) = MapSortOrder(value, SortBy, SortDirection);
      ApplyFilter();
  }
  ```
- **Input / Output Specification**:
  - `MapSortOrder("az", "Name", "Ascending")` -> `("Name", "Ascending")`
  - `MapSortOrder("za", "Name", "Ascending")` -> `("Name", "Descending")`
  - `MapSortOrder("publisher", "Name", "Ascending")` -> `("Publisher", "Ascending")`
  - `MapSortOrder("id", "Name", "Ascending")` -> `("Id", "Ascending")`
  - `MapSortOrder("status", "Name", "Ascending")` -> `("Version", "Descending")`
  - `MapSortOrder("default", "Publisher", "Descending")` -> `("Publisher", "Descending")`
  - `MapSortOrder(null, "Id", "Ascending")` -> `("Id", "Ascending")`
- **xUnit Test Specification**:
  - `[Theory] MapSortOrder_ValidOrders_ReturnsCorrectPair(string order, string expectedBy, string expectedDir)`
  - `[Fact] MapSortOrder_UnknownOrNullOrder_PreservesCurrentValues()`

---

### Proposal 4: Search Parameter Processing & Display Query (`HomeViewModel.cs`)
- **File & Lines**: `WingetStore/ViewModels/HomeViewModel.cs` (lines 57–61, 67)
- **Original Code**:
  ```csharp
  string searchKey = query?.Trim() ?? "";
  if (string.IsNullOrWhiteSpace(searchKey) && !forceSearchAll)
  {
      App.Dispatch(() => { IsSearchActive = false; SearchQuery = ""; });
      return;
  }
  ...
  App.Dispatch(() => { IsLoading = true; IsErrorOpen = false; ErrorMessage = ""; SearchQuery = string.IsNullOrWhiteSpace(searchKey) ? "All Applications" : searchKey; IsSearchActive = true; });
  ```
- **Proposed Static Method**:
  ```csharp
  public static (bool ShouldSearch, string CleanQuery, string DisplayQuery) ProcessSearchQuery(string? query, bool forceSearchAll)
  {
      string cleanQuery = query?.Trim() ?? "";
      bool shouldSearch = !string.IsNullOrWhiteSpace(cleanQuery) || forceSearchAll;
      string displayQuery = string.IsNullOrWhiteSpace(cleanQuery) ? "All Applications" : cleanQuery;
      return (shouldSearch, cleanQuery, displayQuery);
  }
  ```
- **Refactored Method**:
  ```csharp
  public async Task SearchInternalAsync(string query, bool forceSearchAll = false)
  {
      var (shouldSearch, searchKey, displayQuery) = ProcessSearchQuery(query, forceSearchAll);
      if (!shouldSearch)
      {
          App.Dispatch(() => { IsSearchActive = false; SearchQuery = ""; });
          return;
      }
      // ... continue with search execution using searchKey and displayQuery ...
  ```
- **Input / Output Specification**:
  - `ProcessSearchQuery("  git  ", false)` -> `(true, "git", "git")`
  - `ProcessSearchQuery("  ", false)` -> `(false, "", "All Applications")`
  - `ProcessSearchQuery("", true)` -> `(true, "", "All Applications")`
  - `ProcessSearchQuery(null, true)` -> `(true, "", "All Applications")`
  - `ProcessSearchQuery(null, false)` -> `(false, "", "All Applications")`
- **xUnit Test Specification**:
  - `[Theory] ProcessSearchQuery_ValidQueries_ReturnsShouldSearchTrueAndCleanQuery(string input, bool forceAll, string expectedClean, string expectedDisplay)`
  - `[Theory] ProcessSearchQuery_EmptyQueries_ReturnsExpectedBehavior(string? input, bool forceAll, bool expectedShouldSearch)`

---

### Proposal 5: Recommendations Filtering & Sorting (`HomeViewModel.cs`)
- **File & Lines**: `WingetStore/ViewModels/HomeViewModel.cs` (lines 86–88)
- **Original Code**:
  ```csharp
  var filteredRecs = (_allRecommendations ?? []).FindAll(p => p.MatchesQuery(FilterQuery));
  SortPackages(filteredRecs, SortOrder);
  FilteredRecommendations = new ObservableCollection<WingetPackage>(filteredRecs);
  ```
- **Proposed Static Method**:
  ```csharp
  public static List<WingetPackage> FilterAndSortRecommendations(IEnumerable<WingetPackage>? recommendations, string filterQuery, string sortOrder)
  {
      var filtered = (recommendations ?? []).Where(p => p.MatchesQuery(filterQuery)).ToList();
      SortPackages(filtered, sortOrder);
      return filtered;
  }
  ```
- **Refactored Method**:
  ```csharp
  var filteredRecs = FilterAndSortRecommendations(_allRecommendations, FilterQuery, SortOrder);
  FilteredRecommendations = new ObservableCollection<WingetPackage>(filteredRecs);
  ```
- **Input / Output Specification**:
  - Input: list of packages (or null), `filterQuery`, `sortOrder`.
  - Returns: new `List<WingetPackage>` matching query and sorted by sort order.
- **xUnit Test Specification**:
  - `FilterAndSortRecommendations_NullInput_ReturnsEmptyList`
  - `FilterAndSortRecommendations_FiltersByQueryAndSortsByName`

---

### Proposal 6: Search Results Filtering & Default Source Sorting (`HomeViewModel.cs`)
- **File & Lines**: `WingetStore/ViewModels/HomeViewModel.cs` (lines 90–94)
- **Original Code**:
  ```csharp
  var filteredResults = (_allSearchResults ?? []).FindAll(p => p.MatchesQuery(FilterQuery) && MatchesSourceFilter(p.Source, SourceFilter));
  if (SortOrder == SortOrders.Default) filteredResults = [.. filteredResults.OrderBy(p => (p.Source ?? "").Equals(SourceFilters.Winget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];
  else SortPackages(filteredResults, SortOrder);

  FilteredSearchResults = [.. filteredResults];
  HasSearchResults = FilteredSearchResults.Count > 0;
  ```
- **Proposed Static Method**:
  ```csharp
  public static List<WingetPackage> FilterAndSortSearchResults(IEnumerable<WingetPackage>? searchResults, string filterQuery, string sourceFilter, string sortOrder)
  {
      var filtered = (searchResults ?? [])
          .Where(p => p.MatchesQuery(filterQuery) && MatchesSourceFilter(p.Source, sourceFilter))
          .ToList();

      if (sortOrder == SortOrders.Default)
      {
          filtered = [.. filtered.OrderBy(p => (p.Source ?? "").Equals(SourceFilters.Winget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];
      }
      else
      {
          SortPackages(filtered, sortOrder);
      }

      return filtered;
  }
  ```
- **Refactored Method**:
  ```csharp
  var filteredResults = FilterAndSortSearchResults(_allSearchResults, FilterQuery, SourceFilter, SortOrder);
  FilteredSearchResults = [.. filteredResults];
  HasSearchResults = FilteredSearchResults.Count > 0;
  ```
- **Input / Output Specification**:
  - Input: list of search result packages, `filterQuery`, `sourceFilter`, `sortOrder`.
  - Output: filtered and sorted list. If `sortOrder == "default"`, packages with `Source == "winget"` precede packages with other sources (`msstore`, etc.).
- **xUnit Test Specification**:
  - `FilterAndSortSearchResults_NullInput_ReturnsEmptyList`
  - `FilterAndSortSearchResults_DefaultSort_PutsWingetSourceFirst`
  - `FilterAndSortSearchResults_SourceFilter_FiltersByMsStore`

---

## 3. Summary Matrix of Proposed Extractions

| # | ViewModel File | Target Logic | Proposed Static Method Signature | Lines Affected |
|---|---|---|---|---|
| 1 | `FilterableViewModel.cs` | Count text formatting | `public static string FormatAppsCountText(int count)`<br>`public static string FormatRedistCountText(int count)`<br>`public static string FormatAllCountText(int count)` | 21–23 |
| 2 | `FilterableViewModel.cs` | Category filter state selection & update | `public static bool IsCategorySelected(string? categoryFilter, string targetCategory)`<br>`public static string ResolveCategorySelection(string currentCategoryFilter, string targetCategory, bool isSelected)` | 25–39 |
| 3 | `FilterableViewModel.cs` | Sort order string mapping | `public static (string SortBy, string SortDirection) MapSortOrder(string? sortOrder, string currentSortBy = "Name", string currentSortDirection = "Ascending")` | 53–61 |
| 4 | `HomeViewModel.cs` | Search query processing & display text | `public static (bool ShouldSearch, string CleanQuery, string DisplayQuery) ProcessSearchQuery(string? query, bool forceSearchAll)` | 57–61, 67 |
| 5 | `HomeViewModel.cs` | Recommendations list filter & sort | `public static List<WingetPackage> FilterAndSortRecommendations(IEnumerable<WingetPackage>? recommendations, string filterQuery, string sortOrder)` | 86–88 |
| 6 | `HomeViewModel.cs` | Search results list filter & source order sort | `public static List<WingetPackage> FilterAndSortSearchResults(IEnumerable<WingetPackage>? searchResults, string filterQuery, string sourceFilter, string sortOrder)` | 90–94 |

---

## 4. Risks & Preservation Verification
- **XAML Bindings**: Original property signatures (`AppsCountText`, `RedistCountText`, `AllCountText`, `IsCategoryApps`, `IsCategoryRedist`, `IsCategoryAll`, `FilteredRecommendations`, `FilteredSearchResults`) remain completely unchanged and continue to fire `OnPropertyChanged`.
- **MVVM State**: ViewModel methods (`OnSortOrderChanged`, `SearchInternalAsync`, `ApplyFilter`) delegate internal computation to the extracted static helper functions while maintaining state mutations in the ViewModel instance.
- **Backwards Compatibility**: Zero breaking changes for existing unit tests in `WingetStore.Tests/Tests.cs`.
