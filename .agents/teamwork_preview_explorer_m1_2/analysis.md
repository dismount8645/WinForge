# InstalledViewModel & UpdatesViewModel Non-UI Logic Extraction Analysis

## Executive Summary
This report analyzes `WingetStore/ViewModels/InstalledViewModel.cs` and `WingetStore/ViewModels/UpdatesViewModel.cs` to identify untested or testable non-UI business logic suitable for extraction into pure `public static` or `internal static` methods. 
By extracting this logic into static helper methods within the ViewModel files:
1. Logic can be thoroughly tested in `WingetStore.Tests` without instantiation of UI controls, `DispatcherQueue`, or MVVM messaging singletons.
2. XAML bindings and existing VM property contracts remain 100% intact.
3. Code coverage for ViewModels increases directly as extracted static methods are invoked by both unit tests and ViewModel methods.

---

## 1. Existing Baseline & Coverage Analysis

### InstalledViewModel.cs (111 lines)
- **Currently Extracted Static Method**:
  - `public static List<string> ExtractDevelopersList(IEnumerable<WingetPackage>? packages)` (Lines 72-82)
  - Existing tests in `InstalledViewModelStaticTests` (Lines 3537-3562) cover basic `ExtractDevelopersList` with null, empty, and duplicate publishers.
- **Untested / Instance-Bound Logic**:
  - **Developer filter validation and reset**: Lines 87-88 (`PopulateDevelopersList`) checks if `DeveloperFilter` is valid; resets to `FilterDefaults.AllDevelopers` if null, whitespace, or invalid.
  - **Developer filter matching predicate**: Line 92 inline predicate in `ApplyFilter()`. Handles null/empty publisher, case-insensitive comparison, and "All Publishers" default.
  - **Package status change handling**: Lines 28-47 in messenger handler. Removal of uninstalled packages (`PackageStatus.Installable`) and updating target package properties (`PackageStatus.Installed`).
  - **Upgradable badge count calculation**: Line 46 `Count(p => p.Status == PackageStatus.Upgradable)`.

### UpdatesViewModel.cs (112 lines)
- **Currently Extracted Static Method**:
  - `public static (bool IsVisible, double ProgressValue, string PercentText, string StatusText) CalculateGlobalProgress(IEnumerable<WingetPackage>? packages)` (Lines 89-97)
  - Existing tests in `UpdatesViewModelStaticTests` (Lines 3479-3535) cover basic null/empty, no active upgrades, single active upgrade, and multiple active upgrades.
- **Untested / Instance-Bound Logic**:
  - **Installed package removal on update completion**: Lines 31-38 messenger handler removes upgraded package from `_allUpgrades` and `Upgrades` collection by matching ID.
  - **Upgrade All package selection**: Line 88 (`UpgradeAll`) iterates packages and checks `!package.IsInstalling`.
  - **Category filter matching predicate**: Lines 78-82 (`CategoryFilter switch` between "Apps", "Redist", and default).
  - **Pipeline for upgrade filtering and counting**: Lines 72-86 in `ApplyFilter()`.

---

## 2. Concrete Proposals for Extraction

### Proposal 1: `InstalledViewModel.NormalizeDeveloperFilter`
- **Original Location**: `InstalledViewModel.cs` Lines 87-88
- **Proposed Signature**:
  ```csharp
  public static string NormalizeDeveloperFilter(string? currentFilter, IEnumerable<string>? availableOptions)
  ```
- **Input / Output Contract**:
  - `currentFilter`: Current filter string selected by user (can be null/empty/whitespace).
  - `availableOptions`: Sequence of valid developer options available in the drop-down.
  - Returns `currentFilter` if present in `availableOptions`; otherwise returns `FilterDefaults.AllDevelopers` ("All Publishers").
- **Refactoring in `InstalledViewModel.cs`**:
  ```csharp
  private void PopulateDevelopersList()
  {
      DevelopersList = ExtractDevelopersList(_allPackages);
      DeveloperFilter = NormalizeDeveloperFilter(DeveloperFilter, DeveloperOptions);
  }
  ```

### Proposal 2: `InstalledViewModel.MatchesDeveloperFilter`
- **Original Location**: `InstalledViewModel.cs` Line 92
- **Proposed Signature**:
  ```csharp
  public static bool MatchesDeveloperFilter(string? packagePublisher, string? developerFilter)
  ```
- **Input / Output Contract**:
  - `packagePublisher`: Publisher of the package (can be null, empty, or whitespace).
  - `developerFilter`: Selected developer filter (can be null, empty, whitespace, or `FilterDefaults.AllDevelopers`).
  - Returns `true` if `developerFilter` is null/empty/whitespace or equals `FilterDefaults.AllDevelopers` (case-insensitive).
  - Returns `false` if `packagePublisher` is null/empty.
  - Returns `packagePublisher.Equals(developerFilter, StringComparison.OrdinalIgnoreCase)`.
