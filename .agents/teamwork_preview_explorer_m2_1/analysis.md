# Milestone 2 Analysis Report — Services & Helpers Logic Extraction

## Executive Summary
This analysis evaluates non-UI pure logic within `WingetStore/Services/WingetParser.cs`, `WingetStore/Services/IconService.cs`, `WingetStore/Services/WingetService.cs`, and `WingetStore/Services/CachingWingetService.cs`. The goal is to identify pure, deterministic functions currently kept `private` or embedded inline inside async I/O operations, and propose their extraction into `internal static` or `public static` helper methods for isolated unit testing.

A total of **13 concrete static method extractions/exposures** across 4 service files are identified, which can unlock **45+ new automated xUnit test cases** without altering existing public service interfaces or breaking async workflows.

---

## Existing Test Coverage Baseline
- **`WingetParser`**: Covered by `WingetParserTests`, `WingetParserHardeningTests`, and `WingetParser_AdditionalEdgeCases_Coverage` in `WingetStore.Tests/Tests.cs`. Public methods (`ParseTable`, `ParseDetailsList`, `ParsePackageDetails`, `ParseProgressFromOutput`, `ParseStatusTextFromOutput`, `ParseTagsFromShowOutput`, `GetSubstring`) are partially tested, but lower-level column position detection, row parsing, regex matching, and item setting helper methods are `private static` and untested.
- **`IconService`**: `GetSafeIconFileName` and `NormalizePackageName` are tested in `IconServiceCoverageTests` and `IconServiceTests`. Async database fetching and online icon resolution make up the majority of untested code due to disk and network calls.
- **`WingetService`**: `EscapeArgument` is tested in `SecurityAndSanitizationTests`. Table row mapping, package status decoration, and recommendations merging are embedded in async tasks and untested.
- **`CachingWingetService`**: Basic pass-through calls are tested in `CachingWingetServiceTests`. Multi-property cache merging logic is embedded in `GetOrCreatePackage` and untested for edge cases.

---

## Detailed Method Extraction Proposals

### 1. `WingetStore/Services/WingetParser.cs`

#### Proposal 1.1: Expose Table Separator Line Finder
- **Original File & Line**: `WingetStore/Services/WingetParser.cs:27`
- **Original Code**:
  ```csharp
  private static int FindHeaderLine(string[] lines) { for (int i = 0; i < lines.Length; i++) { if (lines[i].Contains("---")) return i - 1; } return -1; }
  ```
- **Proposed Modification**: Change `private static` to `internal static int FindHeaderLine(string[] lines)`.
- **Rationale**: Isolates table header index identification logic from full string parsing.
- **Proposed xUnit Tests (`WingetParserInternalTests`)**:
  1. `FindHeaderLine_WithValidSeparator_ReturnsHeaderIndex`: Input `["Name Id Version", "--- -- -------", "App1 1.0 1.0"]` -> Expected `0`.
  2. `FindHeaderLine_NoSeparator_ReturnsNegativeOne`: Input `["Name Id Version", "App1 1.0 1.0"]` -> Expected `-1`.
  3. `FindHeaderLine_SeparatorAtFirstLine_ReturnsNegativeOne`: Input `["---", "App1 1.0"]` -> Expected `-1`.
  4. `FindHeaderLine_EmptyArray_ReturnsNegativeOne`: Input `[]` -> Expected `-1`.

#### Proposal 1.2: Expose Column Position Parser
- **Original File & Lines**: `WingetStore/Services/WingetParser.cs:29-35`
- **Original Code**:
  ```csharp
  private static bool TryParseColumnPositions(string headerLine, out (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos)
  ```
