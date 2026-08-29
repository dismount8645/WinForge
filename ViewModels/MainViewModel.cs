#pragma warning disable MVVMTK0045
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ViVeToolApp.ViewModels;

/// <summary>
/// MVVM ViewModel for MainWindow. Extracts filtering, scraping, execution, dialog state, logging, and catalog management
/// from the code-behind to satisfy B-01 separation (CommunityToolkit.Mvvm ObservableObject).
/// See https://learn.microsoft.com/en-us/windows/communitytoolkit/mvvm/introduction
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const string AllGroupsLabel = "All groups";
    private const string AllTracksLegacyLabel = "All Tracks";

    private readonly IFeatureScraper _scraper;
    private readonly IViVeToolRunner _runner;
    private readonly IFeatureFilterService _filterService;
    private readonly IViVeToolLocator _locator;
    private readonly IViVeToolDownloader _downloader;

    private readonly StringBuilder _log = new();
    private int _logLineCount = 0;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _batchCts;

    // Collections exposed for x:Bind / Binding
    public ObservableCollection<FeatureItem> Items { get; } = new();
    public ObservableCollection<FeatureItem> Visible { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new();

    // Log access for view
    public string LogText => _log.ToString();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedGroup = AllGroupsLabel;

    [ObservableProperty]
    private string _viveExe = string.Empty;

    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private bool _isDialogOpen = false;

    [ObservableProperty]
    private string _summaryText = "Loading...";

    [ObservableProperty]
    private double _selectionPercentage = 0;

    [ObservableProperty]
    private string _selectionProgressLabel = "0% selected (0 of 0 rows)";

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    [ObservableProperty]
    private string _statusBreakdownDetail = "No features selected";

    [ObservableProperty]
    private double _runProgress = 0;

    [ObservableProperty]
    private bool _whatIfEnabled = false;

    [ObservableProperty]
    private bool _restartExplorerEnabled = false;

    [ObservableProperty]
    private string _viveStatusMessage = "Checking...";

    [ObservableProperty]
    private bool _isViveFound = false;

    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    [ObservableProperty]
    private int _infoBarSeverity = 0;

    [ObservableProperty]
    private bool _isInfoBarOpen = false;

    [ObservableProperty]
    private string _successBreakdownText = "Success: 0";

    [ObservableProperty]
    private string _skippedBreakdownText = "Skipped: 0";

    [ObservableProperty]
    private string _errorBreakdownText = "Error: 0";

    [ObservableProperty]
    private string _pendingBreakdownText = "Pending: 0";

    [ObservableProperty]
    private string _notRunBreakdownText = "Not run: 0";

    private bool _hasError = false;
    public bool HasError
    {
        get => _hasError;
        set
        {
            if (_hasError != value)
            {
                _hasError = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isPaused = false;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused != value)
            {
                _isPaused = value;
                OnPropertyChanged();
            }
        }
    }

    // For debounce handling of checkbox vs row click (preserve MainWindow behavior)
    private long _lastCheckBoxClickTicks = 0;

    public MainViewModel() : this(
        new PureinfotechScraper(),
        new ViVeToolRunner(),
        new FeatureFilterService(),
        new ViVeToolLocator(),
        new ViVeToolDownloader())
    {
    }

    public MainViewModel(
        IFeatureScraper scraper,
        IViVeToolRunner runner,
        IFeatureFilterService filterService,
        IViVeToolLocator locator,
        IViVeToolDownloader downloader)
    {
        _scraper = scraper ?? throw new ArgumentNullException(nameof(scraper));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));

        AvailableGroups.Add(AllGroupsLabel);
        FindViveTool();
    }

    public long LastCheckBoxClickTicks
    {
        get => _lastCheckBoxClickTicks;
        set => _lastCheckBoxClickTicks = value;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedGroupChanged(string value)
    {
        ApplyFilters();
    }

    // ── ViVeTool location ──────────────────────────────
    public void FindViveTool()
    {
        ViveExe = _locator.LocateViVeTool() ?? string.Empty;
        UpdateViveStatus();
    }

    private void UpdateViveStatus()
    {
        bool found = !string.IsNullOrEmpty(ViveExe) && File.Exists(ViveExe);
        IsViveFound = found;
        if (found)
        {
            string companion = Path.Combine(Path.GetDirectoryName(ViveExe) ?? string.Empty, "Albacore.ViVe.dll");
            bool companionMissing = !File.Exists(companion) && File.Exists(Path.Combine(AppContext.BaseDirectory, "Albacore.ViVe.dll"));
            if (companionMissing)
            {
                ViveStatusMessage = $"Found vivetool.exe but Albacore.ViVe.dll missing next to it ({companion}). Reinstall via Download button.";
                return;
            }

            ViveStatusMessage = $"Found: {ViveExe}";
        }
        else
        {
            ViveStatusMessage = "vivetool.exe not found. Click Download.";
        }
    }

    // ── Catalog Loading ─────────────────────────────────
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadCatalogAsync().ConfigureAwait(true);
    }

    public async Task LoadCatalogAsync()
    {
        // Capture UI dispatcher for later collection updates (unpacked apps may not have ApplicationData, but dispatcher is available)
        var uiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        IsRunning = true;
        RunProgress = 0;
        try
        {
            // Fetch off UI thread is okay, but keep continuation on UI thread for ObservableCollection updates
            var fetched = await _scraper.FetchAndParseAsync(cancellationToken: _cts.Token).ConfigureAwait(true);
            Items.Clear();
            foreach (var item in fetched)
            {
                item.PropertyChanged += (_, _) => UpdateSummary();
                Items.Add(item);
            }
            RebuildGroupFilter();
            ApplyFilters();

            var uniqueIds = Items.SelectMany(i => i.IDs).Distinct().Count();
            LastUpdatedText = $"Fetched {DateTime.Now:HH:mm}  ·  {Items.Count} entries, {uniqueIds} unique IDs";
            Log($"[INFO] Loaded {uniqueIds} unique feature IDs from pureinfotech.com ({Items.Count} entries)");
            ShowInfoBar($"Loaded {uniqueIds} unique feature IDs from pureinfotech.com", 2);
        }
        catch (OperationCanceledException)
        {
            Log("[INFO] Catalog fetch cancelled.");
            ShowInfoBar("Catalog refresh cancelled.", 0);
        }
        catch (Exception ex)
        {
            Log($"[WARN] Live catalog fetch failed ({ex.GetType().Name}: {ex.Message}). Showing offline catalog.");
            ShowInfoBar($"Live fetch failed: {ex.Message}. Showing offline catalog.", 1);
            LoadOfflineFallback();
        }
        finally
        {
            IsRunning = false;
            ApplyFilters();
        }
    }

    public void LoadOfflineFallback()
    {
        try
        {
            var offline = _scraper.GetOfflineFallback();
            Items.Clear();
            foreach (var item in offline)
            {
                item.PropertyChanged += (_, _) => UpdateSummary();
                Items.Add(item);
            }
            RebuildGroupFilter();
            ApplyFilters();
            var uniqueIds = Items.SelectMany(i => i.IDs).Distinct().Count();
            LastUpdatedText = $"Offline catalog  ·  {Items.Count} entries, {uniqueIds} unique IDs";
            Log($"[INFO] Loaded offline catalog: {Items.Count} entries, {uniqueIds} unique IDs");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Offline catalog loading failed: {ex.Message}");
            ShowInfoBar($"Failed to load offline catalog: {ex.Message}", 3);
        }
    }

    // ── Filtering ───────────────────────────────────────
    public void RebuildGroupFilter()
    {
        var selected = SelectedGroup;
        AvailableGroups.Clear();
        AvailableGroups.Add(AllGroupsLabel);
        var groups = _filterService.GetDistinctGroups(Items);
        foreach (var g in groups)
        {
            AvailableGroups.Add(g);
        }

        if (AvailableGroups.Contains(selected))
        {
            SelectedGroup = selected;
        }
        else if (string.Equals(selected, AllTracksLegacyLabel, StringComparison.OrdinalIgnoreCase))
        {
            SelectedGroup = AllGroupsLabel;
        }
        else
        {
            SelectedGroup = AllGroupsLabel;
        }
    }

    public void ApplyFilters()
    {
        var search = SearchText?.Trim() ?? string.Empty;
        var rawGroup = SelectedGroup;
        var group = string.Equals(rawGroup, AllTracksLegacyLabel, StringComparison.OrdinalIgnoreCase) ? AllGroupsLabel : (rawGroup ?? AllGroupsLabel);
        var filtered = _filterService.Filter(Items, search, group);
        Visible.Clear();
        foreach (var item in filtered)
        {
            Visible.Add(item);
        }
        UpdateSummary();
    }

    public void UpdateSummary()
    {
        var summary = _filterService.CalculateSummary(Visible, Items);
        SummaryText = $"Checked {summary.SelectedCount} rows → {summary.UniqueSelectedIdsCount} IDs  ·  Visible {summary.VisibleCount} of {summary.TotalCount}";
        SelectionPercentage = summary.SelectionPercentage;
        SelectionProgressLabel = $"{summary.SelectionPercentage:F0}% selected ({summary.SelectedCount} of {summary.TotalCount} rows)";
        UpdateStatusBreakdown();
    }

    private void UpdateStatusBreakdown()
    {
        var sel = Items.Where(i => i.IsSelected).ToList();
        int cSuccess = sel.Count(i => i.LastStatus == FeatureRunStatus.Success);
        int cSkipped = sel.Count(i => i.LastStatus == FeatureRunStatus.Skipped);
        int cError = sel.Count(i => i.LastStatus == FeatureRunStatus.Error);
        int cPending = sel.Count(i => i.LastStatus == FeatureRunStatus.Pending || i.IsPending);
        int cNotRun = sel.Count(i => i.LastStatus == FeatureRunStatus.NotRun);

        SuccessBreakdownText = $"Success: {cSuccess}";
        SkippedBreakdownText = $"Skipped: {cSkipped}";
        ErrorBreakdownText = $"Error: {cError}";
        PendingBreakdownText = $"Pending: {cPending}";
        NotRunBreakdownText = $"Not run: {cNotRun}";
        HasError = cError > 0;
        IsPaused = cPending > 0 && !IsRunning;

        if (sel.Count == 0)
        {
            StatusBreakdownDetail = "No features selected";
        }
        else
        {
            StatusBreakdownDetail = $"{sel.Count} selected — {cSuccess} ok, {cSkipped} skipped, {cError} error, {cPending} pending, {cNotRun} not run";
        }
    }

    // ── Logging ─────────────────────────────────────────
    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _log.AppendLine(line);
        _logLineCount++;
        if (_logLineCount > 400)
        {
            // Efficient trim: remove oldest lines without Split/Join O(n^2)
            var text = _log.ToString();
            int excess = _logLineCount - 400;
            int pos = 0;
            for (int i = 0; i < excess; i++)
            {
                int idx = text.IndexOf('\n', pos);
                if (idx < 0) break;
                pos = idx + 1;
            }
            if (pos > 0)
            {
                _log.Remove(0, pos);
                _logLineCount = 400;
            }
        }
        OnPropertyChanged(nameof(LogText));
    }

    public void ShowInfoBar(string message, int severity)
    {
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarOpen = true;
    }

    // ── Selection Commands ──────────────────────────────
    [RelayCommand]
    public void SelectAll()
    {
        _filterService.SetSelection(Visible, true);
        UpdateSummary();
    }

    [RelayCommand]
    public void ClearAll()
    {
        _filterService.SetSelection(Visible, false);
        UpdateSummary();
    }

    [RelayCommand]
    public void SelectFiltered()
    {
        var g = SelectedGroup ?? AllGroupsLabel;
        if (string.Equals(g, AllGroupsLabel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(g, AllTracksLegacyLabel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _filterService.SetGroupSelection(Items, g, true);
        UpdateSummary();
    }

    [RelayCommand]
    public void ClearFiltered()
    {
        var g = SelectedGroup ?? AllGroupsLabel;
        if (string.Equals(g, AllGroupsLabel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(g, AllTracksLegacyLabel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _filterService.SetGroupSelection(Items, g, false);
        UpdateSummary();
    }

    public void SelectAllCheckBoxClicked(bool isChecked)
    {
        foreach (var item in Visible)
        {
            item.IsSelected = isChecked;
        }
        UpdateSummary();
    }

    public void FeatureCheckBoxClicked()
    {
        LastCheckBoxClickTicks = DateTime.UtcNow.Ticks;
        UpdateSummary();
    }

    public void FeatureListViewItemClick(FeatureItem? item)
    {
        if (item == null) return;
        var elapsedTicks = DateTime.UtcNow.Ticks - LastCheckBoxClickTicks;
        if (elapsedTicks < TimeSpan.TicksPerMillisecond * 350)
        {
            LastCheckBoxClickTicks = 0;
            return;
        }
        item.IsSelected = !item.IsSelected;
        UpdateSummary();
    }

    [RelayCommand]
    public void ClearLog()
    {
        _log.Clear();
        _logLineCount = 0;
        OnPropertyChanged(nameof(LogText));
    }

    [RelayCommand]
    public void Cancel()
    {
        try
        {
            _batchCts?.Cancel();
            Log("[INFO] Cancellation requested...");
        }
        catch (Exception ex)
        {
            Log($"[WARN] Cancel error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void CopyPath()
    {
        try
        {
            if (string.IsNullOrEmpty(ViveExe) || !File.Exists(ViveExe))
            {
                ShowInfoBar("No vivetool.exe path to copy.", 1);
                return;
            }
            var pkg = new DataPackage();
            pkg.SetText(ViveExe);
            Clipboard.SetContent(pkg);
            ShowInfoBar($"Copied: {ViveExe}", 2);
            Log($"[INFO] Copied path: {ViveExe}");
        }
        catch (Exception ex)
        {
            Log($"[WARN] Copy failed: {ex.Message}");
            ShowInfoBar($"Copy failed: {ex.Message}", 3);
        }
    }

    // ── ViVeTool Execution ──────────────────────────────
    [RelayCommand]
    public async Task EnableAsync()
    {
        await RunViVeAsync(ViVeExecutionMode.Enable).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task DisableAsync()
    {
        await RunViVeAsync(ViVeExecutionMode.Disable).ConfigureAwait(true);
    }

    public async Task RunViVeAsync(ViVeExecutionMode mode)
    {
        var selectedIds = _filterService.GetDistinctSelectedFeatureIds(Items);
        var checkedRows = Items.Count(i => i.IsSelected);

        if (selectedIds.Count == 0)
        {
            ShowInfoBar("No features checked. Select some features first.", 1);
            return;
        }

        if (string.IsNullOrEmpty(ViveExe) || !File.Exists(ViveExe))
        {
            ShowInfoBar("vivetool.exe not found. Use the Download button.", 3);
            return;
        }

        bool whatIf = WhatIfEnabled;
        var label = mode == ViVeExecutionMode.Enable ? "Enabling" : "Disabling";

        foreach (var f in Items.Where(i => i.IsSelected))
        {
            f.IsPending = true;
            f.LastStatus = FeatureRunStatus.Pending;
            f.LastMessage = whatIf ? "Preview queued" : "Queued...";
            f.LastExitCode = null;
        }
        UpdateStatusBreakdown();

        IsRunning = true;
        RunProgress = 0;

        Log($"=== {label} {selectedIds.Count} IDs in {checkedRows} features{(whatIf ? " [PREVIEW — no changes]" : "")} ===");
        if (whatIf)
        {
            ShowInfoBar("Preview mode is ON — no system changes will be made. Turn off Preview to apply.", 0);
            Log("[INFO] Preview mode — commands will be logged but not executed.");
        }

        var progress = new Progress<ViVeProgressReport>(report =>
        {
            RunProgress = report.Percentage;
            if (!string.IsNullOrEmpty(report.LogMessage))
            {
                Log(report.LogMessage);
            }

            if (report.LastResult != null)
            {
                var r = report.LastResult;
                FeatureRunStatus mapped = r.Status switch
                {
                    ViVeToolStatus.Success => FeatureRunStatus.Success,
                    ViVeToolStatus.UnsupportedOrNotFound => FeatureRunStatus.Skipped,
                    ViVeToolStatus.Warning => FeatureRunStatus.Error,
                    ViVeToolStatus.Error => FeatureRunStatus.Error,
                    _ => FeatureRunStatus.Error
                };
                string msg = !string.IsNullOrWhiteSpace(r.ErrorMessage) ? r.ErrorMessage : r.Output;
                if (string.IsNullOrWhiteSpace(msg))
                {
                    msg = r.Status.ToString();
                }
                int? exit = r.ExitCode;
                var now = DateTime.Now;
                foreach (var f in Items.Where(item => item.IDs != null && item.IDs.Contains(r.FeatureId)))
                {
                    f.LastStatus = mapped;
                    f.LastMessage = msg;
                    f.LastExitCode = exit;
                    f.LastRunTime = now;
                    f.IsPending = false;
                }
                UpdateStatusBreakdown();
            }
        });

        _batchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        try
        {
            var batchResult = await Task.Run(() => _runner.RunBatchAsync(
                ViveExe,
                selectedIds,
                mode,
                whatIf,
                progress,
                _batchCts.Token)).ConfigureAwait(true);

            if (batchResult.Results.Count > 0)
            {
                var map = batchResult.Results
                    .GroupBy(r => r.FeatureId)
                    .ToDictionary(g => g.Key, g => g.First());
                var nowFinal = DateTime.Now;
                foreach (var item in Items)
                {
                    if (item.IDs == null || item.IDs.Length == 0) continue;
                    var related = item.IDs.Where(id => map.ContainsKey(id)).Select(id => map[id]).ToList();
                    if (related.Count == 0) continue;

                    bool hasError = related.Any(r => r.Status == ViVeToolStatus.Error || r.Status == ViVeToolStatus.Warning);
                    bool hasSuccess = related.Any(r => r.Status == ViVeToolStatus.Success);
                    FeatureRunStatus worst;
                    if (hasError) worst = FeatureRunStatus.Error;
                    else if (hasSuccess) worst = FeatureRunStatus.Success;
                    else worst = FeatureRunStatus.Skipped;

                    string combinedMsg;
                    if (related.Count == 1)
                    {
                        var single = related[0];
                        combinedMsg = !string.IsNullOrWhiteSpace(single.ErrorMessage) ? single.ErrorMessage : single.Output;
                        if (string.IsNullOrWhiteSpace(combinedMsg))
                        {
                            combinedMsg = worst == FeatureRunStatus.Success ? "Success" : worst == FeatureRunStatus.Skipped ? "Skipped" : "Error";
                        }
                    }
                    else
                    {
                        int succ = related.Count(r => r.Status == ViVeToolStatus.Success);
                        int skip = related.Count(r => r.Status == ViVeToolStatus.UnsupportedOrNotFound);
                        int err = related.Count - succ - skip;
                        var parts = new List<string>();
                        if (succ > 0) parts.Add($"{succ} ok");
                        if (skip > 0) parts.Add($"{skip} skipped");
                        if (err > 0) parts.Add($"{err} error");
                        combinedMsg = string.Join(", ", parts);
                        var firstErr = related.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.ErrorMessage));
                        if (firstErr != null && hasError)
                        {
                            combinedMsg += $" — {firstErr.ErrorMessage}";
                        }
                        else
                        {
                            var firstOut = related.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Output));
                            if (firstOut != null) combinedMsg += $" — {firstOut.Output}";
                        }
                    }

                    int maxExit = related.Max(r => r.ExitCode);
                    item.LastStatus = worst;
                    item.LastMessage = combinedMsg;
                    item.LastExitCode = maxExit;
                    item.LastRunTime = nowFinal;
                    item.IsPending = false;
                }

                foreach (var still in Items.Where(i => i.IsPending).ToList())
                {
                    still.IsPending = false;
                    if (still.LastStatus == FeatureRunStatus.Pending)
                    {
                        still.LastStatus = FeatureRunStatus.NotRun;
                        still.LastMessage = "Cancelled";
                    }
                }
                UpdateStatusBreakdown();
            }

            RunProgress = 100;
            Log($"=== Done:  Success={batchResult.SuccessCount}  Skipped={batchResult.SkippedCount}  Errors={batchResult.ErrorCount} ===");
            var firstError = batchResult.Results!.FirstOrDefault(r => r.Status == ViVeToolStatus.Error);
            if (firstError != null && firstError.ErrorMessage != null && firstError.ErrorMessage.Contains("Albacore.ViVe", StringComparison.OrdinalIgnoreCase))
            {
                ShowInfoBar($"ViVeTool installation incomplete: Albacore.ViVe.dll missing. Expected next to {Path.GetFileName(ViveExe)}. Use Download button to reinstall. ({batchResult.ErrorCount} IDs aborted)", 3);
                FindViveTool();
            }
            else
            {
                ShowInfoBar(batchResult.FormattedSummary, batchResult.ErrorCount > 0 ? 1 : 2);
            }
        }
        catch (OperationCanceledException)
        {
            Log("=== Batch operation was cancelled ===");
            ShowInfoBar("Batch operation was cancelled.", 0);
            foreach (var still in Items.Where(i => i.IsPending).ToList())
            {
                still.IsPending = false;
                if (still.LastStatus == FeatureRunStatus.Pending)
                {
                    still.LastStatus = FeatureRunStatus.NotRun;
                    still.LastMessage = "Cancelled";
                }
            }
            UpdateStatusBreakdown();
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Batch execution failed: {ex.Message}");
            ShowInfoBar($"Batch execution failed: {ex.Message}", 3);
            foreach (var still in Items.Where(i => i.IsPending).ToList())
            {
                still.IsPending = false;
                if (still.LastStatus == FeatureRunStatus.Pending)
                {
                    still.LastStatus = FeatureRunStatus.Error;
                    still.LastMessage = ex.Message;
                }
            }
            UpdateStatusBreakdown();
        }
        finally
        {
            _batchCts?.Dispose();
            _batchCts = null;
            IsRunning = false;
        }
    }

    // ── Download ────────────────────────────────────────
    [RelayCommand]
    public async Task DownloadAsync()
    {
        await DownloadViVeToolAsync().ConfigureAwait(true);
    }

    public async Task DownloadViVeToolAsync()
    {
        IsRunning = true;
        Log("[INFO] Fetching latest ViVeTool release from GitHub...");
        ShowInfoBar("Downloading latest ViVeTool release from GitHub...", 0);

        try
        {
            var destDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var progress = new Progress<int>(pct => RunProgress = pct);
            var installedPath = await _downloader.DownloadAndExtractViVeToolAsync(destDir, progress, _cts.Token).ConfigureAwait(true);
            FindViveTool();
            Log($"[SUCCESS] ViVeTool installed to: {installedPath}");
            ShowInfoBar("ViVeTool downloaded and installed successfully.", 2);
        }
        catch (OperationCanceledException)
        {
            Log("[INFO] ViVeTool download was cancelled.");
            ShowInfoBar("ViVeTool download was cancelled.", 0);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Download failed: {ex.Message}");
            ShowInfoBar($"Download failed: {ex.Message}", 3);
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ── Cleanup ─────────────────────────────────────────
    public void Cleanup()
    {
        try { _cts.Cancel(); } catch { }
        try { _batchCts?.Cancel(); } catch { }
        try { _batchCts?.Dispose(); } catch { }
        _batchCts = null;
        try { _cts.Dispose(); } catch { }
    }
}
#pragma warning restore MVVMTK0045
