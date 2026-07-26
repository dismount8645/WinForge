# Analysis Report: Services & Helpers Non-UI Logic Extraction (Explorer M2-3)

## Executive Summary
This investigation analyzed all helper classes in `WingetStore/Services/Helpers.cs` and remaining services in `WingetStore/Services/` (`WingetService.cs`, `WingetParser.cs`, `IconService.cs`, `LogService.cs`, `SettingsService.cs`, `CliProcessRunner.cs`, `CachingWingetService.cs`).

We evaluated existing test coverage in `WingetStore.Tests/Tests.cs` (~4,127 lines) and identified 9 concrete refactoring and test-expansion proposals that extract pure non-UI logic (CLI argument formatting, recommendation list construction, package details decoration, package action determination, homepage domain extraction, JSON screenshot database parsing, log entry formatting, row dictionary mapping, and version comparison boundary conditions).

Together, these proposals enable **52 new xUnit unit tests** running cleanly via `dotnet test` without requiring WinUI Desktop app host or UI thread invocation.

---

## Codebase Audit & Baseline Findings

### Target Files Examined
1. `WingetStore/Services/Helpers.cs` (202 lines)
   - `NavigationHelper`: `CanGoBack`, `GetPageType`
   - `PackageFilteringHelper`: `MatchesQuery`, `FilterAndSortPackages`, `MatchesSourceFilter`, `SortPackages`
   - `GridCalculator`: `CalculateGridDimensions`
   - `VersionComparer`: `Compare`
   - `BulkSelectionHelper`: `Toggle`, `SelectAll`, `DeselectAll`, `ComputeSelectAllState`
   - `PackageDetailHelper`: `ShouldSkipMetadataItem`, `PopulateMetadata`
2. `WingetStore/Services/WingetService.cs` (188 lines)
   - `EscapeArgument`, `IsWingetAvailable`, `ResolveWingetPath`, `MapFromRow`
   - `SearchPackagesAsync`, `GetInstalledPackagesAsync`, `GetUpgradablePackagesAsync`, `GetPopularPackagesAsync`, `GetRecommendationsAsync`, `FetchAndDecoratePackageDetailsAsync`, `TriggerPackageAction`, `InstallPackage`, `UpgradePackage`, `UninstallPackage`, `ExportPackagesAsync`, `ImportPackagesAsync`
3. `WingetStore/Services/WingetParser.cs` (104 lines)
   - `ParseTable`, `ParseDetailsList`, `ParsePackageDetails`, `ParseProgressFromOutput`, `ParseStatusTextFromOutput`, `ParseTagsFromShowOutput`, `GetSubstring`
4. `WingetStore/Services/IconService.cs` (180 lines)
   - `GetSafeIconFileName`, `NormalizePackageName`, `GetIconUrl`, `GetScreenshots`, `LoadDatabaseAsync`, `ResolveIconOnlineAsync`
5. `WingetStore/Services/LogService.cs` (16 lines)
   - `LogInfo`, `LogError`, `WriteLog`
6. `WingetStore/Services/SettingsService.cs` (33 lines)
   - `LoadSettings`, `SaveSettings`
7. `WingetStore/Services/CliProcessRunner.cs` (24 lines)
   - `RunStreamAsync`
8. `WingetStore/Services/CachingWingetService.cs` (45 lines)
   - `GetOrCreatePackage`, delegate calls to inner service

---

## Detailed Extraction & Testing Proposals

