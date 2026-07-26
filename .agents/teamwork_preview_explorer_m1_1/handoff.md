# Handoff Report: ViewModels Logic Extraction (HomeViewModel & FilterableViewModel)

## 1. Observation
- Analyzed `WingetStore/ViewModels/FilterableViewModel.cs` (lines 1 to 69).
  - Lines 21–23: `AppsCountText`, `RedistCountText`, `AllCountText` format strings directly in getters (`$"Applications ({AppsCount})"`, etc.).
  - Lines 25–39: `IsCategoryApps`, `IsCategoryRedist`, `IsCategoryAll` perform inline equality checks and conditional property assignments.
  - Lines 53–61: `OnSortOrderChanged` maps `sortOrder` string values (`SortOrders.Az`, `SortOrders.Za`, `SortOrders.Publisher`, `SortOrders.Id`, `SortOrders.Status`) to `SortBy` and `SortDirection`.
- Analyzed `WingetStore/ViewModels/HomeViewModel.cs` (lines 1 to 98).
  - Lines 57–61, 67: `SearchInternalAsync` trims query string, determines if search should execute based on `forceSearchAll`, and resolves display query title (`"All Applications"` vs clean query).
  - Lines 86–88: `ApplyFilter` filters `_allRecommendations` via `MatchesQuery` and sorts via `SortPackages`.
  - Lines 90–94: `ApplyFilter` filters `_allSearchResults` via `MatchesQuery` and `MatchesSourceFilter`, then sorts with custom `OrderBy` prioritizing `SourceFilters.Winget` when `SortOrder == SortOrders.Default` or calls `SortPackages`.
- Examined existing unit test suite in `WingetStore.Tests/Tests.cs` (~3701 lines, 309 passing tests excluding WinUI UI tests).
  - `FilterableViewModelHelperTests` currently tests `MatchesSourceFilter` and `SortPackages`.
  - `ViewModelTests` tests ViewModel error handling and command execution with throwing/mock services.
  - No direct static unit tests exist for count string formatting, category filter selection, sort order mapping, search query processing, or home recommendations/search filtering.

## 2. Logic Chain
1. *Observation*: Non-UI calculation logic in `FilterableViewModel.cs` (lines 21–23, 25–39, 53–61) and `HomeViewModel.cs` (lines 57–67, 86–94) is tightly coupled to instance methods and property getters.
2. *Reasoning*: Because these calculations are embedded inside ViewModel instance properties and partial methods, testing edge cases (e.g. null inputs, empty queries, custom sort orders, source prioritization, boundary counts) requires constructing full ViewModel instances with mock dependencies or UI dispatchers.
3. *Extraction Plan*: Extracting pure functions into `public static` helper methods within `FilterableViewModel.cs` (`FormatAppsCountText`, `FormatRedistCountText`, `FormatAllCountText`, `IsCategorySelected`, `ResolveCategorySelection`, `MapSortOrder`) and `HomeViewModel.cs` (`ProcessSearchQuery`, `FilterAndSortRecommendations`, `FilterAndSortSearchResults`) allows 100% pure unit testing in `WingetStore.Tests/Tests.cs`.
4. *Non-breaking Contract*: Original property getters (`AppsCountText`, `IsCategoryApps`, etc.) and ViewModel methods (`OnSortOrderChanged`, `ApplyFilter`, `SearchInternalAsync`) delegate directly to the extracted static methods, preserving XAML bindings and MVVM state notifications.

## 3. Caveats
- `HomeViewModel.LoadFeaturedContentAsync` and `HomeViewModel.SearchInternalAsync` perform asynchronous I/O and rely on `_winget` service calls and `App.Dispatch` for state updates. The async I/O and dispatcher calls themselves cannot be extracted into pure static methods, but all input query validation, normalization, string formatting, and list filtering/sorting are completely extracted.

## 4. Conclusion
We have identified 6 high-value static logic extractions across `FilterableViewModel.cs` and `HomeViewModel.cs`:
1. `FilterableViewModel.FormatAppsCountText`, `FormatRedistCountText`, `FormatAllCountText` (count text formatting).
2. `FilterableViewModel.IsCategorySelected` and `ResolveCategorySelection` (category filter state selection and resolution).
3. `FilterableViewModel.MapSortOrder` (sort order mapping to `SortBy` and `SortDirection`).
4. `HomeViewModel.ProcessSearchQuery` (search query trimming, validation, display title resolution).
5. `HomeViewModel.FilterAndSortRecommendations` (recommendations filtering and sorting).
6. `HomeViewModel.FilterAndSortSearchResults` (search results filtering and Winget-source prioritized sorting).

Detailed method specifications, line references, and xUnit test case specifications have been documented in `c:\Users\Jacob\.gemini\antigravity\scratch\WingetStore\.agents\teamwork_preview_explorer_m1_1\analysis.md`.

## 5. Verification Method
1. Inspect `analysis.md` for complete proposed code changes and test specifications.
2. After implementer applies changes, verify build and tests via `dotnet test` (excluding WinUI app host dependent class):
   `dotnet test --filter "FullyQualifiedName!~WinUIPageCreationTests"`
3. Verify all 309+ baseline tests pass plus new static method tests for `FilterableViewModel` and `HomeViewModel`.