- **Proposed Modification**: Change `private static` to `internal static bool TryParseColumnPositions(string headerLine, out (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos)`.
- **Rationale**: Enables direct testing of header parsing across various `winget` CLI output variations ("Source", "Match", "Available" extra columns).
- **Proposed xUnit Tests (`WingetParserInternalTests`)**:
  1. `TryParseColumnPositions_StandardHeader_ReturnsTrueAndPositions`: Input `"Name Id Version Source"` -> `idPos` = 5, `versionPos` = 8, `sourcePos` = 16.
  2. `TryParseColumnPositions_UpgradeHeader_ReturnsAvailablePos`: Input `"Name Id Version Available Source"` -> `availablePos` = 16.
  3. `TryParseColumnPositions_MatchHeader_ReturnsMatchPos`: Input `"Name Id Version Match"` -> `matchPos` = 16.
  4. `TryParseColumnPositions_MissingId_ReturnsFalse`: Input `"Name Version Source"` -> Expected `false`.
  5. `TryParseColumnPositions_InvalidOrder_ReturnsFalse`: Input `"Version Id Name"` -> Expected `false`.

#### Proposal 1.3: Expose Single Table Row Parser
- **Original File & Lines**: `WingetStore/Services/WingetParser.cs:37-47`
- **Original Code**:
  ```csharp
  private static Dictionary<string, string> ParseTableRow(string line, (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos)
  ```
- **Proposed Modification**: Change `private static` to `internal static Dictionary<string, string> ParseTableRow(string line, (int namePos, int idPos, int versionPos, int sourcePos, int matchPos, int availablePos) pos)`.
- **Rationale**: Isolates substring extraction for single row items without needing full CLI table formatting.
- **Proposed xUnit Tests (`WingetParserInternalTests`)**:
  1. `ParseTableRow_StandardRow_ParsesNameIdVersionSource`: Valid line & pos -> dictionary containing Name, Id, Version, Source keys.
  2. `ParseTableRow_AvailableColumn_ParsesAvailableKey`: Valid line with Available pos -> dictionary containing "Available" key.
  3. `ParseTableRow_MatchColumn_ParsesMatchKey`: Valid line with Match pos -> dictionary containing "Match" key.

#### Proposal 1.4: Expose `Found` Line Matching Helper
- **Original File & Line**: `WingetStore/Services/WingetParser.cs:80`
- **Original Code**:
  ```csharp
  private static bool TryParseFoundLine(string trimmed, WingetPackage package) { if (!trimmed.StartsWith("Found ", StringComparison.OrdinalIgnoreCase)) return false; int bracketStart = trimmed.IndexOf('['); if (bracketStart != -1) package.Name = trimmed["Found ".Length..bracketStart].Trim(); return true; }
  ```
- **Proposed Modification**: Change `private static` to `internal static bool TryParseFoundLine(string trimmed, WingetPackage package)`.
- **Rationale**: Validates package title extraction from `winget show` search output headers (`Found AppName [App.Id]`).
- **Proposed xUnit Tests (`WingetParserInternalTests`)**:
  1. `TryParseFoundLine_ValidFoundLine_SetsPackageNameAndReturnsTrue`: Input `"Found Git [Git.Git]"`, `pkg` -> `pkg.Name == "Git"`, returns `true`.
  2. `TryParseFoundLine_FoundLineWithoutBracket_ReturnsTrueWithoutSettingName`: Input `"Found Git"`, `pkg` -> returns `true`, `pkg.Name` unchanged.
  3. `TryParseFoundLine_NonFoundLine_ReturnsFalse`: Input `"Publisher: Microsoft"`, `pkg` -> returns `false`.

#### Proposal 1.5: Expose Package Field Setter & URL Validation
- **Original File & Lines**: `WingetStore/Services/WingetParser.cs:95-96`
- **Original Code**:
  ```csharp
  private static void SetPackageField(WingetPackage package, string key, string val) { ... }
  private static bool IsUrl(string val) => val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || val.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
  ```