### Proposal 1: Winget CLI Command Argument Builder (`WingetService.cs`)
- **Location**: `WingetStore/Services/WingetService.cs`, lines 70, 72, 73, 136, 138, 141-143, 183, 185.
- **Current Problem**: CLI argument strings are constructed inline with string interpolation inside `async` service methods. This mixes process invocation with argument string construction and makes CLI flag validation difficult.
- **Proposed Extraction**: Create `public static class WingetCliCommandBuilder`:
  ```csharp
  namespace WingetStore.Services;

  public static class WingetCliCommandBuilder
  {
      public static string BuildSearchArgs(string query) =>
          $"search {WingetService.EscapeArgument(query)} --source winget --accept-source-agreements";

      public static string BuildListArgs() =>
          "list --source winget --details --accept-source-agreements";

      public static string BuildUpgradeListArgs() =>
          "upgrade --source winget --accept-source-agreements";

      public static string BuildShowArgs(string packageId) =>
          $"show {WingetService.EscapeArgument(packageId)} --accept-source-agreements";

      public static string BuildInstallArgs(string packageId) =>
          $"install {WingetService.EscapeArgument(packageId)} --silent --accept-package-agreements --accept-source-agreements";

      public static string BuildUpgradeArgs(string packageId) =>
          $"upgrade {WingetService.EscapeArgument(packageId)} --silent --accept-package-agreements --accept-source-agreements";

      public static string BuildUninstallArgs(string packageId) =>
          $"uninstall {WingetService.EscapeArgument(packageId)} --silent";

      public static string BuildExportArgs(string filepath) =>
          $"export -o {WingetService.EscapeArgument(filepath)} --source winget --accept-source-agreements";

      public static string BuildImportArgs(string filepath) =>
          $"import -i {WingetService.EscapeArgument(filepath)} --accept-package-agreements --accept-source-agreements";
  }
  ```
- **Input / Output Specification**:
  - `BuildSearchArgs("git")` -> `"search \"git\" --source winget --accept-source-agreements"`
  - `BuildShowArgs("Git.Git")` -> `"show \"Git.Git\" --accept-source-agreements"`
  - `BuildInstallArgs("Microsoft.VisualStudioCode")` -> `"install \"Microsoft.VisualStudioCode\" --silent --accept-package-agreements --accept-source-agreements"`
  - `BuildExportArgs(@"C:\temp\apps.json")` -> `"export -o \"C:\\temp\\apps.json\" --source winget --accept-source-agreements"`
- **xUnit Test Specifications** (9 tests in `WingetCliCommandBuilderTests`):
  1. `BuildSearchArgs_EscapesQueryAndIncludesFlags`
  2. `BuildListArgs_ReturnsExpectedListFlags`
  3. `BuildUpgradeListArgs_ReturnsExpectedUpgradeListFlags`
  4. `BuildShowArgs_EscapesPackageId`
  5. `BuildInstallArgs_IncludesSilentAndAgreementFlags`
  6. `BuildUpgradeArgs_IncludesSilentAndAgreementFlags`
  7. `BuildUninstallArgs_IncludesSilentFlag`
  8. `BuildExportArgs_EscapesFilePath`
  9. `BuildImportArgs_EscapesFilePath`

---

### Proposal 2: Recommendation List Building & Status Decoration (`WingetService.cs`)
- **Location**: `WingetStore/Services/WingetService.cs`, lines 78-135 (`GetRecommendationsAsync`).
- **Current Problem**: Logic for taking popular packages, looking up installed status in a dictionary, setting `PackageStatus.Installed` / `PackageStatus.Installable`, updating version, and taking top N items is tightly coupled inside `GetRecommendationsAsync()`.
- **Proposed Extraction**:
  ```csharp
  // WingetService.cs
  internal static List<WingetPackage> BuildRecommendations(
      IEnumerable<WingetPackage>? popularPackages, 
      IDictionary<string, WingetPackage>? installedMap, 
      int maxCount = 10)
  {
      if (popularPackages == null) return [];
      installedMap ??= new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase);

      var result = new List<WingetPackage>();
      foreach (var p in popularPackages.Where(p => p != null).Take(maxCount))
      {
          string id = p.Id ?? "";
          var pkg = new WingetPackage
          {
              Id = id,
              Name = p.Name ?? "",
              Publisher = p.Publisher ?? "",
              Version = p.Version ?? "",
              Source = p.Source ?? "",
              Description = p.Description ?? ""
          };
          if (!string.IsNullOrEmpty(id) && installedMap.TryGetValue(id, out var inst))
          {
              pkg.Status = PackageStatus.Installed;
              if (!string.IsNullOrEmpty(inst.Version)) pkg.Version = inst.Version;
          }
          else
          {
              pkg.Status = PackageStatus.Installable;
          }
          result.Add(pkg);
      }
      return result;
  }
  ```
