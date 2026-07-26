# Milestone 3 Analysis Report — HomePage & InstalledPage Logic Extraction & Unit Testing

## Executive Summary
This investigation analyzed `HomePage.xaml.cs` and `InstalledPage.xaml.cs` in `WingetStore` to identify non-UI pure logic, helper calculations, data formatting, sorting, filtering, and state visibility logic suitable for extraction into `public static` or `internal static` methods for unit testing.

Existing tests in `WingetStore.Tests/Tests.cs` cover 309 unit tests across 38 classes, including preliminary static methods on `HomePage` (`GetTextScaleData`, `GetSearchInputData`) and `InstalledPage` (`ToggleColumnSort`). However, several key static methods in `InstalledPage.xaml.cs` (`GetUpdateVisibility`, `GetSortGlyph`) currently lack unit test coverage, and several rich logic blocks in both code-behind files remain embedded inside UI event handlers or `PropertyChanged` subscriptions.

Extracting these identified targets into static methods will increase unit test coverage across code-behind files without requiring WinUI XAML dispatcher infrastructure.

---

## 1. Target File 1: `HomePage.xaml.cs`

### 1.1 Existing Static Methods & Coverage Status
1. **`GetTextScaleData(double factor)`** (`HomePage.xaml.cs:117`)
   - **Signature**: `public static (double CardHeight, double ItemHeight) GetTextScaleData(double factor)`
   - **Coverage Status**: Covered in `HomePageHelperTests` (9 theory cases + 1 zero case).
   - **Recommendation**: Existing coverage is solid. Additional edge cases (e.g. negative factor values) can be added.
2. **`GetSearchInputData(string normalized)`** (`HomePage.xaml.cs:239`)
   - **Signature**: `public static (string? HintText, string? SearchQuery) GetSearchInputData(string normalized)`
   - **Coverage Status**: Covered in `HomePageHelperTests` (4 inline data cases).
   - **Recommendation**: Add boundary tests for whitespace-only strings and null/empty inputs.

### 1.2 Proposed New Extraction Targets in `HomePage.xaml.cs`

#### Target H1: Search Query Navigation Parameter Extraction
- **Location**: `HomePage.xaml.cs:62-67` (`OnNavigatedTo`)
- **Current Embedded Logic**:
  ```csharp
  if (e.Parameter is string query && !string.IsNullOrEmpty(query))
  {
      string searchString = query.StartsWith("category:") ? query["category:".Length..] : query;
      HomeSearchBox.Text = searchString;
      ProcessSearchInput(searchString);
  }
  ```
- **Proposed Signature**:
  ```csharp
  public static string ExtractSearchQuery(object? parameter)
  ```
- **Implementation**:
  ```csharp
  public static string ExtractSearchQuery(object? parameter)
  {
      if (parameter is string query && !string.IsNullOrWhiteSpace(query))
      {
          return query.StartsWith("category:", StringComparison.OrdinalIgnoreCase)
              ? query["category:".Length..].Trim()
              : query.Trim();
      }
      return string.Empty;
  }
  ```
- **Recommended Unit Test Cases**:
  - `ExtractSearchQuery(null)` -> `""`
  - `ExtractSearchQuery("")` -> `""`
  - `ExtractSearchQuery("git")` -> `"git"`
  - `ExtractSearchQuery("category:Developer Tools")` -> `"Developer Tools"`
  - `ExtractSearchQuery("category:")` -> `""`
  - `ExtractSearchQuery(12345)` -> `""`

#### Target H2: Search View State Visibility Determination
- **Location**: `HomePage.xaml.cs:185-201` (`ViewModel_PropertyChanged`)
- **Current Embedded Logic**:
  Calculates Visibilities for `SearchResultsPanel`, `DiscoverContentPanel`, `SearchResultsList`, `EmptyStatePanel`, and formats the Search Results title string.
