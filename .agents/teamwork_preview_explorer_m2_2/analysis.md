# Analysis Report: Services & Caching Logic Extraction (Milestone 2 - Explorer 2)

## Executive Summary
This report analyzes `WingetStore/Services/CachingWingetService.cs`, `WingetStore/Services/IconService.cs`, `WingetStore/Services/SettingsService.cs`, `WingetStore/Services/LogService.cs`, and `WingetStore/Services/WingetService.cs` to identify non-UI, testable pure logic suitable for static method extraction.

Extracting these pure functions into `public static` or `internal static` methods on their respective parent classes preserves all existing service contracts while enabling direct, fast, and comprehensive unit testing without dependencies on disk I/O, process execution, network calls, or reflection.

---

## Existing Test Coverage Assessment (`WingetStore.Tests/Tests.cs`)
An audit of `WingetStore.Tests/Tests.cs` (~4,127 lines) revealed the following coverage status for caching and service logic:

1. **`CachingWingetService`**:
   - Tested via `WingetServiceTests.GetOrCreatePackage_CoreInstanceMerging`, `GetOrCreatePackage_ScreenshotsMerging`, and `GetOrCreatePackage_EmptyMerging`.
   - **Gap**: Property update/merging logic is inlined in `GetOrCreatePackage`. Edge-case property combinations (e.g. null arguments, empty string overrides vs non-empty updates, status transitions) cannot be tested directly without instantiating the full service hierarchy (`App.Winget`).

2. **`IconService`**:
   - `IconService.NormalizePackageName` (made `internal static`) and `GetSafeIconFileName` (made `public static`) are currently tested.
   - **Gap**: JSON database parsing (`icons_and_screenshots` node), cache expiration calculation (24-hour window), `winget show` homepage output extraction, and URL host/domain extraction are private and embedded inside async I/O or network methods (`InitializeAsync`, `LoadDatabaseAsync`, `ResolveIconOnlineAsync`). Zero direct unit tests exist for these payload parsing and cache calculation rules.

3. **`SettingsService`**:
   - Tested via file system side-effects (`SettingsService_CorruptFileLoadException`, `SettingsService_EdgeCases_Coverage`) using Reflection to invoke private static methods `LoadSettings` and `SaveSettings`.
   - **Gap**: JSON serialization/deserialization logic is tied directly to disk paths. Extracting pure serialization functions will eliminate test reliance on reflection and disk manipulation.

4. **`LogService`**:
   - Tested via file system log file inspection (`LogService_LogsCorrectly`).
   - **Gap**: Formatting of log entry timestamps and severity levels is inlined with file writing.

5. **`WingetService`**:
   - `EscapeArgument` and parser helpers (`WingetParser`) are well-tested.
   - **Gap**: Recommendation decoration (`BuildRecommendations`), which matches popular packages with installed packages and maps statuses, is embedded in async methods relying on asset files and CLI processes.

---

## Detailed Static Method Extraction Proposals

### Proposal 1: Package Property Merging (`CachingWingetService.cs`)

- **Original Location**: `WingetStore/Services/CachingWingetService.cs`, Lines 18–21
- **Proposed Signature**:
  ```csharp
  public static void MergePackageProperties(WingetPackage existing, WingetPackage incoming)
  ```
- **Input Specifications**:
  - `existing`: `WingetPackage` — target package instance already stored in cache.
  - `incoming`: `WingetPackage` — incoming package instance containing updated metadata.
- **Output Specifications**:
  - `void` (modifies `existing` package properties in-place).
- **Extracted Logic Rationale**:
  Currently, `GetOrCreatePackage` contains 18 consecutive `if` statements updating fields (`Name`, `Version`, `AvailableVersion`, `Source`, `Publisher`, `Status`, `Description`, `Homepage`, `License`, `ReleaseNotes`, `PublisherUrl`, `InstallerType`, `InstallerUrl`, `Tags`, `Details`, `Screenshots`).
  Extracting this into `MergePackageProperties` isolates the cache model state synchronization rules into a pure method.