- **Input / Output Specification**:
  - `popularPackages`: `[{Id: "Git.Git"}, {Id: "NodeJS.NodeJS"}]`
  - `installedMap`: `{"git.git": {Id: "Git.Git", Version: "2.40.0"}}`
  - Output: 2 packages; `Git.Git` has `Status = PackageStatus.Installed`, `Version = "2.40.0"`; `NodeJS` has `Status = PackageStatus.Installable`.
- **xUnit Test Specifications** (6 tests in `WingetServiceRecommendationsTests`):
  1. `BuildRecommendations_NullPopular_ReturnsEmptyList`
  2. `BuildRecommendations_NullInstalledMap_MarksAllAsInstallable`
  3. `BuildRecommendations_MatchingInstalledPackage_SetsInstalledStatusAndVersion`
  4. `BuildRecommendations_CaseInsensitiveIdMatch_UpdatesStatusCorrectly`
  5. `BuildRecommendations_RespectsMaxCountLimit`
  6. `BuildRecommendations_HandlesNullElementsInPopularList`

---

### Proposal 3: Package Details Status & Version Decoration (`WingetService.cs`)
- **Location**: `WingetStore/Services/WingetService.cs`, line 139 (`FetchAndDecoratePackageDetailsAsync`).
- **Current Problem**: `FetchAndDecoratePackageDetailsAsync` combines details fetched from CLI with installed list and upgradable list. The logic for determining whether a package is upgradable, installed, or installable (and populating current vs available version) is inline.
- **Proposed Extraction**:
  ```csharp
  // WingetService.cs
  internal static WingetPackage DecoratePackageDetails(
      WingetPackage? details, 
      string packageId, 
      IEnumerable<WingetPackage>? installedPackages, 
      IEnumerable<WingetPackage>? upgradablePackages)
  {
      var pkg = details ?? new WingetPackage { Id = packageId, Name = packageId };
      installedPackages ??= [];
      upgradablePackages ??= [];

      var upg = upgradablePackages.FirstOrDefault(p => p != null && p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
      if (upg != null)
      {
          pkg.Status = PackageStatus.Upgradable;
          if (!string.IsNullOrEmpty(upg.Version)) pkg.Version = upg.Version;
          if (!string.IsNullOrEmpty(upg.AvailableVersion)) pkg.AvailableVersion = upg.AvailableVersion;
          return pkg;
      }

      var inst = installedPackages.FirstOrDefault(p => p != null && p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
      if (inst != null)
      {
          pkg.Status = PackageStatus.Installed;
          if (!string.IsNullOrEmpty(inst.Version)) pkg.Version = inst.Version;
          return pkg;
      }

      pkg.Status = PackageStatus.Installable;
      return pkg;
  }
  ```
- **Input / Output Specification**:
  - `details` = null, `packageId` = "Git.Git", `installed` = [], `upgradable` = `[{Id: "Git.Git", Version: "2.40.0", AvailableVersion: "2.41.0"}]`
  - Output: `WingetPackage` with `Status = PackageStatus.Upgradable`, `Version = "2.40.0"`, `AvailableVersion = "2.41.0"`.
- **xUnit Test Specifications** (6 tests in `DecoratePackageDetailsTests`):
  1. `DecoratePackageDetails_NullDetails_UsesFallbackPackage`
  2. `DecoratePackageDetails_UpgradableMatch_SetsUpgradableStatusAndVersions`
  3. `DecoratePackageDetails_InstalledMatch_SetsInstalledStatusAndVersion`
  4. `DecoratePackageDetails_NoMatch_SetsInstallableStatus`
  5. `DecoratePackageDetails_UpgradableTakesPrecedenceOverInstalled`
  6. `DecoratePackageDetails_CaseInsensitiveIdMatching`

---