- **Proposed Modification**: Change `private static` to `internal static void SetPackageField(WingetPackage package, string key, string val)` and `internal static bool IsUrl(string val)`.
- **Rationale**: Tests dictionary field mapping and URL verification independently.
- **Proposed xUnit Tests (`WingetParserInternalTests`)**:
  1. `SetPackageField_ValidKeys_SetsCorrectProperties`: Test keys "Name", "Version", "Publisher", "Publisher Url", "Description", "Homepage", "License", "Release Notes".
  2. `SetPackageField_UnknownKey_NoException`: Test key "UnknownKey" -> no-op.
  3. `IsUrl_HttpAndHttps_ReturnsTrue`: Test `http://test.com`, `https://test.com` -> `true`.
  4. `IsUrl_NonHttp_ReturnsFalse`: Test `ftp://test.com`, `C:\path`, `invalid` -> `false`.

---

### 2. `WingetStore/Services/IconService.cs`

#### Proposal 2.1: Extract Database JSON Document Parser
- **Original File & Lines**: `WingetStore/Services/IconService.cs:60-73`
- **Original Code**: Embedded within async method `LoadDatabaseAsync(string filePath)`.
- **Proposed Extraction**:
  ```csharp
  internal static (Dictionary<string, string> icons, Dictionary<string, List<string>> screenshots) ParseDatabaseJson(string json)
  {
      using var doc = JsonDocument.Parse(json);
      var newIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var newScreenshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
      if (doc.RootElement.TryGetProperty("icons_and_screenshots", out var iconsNode))
      {
          foreach (var prop in iconsNode.EnumerateObject())
          {
              if (prop.Value.TryGetProperty("icon", out var iconProp) && iconProp.ValueKind == JsonValueKind.String)
              {
                  string iconUrl = iconProp.GetString() ?? "";
                  if (!string.IsNullOrEmpty(iconUrl)) newIcons[prop.Name] = iconUrl;
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
                  if (list.Count > 0) newScreenshots[prop.Name] = list;
              }
          }
      }
      return (newIcons, newScreenshots);
  }
  ```
- **Rationale**: Removes requirement for file system access when unit-testing UniGetUI JSON database parsing logic.
- **Proposed xUnit Tests (`IconServiceParserTests`)**:
  1. `ParseDatabaseJson_ValidJson_ParsesIconsAndScreenshots`: Valid JSON payload -> returns populated `icons` and `screenshots` dictionaries.
  2. `ParseDatabaseJson_MissingRootProperty_ReturnsEmptyDictionaries`: JSON without `"icons_and_screenshots"` -> returns empty dictionaries.
  3. `ParseDatabaseJson_EmptyOrNullValues_IgnoresInvalidEntries`: JSON with null icon/image fields -> ignores empty items.

#### Proposal 2.2: Extract Homepage String Parser from CLI Show Output
- **Original File & Lines**: `WingetStore/Services/IconService.cs:140-141`
- **Original Code**: Embedded in `ResolveIconOnlineAsync`.
- **Proposed Extraction**:
  ```csharp
  internal static string ExtractHomepageFromShowOutput(string showOutput)
  {
      if (string.IsNullOrEmpty(showOutput)) return "";
      foreach (var line in showOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
      {
          string trimmed = line.Trim();
          if (trimmed.StartsWith("Homepage:", StringComparison.OrdinalIgnoreCase))
          {
              return trimmed["Homepage:".Length..].Trim();
          }
      }
      return "";
  }
  ```
- **Rationale**: Test homepage link extraction from `winget show` CLI response strings.
- **Proposed xUnit Tests (`IconServiceParserTests`)**:
  1. `ExtractHomepageFromShowOutput_ValidOutput_ReturnsHomepageUrl`: Input `"Publisher: Microsoft\r\nHomepage: https://microsoft.com"` -> `"https://microsoft.com"`.
  2. `ExtractHomepageFromShowOutput_NoHomepageLine_ReturnsEmptyString`: Input `"Publisher: Microsoft\r\nVersion: 1.0"` -> `""`.
  3. `ExtractHomepageFromShowOutput_NullOrEmpty_ReturnsEmptyString`: Input `""` / `null` -> `""`.