- **Original Code Snippet**:
  ```csharp
  existing.Name = incoming.Name; if (!string.IsNullOrEmpty(incoming.Version)) existing.Version = incoming.Version; if (!string.IsNullOrEmpty(incoming.AvailableVersion)) existing.AvailableVersion = incoming.AvailableVersion; if (!string.IsNullOrEmpty(incoming.Source)) existing.Source = incoming.Source; if (!string.IsNullOrEmpty(incoming.Publisher)) existing.Publisher = incoming.Publisher; if (incoming.Status != PackageStatus.Installable) existing.Status = incoming.Status; if (!string.IsNullOrEmpty(incoming.Description)) existing.Description = incoming.Description; if (!string.IsNullOrEmpty(incoming.Homepage)) existing.Homepage = incoming.Homepage; if (!string.IsNullOrEmpty(incoming.License)) existing.License = incoming.License; if (!string.IsNullOrEmpty(incoming.ReleaseNotes)) existing.ReleaseNotes = incoming.ReleaseNotes; if (!string.IsNullOrEmpty(incoming.PublisherUrl)) existing.PublisherUrl = incoming.PublisherUrl; if (!string.IsNullOrEmpty(incoming.InstallerType)) existing.InstallerType = incoming.InstallerType; if (!string.IsNullOrEmpty(incoming.InstallerUrl)) existing.InstallerUrl = incoming.InstallerUrl; if (incoming.Tags != null && incoming.Tags.Count > 0) existing.Tags = incoming.Tags; if (incoming.Details != null && incoming.Details.Count > 0) existing.Details = incoming.Details; if (incoming.Screenshots.Count > 0) existing.Screenshots = incoming.Screenshots;
  ```
- **Proposed Refactored Code**:
  ```csharp
  public static void MergePackageProperties(WingetPackage existing, WingetPackage incoming)
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
      if (incoming.Screenshots.Count > 0) existing.Screenshots = incoming.Screenshots;
  }
  ```
- **xUnit Test Specifications**:
  1. `MergePackageProperties_NullArguments_ThrowsArgumentNullException`: Validates null checks for target or source.
  2. `MergePackageProperties_OverwritesScalarPropertiesWhenNonEmpty`: Verifies `Version`, `Publisher`, `Description`, etc., update `existing`.
  3. `MergePackageProperties_PreservesExistingWhenIncomingEmpty`: Verifies empty/null incoming strings do not overwrite existing valid strings.
  4. `MergePackageProperties_StatusTransitions`: Verifies `Status` updates when incoming status is `Installed` or `Upgradable`, but retains `existing.Status` when incoming is `Installable`.
  5. `MergePackageProperties_ListCollections`: Verifies `Tags`, `Details`, and `Screenshots` collections are copied only when incoming lists have items.

---

### Proposal 2: Icon Database JSON Payload Parsing & Validation (`IconService.cs`)

- **Original Location**: `WingetStore/Services/IconService.cs`, Lines 60–73
- **Proposed Signature**:
  ```csharp
  public static (Dictionary<string, string> Icons, Dictionary<string, List<string>> Screenshots) ParseDatabaseJson(string json)
  ```
- **Input Specifications**:
  - `json`: `string` — raw JSON string representing the icon/screenshot database.
- **Output Specifications**:
  - `(Dictionary<string, string> Icons, Dictionary<string, List<string>> Screenshots)` — parsed, case-insensitive dictionaries mapping package IDs to icon URLs and screenshot URL lists.
- **Extracted Logic Rationale**:
  The JSON payload structure sent from GitHub contains package IDs as key names, each having `icon` (string) and `images` (string array) properties. Extracting string parsing from disk I/O allows full payload validation testing.
- **Original Code Snippet**:
  ```csharp
  if (doc.RootElement.TryGetProperty("icons_and_screenshots", out var iconsNode))
  {
      var newIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var newScreenshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
      foreach (var prop in iconsNode.EnumerateObject())
      {
          if (prop.Value.TryGetProperty("icon", out var iconProp) && iconProp.ValueKind == JsonValueKind.String) { string iconUrl = iconProp.GetString() ?? ""; if (!string.IsNullOrEmpty(iconUrl)) newIcons[prop.Name] = iconUrl; }
          if (prop.Value.TryGetProperty("images", out var imagesProp) && imagesProp.ValueKind == JsonValueKind.Array) { var list = new List<string>(); foreach (var item in imagesProp.EnumerateArray()) { if (item.ValueKind == JsonValueKind.String) { string imgUrl = item.GetString() ?? ""; if (!string.IsNullOrEmpty(imgUrl)) list.Add(imgUrl); } } if (list.Count > 0) newScreenshots[prop.Name] = list; }
      }
      lock (_icons) _icons = newIcons;
      lock (_screenshots) _screenshots = newScreenshots;
  }
  ```
- **Proposed Refactored Code**:
  ```csharp
  public static (Dictionary<string, string> Icons, Dictionary<string, List<string>> Screenshots) ParseDatabaseJson(string json)
  {
      var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var screenshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

      if (string.IsNullOrWhiteSpace(json)) return (icons, screenshots);

      try
      {
          using var doc = JsonDocument.Parse(json);
          if (doc.RootElement.TryGetProperty("icons_and_screenshots", out var iconsNode) && iconsNode.ValueKind == JsonValueKind.Object)
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
      catch
      {
          // Ignore malformed JSON and return empty dictionaries
      }

      return (icons, screenshots);
  }
  ```
