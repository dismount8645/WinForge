using WingetStore.Models;

namespace WingetStore.Services;

public static class NavigationHelper
{
    public static bool CanGoBack(Microsoft.UI.Xaml.Controls.Frame frame) => frame.CanGoBack;
    public static Type? GetPageType(string? tag, bool isSettingsSelected, bool isWingetAvailable) { if (!isWingetAvailable) return typeof(Pages.NoWingetPage); if (isSettingsSelected) return typeof(Pages.SettingsPage); if (string.IsNullOrEmpty(tag)) return null; return tag switch { NavTags.Home or NavTags.Search => typeof(Pages.HomePage), NavTags.Installed => typeof(Pages.InstalledPage), NavTags.Updates => typeof(Pages.UpdatesPage), NavTags.About => typeof(Pages.AboutPage), _ => null }; }
}

public static class PackageFilteringHelper
{
    public static bool MatchesQuery(this WingetPackage pkg, string query) { if (pkg == null) return false; if (string.IsNullOrWhiteSpace(query)) return true; query = query.Trim(); if (query.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)) { string targetTag = query["tag:".Length..].Trim(); if (pkg.Tags != null && pkg.Tags.Exists(t => t.Equals(targetTag, StringComparison.OrdinalIgnoreCase))) return true; } return (pkg.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) || (pkg.Id ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) || (pkg.Publisher ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) || (pkg.Description ?? "").Contains(query, StringComparison.OrdinalIgnoreCase); }
    public static List<WingetPackage> FilterAndSortPackages(List<WingetPackage> source, string query, string sourceFilter = "all", string sortOrder = "default") { var filtered = source.FindAll(p => p.MatchesQuery(query) && MatchesSourceFilter(p.Source, sourceFilter)); SortPackages(filtered, sortOrder); return filtered; }
    public static bool MatchesSourceFilter(string? packageSource, string sourceFilter) => sourceFilter switch { SourceFilters.All => true, SourceFilters.Winget => (packageSource ?? "").Contains("winget", StringComparison.OrdinalIgnoreCase), _ => false };
    public static void SortPackages(List<WingetPackage> packages, string sortBy, string sortDirection = "Descending")
    {
        if (sortBy == SortOrders.Az) { packages.Sort((a, b) => string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase)); return; }
        if (sortBy == SortOrders.Za) { packages.Sort((a, b) => string.Compare(b.Name ?? "", a.Name ?? "", StringComparison.OrdinalIgnoreCase)); return; }
        if (sortBy == SortOrders.Publisher) { packages.Sort((a, b) => string.Compare(a.Publisher ?? "", b.Publisher ?? "", StringComparison.OrdinalIgnoreCase)); return; }
        if (sortBy == SortOrders.Id) { packages.Sort((a, b) => string.Compare(a.Id ?? "", b.Id ?? "", StringComparison.OrdinalIgnoreCase)); return; }
        if (sortBy == SortOrders.Status)
        {
            static int GetStatusWeight(PackageStatus status) => status switch { PackageStatus.Upgradable => 0, PackageStatus.Installed => 1, _ => 2 };
            packages.Sort((a, b) => GetStatusWeight(a.Status).CompareTo(GetStatusWeight(b.Status)));
            return;
        }

        bool isDescending = string.Equals(sortDirection, "Descending", StringComparison.OrdinalIgnoreCase);
        int descMultiplier = isDescending ? -1 : 1;

        switch (sortBy?.ToLowerInvariant())
        {
            case "name":
                packages.Sort((a, b) => descMultiplier * string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase));
                break;

            case "version":
                packages.Sort((a, b) =>
                {
                    int cmp = VersionComparer.Instance.Compare(a.Version ?? "", b.Version ?? "");
                    if (cmp == 0) cmp = a.Status.CompareTo(b.Status);
                    return descMultiplier * cmp;
                });
                break;

            case "publisher":
                packages.Sort((a, b) => descMultiplier * string.Compare(a.Publisher ?? "", b.Publisher ?? "", StringComparison.OrdinalIgnoreCase));
                break;

            case "id":
                packages.Sort((a, b) => descMultiplier * string.Compare(a.Id ?? "", b.Id ?? "", StringComparison.OrdinalIgnoreCase));
                break;

            default:
                packages.Sort((a, b) => descMultiplier * string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase));
                break;
        }
    }
}

