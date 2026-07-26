# Logic Extraction & Test Analysis — Milestone 3 (Explorer 2)

## Executive Summary
This report analyzes `WingetStore/Pages/UpdatesPage.xaml.cs` and `WingetStore/Pages/DetailsPage.xaml.cs` for logic extraction targets to expand automated unit test coverage without relying on WinUI XAML host instantiation.

Both pages already contain a few `public static` methods (`GetUpdatesViewState`, `GetSortGlyph` in `UpdatesPage`; `GetActionButtonData`, `GetProgressData`, `GetViewLogsVisibility` in `DetailsPage`) with test coverage in `WingetStore.Tests/Tests.cs`. However, several non-UI calculations, data formatting logic, task matching, and filter decisions remain inline within instance UI methods.

We present **7 new extraction targets** across `UpdatesPage.xaml.cs` (2 targets) and `DetailsPage.xaml.cs` (5 targets) complete with proposed method signatures, code replacements, and 23 proposed unit test cases.

---

## Existing Test Coverage Audit (`WingetStore.Tests/Tests.cs`)

Before proposing new extractions, existing tests were audited to avoid duplicate coverage:
- **`UpdatesPageViewStateTests` & `UpdatesPageStaticTests`**: Tests `UpdatesPage.GetUpdatesViewState(int count)` for count = 0, small sets (1..3), and large sets (4+).
- **`PageSortGlyphTests` & `UpdatesPageStaticTests`**: Tests `UpdatesPage.GetSortGlyph(string direction, string sortBy, string targetField)` for ascending/descending and active vs inactive columns.
- **`DetailsPageHelperTests` & `DetailsPageStaticTests`**: Tests `DetailsPage.GetActionButtonData(WingetPackage pkg)`, `DetailsPage.GetProgressData(WingetPackage pkg)`, and `DetailsPage.GetViewLogsVisibility(WingetPackage? pkg, ObservableCollection<InstallTask> activeTasks)`.

None of the proposed extraction targets below are currently covered or extracted.

---

## 1. `UpdatesPage.xaml.cs` Extraction Targets

### Target U1: `CanUpdateAll`
- **File**: `WingetStore/Pages/UpdatesPage.xaml.cs`
- **Location**: Line 117 inside `UpdateViewForResultCount()`
- **Current Inline Code**:
  ```csharp
  UpdateAllButton.IsEnabled = hasItems && ViewModel.FilteredUpgrades.Any(p => !p.IsInstalling);
  ```
- **Rationale**:
  Determines whether the "Update All" button should be enabled based on whether updates exist and whether at least one package in the list is not currently installing. Extracting this pure boolean evaluation logic enables direct unit testing for edge cases (empty lists, all packages currently upgrading, partial upgrades, null collections) without needing ViewModel or UI control references.
- **Proposed Signature**:
  ```csharp
  public static bool CanUpdateAll(bool hasItems, IEnumerable<WingetPackage>? packages)
  ```
- **Proposed Implementation**:
  ```csharp
  public static bool CanUpdateAll(bool hasItems, IEnumerable<WingetPackage>? packages)
  {
      if (!hasItems || packages == null) return false;
      return packages.Any(p => p != null && !p.IsInstalling);
  }
  ```
- **Code-behind Replacement** in `UpdateViewForResultCount()`:
  ```csharp
  UpdateAllButton.IsEnabled = CanUpdateAll(hasItems, ViewModel.FilteredUpgrades);
  ```
- **Recommended Unit Test Cases (`UpdatesPageStaticTests`)**:
  1. `CanUpdateAll_NoItems_ReturnsFalse`: `hasItems = false`, `packages = [pkg1]`, expects `false`.
  2. `CanUpdateAll_NullPackages_ReturnsFalse`: `hasItems = true`, `packages = null`, expects `false`.
  3. `CanUpdateAll_EmptyPackages_ReturnsFalse`: `hasItems = true`, `packages = []`, expects `false`.
  4. `CanUpdateAll_AllInstalling_ReturnsFalse`: `hasItems = true`, `packages = [IsInstalling=true, IsInstalling=true]`, expects `false`.
  5. `CanUpdateAll_HasNonInstalling_ReturnsTrue`: `hasItems = true`, `packages = [IsInstalling=true, IsInstalling=false]`, expects `true`.
  6. `CanUpdateAll_NullElementInPackages_HandledGracefully`: `hasItems = true`, `packages = [null, IsInstalling=false]`, expects `true`.