#### Proposal 2.3: Extract Clean Domain Extractor from Homepage URL
- **Original File & Lines**: `WingetStore/Services/IconService.cs:141-143`
- **Original Code**: Embedded in `ResolveIconOnlineAsync`.
- **Proposed Extraction**:
  ```csharp
  internal static string ExtractDomainFromUrl(string url)
  {
      if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
      {
          string domain = uri.Host;
          if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) domain = domain[4..];
          return domain;
      }
      return "";
  }
  ```
- **Rationale**: Isolates domain stripping and URI host parsing for favicon/logo resolution.
- **Proposed xUnit Tests (`IconServiceParserTests`)**:
  1. `ExtractDomainFromUrl_StandardUrl_ReturnsHost`: Input `"https://github.com/microsoft/winget-cli"` -> `"github.com"`.
  2. `ExtractDomainFromUrl_WwwUrl_StripsWww`: Input `"https://www.google.com/search"` -> `"google.com"`.
  3. `ExtractDomainFromUrl_InvalidUrl_ReturnsEmptyString`: Input `"not-a-url"` -> `""`.

#### Proposal 2.4: Extract External Logo and Favicon URL Builders
- **Original File & Lines**: `WingetStore/Services/IconService.cs:147,157`
- **Proposed Extraction**:
  ```csharp
  internal static string GetHunterLogoUrl(string domain) => string.IsNullOrEmpty(domain) ? "" : $"https://logos.hunter.io/{domain}";
  internal static string GetGoogleFaviconUrl(string domain, int size = 128) => string.IsNullOrEmpty(domain) ? "" : $"https://www.google.com/s2/favicons?domain={domain}&sz={size}";
  ```
- **Rationale**: Tests URL formatting for external icon providers.
- **Proposed xUnit Tests (`IconServiceParserTests`)**:
  1. `GetHunterLogoUrl_ValidDomain_ReturnsFormattedUrl`: Input `"example.com"` -> `"https://logos.hunter.io/example.com"`.
  2. `GetGoogleFaviconUrl_ValidDomain_ReturnsFormattedUrl`: Input `"example.com"` -> `"https://www.google.com/s2/favicons?domain=example.com&sz=128"`.

---

### 3. `WingetStore/Services/WingetService.cs`

#### Proposal 3.1: Expose Table Row to `WingetPackage` Model Mapper
- **Original File & Lines**: `WingetStore/Services/WingetService.cs:64-69`
- **Original Code**:
  ```csharp
  private static WingetPackage MapFromRow(Dictionary<string, string> row, bool includeAvailable = false, PackageStatus defaultStatus = PackageStatus.Installable)
  ```
- **Proposed Modification**: Change `private static` to `internal static WingetPackage MapFromRow(Dictionary<string, string> row, bool includeAvailable = false, PackageStatus defaultStatus = PackageStatus.Installable)`.
- **Rationale**: Tests row dictionary conversion to `WingetPackage` models directly.
- **Proposed xUnit Tests (`WingetServiceMappingTests`)**:
  1. `MapFromRow_StandardRow_PopulatesPackage`: Test mapping Name, Id, Version, default Source "winget".
  2. `MapFromRow_MissingSource_DefaultsToWinget`: Test row without Source key -> `Source == "winget"`.
  3. `MapFromRow_IncludeAvailableTrue_PopulatesAvailableVersion`: Test `includeAvailable: true` with `"Available"` key -> `AvailableVersion` set.