public readonly record struct GridDimensions(
    int Columns,
    double SlotWidth,
    double EffectiveGap)
{
    public double CardWidth => Math.Max(0, SlotWidth - EffectiveGap);
}

public static class GridCalculator
{
    public static GridDimensions CalculateGridDimensions(
        double usableWidth, 
        double minCardWidth = 300, 
        double gap = 16, 
        int maxColumns = 5)
    {
        if (!double.IsFinite(minCardWidth) || minCardWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(minCardWidth), "Card width must be > 0.");
        if (!double.IsFinite(gap) || gap < 0)
            throw new ArgumentOutOfRangeException(nameof(gap), "Gap cannot be negative.");
        if (maxColumns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxColumns), "Max columns must be > 0.");

        if (!double.IsFinite(usableWidth) || usableWidth <= 0)
            return new GridDimensions(1, 0, 0);

        double minSlotWidth = minCardWidth + gap; // 316 DIPs
        int columns = Math.Clamp((int)Math.Floor(usableWidth / minSlotWidth), 1, maxColumns);
        double slotWidth = usableWidth / columns;
        double effectiveGap = columns == 1 ? 0 : gap;

        return new GridDimensions(columns, slotWidth, effectiveGap);
    }
}

public class VersionComparer : IComparer<string>
{
    public static VersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        string cleanX = x.TrimStart('v', 'V').Trim();
        string cleanY = y.TrimStart('v', 'V').Trim();

        bool hasPrereleaseX = cleanX.Contains('-');
        bool hasPrereleaseY = cleanY.Contains('-');

        string baseX = hasPrereleaseX ? cleanX.Split('-', 2)[0] : cleanX;
        string baseY = hasPrereleaseY ? cleanY.Split('-', 2)[0] : cleanY;

        string[] partsX = baseX.Split(new[] { '.', '+' }, StringSplitOptions.RemoveEmptyEntries);
        string[] partsY = baseY.Split(new[] { '.', '+' }, StringSplitOptions.RemoveEmptyEntries);

        int minLen = Math.Min(partsX.Length, partsY.Length);
        for (int i = 0; i < minLen; i++)
        {
            bool isNumX = ulong.TryParse(partsX[i], out ulong numX);
            bool isNumY = ulong.TryParse(partsY[i], out ulong numY);

            if (isNumX && isNumY)
            {
                int numCmp = numX.CompareTo(numY);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                int strCmp = string.Compare(partsX[i], partsY[i], StringComparison.OrdinalIgnoreCase);
                if (strCmp != 0) return strCmp;
            }
        }

        if (partsX.Length != partsY.Length)
            return partsX.Length.CompareTo(partsY.Length);

        if (hasPrereleaseX && !hasPrereleaseY) return -1;
        if (!hasPrereleaseX && hasPrereleaseY) return 1;

        if (hasPrereleaseX && hasPrereleaseY)
        {
            string preX = cleanX.Split('-', 2)[1];
            string preY = cleanY.Split('-', 2)[1];
            return string.Compare(preX, preY, StringComparison.OrdinalIgnoreCase);
        }

        return 0;
    }
}


public class BulkSelectionHelper(Action onSelectionChanged)
{
    private readonly Action _onSelectionChanged = onSelectionChanged;
    public bool IsActive { get; set; }
    public List<WingetPackage> SelectedPackages { get; set; } = [];
    public void Toggle() { IsActive = !IsActive; if (!IsActive) SelectedPackages.Clear(); _onSelectionChanged(); }
    public void SelectAll(IEnumerable<WingetPackage> packages) { SelectedPackages = [.. packages]; _onSelectionChanged(); }
    public void DeselectAll() { SelectedPackages.Clear(); _onSelectionChanged(); }
    public static bool? ComputeSelectAllState(int totalCount, int selectedCount) { if (totalCount == 0 || selectedCount == 0) return false; if (selectedCount == totalCount) return true; return null; }
}