### Proposal 4: Package Action Determination (`WingetService.cs`)
- **Location**: `WingetStore/Services/WingetService.cs`, line 140 (`TriggerPackageAction`).
- **Current Problem**: Action decision logic (`Cancel`, `Uninstall`, `Upgrade`, `Install`) is coupled directly to task dispatching.
- **Proposed Extraction**:
  ```csharp
  // WingetService.cs
  public enum PackageActionKind { None, Cancel, Uninstall, Upgrade, Install }

  internal static PackageActionKind DeterminePackageAction(WingetPackage? package)
  {
      if (package == null) return PackageActionKind.None;
      if (package.IsInstalling) return PackageActionKind.Cancel;
      if (package.Status == PackageStatus.Installed) return PackageActionKind.Uninstall;
      if (package.Status == PackageStatus.Upgradable) return PackageActionKind.Upgrade;
      return PackageActionKind.Install;
  }
  ```
- **xUnit Test Specifications** (5 tests in `DeterminePackageActionTests`):
  1. `DeterminePackageAction_NullPackage_ReturnsNone`
  2. `DeterminePackageAction_IsInstalling_ReturnsCancel`
  3. `DeterminePackageAction_InstalledStatus_ReturnsUninstall`
  4. `DeterminePackageAction_UpgradableStatus_ReturnsUpgrade`
  5. `DeterminePackageAction_InstallableStatus_ReturnsInstall`

---

### Proposal 5: Icon Online Resolution & Domain Extraction (`IconService.cs`)
- **Location**: `WingetStore/Services/IconService.cs`, lines 138-161 (`ResolveIconOnlineAsync`).
- **Current Problem**: Homepage extraction from winget `show` output, domain cleaning (`www.` stripping), and Hunter/Google icon URL building is embedded in an `async` HTTP/CLI method.
- **Proposed Extraction**:
  ```csharp
  // IconService.cs
  internal static string ExtractHomepageFromShowOutput(string showOutput)
  {
      if (string.IsNullOrEmpty(showOutput)) return "";
      foreach (var line in showOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
      {
          string trimmed = line.Trim();
          if (trimmed.StartsWith("Homepage:", StringComparison.OrdinalIgnoreCase))
              return trimmed["Homepage:".Length..].Trim();
      }
      return "";
  }

  internal static (string Domain, string LogoUrl, string FaviconUrl)? ExtractIconUrlsFromHomepage(string homepageUrl)
  {
      if (string.IsNullOrWhiteSpace(homepageUrl) || !Uri.TryCreate(homepageUrl, UriKind.Absolute, out var uri))
          return null;

      string domain = uri.Host;
      if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
          domain = domain[4..];

      if (string.IsNullOrEmpty(domain)) return null;

      string logoUrl = $"https://logos.hunter.io/{domain}";
      string favUrl = $"https://www.google.com/s2/favicons?domain={domain}&sz=128";
      return (domain, logoUrl, favUrl);
  }
  ```
- **Input / Output Specification**:
  - `ExtractHomepageFromShowOutput("Name: Git\nHomepage: https://git-scm.com\n")` -> `"https://git-scm.com"`
  - `ExtractIconUrlsFromHomepage("https://www.git-scm.com/downloads")` -> `("git-scm.com", "https://logos.hunter.io/git-scm.com", "https://www.google.com/s2/favicons?domain=git-scm.com&sz=128")`
- **xUnit Test Specifications** (6 tests in `IconServiceDomainExtractionTests`):
  1. `ExtractHomepageFromShowOutput_ValidHomepage_ExtractsUrl`
  2. `ExtractHomepageFromShowOutput_NoHomepage_ReturnsEmptyString`
  3. `ExtractHomepageFromShowOutput_NullOrEmptyOutput_ReturnsEmptyString`
  4. `ExtractIconUrlsFromHomepage_StripsWwwPrefix`
  5. `ExtractIconUrlsFromHomepage_ValidUrl_ReturnsDomainAndUrls`
  6. `ExtractIconUrlsFromHomepage_InvalidOrRelativeUrl_ReturnsNull`

---