- **xUnit Test Specifications**:
  1. `ParseDatabaseJson_ValidPayload_ReturnsParsedDictionaries`: Validates parsing of icons and images lists.
  2. `ParseDatabaseJson_MissingIconsKey_ReturnsEmptyDictionaries`: Tests JSON missing `"icons_and_screenshots"`.
  3. `ParseDatabaseJson_FiltersEmptyOrNullImageStrings`: Ensures empty image URLs in arrays are skipped.
  4. `ParseDatabaseJson_MalformedJson_ReturnsEmptyDictionariesWithoutThrowing`: Ensures bad JSON does not throw exceptions.
  5. `ParseDatabaseJson_CaseInsensitiveKeys`: Verifies dictionary lookup is case-insensitive.

---

### Proposal 3: Cache Expiration & URL Domain Extraction (`IconService.cs`)

- **Original Location**: `WingetStore/Services/IconService.cs`, Lines 46 & 140–144
- **Proposed Signatures**:
  ```csharp
  public static bool IsCacheExpired(DateTime lastWriteTime, DateTime currentTime, TimeSpan maxAge)
  public static string ExtractHomepageFromShowOutput(string showOutput)
  public static string ExtractDomainFromUrl(string url)
  ```
- **Input / Output Specifications**:
  - `IsCacheExpired`: takes `lastWriteTime`, `currentTime`, and `maxAge`; returns `bool`.
  - `ExtractHomepageFromShowOutput`: takes `showOutput` string from `winget show`; returns extracted homepage URL or `""`.
  - `ExtractDomainFromUrl`: takes homepage URL; returns normalized domain host (removing `www.`) or `""`.
- **Extracted Logic Rationale**:
  Cache invalidation threshold (24 hours) and online logo domain resolution (parsing `Homepage:` from `winget show` stdout and extracting host domains) are non-UI rules currently inlined inside async network methods.
- **Proposed Refactored Code**:
  ```csharp
  public static bool IsCacheExpired(DateTime lastWriteTime, DateTime currentTime, TimeSpan maxAge)
  {
      if (lastWriteTime > currentTime) return true; // Invalid future timestamp
      return (currentTime - lastWriteTime) > maxAge;
  }

  public static string ExtractHomepageFromShowOutput(string showOutput)
  {
      if (string.IsNullOrWhiteSpace(showOutput)) return "";
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

  public static string ExtractDomainFromUrl(string url)
  {
      if (string.IsNullOrWhiteSpace(url)) return "";
      if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
      {
          string domain = uri.Host;
          if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
              domain = domain[4..];
          return domain;
      }
      return "";
  }
  ```
- **xUnit Test Specifications**:
  1. `IsCacheExpired_WithinThreshold_ReturnsFalse`: Test 23 hours difference vs 24 hour max age.
  2. `IsCacheExpired_ExceedsThreshold_ReturnsTrue`: Test 25 hours difference vs 24 hour max age.
  3. `ExtractHomepageFromShowOutput_ValidOutput_ReturnsHomepageUrl`: Test standard `winget show` stdout line `Homepage: https://example.com`.
  4. `ExtractHomepageFromShowOutput_NoHomepage_ReturnsEmptyString`: Test stdout without homepage field.
  5. `ExtractDomainFromUrl_StripsWwwPrefix`: Test `"https://www.github.com/git/git"` returns `"github.com"`.
  6. `ExtractDomainFromUrl_InvalidUrl_ReturnsEmptyString`: Test bad URL strings.

---

### Proposal 4: Settings Serialization & Deserialization (`SettingsService.cs`)

- **Original Location**: `WingetStore/Services/SettingsService.cs`, Lines 25 & 31
- **Proposed Signatures**:
  ```csharp
  public static AppSettings DeserializeSettings(string? json)
  public static string SerializeSettings(AppSettings settings)
  ```
- **Input / Output Specifications**:
  - `DeserializeSettings`: takes `json` string; returns deserialized `AppSettings` (or default `AppSettings` if `json` is null/invalid).
  - `SerializeSettings`: takes `AppSettings`; returns JSON string representation.
- **Extracted Logic Rationale**:
  Decouples JSON payload parsing from `File.ReadAllText` and `File.WriteAllText`, replacing reflection-based test execution with direct static unit calls.
- **Proposed Refactored Code**:
  ```csharp
  public static AppSettings DeserializeSettings(string? json)
  {
      if (string.IsNullOrWhiteSpace(json)) return new AppSettings { AutoUpdate = false };
      try
      {
          var loaded = JsonSerializer.Deserialize<AppSettings>(json);
          return loaded ?? new AppSettings { AutoUpdate = false };
      }
      catch
      {
          return new AppSettings { AutoUpdate = false };
      }
  }

  public static string SerializeSettings(AppSettings settings)
  {
      ArgumentNullException.ThrowIfNull(settings);
      return JsonSerializer.Serialize(settings);
  }
  ```