- **Refactoring in `InstalledViewModel.cs`**:
  ```csharp
  public override void ApplyFilter()
  {
      var baseList = _allPackages.FindAll(p => p.MatchesQuery(FilterQuery) 
          && MatchesDeveloperFilter(p.Publisher, DeveloperFilter) 
          && MatchesSourceFilter(p.Source, SourceFilter));
      ...
  }
  ```

### Proposal 3: `InstalledViewModel.HandlePackageStatusChange` & `CountUpgradablePackages`
- **Original Location**: `InstalledViewModel.cs` Lines 28-47
- **Proposed Signatures**:
  ```csharp
  public static bool HandlePackageStatusChange(List<WingetPackage> packages, WingetPackage statusPackage)
  public static int CountUpgradablePackages(IEnumerable<WingetPackage>? packages)
  ```
- **Input / Output Contract**:
  - `HandlePackageStatusChange`:
    - If `statusPackage.Status == PackageStatus.Installable`: removes matching package by `Id` (case-insensitive) from `packages`. Returns `true` if any removed.
    - If `statusPackage.Status == PackageStatus.Installed`: finds matching package by `Id` (case-insensitive) in `packages`. Updates `target.Status = PackageStatus.Installed`, copies `AvailableVersion` to `Version` if non-empty, and clears `AvailableVersion`. Returns `true` if target found.
  - `CountUpgradablePackages`:
    - Returns count of non-null packages where `Status == PackageStatus.Upgradable`. Returns `0` if null.
- **Refactoring in `InstalledViewModel.cs`**:
  ```csharp
  WeakReferenceMessenger.Default.Register<PackageStatusChangedMessage>(this, (r, m) =>
  {
      var package = m.Value; if (package == null || string.IsNullOrEmpty(package.Id)) return;
      App.Dispatch(() =>
      {
          bool updated = HandlePackageStatusChange(_allPackages, package);
          if (updated)
          {
              ApplyFilter();
              if (App.MainWindow is MainWindow mainWindow) 
                  mainWindow.UpdateUpdatesBadge(CountUpgradablePackages(_allPackages));
          }
      });
  });
  ```

### Proposal 4: `UpdatesViewModel.HandlePackageInstalled`
- **Original Location**: `UpdatesViewModel.cs` Lines 31-38
- **Proposed Signature**:
  ```csharp
  public static bool HandlePackageInstalled(List<WingetPackage> allUpgrades, ObservableCollection<WingetPackage> upgradesCollection, WingetPackage installedPackage)
  ```
- **Input / Output Contract**:
  - Removes matching package by `Id` (case-insensitive) from both `allUpgrades` list and `upgradesCollection`.
  - Returns `true` if package was found and removed from either collection; otherwise `false`.
- **Refactoring in `UpdatesViewModel.cs`**:
  ```csharp
  if (package.Status == PackageStatus.Installed)
  {
      bool removed = HandlePackageInstalled(_allUpgrades, Upgrades, package);
      if (removed)
      {
          ApplyFilter();
          if (App.MainWindow is MainWindow mainWindow) mainWindow.UpdateUpdatesBadge(Upgrades.Count);
      }
  }
  ```

### Proposal 5: `UpdatesViewModel.GetEligiblePackagesForUpgrade`
- **Original Location**: `UpdatesViewModel.cs` Line 88
- **Proposed Signature**:
  ```csharp
  public static List<WingetPackage> GetEligiblePackagesForUpgrade(IEnumerable<WingetPackage>? packages)
  ```
- **Input / Output Contract**:
  - Filters `packages` returning non-null packages where `!package.IsInstalling`.
  - Returns empty list `[]` if input is null or no packages match.
- **Refactoring in `UpdatesViewModel.cs`**:
  ```csharp
  [RelayCommand]
  public void UpgradeAll()
  {
      LogService.LogInfo("Upgrading all available packages...");
      var itemsToUpgrade = GetEligiblePackagesForUpgrade(Upgrades);
      foreach (var package in itemsToUpgrade)
      {
          _winget.UpgradePackage(package);
      }
      UpdateGlobalProgress();
  }
  ```

### Proposal 6: `FilterableViewModel.MatchesCategoryFilter` (Shared Helper)
- **Original Location**: `InstalledViewModel.cs` Lines 97-101 and `UpdatesViewModel.cs` Lines 78-82
- **Proposed Signature**:
  ```csharp
  public static bool MatchesCategoryFilter(bool isRedistributable, string? categoryFilter)
  ```