### Proposal 6: Screenshot Database JSON Parsing (`IconService.cs`)
- **Location**: `WingetStore/Services/IconService.cs`, lines 57-73 (`LoadDatabaseAsync`).
- **Current Problem**: `LoadDatabaseAsync` reads file stream and parses `icons_and_screenshots` JSON node. Parsing cannot be tested without file I/O.
- **Proposed Extraction**:
  ```csharp
  // IconService.cs
  internal static (Dictionary<string, string> Icons, Dictionary<string, List<string>> Screenshots) ParseScreenshotDatabaseJson(string jsonContent)
  {
      var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var screenshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

      if (string.IsNullOrWhiteSpace(jsonContent)) return (icons, screenshots);

      try
      {
          using var doc = JsonDocument.Parse(jsonContent);
          if (doc.RootElement.TryGetProperty("icons_and_screenshots", out var iconsNode))
          {
              foreach (var prop in iconsNode.EnumerateObject())
              {
                  if (prop.Value.TryGetProperty("icon", out var iconProp) && iconProp.ValueKind == JsonValueKind.String)
                  {
                      string iconUrl = iconProp.GetString() ?? "";
                      if (!string.IsNullOrEmpty(iconUrl)) icons[prop.Name] = iconUrl;
                  }
                  if (prop.Value.TryGetProperty("images", out var imagesProp) && imagesProp.ValueKind == JsonValueKind.Array)
                  {
                      var list = new List<string>();
                      foreach (var item in imagesProp.EnumerateArray())
                      {
                          if (item.ValueKind == JsonValueKind.String)
                          {
                              string imgUrl = item.GetString() ?? "";
                              if (!string.IsNullOrEmpty(imgUrl)) list.Add(imgUrl);
                          }
                      }
                      if (list.Count > 0) screenshots[prop.Name] = list;
                  }
              }
          }
      }
      catch (Exception ex)
      {
          Debug.WriteLine($"Failed to parse screenshot database JSON: {ex.Message}");
      }
      return (icons, screenshots);
  }
  ```
- **xUnit Test Specifications** (5 tests in `IconServiceJsonParserTests`):
  1. `ParseScreenshotDatabaseJson_ValidJson_ParsesIconsAndScreenshots`
  2. `ParseScreenshotDatabaseJson_NullOrEmptyJson_ReturnsEmptyDictionaries`
  3. `ParseScreenshotDatabaseJson_InvalidJsonSyntax_HandlesExceptionGracefully`
  4. `ParseScreenshotDatabaseJson_MissingProperty_ReturnsEmptyDictionaries`
  5. `ParseScreenshotDatabaseJson_EmptyImagesArray_ExcludesPackageFromScreenshots`

---

### Proposal 7: Log Entry Formatting (`LogService.cs`)
- **Location**: `WingetStore/Services/LogService.cs`, lines 12-14.
- **Current Problem**: `WriteLog` builds timestamped string lines and formats exceptions inline.
- **Proposed Extraction**:
  ```csharp
  // LogService.cs
  internal static string FormatLogMessage(string level, string message, Exception? ex = null, DateTime? timestamp = null)
  {
      DateTime time = timestamp ?? DateTime.Now;
      string formattedMessage = ex != null 
          ? $"{message} | Exception: {ex.Message}\nStack: {ex.StackTrace}" 
          : message;
      return $"[{time:yyyy-MM-dd HH:mm:ss}] [{level}] {formattedMessage}";
  }
  ```
- **xUnit Test Specifications** (3 tests in `LogServiceFormattingTests`):
  1. `FormatLogMessage_InfoMessage_FormatsWithTimestampAndLevel`
  2. `FormatLogMessage_WithException_AppendsExceptionDetailsAndStackTrace`
  3. `FormatLogMessage_CustomTimestamp_UsesProvidedTimestamp`

---