- **xUnit Test Specifications**:
  1. `DeserializeSettings_ValidJson_ReturnsPopulatedAppSettings`: Verifies deserialization of settings flags.
  2. `DeserializeSettings_NullOrCorruptJson_ReturnsDefaultSettings`: Verifies fallback for null, empty, or malformed JSON.
  3. `SerializeSettings_ValidInstance_ProducesValidJson`: Verifies output JSON contains expected properties.

---

### Proposal 5: Log Entry Formatting (`LogService.cs`)

- **Original Location**: `WingetStore/Services/LogService.cs`, Line 14
- **Proposed Signature**:
  ```csharp
  public static string FormatLogEntry(string level, string message, DateTime timestamp)
  ```
- **Input / Output Specifications**:
  - `level`: `string` ("INFO", "ERROR")
  - `message`: `string`
  - `timestamp`: `DateTime`
  - Returns `string` formatted log line (`"[2026-07-23 18:18:05] [INFO] Message"`).
- **Proposed Refactored Code**:
  ```csharp
  public static string FormatLogEntry(string level, string message, DateTime timestamp)
  {
      return $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
  }
  ```
- **xUnit Test Specifications**:
  1. `FormatLogEntry_ValidInputs_FormatsCorrectly`: Verifies timestamp format, level, and message concatenation.

---

### Proposal 6: Recommendation Decoration & Status Mapping (`WingetService.cs`)

- **Original Location**: `WingetStore/Services/WingetService.cs`, Lines 109–133
- **Proposed Signature**:
  ```csharp
  public static List<WingetPackage> BuildRecommendations(IEnumerable<WingetPackage>? popularPackages, IDictionary<string, WingetPackage>? installedMap, int maxCount = 10)
  ```
- **Input / Output Specifications**:
  - `popularPackages`: List of popular packages from asset store.
  - `installedMap`: Dictionary of currently installed packages keyed by package ID.
  - `maxCount`: Maximum number of recommendation items to return (default 10).
  - Output: `List<WingetPackage>` with updated `Status` (`Installed` vs `Installable`) and version details.
- **Proposed Refactored Code**:
  ```csharp
  public static List<WingetPackage> BuildRecommendations(IEnumerable<WingetPackage>? popularPackages, IDictionary<string, WingetPackage>? installedMap, int maxCount = 10)
  {
      if (popularPackages == null) return [];
      installedMap ??= new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase);

      var result = new List<WingetPackage>();
      foreach (var p in popularPackages.Take(maxCount))
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
- **xUnit Test Specifications**:
  1. `BuildRecommendations_MatchingInstalled_SetsStatusInstalled`: Tests installed package matching and version copy.
  2. `BuildRecommendations_NotInstalled_SetsStatusInstallable`: Tests non-installed package status.
  3. `BuildRecommendations_RespectsMaxCount`: Tests list trimming to `maxCount`.
  4. `BuildRecommendations_NullInputs_ReturnsEmptyList`: Tests graceful handling of null lists.

---

## Summary Table of Extraction Proposals

| # | File Path | Extracted Static Method Signature | Target Original Lines | New xUnit Tests Count |
|---|-----------|----------------------------------|----------------------|----------------------|
| 1 | `CachingWingetService.cs` | `public static void MergePackageProperties(WingetPackage existing, WingetPackage incoming)` | L18–21 | 5 |
| 2 | `IconService.cs` | `public static (Dictionary<string, string> Icons, Dictionary<string, List<string>> Screenshots) ParseDatabaseJson(string json)` | L60–73 | 5 |
| 3a | `IconService.cs` | `public static bool IsCacheExpired(DateTime lastWriteTime, DateTime currentTime, TimeSpan maxAge)` | L46 | 3 |
| 3b | `IconService.cs` | `public static string ExtractHomepageFromShowOutput(string showOutput)` | L140 | 2 |
| 3c | `IconService.cs` | `public static string ExtractDomainFromUrl(string url)` | L141–144 | 3 |
| 4a | `SettingsService.cs` | `public static AppSettings DeserializeSettings(string? json)` | L25 | 3 |
| 4b | `SettingsService.cs` | `public static string SerializeSettings(AppSettings settings)` | L31 | 2 |
| 5 | `LogService.cs` | `public static string FormatLogEntry(string level, string message, DateTime timestamp)` | L14 | 1 |
| 6 | `WingetService.cs` | `public static List<WingetPackage> BuildRecommendations(IEnumerable<WingetPackage>? popular, IDictionary<string, WingetPackage>? installedMap, int maxCount = 10)` | L109–133 | 4 |

**Total Proposed Static Methods**: 9
**Total Proposed New Unit Tests**: 28