- **Proposed Signature**:
  ```csharp
  public static (Visibility SearchResultsVis, Visibility DiscoverContentVis, Visibility SearchResultsListVis, Visibility EmptyStateVis, string TitleText) DetermineSearchViewState(bool isSearchActive, int itemCount, bool isLoading, string searchQuery)
  ```
- **Implementation**:
  ```csharp
  public static (Visibility SearchResultsVis, Visibility DiscoverContentVis, Visibility SearchResultsListVis, Visibility EmptyStateVis, string TitleText) DetermineSearchViewState(bool isSearchActive, int itemCount, bool isLoading, string searchQuery)
  {
      if (!isSearchActive)
      {
          return (Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, string.Empty);
      }

      bool hasItems = itemCount > 0;
      Visibility listVis = hasItems ? Visibility.Visible : Visibility.Collapsed;
      Visibility emptyVis = (!hasItems && !isLoading) ? Visibility.Visible : Visibility.Collapsed;
      string title = $"Search Results for \"{searchQuery}\"";

      return (Visibility.Visible, Visibility.Collapsed, listVis, emptyVis, title);
  }
  ```
- **Recommended Unit Test Cases**:
  - `isSearchActive = false`: `(Collapsed, Visible, Collapsed, Collapsed, "")`
  - `isSearchActive = true, itemCount = 5, isLoading = false, query = "python"`: `(Visible, Collapsed, Visible, Collapsed, "Search Results for \"python\"")`
  - `isSearchActive = true, itemCount = 0, isLoading = true, query = "vs"`: `(Visible, Collapsed, Collapsed, Collapsed, "Search Results for \"vs\"")` (Suppress empty state while loading)
  - `isSearchActive = true, itemCount = 0, isLoading = false, query = "unknownpkg"`: `(Visible, Collapsed, Collapsed, Visible, "Search Results for \"unknownpkg\"")`

#### Target H3: Grid Layout Reflow Evaluation Logic
- **Location**: `HomePage.xaml.cs:136-148` (`ApplyRecommendationGridLayout`)
- **Current Embedded Logic**:
  Evaluates delta comparisons (`>= 0.5`) on slot widths, item heights, card heights, column counts, and effective gap to determine if grid reflow is necessary.
- **Proposed Signature**:
  ```csharp
  public static bool ShouldUpdateGridLayout(bool gridRecreated, int newColumns, int lastColumns, double newSlotWidth, double lastSlotWidth, double newItemHeight, double lastItemHeight, double newCardHeight, double lastCardHeight, double newGap, double lastGap)
  ```
- **Implementation**:
  ```csharp
  public static bool ShouldUpdateGridLayout(bool gridRecreated, int newColumns, int lastColumns, double newSlotWidth, double lastSlotWidth, double newItemHeight, double lastItemHeight, double newCardHeight, double lastCardHeight, double newGap, double lastGap)
  {
      if (gridRecreated) return true;
      bool widthChanged = newColumns != lastColumns || Math.Abs(newSlotWidth - lastSlotWidth) >= 0.5;
      bool heightChanged = Math.Abs(newItemHeight - lastItemHeight) >= 0.5;
      bool cardHeightChanged = Math.Abs(newCardHeight - lastCardHeight) >= 0.5;
      bool gapChanged = Math.Abs(newGap - lastGap) >= 0.5;
      return widthChanged || heightChanged || cardHeightChanged || gapChanged;
  }
  ```
- **Recommended Unit Test Cases**:
  - `gridRecreated = true` -> `true`
  - Identical parameters (all deltas < 0.5) -> `false`
  - Column count changed (3 vs 4) -> `true`
  - Slot width delta >= 0.5 -> `true`
  - Slot width delta < 0.5 -> `false`

---

## 2. Target File 2: `InstalledPage.xaml.cs`

### 2.1 Existing Static Methods & Coverage Status
1. **`ToggleColumnSort(...)`** (`InstalledPage.xaml.cs:225`)
   - **Signature**: `public static (string NewSortBy, string NewSortDirection) ToggleColumnSort(string currentSortBy, string currentSortDirection, string targetField)`
   - **Coverage Status**: Covered in `InstalledPageStaticTests` (4 inline data cases).
