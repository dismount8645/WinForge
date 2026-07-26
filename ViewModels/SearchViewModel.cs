using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WingetStore.Models;
using WingetStore.Services;

namespace WingetStore.ViewModels;

public partial class SearchViewModel(IWingetService winget) : FilterableViewModel
{
    private readonly IWingetService _winget = winget;
    private CancellationTokenSource? _searchCts;
    private List<WingetPackage> _allResults = [];
    [ObservableProperty] public partial ObservableCollection<WingetPackage> FilteredResults { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<WingetPackage> SearchResults { get; set; } = [];
    [ObservableProperty] public partial string SourceFilter { get; set; } = SourceFilters.All;
    [ObservableProperty] public partial string SearchQuery { get; set; } = "";
    [ObservableProperty] public partial bool HasResults { get; set; } = true;
    partial void OnSourceFilterChanged(string value) => ApplyFilter();
    [RelayCommand]
    public async Task SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        _searchCts?.Cancel(); _searchCts = new CancellationTokenSource(); var token = _searchCts.Token;
        try
        {
            App.Dispatch(() => { IsLoading = true; IsErrorOpen = false; ErrorMessage = ""; SearchQuery = query; });
            LogService.LogInfo($"Searching winget for query: {query}");
            var results = await _winget.SearchPackagesAsync(query, token);
            if (!token.IsCancellationRequested) { App.Dispatch(() => { _allResults = results; SearchResults = new ObservableCollection<WingetPackage>(results); ApplyFilter(); HasResults = FilteredResults.Count > 0; }); }
        }
        catch (OperationCanceledException) { LogService.LogInfo($"Search cancelled for query: {query}"); }
        catch (Exception ex)
        {
            LogService.LogError($"SearchAsync failed for query {query}", ex);
            App.Dispatch(() => { ErrorMessage = $"Search failed: {ex.Message}"; IsErrorOpen = true; });
        }
        finally
        {
            if (!token.IsCancellationRequested) App.Dispatch(() => IsLoading = false);
        }
    }
    public static List<WingetPackage> FilterAndSortSearchResults(IEnumerable<WingetPackage>? searchResults, string filterQuery, string sourceFilter, string sortOrder)
    {
        var filtered = (searchResults ?? [])
            .Where(p => p != null && p.MatchesQuery(filterQuery ?? "") && MatchesSourceFilter(p.Source, sourceFilter))
            .ToList();

        if (sortOrder == SortOrders.Default)
        {
            filtered = [.. filtered.OrderBy(p => (p.Source ?? "").Equals(SourceFilters.Winget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];
        }
        else
        {
            SortPackages(filtered, sortOrder);
        }

        return filtered;
    }

    public override void ApplyFilter()
    {
        var filtered = FilterAndSortSearchResults(_allResults, FilterQuery, SourceFilter, SortOrder);
        FilteredResults = [.. filtered]; HasResults = FilteredResults.Count > 0;
    }
}
