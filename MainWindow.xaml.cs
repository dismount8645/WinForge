using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using ViVeToolApp.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ViVeToolApp;

/// <summary>
/// Thin view for MainWindow. Delegates catalog, filtering, execution and state to MainViewModel (MVVM).
/// Keeps only XAML wiring, XamlRoot dialogs, and UI synchronization.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string AllGroupsLabel = "All groups";
    private const string AllTracksLegacyLabel = "All Tracks";

    public MainViewModel ViewModel { get; }

    private long _lastCheckBoxClickTicks = 0;
    private bool _isDialogOpen = false;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchDebounceTimer;

    public MainWindow() : this(
        new PureinfotechScraper(),
        new ViVeToolRunner(),
        new FeatureFilterService(),
        new ViVeToolLocator(),
        new ViVeToolDownloader())
    {
    }

    public MainWindow(
        IFeatureScraper scraper,
        IViVeToolRunner runner,
        IFeatureFilterService filterService,
        IViVeToolLocator locator,
        IViVeToolDownloader downloader)
    {
        ViewModel = new MainViewModel(scraper, runner, filterService, locator, downloader);

        this.InitializeComponent();
        this.ExtendsContentIntoTitleBar = true;
        this.Title = "ViVeTool Feature Enabler";

        // MVVM: DataContext = ViewModel for x:Bind / Binding scenarios (thin view pattern)
        // Window itself has no DataContext, but expose ViewModel property; set FrameworkElement DataContext if available
        if (Content is FrameworkElement fe)
        {
            fe.DataContext = ViewModel;
        }

        // Search debounce 150ms
        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
        _searchDebounceTimer.Tick += (s, e) =>
        {
            _searchDebounceTimer.Stop();
            ViewModel.SearchText = SearchBox.Text?.Trim() ?? string.Empty;
        };

        if (!MicaController.IsSupported())
        {
            this.SystemBackdrop = null;
        }

        var appWindow = this.AppWindow;
        if (appWindow != null)
        {
            appWindow.Title = "ViVeTool Feature Enabler";
            appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 840));
            try
            {
                var presenter = appWindow.Presenter as OverlappedPresenter;
                if (presenter != null)
                {
                    presenter.PreferredMinimumWidth = 720;
                    presenter.PreferredMinimumHeight = 600;
                    presenter.IsResizable = true;
                }
            }
            catch { }
            try
            {
                var titleBar = appWindow.TitleBar;
                if (titleBar != null)
                {
                    titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                    titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                }
            }
            catch { }
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }

        this.Closed += MainWindow_Closed;

        // Bind ViewModel collections to view
        FeatureListView.ItemsSource = ViewModel.Visible;
        // Sync ViewModel -> View
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.AvailableGroups.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(SyncGroupFilter);

        SyncAllFromViewModel();
        FindViveTool();
        UpdateActionButtonText();

        // First-run TeachingTip — ApplicationData.Current throws InvalidOperationException
        // in unpackaged (WindowsPackageType=None) without package identity. Wrap safely.
        bool shouldShowTip = false;
        Windows.Storage.ApplicationDataContainer? tipSettings = null;
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            tipSettings = localSettings;
            if (localSettings.Values["FirstRunTipShown"] == null)
            {
                shouldShowTip = true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.IO.FileNotFoundException || ex is System.Runtime.InteropServices.COMException)
        {
            // Unpackaged or RDP/Win10 fallback where ApplicationData.Current is unavailable.
            // Skip tip persistence; just load catalog.
            System.Diagnostics.Debug.WriteLine($"ApplicationData.Current unavailable (unpackaged): {ex.Message}");
        }

        if (shouldShowTip && tipSettings != null)
        {
            FirstRunTip.Closed += (s, e) =>
            {
                try { tipSettings.Values["FirstRunTipShown"] = true; } catch { }
            };
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ViewModel.LoadCatalogAsync().ConfigureAwait(false);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { try { FirstRunTip.IsOpen = true; } catch { } });
            });
        }
        else
        {
            DispatcherQueue.TryEnqueue(async () => await ViewModel.LoadCatalogAsync().ConfigureAwait(false));
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ViewModel_PropertyChanged(sender, e));
            return;
        }
        switch (e.PropertyName)
        {
            case nameof(ViewModel.SummaryText):
                SummaryText.Text = ViewModel.SummaryText;
                break;
            case nameof(ViewModel.SelectionPercentage):
                SelectionProgress.Value = ViewModel.SelectionPercentage;
                break;
            case nameof(ViewModel.SelectionProgressLabel):
                SelectionProgressLabel.Text = ViewModel.SelectionProgressLabel;
                break;
            case nameof(ViewModel.LastUpdatedText):
                LastUpdatedText.Text = ViewModel.LastUpdatedText;
                break;
            case nameof(ViewModel.StatusBreakdownDetail):
                StatusBreakdownDetail.Text = ViewModel.StatusBreakdownDetail;
                break;
            case nameof(ViewModel.SuccessBreakdownText):
                SuccessBreakdownText.Text = ViewModel.SuccessBreakdownText;
                break;
            case nameof(ViewModel.SkippedBreakdownText):
                SkippedBreakdownText.Text = ViewModel.SkippedBreakdownText;
                break;
            case nameof(ViewModel.ErrorBreakdownText):
                ErrorBreakdownText.Text = ViewModel.ErrorBreakdownText;
                break;
            case nameof(ViewModel.PendingBreakdownText):
                PendingBreakdownText.Text = ViewModel.PendingBreakdownText;
                break;
            case nameof(ViewModel.NotRunBreakdownText):
                NotRunBreakdownText.Text = ViewModel.NotRunBreakdownText;
                break;
            case nameof(ViewModel.RunProgress):
                RunProgress.Value = ViewModel.RunProgress;
                break;
            case nameof(ViewModel.HasError):
                RunProgress.ShowError = ViewModel.HasError;
                SelectionProgress.ShowError = ViewModel.HasError;
                break;
            case nameof(ViewModel.IsPaused):
                RunProgress.ShowPaused = ViewModel.IsPaused;
                SelectionProgress.ShowPaused = ViewModel.IsPaused;
                break;
            case nameof(ViewModel.LogText):
                LogText.Text = ViewModel.LogText;
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null));
                break;
            case nameof(ViewModel.InfoBarMessage):
            case nameof(ViewModel.InfoBarSeverity):
            case nameof(ViewModel.IsInfoBarOpen):
                MainInfoBar.Message = ViewModel.InfoBarMessage;
                MainInfoBar.Severity = (InfoBarSeverity)ViewModel.InfoBarSeverity;
                MainInfoBar.IsOpen = ViewModel.IsInfoBarOpen;
                break;
            case nameof(ViewModel.ViveStatusMessage):
            case nameof(ViewModel.IsViveFound):
            case nameof(ViewModel.ViveExe):
                UpdateViveStatus();
                break;
            case nameof(ViewModel.IsRunning):
                SetRunningState(ViewModel.IsRunning);
                break;
            case nameof(ViewModel.SelectedGroup):
                if (GroupFilter.SelectedItem as string != ViewModel.SelectedGroup)
                {
                    GroupFilter.SelectedItem = ViewModel.SelectedGroup;
                }
                break;
        }
        // Keep summary-related enables in sync
        if (e.PropertyName == nameof(ViewModel.SummaryText) || e.PropertyName == nameof(ViewModel.IsRunning) || e.PropertyName == nameof(ViewModel.SelectedGroup))
        {
            UpdateSummaryEnables();
        }
    }

    private void SyncAllFromViewModel()
    {
        SummaryText.Text = ViewModel.SummaryText;
        SelectionProgress.Value = ViewModel.SelectionPercentage;
        SelectionProgressLabel.Text = ViewModel.SelectionProgressLabel;
        LastUpdatedText.Text = ViewModel.LastUpdatedText;
        StatusBreakdownDetail.Text = ViewModel.StatusBreakdownDetail;
        SuccessBreakdownText.Text = ViewModel.SuccessBreakdownText;
        SkippedBreakdownText.Text = ViewModel.SkippedBreakdownText;
        ErrorBreakdownText.Text = ViewModel.ErrorBreakdownText;
        PendingBreakdownText.Text = ViewModel.PendingBreakdownText;
        NotRunBreakdownText.Text = ViewModel.NotRunBreakdownText;
        RunProgress.Value = ViewModel.RunProgress;
        RunProgress.ShowError = ViewModel.HasError;
        RunProgress.ShowPaused = ViewModel.IsPaused;
        SelectionProgress.ShowError = ViewModel.HasError;
        SelectionProgress.ShowPaused = ViewModel.IsPaused;
        LogText.Text = ViewModel.LogText;
        MainInfoBar.Message = ViewModel.InfoBarMessage;
        MainInfoBar.Severity = (InfoBarSeverity)ViewModel.InfoBarSeverity;
        MainInfoBar.IsOpen = ViewModel.IsInfoBarOpen;
        SyncGroupFilter();
        UpdateViveStatus();
        SetRunningState(ViewModel.IsRunning);
    }

    private void SyncGroupFilter()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(SyncGroupFilter);
            return;
        }
        var selected = GroupFilter.SelectedItem as string;
        GroupFilter.Items.Clear();
        foreach (var g in ViewModel.AvailableGroups)
        {
            GroupFilter.Items.Add(g);
        }
        if (selected != null && GroupFilter.Items.Contains(selected))
        {
            GroupFilter.SelectedItem = selected;
        }
        else
        {
            GroupFilter.SelectedItem = ViewModel.SelectedGroup;
        }
    }

    private void UpdateSummaryEnables()
    {
        var currentGroup = GroupFilter.SelectedItem as string ?? AllGroupsLabel;
        bool isAllGroups = string.Equals(currentGroup, AllGroupsLabel, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(currentGroup, AllTracksLegacyLabel, StringComparison.OrdinalIgnoreCase);
        SelectFilteredBtn.IsEnabled = !ViewModel.IsRunning && !isAllGroups;
        ClearFilteredBtn.IsEnabled = !ViewModel.IsRunning && !isAllGroups;
        SelectAllBtn.IsEnabled = !ViewModel.IsRunning;
        ClearAllBtn.IsEnabled = !ViewModel.IsRunning;
        SelectAllCheckBox.IsEnabled = !ViewModel.IsRunning;
    }

    // ── View helpers delegating to ViewModel ────────────
    private void FindViveTool()
    {
        ViewModel.FindViveTool();
        UpdateViveStatus();
    }

    private void UpdateViveStatus()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(UpdateViveStatus);
            return;
        }
        bool found = !string.IsNullOrEmpty(ViewModel.ViveExe) && File.Exists(ViewModel.ViveExe);
        string companion = found ? Path.Combine(Path.GetDirectoryName(ViewModel.ViveExe) ?? string.Empty, "Albacore.ViVe.dll") : string.Empty;
        bool companionMissing = found && !File.Exists(companion) && File.Exists(Path.Combine(AppContext.BaseDirectory, "Albacore.ViVe.dll"));

        if (companionMissing)
        {
            ViveToolStatus.Severity = InfoBarSeverity.Error;
            ViveToolStatus.IsOpen = true;
            ViveToolStatus.Title = "ViVeTool incomplete";
            ViveToolStatus.Message = $"Found vivetool.exe but Albacore.ViVe.dll missing next to it ({companion}). Reinstall via Download button.";
            DownloadViveToolBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            DownloadViveToolBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            CopyVivePathBtn.Visibility = Visibility.Collapsed;
            return;
        }
        if (found)
        {
            ViveToolStatus.Severity = InfoBarSeverity.Success;
            ViveToolStatus.Title = "ViVeTool";
            ViveToolStatus.Message = $"Found: {ViewModel.ViveExe}";
            DownloadViveToolBtn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            DownloadViveToolBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            CopyVivePathBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ViveToolStatus.Severity = InfoBarSeverity.Warning;
            ViveToolStatus.Title = "ViVeTool";
            ViveToolStatus.Message = "vivetool.exe not found. Click Download.";
            DownloadViveToolBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            DownloadViveToolBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            CopyVivePathBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSummary()
    {
        ViewModel.UpdateSummary();
    }

    private void UpdateStatusBreakdown()
    {
        // Delegated to ViewModel via PropertyChanged sync; keep for test containment check
        ViewModel.UpdateSummary();
    }

    private void UpdateActionButtonText()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(UpdateActionButtonText);
            return;
        }
        bool preview = WhatIfToggle != null && WhatIfToggle.IsOn;
        if (EnableBtnText != null)
        {
            EnableBtnText.Text = preview ? "Preview Enable" : "Enable Selected";
        }
        if (DisableBtnText != null)
        {
            DisableBtnText.Text = preview ? "Preview Disable" : "Disable Selected";
        }
        if (PreviewInfoBar != null)
        {
            PreviewInfoBar.IsOpen = preview;
        }
        if (EnableBtn != null)
        {
            EnableBtn.ToolTipServiceSetToolTip(preview ? "Preview: no system changes will be made" : "Enable checked feature IDs via vivetool /enable");
        }
        if (DisableBtn != null)
        {
            DisableBtn.ToolTipServiceSetToolTip(preview ? "Preview: no system changes will be made" : "Disable checked feature IDs — rollback");
        }
        ViewModel.WhatIfEnabled = preview;
    }

    private void SetRunningState(bool running)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetRunningState(running));
            return;
        }
        SearchBox.IsEnabled = !running;
        GroupFilter.IsEnabled = !running;
        RefreshBtn.IsEnabled = !running;
        FeatureListView.IsEnabled = !running;
        SelectAllCheckBox.IsEnabled = !running;
        SelectAllBtn.IsEnabled = !running;
        ClearAllBtn.IsEnabled = !running;
        var currentGroup = GroupFilter.SelectedItem as string ?? AllGroupsLabel;
        bool isAllGroups = string.Equals(currentGroup, AllGroupsLabel, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(currentGroup, AllTracksLegacyLabel, StringComparison.OrdinalIgnoreCase);
        SelectFilteredBtn.IsEnabled = !running && !isAllGroups;
        ClearFilteredBtn.IsEnabled = !running && !isAllGroups;
        EnableBtn.IsEnabled = !running;
        DisableBtn.IsEnabled = !running;
        CancelBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        WhatIfToggle.IsEnabled = !running;
        RestartExplorerToggle.IsEnabled = !running;
        if (!running)
        {
            UpdateSummaryEnables();
        }
    }

    private void Log(string message)
    {
        ViewModel.Log(message);
    }

    private void ShowInfoBar(string message, InfoBarSeverity severity)
    {
        ViewModel.ShowInfoBar(message, (int)severity);
    }

    private void ApplyFilters()
    {
        ViewModel.ApplyFilters();
    }

    private void RebuildGroupFilter()
    {
        ViewModel.RebuildGroupFilter();
    }

    private async Task RunViVeAsync(ViVeExecutionMode mode)
    {
        await ViewModel.RunViVeAsync(mode).ConfigureAwait(true);
        RunProgress.ShowError = ViewModel.HasError;
        SelectionProgress.ShowError = ViewModel.HasError;
        RunProgress.ShowPaused = ViewModel.IsPaused;
        SelectionProgress.ShowPaused = ViewModel.IsPaused;
        if (RestartExplorerToggle.IsOn && !ViewModel.WhatIfEnabled && !ViewModel.IsRunning)
        {
            // Check if batch had success via last log? ViewModel handles explorer prompt; view handles XamlRoot dialog
            // Instead delegate prompt to view's PromptRestartExplorerAsync after ViewModel run
            // For thin view, we keep explorer prompt here if needed
            // ViewModel already would have triggered ShowInfoBar; we handle explorer restart via ViewModel success count check
            // Simplified: call view dialog if needed
            var success = ViewModel.Items.Count(i => i.LastStatus == FeatureRunStatus.Success) > 0;
            if (success)
            {
                await PromptRestartExplorerAsync().ConfigureAwait(true);
            }
        }
    }

    private async Task PromptRestartExplorerAsync()
    {
        if (_isDialogOpen || ViewModel.IsDialogOpen || this.Content?.XamlRoot == null) return;
        _isDialogOpen = true;
        ViewModel.IsDialogOpen = true;
        try
        {
            var dlg = new ContentDialog
            {
                Title = "Restart Explorer?",
                Content = "Explorer will be restarted to apply changes. Open folders will close. Continue?",
                PrimaryButtonText = "Restart",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                Log("[INFO] Attempting graceful Explorer restart...");
                var explorers = Process.GetProcessesByName("explorer");
                foreach (var p in explorers)
                {
                    try
                    {
                        bool closed = p.CloseMainWindow();
                        Debug.WriteLine($"CloseMainWindow {p.Id} -> {closed}");
                    }
                    catch { }
                }
                await Task.Delay(1200).ConfigureAwait(true);
                var remaining = Process.GetProcessesByName("explorer");
                foreach (var p in remaining)
                {
                    try
                    {
                        if (!p.HasExited) p.Kill();
                    }
                    catch { }
                }
                await Task.Delay(1500).ConfigureAwait(true);
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                    Log("[INFO] Explorer restarted.");
                }
                catch (Exception ex)
                {
                    Log($"[WARN] Failed to restart Explorer: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[WARN] Dialog error: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
            ViewModel.IsDialogOpen = false;
        }
    }

    private async Task DownloadViVeToolAsync()
    {
        await ViewModel.DownloadViVeToolAsync().ConfigureAwait(true);
        UpdateViveStatus();
    }

    // ── Event Handlers (preserved for XAML + tests) ─────
    private void SearchBox_TextChanged(AutoSuggestBox s, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            if (_searchDebounceTimer != null)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
            else
            {
                ViewModel.SearchText = s.Text?.Trim() ?? string.Empty;
            }
        }
        else
        {
            ViewModel.SearchText = s.Text?.Trim() ?? string.Empty;
        }
    }

    private void GroupFilter_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedGroup = GroupFilter.SelectedItem as string ?? AllGroupsLabel;
    }

    private async void RefreshBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] RefreshBtn error: {ex.Message}");
        }
    }

    private void SelectAllCheckBox_Click(object s, RoutedEventArgs e)
    {
        var isChecked = (s as CheckBox)?.IsChecked == true;
        ViewModel.SelectAllCheckBoxClicked(isChecked);
    }

    private void FeatureCheckBox_Click(object s, RoutedEventArgs e)
    {
        _lastCheckBoxClickTicks = DateTime.UtcNow.Ticks;
        ViewModel.FeatureCheckBoxClicked();
        UpdateSummary();
    }

    private void FeatureListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        var elapsedTicks = DateTime.UtcNow.Ticks - _lastCheckBoxClickTicks;
        if (elapsedTicks < TimeSpan.TicksPerMillisecond * 350)
        {
            _lastCheckBoxClickTicks = 0;
            return;
        }
        if (e.ClickedItem is FeatureItem item)
        {
            item.IsSelected = !item.IsSelected;
            ViewModel.FeatureListViewItemClick(item);
            UpdateSummary();
        }
    }

    private void SelectAllBtn_Click(object s, RoutedEventArgs e)
    {
        ViewModel.SelectAll();
    }

    private void ClearAllBtn_Click(object s, RoutedEventArgs e)
    {
        ViewModel.ClearAll();
    }

    private void SelectGroupBtn_Click(object s, RoutedEventArgs e)
    {
        ViewModel.SelectFiltered();
    }

    private void ClearGroupBtn_Click(object s, RoutedEventArgs e)
    {
        ViewModel.ClearFiltered();
    }

    private async void EnableBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var selectedIds = ViewModel.Items.Where(i => i.IsSelected).SelectMany(i => i.IDs).Where(id => id > 0).Distinct().Count();
            var checkedRows = ViewModel.Items.Count(i => i.IsSelected);
            if (selectedIds == 0)
            {
                ShowInfoBar("No features checked. Select some features first.", InfoBarSeverity.Warning);
                return;
            }
            bool whatIf = WhatIfToggle.IsOn;
            ViewModel.WhatIfEnabled = whatIf;
            bool needConfirm = selectedIds > 5 || checkedRows > 10;
            if (needConfirm)
            {
                if (_isDialogOpen || ViewModel.IsDialogOpen || this.Content?.XamlRoot == null) return;
                _isDialogOpen = true;
                ViewModel.IsDialogOpen = true;
                try
                {
                    string previewNote = whatIf ? "\n\nPreview mode is ON — no system changes will be made." : "";
                    string content = $"This will run vivetool /enable on {selectedIds} IDs in {checkedRows} features.{previewNote}\n\nContinue?";
                    var dlg = new ContentDialog
                    {
                        Title = "Confirm Enable",
                        Content = content,
                        PrimaryButtonText = whatIf ? "Preview Enable" : "Yes, Enable",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = this.Content.XamlRoot
                    };
                    if (await dlg.ShowAsync() != ContentDialogResult.Primary)
                    {
                        return;
                    }
                }
                finally
                {
                    _isDialogOpen = false;
                    ViewModel.IsDialogOpen = false;
                }
            }
            await RunViVeAsync(ViVeExecutionMode.Enable).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] EnableBtn error: {ex.Message}");
        }
    }

    private async void DisableBtn_Click(object s, RoutedEventArgs e)
    {
        if (_isDialogOpen || ViewModel.IsDialogOpen || this.Content?.XamlRoot == null) return;
        var selectedIds = ViewModel.Items.Where(i => i.IsSelected).SelectMany(i => i.IDs).Where(id => id > 0).Distinct().Count();
        var checkedRows = ViewModel.Items.Count(i => i.IsSelected);
        if (selectedIds == 0)
        {
            ShowInfoBar("No features checked. Select some features first.", InfoBarSeverity.Warning);
            return;
        }
        bool whatIf = WhatIfToggle.IsOn;
        ViewModel.WhatIfEnabled = whatIf;
        _isDialogOpen = true;
        ViewModel.IsDialogOpen = true;
        try
        {
            string previewNote = whatIf ? "\n\nPreview mode is ON — no system changes will be made." : "";
            string content = $"This will run vivetool /disable on {selectedIds} IDs in {checkedRows} features.{previewNote}\n\nContinue?";
            var dlg = new ContentDialog
            {
                Title = "Confirm Rollback",
                Content = content,
                PrimaryButtonText = whatIf ? "Preview Disable" : "Yes, Disable",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                await RunViVeAsync(ViVeExecutionMode.Disable).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log($"[WARN] Dialog error: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
            ViewModel.IsDialogOpen = false;
        }
    }

    private async void DownloadViveToolBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            await DownloadViVeToolAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] DownloadViveToolBtn error: {ex.Message}");
        }
    }

    private void CopyVivePathBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            ViewModel.CopyPath();
        }
        catch (Exception ex)
        {
            Log($"[WARN] Copy failed: {ex.Message}");
            ShowInfoBar($"Copy failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void CancelBtn_Click(object s, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Cancel();
        }
        catch (Exception ex)
        {
            Log($"[WARN] Cancel error: {ex.Message}");
        }
    }

    private void WhatIfToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.WhatIfEnabled = WhatIfToggle.IsOn;
        UpdateActionButtonText();
        if (WhatIfToggle.IsOn)
        {
            ShowInfoBar("Preview mode is ON — no system changes will be made. Turn off Preview to apply.", InfoBarSeverity.Informational);
        }
        else
        {
            MainInfoBar.IsOpen = false;
            ViewModel.IsInfoBarOpen = false;
        }
    }

    private void ClearLogBtn_Click(object s, RoutedEventArgs e)
    {
        ViewModel.ClearLog();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try { _searchDebounceTimer?.Stop(); } catch { }
        ViewModel.Cleanup();
        _isDialogOpen = false;
    }
}

internal static class ControlExtensions
{
    internal static void ToolTipServiceSetToolTip(this Control control, string text)
    {
        ToolTipService.SetToolTip(control, text);
    }
}