---

### Target U2: `FilterPackagesForBulkUpdate`
- **File**: `WingetStore/Pages/UpdatesPage.xaml.cs`
- **Location**: Lines 193-198 inside `BulkUpdateButton_Click()`
- **Current Inline Code**:
  ```csharp
  var selected = UpdatesList.SelectedItems.Cast<WingetPackage>().ToList();
  if (selected.Count == 0) return;
  foreach (var package in selected)
  {
      if (package != null && !package.IsInstalling) ViewModel.UpgradeCommand.Execute(package);
  }
  ```
- **Rationale**:
  Extracts package selection filtering logic for bulk update actions. When users trigger bulk update on a list of selected items, packages that are null or already installing must be filtered out. Extracting this helper method makes the decision logic testable independently of UI selection controls (`ListView.SelectedItems`).
- **Proposed Signature**:
  ```csharp
  public static List<WingetPackage> FilterPackagesForBulkUpdate(IEnumerable<WingetPackage>? selectedPackages)
  ```
- **Proposed Implementation**:
  ```csharp
  public static List<WingetPackage> FilterPackagesForBulkUpdate(IEnumerable<WingetPackage>? selectedPackages)
  {
      if (selectedPackages == null) return new List<WingetPackage>();
      return selectedPackages.Where(p => p != null && !p.IsInstalling).ToList();
  }
  ```
- **Code-behind Replacement** in `BulkUpdateButton_Click()`:
  ```csharp
  var upgradable = FilterPackagesForBulkUpdate(UpdatesList.SelectedItems.OfType<WingetPackage>());
  if (upgradable.Count == 0) return;
  foreach (var package in upgradable)
  {
      ViewModel.UpgradeCommand.Execute(package);
  }
  _bulkSelect?.Deactivate();
  ```
- **Recommended Unit Test Cases (`UpdatesPageStaticTests`)**:
  1. `FilterPackagesForBulkUpdate_NullInput_ReturnsEmptyList`: `selectedPackages = null`, expects empty list.
  2. `FilterPackagesForBulkUpdate_EmptyList_ReturnsEmptyList`: `selectedPackages = []`, expects empty list.
  3. `FilterPackagesForBulkUpdate_FiltersOutInstallingAndNull`: `selectedPackages = [pkg1 (IsInstalling=true), null, pkg2 (IsInstalling=false)]`, expects `[pkg2]`.
  4. `FilterPackagesForBulkUpdate_AllValid_ReturnsAll`: `selectedPackages = [pkg1, pkg2]` (both `IsInstalling=false`), expects 2 packages.

---

## 2. `DetailsPage.xaml.cs` Extraction Targets

### Target D1: `FormatPublisher`
- **File**: `WingetStore/Pages/DetailsPage.xaml.cs`
- **Location**: Line 64 inside `LoadDetailsAsync()`
- **Current Inline Code**:
  ```csharp
  PublisherText.Text = string.IsNullOrEmpty(_package.Publisher) ? "Unknown Publisher" : _package.Publisher;
  ```
- **Rationale**:
  Formats publisher display string for package detail header. When a package has no publisher string or an empty string, fallback text `"Unknown Publisher"` is presented. Extracting this static method allows testing publisher formatting for edge cases (null, empty, whitespace, valid publisher strings) without UI element references.
- **Proposed Signature**:
  ```csharp
  public static string FormatPublisher(string? publisher)
  ```
- **Proposed Implementation**:
  ```csharp
  public static string FormatPublisher(string? publisher)
  {
      return string.IsNullOrWhiteSpace(publisher) ? "Unknown Publisher" : publisher;
  }
  ```