2. **`GetUpdateVisibility(PackageStatus status)`** (`InstalledPage.xaml.cs:103`)
   - **Signature**: `public static Visibility GetUpdateVisibility(PackageStatus status)`
   - **Coverage Status**: UNTESTED in `WingetStore.Tests/Tests.cs`.
   - **Recommendation**: Add unit tests in `InstalledPageStaticTests`.
3. **`GetSortGlyph(...)`** (`InstalledPage.xaml.cs:251`)
   - **Signature**: `public static (string Glyph, Visibility Visibility) GetSortGlyph(string sortDirection, string sortBy, string targetField)`
   - **Coverage Status**: UNTESTED directly on `InstalledPage` (only `UpdatesPage.GetSortGlyph` was tested).
   - **Recommendation**: Add unit tests in `InstalledPageStaticTests`.

### 2.2 Proposed New Extraction Targets in `InstalledPage.xaml.cs`

#### Target I1: Installed View State Determination
- **Location**: `InstalledPage.xaml.cs:40-64` (`ViewModel_PropertyChanged`)
- **Current Embedded Logic**:
  Determines visibility of loading spinner, list view, and empty state panel based on `IsLoading` and package item count.
- **Proposed Signature**:
  ```csharp
  public static (Visibility LoadingProgressVis, Visibility AppsListVis, Visibility EmptyStateVis) GetInstalledViewState(bool isLoading, int itemCount)
  ```
- **Implementation**:
  ```csharp
  public static (Visibility LoadingProgressVis, Visibility AppsListVis, Visibility EmptyStateVis) GetInstalledViewState(bool isLoading, int itemCount)
  {
      if (isLoading)
      {
          return (Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed);
      }

      bool hasItems = itemCount > 0;
      return (
          Visibility.Collapsed,
          hasItems ? Visibility.Visible : Visibility.Collapsed,
          hasItems ? Visibility.Collapsed : Visibility.Visible
      );
  }
  ```
- **Recommended Unit Test Cases**:
  - `isLoading = true, itemCount = 0` -> `(Visible, Collapsed, Collapsed)`
  - `isLoading = true, itemCount = 10` -> `(Visible, Collapsed, Collapsed)`
  - `isLoading = false, itemCount = 5` -> `(Collapsed, Visible, Collapsed)`
  - `isLoading = false, itemCount = 0` -> `(Collapsed, Collapsed, Visible)`

#### Target I2: Eligible Bulk Uninstall Packages Filtering
- **Location**: `InstalledPage.xaml.cs:285-295` (`BulkUninstallButton_Click`)
- **Current Embedded Logic**:
  Iterates over selected items, filtering out nulls and packages where `IsInstalling == true`.
- **Proposed Signature**:
  ```csharp
  public static List<WingetPackage> GetEligibleBulkUninstallPackages(IEnumerable<WingetPackage?>? selectedPackages)
  ```
- **Implementation**:
  ```csharp
  public static List<WingetPackage> GetEligibleBulkUninstallPackages(IEnumerable<WingetPackage?>? selectedPackages)
  {
      if (selectedPackages == null) return [];
      return selectedPackages
          .Where(pkg => pkg != null && !pkg.IsInstalling)
          .Cast<WingetPackage>()
          .ToList();
  }
  ```
- **Recommended Unit Test Cases**:
  - `null` input -> empty list
  - Empty collection -> empty list
  - Collection with null items -> nulls filtered out
  - Package with `IsInstalling = true` -> filtered out
  - Package with `IsInstalling = false` -> included

#### Target I3: Import / Export Status InfoBar Formatting
- **Location**: `InstalledPage.xaml.cs:122-150` & `180-197` (`ImportButton_Click`, `ExportButton_Click`)
- **Current Embedded Logic**:
  Constructs `InfoBarSeverity`, `Title`, and `Message` strings for background task reporting.