### Proposal 8: VersionComparer Boundary & Edge-Case Unit Tests (`Services/Helpers.cs`)
- **Location**: `WingetStore/Services/Helpers.cs`, lines 97-152 (`VersionComparer`).
- **Current Problem**: `VersionComparer` handles complex SemVer logic (prerelease tags like `-alpha`, section length differences, non-numeric parts, `v` prefix, `+` build metadata). Currently `Tests.cs` only tests basic 3-segment numeric versions.
- **Proposed Unit Tests** (7 tests in `VersionComparerEdgeCaseTests`):
  1. `Compare_NullArguments_HandlesNullsCorrectly` (`Compare(null, null) == 0`, `Compare(null, "1.0") < 0`, `Compare("1.0", null) > 0`)
  2. `Compare_PrereleaseVsNonPrerelease_PrereleaseIsLower` (`Compare("1.0.0-alpha", "1.0.0") < 0`)
  3. `Compare_PrereleaseAlphabeticalOrdering_SortsCorrectly` (`Compare("1.0.0-alpha", "1.0.0-beta") < 0`)
  4. `Compare_DifferentSectionLengths_ShorterIsLower` (`Compare("1.0", "1.0.0") < 0`)
  5. `Compare_NonNumericParts_UsesCaseInsensitiveStringComparison` (`Compare("1.0.0.a", "1.0.0.b") < 0`)
  6. `Compare_LeadingVPrefix_IgnoresPrefixCaseInsensitively` (`Compare("v2.1.0", "V2.1.0") == 0`)
  7. `Compare_BuildMetadataPlusSign_IgnoresPlusSignMetadata` (`Compare("1.0.0+build1", "1.0.0+build2") == 0`)

---

### Proposal 9: MapFromRow Visibility & Testing (`WingetService.cs`)
- **Location**: `WingetStore/Services/WingetService.cs`, lines 64-69 (`MapFromRow`).
- **Current Problem**: `MapFromRow` converts dictionary rows from `WingetParser.ParseTable` into `WingetPackage` instances, applying fallbacks for empty `Source` to `"winget"`. Currently `private static`.
- **Proposed Extraction**: Change visibility to `internal static`:
  ```csharp
  internal static WingetPackage MapFromRow(Dictionary<string, string> row, bool includeAvailable = false, PackageStatus defaultStatus = PackageStatus.Installable)
  ```
- **xUnit Test Specifications** (5 tests in `WingetServiceMapFromRowTests`):
  1. `MapFromRow_StandardRow_MapsPropertiesCorrectly`
  2. `MapFromRow_EmptySource_DefaultsToWinget`
  3. `MapFromRow_IncludeAvailableTrue_MapsAvailableVersion`
  4. `MapFromRow_CustomDefaultStatus_AppliesStatus`
  5. `MapFromRow_MissingKeys_HandlesGracefullyWithDefaults`

---

## Summary Matrix of Proposals

| # | Feature / Target | Original File & Lines | Proposed Extracted Method / Signature | New Tests |
|---|---|---|---|---|
| 1 | Winget CLI Command Arguments | `Services/WingetService.cs:70,72,73,136,138,141-143,183,185` | `public static class WingetCliCommandBuilder` | 9 |
| 2 | Recommendations Builder | `Services/WingetService.cs:78-135` | `internal static List<WingetPackage> BuildRecommendations(...)` | 6 |
| 3 | Package Details Decoration | `Services/WingetService.cs:139` | `internal static WingetPackage DecoratePackageDetails(...)` | 6 |
| 4 | Package Action Determination | `Services/WingetService.cs:140` | `internal static PackageActionKind DeterminePackageAction(...)` | 5 |
| 5 | Homepage & Icon URL Resolution | `Services/IconService.cs:138-161` | `internal static string ExtractHomepageFromShowOutput(...)`, `internal static (string, string, string)? ExtractIconUrlsFromHomepage(...)` | 6 |
| 6 | Screenshot DB JSON Parsing | `Services/IconService.cs:57-73` | `internal static (Dictionary<string, string>, Dictionary<string, List<string>>) ParseScreenshotDatabaseJson(...)` | 5 |
| 7 | Log Line Formatting | `Services/LogService.cs:12-14` | `internal static string FormatLogMessage(...)` | 3 |
| 8 | VersionComparer Edge Cases | `Services/Helpers.cs:97-152` | Direct tests for `VersionComparer.Instance.Compare` | 7 |
| 9 | Row Dictionary Mapping | `Services/WingetService.cs:64-69` | `internal static WingetPackage MapFromRow(...)` | 5 |

**Total Proposed New Tests**: 52 tests
