using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WingetStore.Services;

public class IconService
{
    private static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingetStore");
    private static readonly string CacheFile = Path.Combine(CacheDir, "screenshot-database-v2.json");
    private static readonly string IconsDir = Path.Combine(CacheDir, "icons");
    private const string DbUrl = "https://raw.githubusercontent.com/Devolutions/UniGetUI/main/WebBasedData/screenshot-database-v2.json";
    private Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _screenshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _downloadingIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resolvingIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitialized;
    public static IconService Instance { get; } = new();
    public event EventHandler? IconsUpdated;
    private IconService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public static string GetSafeIconFileName(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return "unknown.png";
        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] sanitized = packageId.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
        string name = new string(sanitized).Replace("..", "_");
        return $"{name}.png";
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        try { Directory.CreateDirectory(CacheDir); Directory.CreateDirectory(IconsDir); } catch (Exception ex) { Debug.WriteLine($"Failed to create icon cache dirs: {ex.Message}"); }
        try { if (File.Exists(CacheFile)) { await LoadDatabaseAsync(CacheFile); _isInitialized = true; } } catch (Exception ex) { Debug.WriteLine($"Failed to load icon cache: {ex.Message}"); }
        _ = Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(CacheFile) || IsCacheExpired(File.GetLastWriteTime(CacheFile), DateTime.Now, TimeSpan.FromHours(24))) { var data = await _httpClient.GetStringAsync(DbUrl); await File.WriteAllTextAsync(CacheFile, data); await LoadDatabaseAsync(CacheFile); _isInitialized = true; NotifyIconsUpdated(); }
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to download icon database: {ex.Message}"); }
        });
    }

    private void NotifyIconsUpdated()
    {
        App.Dispatch(() => IconsUpdated?.Invoke(this, EventArgs.Empty));
    }

    internal static (Dictionary<string, string> icons, Dictionary<string, List<string>> screenshots) ParseDatabaseJson(string json)
    {
        var newIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newScreenshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return (newIcons, newScreenshots);
        try
        {
            using var doc = JsonDocument.Parse(json);
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
        }
        catch
        {
            // Ignore malformed JSON
        }
        return (newIcons, newScreenshots);
    }

    internal static bool IsCacheExpired(DateTime lastWriteTime, DateTime currentTime, TimeSpan maxAge)
    {
        if (lastWriteTime > currentTime) return true;
        return (currentTime - lastWriteTime) > maxAge;
    }

    internal static string ExtractHomepageFromShowOutput(string showOutput)
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

    internal static string ExtractDomainFromUrl(string url)
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

    internal static string GetHunterLogoUrl(string domain) => string.IsNullOrEmpty(domain) ? "" : $"https://logos.hunter.io/{domain}";
    internal static string GetGoogleFaviconUrl(string domain, int size = 128) => string.IsNullOrEmpty(domain) ? "" : $"https://www.google.com/s2/favicons?domain={domain}&sz={size}";

    private async Task LoadDatabaseAsync(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);
        string json = await reader.ReadToEndAsync();
        var (newIcons, newScreenshots) = ParseDatabaseJson(json);
        lock (_icons) _icons = newIcons;
        lock (_screenshots) _screenshots = newScreenshots;
    }

    public string GetIconUrl(string packageId, string packageName)
    {
        if (string.IsNullOrEmpty(packageId)) return "";
        lock (_failedIds) { if (_failedIds.Contains(packageId)) return ""; }
        string localFilePath = Path.Combine(IconsDir, GetSafeIconFileName(packageId));
        if (File.Exists(localFilePath)) return new Uri(localFilePath).AbsoluteUri;
        string? remoteUrl = ResolveRemoteUrl(packageId, packageName);
        if (!string.IsNullOrEmpty(remoteUrl)) _ = DownloadIconAsync(packageId, remoteUrl);
        else _ = ResolveIconOnlineAsync(packageId);
        return "";
    }
    public List<string> GetScreenshots(string packageId, string packageName)
    {
        string pid = packageId ?? "";
        string pname = packageName ?? "";
        if (string.IsNullOrEmpty(pid) && string.IsNullOrEmpty(pname)) return [];
        lock (_screenshots) { if (!string.IsNullOrEmpty(pid) && _screenshots.TryGetValue(pid, out var cached)) return cached; if (!string.IsNullOrEmpty(pname) && _screenshots.TryGetValue(pname, out var cachedName)) return cachedName; }
        return [];
    }
    private string? ResolveRemoteUrl(string packageId, string packageName)
    {
        lock (_icons) { if (_icons.TryGetValue(packageId, out var url)) return url; if (_icons.TryGetValue(packageName, out var urlName)) return urlName; }
        return null;
    }

    private readonly SemaphoreSlim _downloadSemaphore = new(3, 3);
    private async Task DownloadIconAsync(string packageId, string remoteUrl)
    {
        lock (_downloadingIds) { if (!_downloadingIds.Add(packageId)) return; }
        await _downloadSemaphore.WaitAsync();
        try
        {
            string localFilePath = Path.Combine(IconsDir, GetSafeIconFileName(packageId));
            using var response = await _httpClient.GetAsync(remoteUrl, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(localFilePath);
                await stream.CopyToAsync(fileStream);
                NotifyIconsUpdated();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DownloadIconAsync failed for {packageId}: {ex.Message}");
            lock (_failedIds) _failedIds.Add(packageId);
        }
        finally
        {
            lock (_downloadingIds) _downloadingIds.Remove(packageId);
            _downloadSemaphore.Release();
        }
    }

    private readonly SemaphoreSlim _resolveSemaphore = new(2, 2);
    private async Task ResolveIconOnlineAsync(string packageId)
    {
        if (packageId.StartsWith("Dummy.", StringComparison.OrdinalIgnoreCase)) return;
        lock (_resolvingIds) { if (!_resolvingIds.Add(packageId)) return; }
        await _resolveSemaphore.WaitAsync();
        try
        {
            string safePackageId = WingetService.EscapeArgument(packageId);
            string showOutput = await App.Winget.RunCommandAsync($"show {safePackageId} --accept-source-agreements");
            string homepage = ExtractHomepageFromShowOutput(showOutput);
            string domain = ExtractDomainFromUrl(homepage);
            if (!string.IsNullOrEmpty(domain))
            {
                string localFilePath = Path.Combine(IconsDir, GetSafeIconFileName(packageId));
                string logoUrl = GetHunterLogoUrl(domain);
                var request = new HttpRequestMessage(HttpMethod.Head, logoUrl);
                using var checkResponse = await _httpClient.SendAsync(request);
                if (checkResponse.IsSuccessStatusCode)
                {
                    using var response = await _httpClient.GetAsync(logoUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode) { using var stream = await response.Content.ReadAsStreamAsync(); using var fileStream = File.Create(localFilePath); await stream.CopyToAsync(fileStream); NotifyIconsUpdated(); }
                }
                else
                {
                    string favUrl = GetGoogleFaviconUrl(domain);
                    using var response = await _httpClient.GetAsync(favUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode) { using var stream = await response.Content.ReadAsStreamAsync(); using var fileStream = File.Create(localFilePath); await stream.CopyToAsync(fileStream); NotifyIconsUpdated(); }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"ResolveIconOnlineAsync failed for {packageId}: {ex.Message}"); }
        finally
        {
            lock (_resolvingIds) _resolvingIds.Remove(packageId);
            _resolveSemaphore.Release();
        }
    }

    internal static string NormalizePackageName(string packageName)
    {
        if (string.IsNullOrEmpty(packageName)) return "";
        string normalized = packageName.Replace("Microsoft.", "", StringComparison.OrdinalIgnoreCase).Replace(".", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Trim();
        int idx = normalized.IndexOf("for", StringComparison.OrdinalIgnoreCase); if (idx > 0) normalized = normalized[..idx].Trim();
        return normalized.Length > 2 ? normalized : packageName;
    }
}