- **Proposed Signatures**:
  ```csharp
  public static (InfoBarSeverity Severity, string Title, string Message) GetImportStatusMessage(bool isSuccess, Exception? exception)
  public static (InfoBarSeverity Severity, string Title, string Message) GetExportStatusMessage(bool isSuccess, string? filePath, Exception? exception)
  ```
- **Implementation**:
  ```csharp
  public static (InfoBarSeverity Severity, string Title, string Message) GetImportStatusMessage(bool isSuccess, Exception? exception)
  {
      if (isSuccess)
      {
          return (InfoBarSeverity.Success, "Import Completed", "Packages list imported and processed successfully.");
      }
      return (InfoBarSeverity.Error, "Import Failed", $"An error occurred during import: {exception?.Message}");
  }

  public static (InfoBarSeverity Severity, string Title, string Message) GetExportStatusMessage(bool isSuccess, string? filePath, Exception? exception)
  {
      if (isSuccess)
      {
          return (InfoBarSeverity.Success, "Export Complete", $"Successfully exported your installed packages list to: {filePath}");
      }
      return (InfoBarSeverity.Error, "Export Failed", $"An error occurred during export: {exception?.Message}");
  }
  ```
- **Recommended Unit Test Cases**:
  - `GetImportStatusMessage(true, null)` -> `Success`, `"Import Completed"`, `"Packages list imported..."`
  - `GetImportStatusMessage(false, new Exception("File corrupt"))` -> `Error`, `"Import Failed"`, `"An error occurred during import: File corrupt"`
  - `GetExportStatusMessage(true, "C:\\export.json", null)` -> `Success`, `"Export Complete"`, `"Successfully exported your installed packages list to: C:\\export.json"`
  - `GetExportStatusMessage(false, null, new Exception("Permission denied"))` -> `Error`, `"Export Failed"`, `"An error occurred during export: Permission denied"`

---

## 3. Implementation & Test Plan Summary Table

| Page File | Target Method | Status | Proposed Method Signature | Estimated New Tests |
|---|---|---|---|---|
| `HomePage.xaml.cs` | `ExtractSearchQuery` | New Extraction | `public static string ExtractSearchQuery(object? parameter)` | 6 tests |
| `HomePage.xaml.cs` | `DetermineSearchViewState` | New Extraction | `public static (Visibility, Visibility, Visibility, Visibility, string) DetermineSearchViewState(bool, int, bool, string)` | 4 tests |
| `HomePage.xaml.cs` | `ShouldUpdateGridLayout` | New Extraction | `public static bool ShouldUpdateGridLayout(bool, int, int, double, double, double, double, double, double, double, double)` | 5 tests |
| `InstalledPage.xaml.cs` | `GetUpdateVisibility` | Existing Untested | `public static Visibility GetUpdateVisibility(PackageStatus status)` | 4 tests |
| `InstalledPage.xaml.cs` | `GetSortGlyph` | Existing Untested | `public static (string, Visibility) GetSortGlyph(string, string, string)` | 4 tests |
| `InstalledPage.xaml.cs` | `GetInstalledViewState` | New Extraction | `public static (Visibility, Visibility, Visibility) GetInstalledViewState(bool, int)` | 4 tests |
| `InstalledPage.xaml.cs` | `GetEligibleBulkUninstallPackages` | New Extraction | `public static List<WingetPackage> GetEligibleBulkUninstallPackages(IEnumerable<WingetPackage?>?)` | 4 tests |
| `InstalledPage.xaml.cs` | `GetImportStatusMessage` / `GetExportStatusMessage` | New Extraction | `public static (InfoBarSeverity, string, string) GetImportStatusMessage(bool, Exception?)` / `GetExportStatusMessage(bool, string?, Exception?)` | 6 tests |

**Total New Unit Tests Planned**: ~37 new test cases across `HomePage` and `InstalledPage`.