- **Code-behind Replacement** in `LoadDetailsAsync()`:
  ```csharp
  PublisherText.Text = FormatPublisher(_package.Publisher);
  ```
- **Recommended Unit Test Cases (`DetailsPageStaticTests`)**:
  1. `FormatPublisher_NullOrEmpty_ReturnsUnknownPublisher`: `null`, `""`, `"   "` return `"Unknown Publisher"`.
  2. `FormatPublisher_ValidPublisher_ReturnsPublisher`: `"Microsoft Corporation"` returns `"Microsoft Corporation"`.

---

### Target D2: `FormatVersionText`
- **File**: `WingetStore/Pages/DetailsPage.xaml.cs`
- **Location**: Line 65 inside `LoadDetailsAsync()`
- **Current Inline Code**:
  ```csharp
  VersionText.Text = $"Version: {_package.Version}" + (string.IsNullOrEmpty(_package.AvailableVersion) ? "" : $" (Latest: {_package.AvailableVersion})");
  ```
- **Rationale**:
  Formats version header string, concatenating current version and optionally available update version. Extracting this into pure logic allows testing all combinations of installed version and available version strings.
- **Proposed Signature**:
  ```csharp
  public static string FormatVersionText(string? version, string? availableVersion)
  ```
- **Proposed Implementation**:
  ```csharp
  public static string FormatVersionText(string? version, string? availableVersion)
  {
      string baseVersion = string.IsNullOrEmpty(version) ? "Unknown" : version;
      string latestSuffix = string.IsNullOrEmpty(availableVersion) ? "" : $" (Latest: {availableVersion})";
      return $"Version: {baseVersion}{latestSuffix}";
  }
  ```
- **Code-behind Replacement** in `LoadDetailsAsync()`:
  ```csharp
  VersionText.Text = FormatVersionText(_package.Version, _package.AvailableVersion);
  ```
- **Recommended Unit Test Cases (`DetailsPageStaticTests`)**:
  1. `FormatVersionText_VersionOnly_ReturnsVersionString`: `version = "1.0.0"`, `availableVersion = null`, expects `"Version: 1.0.0"`.
  2. `FormatVersionText_VersionAndAvailableVersion_ReturnsBoth`: `version = "1.0.0"`, `availableVersion = "1.2.0"`, expects `"Version: 1.0.0 (Latest: 1.2.0)"`.
  3. `FormatVersionText_NullVersionWithAvailableVersion_HandlesNull`: `version = null`, `availableVersion = "2.0.0"`, expects `"Version: Unknown (Latest: 2.0.0)"`.
  4. `FormatVersionText_EmptyAvailableVersion_IgnoresAvailable`: `version = "2.40.0"`, `availableVersion = ""`, expects `"Version: 2.40.0"`.

---

### Target D3: `FormatDescription`
- **File**: `WingetStore/Pages/DetailsPage.xaml.cs`
- **Location**: Line 66 inside `LoadDetailsAsync()`
- **Current Inline Code**:
  ```csharp
  DescriptionText.Text = string.IsNullOrEmpty(_package.Description) ? "No description available for this package." : _package.Description;
  ```
- **Rationale**:
  Formats description body text, providing standard fallback when description is missing.
- **Proposed Signature**:
  ```csharp
  public static string FormatDescription(string? description)
  ```
- **Proposed Implementation**:
  ```csharp
  public static string FormatDescription(string? description)
  {
      return string.IsNullOrWhiteSpace(description) ? "No description available for this package." : description;
  }
  ```
- **Code-behind Replacement** in `LoadDetailsAsync()`:
  ```csharp
  DescriptionText.Text = FormatDescription(_package.Description);
  ```
- **Recommended Unit Test Cases (`DetailsPageStaticTests`)**:
  1. `FormatDescription_NullOrEmpty_ReturnsFallbackText`: `null` and `""` return `"No description available for this package."`.
  2. `FormatDescription_ValidDescription_ReturnsDescription`: `"Git is a free tool"` returns `"Git is a free tool"`.