public class BulkSelectionHelperUI(Microsoft.UI.Xaml.Controls.ListView listView, Microsoft.UI.Xaml.Controls.Button actionButton, Microsoft.UI.Xaml.Controls.TextBlock countText, Microsoft.UI.Xaml.Controls.CheckBox selectAllCheckBox, Microsoft.UI.Xaml.UIElement actionBar, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggleButton)
{
    private readonly Microsoft.UI.Xaml.Controls.ListView _listView = listView; private readonly Microsoft.UI.Xaml.Controls.Button _actionButton = actionButton; private readonly Microsoft.UI.Xaml.Controls.TextBlock _countText = countText; private readonly Microsoft.UI.Xaml.Controls.CheckBox _selectAllCheckBox = selectAllCheckBox; private readonly Microsoft.UI.Xaml.UIElement _actionBar = actionBar; private readonly Microsoft.UI.Xaml.Controls.Primitives.ToggleButton _toggleButton = toggleButton; private bool _isUpdatingSelection;
    public void Toggle() { if (_toggleButton.IsChecked == true) Activate(); else Deactivate(); }
    public void Activate() { _toggleButton.IsChecked = true; _listView.SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Multiple; _listView.IsItemClickEnabled = false; _actionBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible; UpdateSelectionUI(); }
    public void Deactivate() { _toggleButton.IsChecked = false; _listView.SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.None; _listView.IsItemClickEnabled = true; _actionBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed; _listView.SelectedItems.Clear(); UpdateSelectionUI(); }
    public void Cancel() => Deactivate();
    public void SelectAll() { _isUpdatingSelection = true; _listView.SelectAll(); _isUpdatingSelection = false; UpdateSelectionUI(); }
    public void DeselectAll() { _isUpdatingSelection = true; _listView.SelectedItems.Clear(); _isUpdatingSelection = false; UpdateSelectionUI(); }
    public void OnSelectionChanged() { if (!_isUpdatingSelection) UpdateSelectionUI(); }
    private void UpdateSelectionUI() { int count = _listView.SelectedItems.Count; int total = _listView.Items.Count; _actionButton.IsEnabled = count > 0; _countText.Text = $"{count} app{(count == 1 ? "" : "s")} selected"; _isUpdatingSelection = true; _selectAllCheckBox.IsChecked = total > 0 && count == total ? true : count > 0 ? null : false; _isUpdatingSelection = false; }
}

public static class PackageDetailHelper
{
    public static bool ShouldSkipMetadataItem(string key) => key switch { "Name" or "Version" or "Description" or "Release Notes" => true, _ => false };
    public static void PopulateMetadata(Microsoft.UI.Xaml.Controls.Panel container, IEnumerable<MetadataItem> details)
    {
        container.Children.Clear();
        foreach (var item in details)
        {
            var card = new Microsoft.UI.Xaml.Controls.Border { Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8), Padding = new Microsoft.UI.Xaml.Thickness(12), CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6), Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] };
            var stack = new Microsoft.UI.Xaml.Controls.StackPanel();
            if (!string.IsNullOrEmpty(item.Key)) stack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = item.Key, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"] });
            if (!string.IsNullOrEmpty(item.Value)) { if (item.IsUrl) stack.Children.Add(new Microsoft.UI.Xaml.Controls.HyperlinkButton { Content = item.Value, NavigateUri = new Uri(item.Value), Padding = new Microsoft.UI.Xaml.Thickness(0), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) }); else stack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = item.Value, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) }); }
            foreach (var sub in item.SubItems)
            {
                var subStack = new Microsoft.UI.Xaml.Controls.StackPanel { Margin = new Microsoft.UI.Xaml.Thickness(12, 4, 0, 0) };
                if (!string.IsNullOrEmpty(sub.Key)) subStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = $"{sub.Key}:", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorTertiaryBrush"] });
                if (!string.IsNullOrEmpty(sub.Value)) { if (sub.IsUrl) subStack.Children.Add(new Microsoft.UI.Xaml.Controls.HyperlinkButton { Content = sub.Value, NavigateUri = new Uri(sub.Value), Padding = new Microsoft.UI.Xaml.Thickness(0) }); else subStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = sub.Value, FontSize = 12, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }); }
                stack.Children.Add(subStack);
            }
            card.Child = stack; container.Children.Add(card);
        }
    }
}