- **Input / Output Contract**:
  - `categoryFilter == "Apps"` => returns `!isRedistributable`.
  - `categoryFilter == "Redist"` => returns `isRedistributable`.
  - Any other value (e.g. `"All"`, null, "") => returns `true`.

---

## 3. xUnit Test Case Specifications

To achieve maximum test coverage on extracted static methods, the following test classes should be added or expanded in `WingetStore.Tests/Tests.cs`:

### Test Suite 1: `InstalledViewModelStaticTests`
1. `NormalizeDeveloperFilter_NullOrEmpty_ReturnsAllDevelopers`
   - Test inputs: `(null, ["Dev A"])`, `("", ["Dev A"])`, `("  ", ["Dev A"])`
   - Expected: `"All Publishers"`
2. `NormalizeDeveloperFilter_InvalidOption_ReturnsAllDevelopers`
   - Test inputs: `("Dev X", ["Dev A", "Dev B"])`
   - Expected: `"All Publishers"`
3. `NormalizeDeveloperFilter_ValidOption_ReturnsCurrentFilter`
   - Test inputs: `("Dev A", ["All Publishers", "Dev A", "Dev B"])`
   - Expected: `"Dev A"`
4. `MatchesDeveloperFilter_AllDevelopersOrNull_ReturnsTrue`
   - Test inputs: `("Microsoft", "All Publishers")`, `("Microsoft", null)`, `("Microsoft", "")`
   - Expected: `true`
5. `MatchesDeveloperFilter_NullPackagePublisher_ReturnsFalse`
   - Test inputs: `(null, "Microsoft")`, `("", "Microsoft")`
   - Expected: `false`
6. `MatchesDeveloperFilter_CaseInsensitiveMatch_ReturnsTrue`
   - Test inputs: `("Microsoft Corporation", "microsoft corporation")`
   - Expected: `true`
7. `MatchesDeveloperFilter_Mismatch_ReturnsFalse`
   - Test inputs: `("Microsoft", "Adobe")`
   - Expected: `false`
8. `HandlePackageStatusChange_Installable_RemovesPackageCaseInsensitively`
   - Setup: List containing `[ { Id: "App.Git" }, { Id: "App.VSCode" } ]`
   - Action: `HandlePackageStatusChange(list, new WingetPackage { Id = "app.git", Status = PackageStatus.Installable })`
   - Asserts: Returns `true`, list count is 1, remaining item is `"App.VSCode"`.
9. `HandlePackageStatusChange_Installed_UpdatesVersionAndStatus`
   - Setup: List containing `[ { Id: "App.Git", Status = PackageStatus.Upgradable, Version = "1.0", AvailableVersion = "2.0" } ]`
   - Action: `HandlePackageStatusChange(list, new WingetPackage { Id = "App.Git", Status = PackageStatus.Installed, AvailableVersion = "2.0" })`
   - Asserts: Target package `Status == PackageStatus.Installed`, `Version == "2.0"`, `AvailableVersion == ""`.
10. `CountUpgradablePackages_NullOrEmpty_ReturnsZero`
    - Test inputs: `null`, `[]`, `[ { Status: Installed }, { Status: Installable } ]`
    - Expected: `0`
11. `CountUpgradablePackages_ValidPackages_CountsUpgradableOnly`
    - Test input: `[ { Status: Upgradable }, { Status: Upgradable }, { Status: Installed }, null ]`
    - Expected: `2`

### Test Suite 2: `UpdatesViewModelStaticTests`
1. `HandlePackageInstalled_RemovesFromBothCollections`
   - Setup: `allUpgrades = [ { Id: "Upg.App1" }, { Id: "Upg.App2" } ]`, `upgradesObs = [ { Id: "Upg.App1" }, { Id: "Upg.App2" } ]`
   - Action: `HandlePackageInstalled(allUpgrades, upgradesObs, new WingetPackage { Id = "upg.app1", Status = PackageStatus.Installed })`
   - Asserts: Returns `true`, both collections have 1 item left (`"Upg.App2"`).
2. `HandlePackageInstalled_NullOrNotFound_ReturnsFalse`
   - Action: Test with null package, empty ID, or package ID not in list.
   - Asserts: Returns `false`, collections remain unchanged.
3. `GetEligiblePackagesForUpgrade_NullOrEmpty_ReturnsEmpty`
   - Test inputs: `null`, `[]`
   - Expected: Empty list `[]`
4. `GetEligiblePackagesForUpgrade_FiltersOutInstallingPackages`
   - Setup: `[ { Id: "p1", IsInstalling = false }, { Id: "p2", IsInstalling = true }, { Id: "p3", IsInstalling = false } ]`
   - Expected: List containing `"p1"` and `"p3"` only (count 2).