---

### Target D4: `FindActiveTaskForPackage`
- **File**: `WingetStore/Pages/DetailsPage.xaml.cs`
- **Location**: Lines 135-148 inside `SyncWithRunningTasks()`
- **Current Inline Code**:
  ```csharp
  private void SyncWithRunningTasks()
  {
      if (_package == null) return;
      foreach (var task in App.Winget.ActiveTasks)
      {
          if (task.PackageId.Equals(_package.Id, StringComparison.OrdinalIgnoreCase) && (task.Status == InstallTaskStatus.Running || task.Status == InstallTaskStatus.Queued))
          {
              _package.IsInstalling = true;
              _package.InstallProgress = task.Progress;
              _package.InstallStatusText = task.StatusText;
              break;
          }
      }
  }
  ```
- **Rationale**:
  Searches an active task collection for a task matching a specific package ID that is currently running or queued. Extracting this search logic into a pure static method enables testing for task matching (case insensitivity, status filtering, missing tasks, null parameters) without needing the singleton `App.Winget` instance or UI binding updates.
- **Proposed Signature**:
  ```csharp
  public static InstallTask? FindActiveTaskForPackage(string? packageId, IEnumerable<InstallTask>? activeTasks)
  ```
- **Proposed Implementation**:
  ```csharp
  public static InstallTask? FindActiveTaskForPackage(string? packageId, IEnumerable<InstallTask>? activeTasks)
  {
      if (string.IsNullOrEmpty(packageId) || activeTasks == null) return null;
      return activeTasks.FirstOrDefault(task =>
          task.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
          (task.Status == InstallTaskStatus.Running || task.Status == InstallTaskStatus.Queued));
  }
  ```
- **Code-behind Replacement** in `SyncWithRunningTasks()`:
  ```csharp
  private void SyncWithRunningTasks()
  {
      if (_package == null) return;
      var matchingTask = FindActiveTaskForPackage(_package.Id, App.Winget.ActiveTasks);
      if (matchingTask != null)
      {
          _package.IsInstalling = true;
          _package.InstallProgress = matchingTask.Progress;
          _package.InstallStatusText = matchingTask.StatusText;
      }
  }
  ```
- **Recommended Unit Test Cases (`DetailsPageStaticTests`)**:
  1. `FindActiveTaskForPackage_NullOrEmptyPackageId_ReturnsNull`: `null` and `""` package ID.
  2. `FindActiveTaskForPackage_NullTasksCollection_ReturnsNull`: `activeTasks = null`.
  3. `FindActiveTaskForPackage_MatchingRunningTask_ReturnsTask`: case-insensitive ID match with status `Running`.
  4. `FindActiveTaskForPackage_MatchingQueuedTask_ReturnsTask`: case-insensitive ID match with status `Queued`.
  5. `FindActiveTaskForPackage_MatchingCompletedTask_ReturnsNull`: matching ID with status `Completed` (returns `null` because not active).
  6. `FindActiveTaskForPackage_NoMatchingTask_ReturnsNull`: collection with non-matching package IDs.

---

### Target D5: `GetTextSectionVisibility` & `GetCollectionVisibility`
- **File**: `WingetStore/Pages/DetailsPage.xaml.cs`
- **Location**: Lines 93-112 inside `LoadDetailsAsync()`
- **Current Inline Code**:
  ```csharp
  if (!string.IsNullOrEmpty(_package.ReleaseNotes)) { ReleaseNotesPanel.Visibility = Visibility.Visible; ... } else ReleaseNotesPanel.Visibility = Visibility.Collapsed;
  if (_package.Tags.Count > 0) { ... TagsPanel.Visibility = Visibility.Visible; } else TagsPanel.Visibility = Visibility.Collapsed;
  if (_package.Screenshots.Count > 0) { ... ScreenshotsPanel.Visibility = Visibility.Visible; } else ScreenshotsPanel.Visibility = Visibility.Collapsed;
  ```
- **Rationale**:
  Determines visibility state (`Visible` vs `Collapsed`) for UI sections (release notes, tags, screenshots) based on whether content or items exist.
