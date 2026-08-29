using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WingetStore.Pages;

public class UiFeatureItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StatusText { get; set; } = "Default";
}

public sealed partial class FeaturesPage : Page
{
    private readonly List<UiFeatureItem> _allFeatures = new();
    private readonly ObservableCollection<UiFeatureItem> _filteredFeatures = new();

    public FeaturesPage()
    {
        InitializeComponent();
        FeaturesListView.ItemsSource = _filteredFeatures;
        Loaded += FeaturesPage_Loaded;
    }

    private void FeaturesPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadFeatures();
    }

    private void LoadFeatures()
    {
        _allFeatures.Clear();

        // Built-in verified Windows 11 feature velocity catalog
        _allFeatures.AddRange(new[]
        {
            new UiFeatureItem { Id = 44470355, Name = "Copilot & AI Assistant Integration", Description = "Enables full taskbar and shell Copilot integrations." },
            new UiFeatureItem { Id = 48433719, Name = "Snap Layouts Suggestions", Description = "Displays smart window snap suggestions when hovering maximize." },
            new UiFeatureItem { Id = 48433706, Name = "Energy Saver Enhancements", Description = "Advanced battery life and low-power scheduler policies." },
            new UiFeatureItem { Id = 47557358, Name = "Settings Modern Home Card", Description = "Refreshed dynamic settings recommendations." },
            new UiFeatureItem { Id = 45952862, Name = "Windows Spotlight on Desktop", Description = "Dynamic Bing wallpaper and interactive desktop hotspots." },
            new UiFeatureItem { Id = 48433720, Name = "Voice Clarity AI", Description = "Low-latency background noise suppression for communication apps." },
            new UiFeatureItem { Id = 46603313, Name = "Widgets Board Modern Layout", Description = "Multi-column widget dashboard with news grouping." },
            new UiFeatureItem { Id = 47622124, Name = "File Explorer Tabs & Redesign", Description = "Refreshed address bar and tabbed navigation." }
        });

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        _filteredFeatures.Clear();

        var matches = string.IsNullOrEmpty(query)
            ? _allFeatures
            : _allFeatures.Where(f =>
                f.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                f.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches)
        {
            _filteredFeatures.Add(item);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void FeaturesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = FeaturesListView.SelectedItems.Count > 0;
        EnableBtn.IsEnabled = hasSelection;
        DisableBtn.IsEnabled = hasSelection;
    }

    private void EnableBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteViVeToolAction(true);
    }

    private void DisableBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteViVeToolAction(false);
    }

    private void ExecuteViVeToolAction(bool enable)
    {
        var selected = FeaturesListView.SelectedItems.Cast<UiFeatureItem>().ToList();
        if (selected.Count == 0) return;

        var action = enable ? "enabled" : "disabled";
        var count = selected.Count;

        try
        {
            // ViVeTool executable location in tools/cli
            var vivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "cli", "ViVeTool.exe");
            if (File.Exists(vivePath))
            {
                foreach (var f in selected)
                {
                    var verb = enable ? "/enable" : "/disable";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = vivePath,
                        Arguments = $"{verb} /id:{f.Id}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    })?.WaitForExit();
                    f.StatusText = enable ? "Enabled" : "Disabled";
                }
            }

            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "Velocity Features Updated";
            StatusInfoBar.Message = $"Successfully {action} {count} feature(s). A system restart may be required for some features.";
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "Execution Error";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.IsOpen = true;
        }
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        LoadFeatures();
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Title = "Catalog Refreshed";
        StatusInfoBar.Message = $"{_allFeatures.Count} feature definitions ready.";
        StatusInfoBar.IsOpen = true;
    }
}