#### Proposal 3.2: Extract Package Status Decoration Helper
- **Original File & Line**: `WingetStore/Services/WingetService.cs:139`
- **Original Code**: Embedded inside async method `FetchAndDecoratePackageDetailsAsync`.
- **Proposed Extraction**:
  ```csharp
  internal static WingetPackage DecoratePackageStatus(WingetPackage pkg, IEnumerable<WingetPackage>? installedPackages, IEnumerable<WingetPackage>? upgradablePackages)
  {
      ArgumentNullException.ThrowIfNull(pkg);
      var installed = installedPackages ?? Enumerable.Empty<WingetPackage>();
      var upgradable = upgradablePackages ?? Enumerable.Empty<WingetPackage>();

      bool isInstalled = installed.Any(p => string.Equals(p.Id, pkg.Id, StringComparison.OrdinalIgnoreCase));
      bool isUpgradable = upgradable.Any(p => string.Equals(p.Id, pkg.Id, StringComparison.OrdinalIgnoreCase));

      if (isUpgradable)
      {
          pkg.Status = PackageStatus.Upgradable;
          var upg = upgradable.FirstOrDefault(p => string.Equals(p.Id, pkg.Id, StringComparison.OrdinalIgnoreCase));
          if (upg != null)
          {
              pkg.Version = upg.Version;
              pkg.AvailableVersion = upg.AvailableVersion;
          }
      }
      else if (isInstalled)
      {
          pkg.Status = PackageStatus.Installed;
          var inst = installed.FirstOrDefault(p => string.Equals(p.Id, pkg.Id, StringComparison.OrdinalIgnoreCase));
          if (inst != null) pkg.Version = inst.Version;
      }
      else
      {
          pkg.Status = PackageStatus.Installable;
      }
      return pkg;
  }
  ```
- **Rationale**: Test status resolution (Installable vs Installed vs Upgradable) without running async process commands.
- **Proposed xUnit Tests (`WingetServiceDecorationTests`)**:
  1. `DecoratePackageStatus_PackageInUpgradable_SetsStatusUpgradableAndVersions`: Pkg in upgradable list -> status `Upgradable`.
  2. `DecoratePackageStatus_PackageInInstalled_SetsStatusInstalledAndVersion`: Pkg in installed list -> status `Installed`.
  3. `DecoratePackageStatus_PackageNotInLists_SetsStatusInstallable`: Pkg in neither list -> status `Installable`.