- **Proposed Signatures**:
  ```csharp
  public static Visibility GetTextSectionVisibility(string? text)
  public static Visibility GetCollectionVisibility<T>(IReadOnlyCollection<T>? collection)
  ```
- **Proposed Implementation**:
  ```csharp
  public static Visibility GetTextSectionVisibility(string? text) =>
      !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

  public static Visibility GetCollectionVisibility<T>(IReadOnlyCollection<T>? collection) =>
      collection != null && collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
  ```
- **Code-behind Replacement** in `LoadDetailsAsync()`:
  ```csharp
  ReleaseNotesPanel.Visibility = GetTextSectionVisibility(_package.ReleaseNotes);
  if (_package.ReleaseNotes != null) ReleaseNotesText.Text = _package.ReleaseNotes;

  TagsPanel.Visibility = GetCollectionVisibility(_package.Tags);
  if (_package.Tags.Count > 0) TagsList.ItemsSource = _package.Tags;

  ScreenshotsPanel.Visibility = GetCollectionVisibility(_package.Screenshots);
  if (_package.Screenshots.Count > 0) ScreenshotsList.ItemsSource = _package.Screenshots;
  ```
- **Recommended Unit Test Cases (`DetailsPageStaticTests`)**:
  1. `GetTextSectionVisibility_NullOrEmpty_ReturnsCollapsed`: `null` and `""` return `Visibility.Collapsed`.
  2. `GetTextSectionVisibility_NonEmpty_ReturnsVisible`: `"Notes"` returns `Visibility.Visible`.
  3. `GetCollectionVisibility_NullOrEmpty_ReturnsCollapsed`: `null` and empty collection return `Visibility.Collapsed`.
  4. `GetCollectionVisibility_HasElements_ReturnsVisible`: non-empty collection returns `Visibility.Visible`.

---

## Summary Table of Proposed Extractions

| ID | File | Method Signature | Extracted Pure Logic | Target Test Class | New Tests |
|---|---|---|---|---|---|
| **U1** | `UpdatesPage.xaml.cs` | `public static bool CanUpdateAll(bool hasItems, IEnumerable<WingetPackage>? packages)` | Update All enable condition | `UpdatesPageStaticTests` | 6 |
| **U2** | `UpdatesPage.xaml.cs` | `public static List<WingetPackage> FilterPackagesForBulkUpdate(IEnumerable<WingetPackage>? selectedPackages)` | Bulk update selection filter | `UpdatesPageStaticTests` | 4 |
| **D1** | `DetailsPage.xaml.cs` | `public static string FormatPublisher(string? publisher)` | Publisher fallback formatting | `DetailsPageStaticTests` | 2 |
| **D2** | `DetailsPage.xaml.cs` | `public static string FormatVersionText(string? version, string? availableVersion)` | Version string formatting | `DetailsPageStaticTests` | 4 |
| **D3** | `DetailsPage.xaml.cs` | `public static string FormatDescription(string? description)` | Description fallback formatting | `DetailsPageStaticTests` | 2 |
| **D4** | `DetailsPage.xaml.cs` | `public static InstallTask? FindActiveTaskForPackage(string? packageId, IEnumerable<InstallTask>? activeTasks)` | Active task matching | `DetailsPageStaticTests` | 6 |
| **D5** | `DetailsPage.xaml.cs` | `public static Visibility GetTextSectionVisibility(string? text)` & `GetCollectionVisibility<T>(...)` | Section visibility determination | `DetailsPageStaticTests` | 4 |
| **Total** | | **7 Methods** | | | **28 Tests** |

---

## Verification Method

To verify these proposed extractions after implementation:

1. **Build Verification**:
   ```pwsh
   dotnet build WingetStore.sln
   ```
   Ensure 0 build errors.

2. **Test Execution**:
   ```pwsh
   dotnet test --filter "FullyQualifiedName!~WinUIPageCreationTests"
   ```
   All existing tests plus the 28 new tests should pass.