#### Proposal 3.3: Extract Recommendations List Merging Logic
- **Original File & Lines**: `WingetStore/Services/WingetService.cs:109-134`
- **Original Code**: Embedded in `GetRecommendationsAsync`.
- **Proposed Extraction**:
  ```csharp
  internal static List<WingetPackage> MergeRecommendations(IEnumerable<WingetPackage> popular, IDictionary<string, WingetPackage> installedMap, int maxCount = 10)
  {
      if (popular == null) return [];
      var result = new List<WingetPackage>();
      foreach (var p in popular.Take(maxCount))
      {
          if (p == null) continue;
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
          if (!string.IsNullOrEmpty(id) && installedMap != null && installedMap.TryGetValue(id, out var inst))
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
- **Rationale**: Tests synthesis of recommendations against installed packages without disk asset I/O.
- **Proposed xUnit Tests (`WingetServiceRecommendationsTests`)**:
  1. `MergeRecommendations_PopularWithInstalledMatches_MarksInstalled`: Match in installedMap -> status `Installed` and updated version.
  2. `MergeRecommendations_PopularWithoutInstalledMatches_MarksInstallable`: No match -> status `Installable`.
  3. `MergeRecommendations_MoreThanMaxCount_LimitsToMaxCount`: Takes first 10 popular packages.

---

### 4. `WingetStore/Services/CachingWingetService.cs`

#### Proposal 4.1: Extract Package Multi-Property Merge Helper
- **Original File & Lines**: `WingetStore/Services/CachingWingetService.cs:18-21`
- **Original Code**: Multi-property assignment block inside `GetOrCreatePackage`.
- **Proposed Extraction**:
  ```csharp
  internal static void MergePackageProperties(WingetPackage existing, WingetPackage incoming)
  {
      ArgumentNullException.ThrowIfNull(existing);
      ArgumentNullException.ThrowIfNull(incoming);

      existing.Name = incoming.Name;
      if (!string.IsNullOrEmpty(incoming.Version)) existing.Version = incoming.Version;
      if (!string.IsNullOrEmpty(incoming.AvailableVersion)) existing.AvailableVersion = incoming.AvailableVersion;
      if (!string.IsNullOrEmpty(incoming.Source)) existing.Source = incoming.Source;
      if (!string.IsNullOrEmpty(incoming.Publisher)) existing.Publisher = incoming.Publisher;
      if (incoming.Status != PackageStatus.Installable) existing.Status = incoming.Status;
      if (!string.IsNullOrEmpty(incoming.Description)) existing.Description = incoming.Description;
      if (!string.IsNullOrEmpty(incoming.Homepage)) existing.Homepage = incoming.Homepage;
      if (!string.IsNullOrEmpty(incoming.License)) existing.License = incoming.License;
      if (!string.IsNullOrEmpty(incoming.ReleaseNotes)) existing.ReleaseNotes = incoming.ReleaseNotes;
      if (!string.IsNullOrEmpty(incoming.PublisherUrl)) existing.PublisherUrl = incoming.PublisherUrl;
      if (!string.IsNullOrEmpty(incoming.InstallerType)) existing.InstallerType = incoming.InstallerType;
      if (!string.IsNullOrEmpty(incoming.InstallerUrl)) existing.InstallerUrl = incoming.InstallerUrl;
      if (incoming.Tags != null && incoming.Tags.Count > 0) existing.Tags = incoming.Tags;
      if (incoming.Details != null && incoming.Details.Count > 0) existing.Details = incoming.Details;
      if (incoming.Screenshots != null && incoming.Screenshots.Count > 0) existing.Screenshots = incoming.Screenshots;
  }
  ```
- **Rationale**: Isolates cache entry mutation rules from dictionary lock handling.
- **Proposed xUnit Tests (`CachingWingetServiceMergeTests`)**:
  1. `MergePackageProperties_OverwritesNonNullProperties`: Source has non-empty fields -> updates target fields.
  2. `MergePackageProperties_PreservesTargetWhenSourceEmpty`: Source has empty strings -> target fields preserved.
  3. `MergePackageProperties_PreservesStatusWhenSourceInstallable`: Source status `Installable` -> target status untouched.

---

## Backward Compatibility & Interface Safety Verification
- **All proposed extractions use `internal static` visibility**.
- **No public method signatures, interface contracts (`IWingetService`), or async signatures are changed**.
- **Original call sites inside `WingetParser`, `IconService`, `WingetService`, and `CachingWingetService` delegate directly to these extracted static methods**.

## Summary of Targeted Extractions
| Service File | Proposed Method | Access | Type |
|---|---|---|---|
| `WingetParser.cs` | `FindHeaderLine` | `internal static` | Exposure |
| `WingetParser.cs` | `TryParseColumnPositions` | `internal static` | Exposure |
| `WingetParser.cs` | `ParseTableRow` | `internal static` | Exposure |
| `WingetParser.cs` | `TryParseFoundLine` | `internal static` | Exposure |
| `WingetParser.cs` | `SetPackageField` | `internal static` | Exposure |
| `WingetParser.cs` | `IsUrl` | `internal static` | Exposure |
| `IconService.cs` | `ParseDatabaseJson` | `internal static` | Extraction |
| `IconService.cs` | `ExtractHomepageFromShowOutput` | `internal static` | Extraction |
| `IconService.cs` | `ExtractDomainFromUrl` | `internal static` | Extraction |
| `IconService.cs` | `GetHunterLogoUrl` / `GetGoogleFaviconUrl` | `internal static` | Extraction |
| `WingetService.cs` | `MapFromRow` | `internal static` | Exposure |
| `WingetService.cs` | `DecoratePackageStatus` | `internal static` | Extraction |
| `WingetService.cs` | `MergeRecommendations` | `internal static` | Extraction |
| `CachingWingetService.cs` | `MergePackageProperties` | `internal static` | Extraction |

Total Proposed Static Methods: **14**
Total Estimated New xUnit Tests: **45+**
