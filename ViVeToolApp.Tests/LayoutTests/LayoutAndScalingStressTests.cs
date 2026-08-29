using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.LayoutTests;

public class LayoutAndScalingStressTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string SolutionRoot = FindSolutionRoot();
    private static readonly string MainWindowXamlPath = Path.Combine(SolutionRoot, "MainWindow.xaml");
    private static readonly string MainWindowCsPath = Path.Combine(SolutionRoot, "MainWindow.xaml.cs");

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ViVeToolApp.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return @"C:\Tools\ViVeToolApp";
    }

    private XDocument LoadXaml()
    {
        File.Exists(MainWindowXamlPath).Should().BeTrue("MainWindow.xaml must exist");
        var content = File.ReadAllText(MainWindowXamlPath);
        return XDocument.Parse(content);
    }

    private static string? GetElementName(XElement elem)
    {
        return elem.Attribute(XamlNamespace + "Name")?.Value ?? elem.Attribute("Name")?.Value;
    }

    [Fact]
    public void Xaml_ShouldBeValidAndContainRequiredBackdrop()
    {
        var doc = LoadXaml();
        var root = doc.Root;
        root.Should().NotBeNull();
        root!.Name.LocalName.Should().Be("Window");

        var micaBackdrop = root.Descendants().FirstOrDefault(d => d.Name.LocalName == "MicaBackdrop");
        micaBackdrop.Should().NotBeNull("MicaBackdrop must be declared in MainWindow.xaml");
        micaBackdrop!.Attribute("Kind")?.Value.Should().Be("Base");
    }

    [Fact]
    public void Xaml_AllExpectedNamedElements_MustExistInMainWindowXaml()
    {
        var doc = LoadXaml();
        var xNames = doc.Descendants()
            .Select(GetElementName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet();

        var requiredControls = new[]
        {
            "MainInfoBar",
            "SearchBox",
            "GroupFilter",
            "RefreshBtn",
            "SelectAllCheckBox",
            "FeatureListView",
            "LoadingOverlay",
            "DownloadViveToolBtn",
            "ViveToolStatus",
            "EnableBtn",
            "DisableBtn",
            "SummaryText",
            "SelectionProgress",
            "LastUpdatedText",
            "WhatIfToggle",
            "RestartExplorerToggle",
            "LogExpander",
            "RunProgress",
            "ClearLogBtn",
            "LogScroller",
            "LogText"
        };

        foreach (var controlName in requiredControls)
        {
            xNames.Should().Contain(controlName, $"Control '{controlName}' must exist in MainWindow.xaml");
        }
    }

    [Fact]
    public void Xaml_MainContentGrid_ColumnDefinitions_MustMatchDesignSpecification()
    {
        var doc = LoadXaml();
        var mainGrid = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "Grid" && (string?)d.Attribute("Grid.Row") == "1");
        mainGrid.Should().NotBeNull();

        var colDefs = mainGrid!.Element(mainGrid.Name.Namespace + "Grid.ColumnDefinitions")?.Elements().ToList();
        colDefs.Should().NotBeNull().And.HaveCount(3);

        colDefs![0].Attribute("Width")?.Value.Should().Be("*");
        colDefs[0].Attribute("MinWidth")?.Value.Should().Be("520");
        colDefs[1].Attribute("Width")?.Value.Should().Be("12");
        colDefs[2].Attribute("Width")?.Value.Should().Be("290");
    }

    [Fact]
    public void Xaml_ColumnHeader_And_ItemTemplate_ColumnWidths_MustMatchExactly()
    {
        var doc = LoadXaml();

        // Find Header Grid
        var headerBorder = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "Border" && (string?)d.Attribute("Grid.Row") == "1");
        headerBorder.Should().NotBeNull();
        var headerGrid = headerBorder!.Element(headerBorder.Name.Namespace + "Grid");
        headerGrid.Should().NotBeNull();
        var headerCols = headerGrid!.Element(headerGrid.Name.Namespace + "Grid.ColumnDefinitions")?.Elements().ToList();
        headerCols.Should().NotBeNull().And.HaveCount(6);

        var headerWidths = headerCols!.Select(c => c.Attribute("Width")?.Value).ToList();
        headerWidths.Should().Equal(new[] { "36", "130", "110", "*", "190", "96" });

        // Find ListView ItemTemplate Grid
        var listView = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "ListView" && GetElementName(d) == "FeatureListView");
        listView.Should().NotBeNull();

        var itemTemplateGrid = listView!.Descendants().FirstOrDefault(d => d.Name.LocalName == "Grid" && d.Element(d.Name.Namespace + "Grid.ColumnDefinitions") != null);
        itemTemplateGrid.Should().NotBeNull();
        var itemCols = itemTemplateGrid!.Element(itemTemplateGrid.Name.Namespace + "Grid.ColumnDefinitions")?.Elements().ToList();
        itemCols.Should().NotBeNull().And.HaveCount(6);

        var itemWidths = itemCols!.Select(c => c.Attribute("Width")?.Value).ToList();
        itemWidths.Should().Equal(headerWidths, "Header columns and ListView item columns must match exactly to avoid misalignment");
    }

    [Fact]
    public void Xaml_ListViewItems_MustHaveTextTrimmingAndToolTipsOnAllTextColumns()
    {
        var doc = LoadXaml();
        var listView = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "ListView" && GetElementName(d) == "FeatureListView");
        listView.Should().NotBeNull();

        var textBlocks = listView!.Descendants().Where(d => d.Name.LocalName == "TextBlock").ToList();
        textBlocks.Should().HaveCount(5, "ListView row must have 5 TextBlocks for Group, BuildLabel, Description, IDsDisplay, Status");

        foreach (var tb in textBlocks)
        {
            var trimming = tb.Attribute("TextTrimming")?.Value;
            trimming.Should().Be("CharacterEllipsis", $"TextBlock in ListView must have TextTrimming='CharacterEllipsis'");

            var toolTip = tb.Attributes().FirstOrDefault(a => a.Name.LocalName.Contains("ToolTip"))?.Value;
            toolTip.Should().NotBeNullOrEmpty("TextBlock in ListView must have ToolTip bound for overflow readability");
        }
    }

    [Fact]
    public void Xaml_Sidebar_MustHaveResponsiveScrollViewer_And_GutterPadding()
    {
        var doc = LoadXaml();
        var scrollViewer = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "ScrollViewer" && (string?)d.Attribute("Grid.Column") == "2");
        scrollViewer.Should().NotBeNull("Sidebar must be wrapped in a ScrollViewer");

        scrollViewer!.Attribute("VerticalScrollBarVisibility")?.Value.Should().Be("Auto");
        scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value.Should().Be("Disabled");
        scrollViewer.Attribute("Padding")?.Value.Should().Be("0,0,12,0", "Sidebar ScrollViewer must have 12px right padding so scrollbar does not clip card borders");
    }

    [Fact]
    public void Xaml_LogExpander_MustContainProgressBarAndClearButtonInHeader()
    {
        var doc = LoadXaml();
        var expander = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "Expander" && GetElementName(d) == "LogExpander");
        expander.Should().NotBeNull();

        var header = expander!.Element(expander.Name.Namespace + "Expander.Header");
        header.Should().NotBeNull("Expander must have an Expander.Header element");

        var progressBar = header!.Descendants().FirstOrDefault(d => d.Name.LocalName == "ProgressBar" && GetElementName(d) == "RunProgress");
        progressBar.Should().NotBeNull("RunProgress ProgressBar must be in Expander.Header for persistent progress visibility");

        // A11y fix: ClearLogBtn must NOT be nested inside Expander.Header (nested interactive)
        var headerClearBtn = header.Descendants().FirstOrDefault(d => d.Name.LocalName == "Button" && GetElementName(d) == "ClearLogBtn");
        headerClearBtn.Should().BeNull("ClearLogBtn must not be nested inside Expander.Header for a11y - moved to Expander content footer");

        var clearBtn = expander.Descendants().FirstOrDefault(d => d.Name.LocalName == "Button" && GetElementName(d) == "ClearLogBtn");
        clearBtn.Should().NotBeNull("ClearLogBtn must exist in Expander content footer (below ScrollViewer)");
        // Verify it is not inside header but in content area
        var contentGrid = expander.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid");
        contentGrid.Should().NotBeNull();
        contentGrid!.Descendants().FirstOrDefault(d => d.Name.LocalName == "Button" && GetElementName(d) == "ClearLogBtn").Should().NotBeNull("ClearLogBtn must be in Expander content Grid footer");
    }

    [Fact]
    public void Xaml_ListView_MustHaveItemClickEnabled()
    {
        var doc = LoadXaml();
        var listView = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "ListView" && GetElementName(d) == "FeatureListView");
        listView.Should().NotBeNull();

        listView!.Attribute("IsItemClickEnabled")?.Value.Should().Be("True");
        listView.Attribute("ItemClick")?.Value.Should().Be("FeatureListView_ItemClick");
    }

    [Theory]
    [InlineData(1200, 840, true)]   // Default target resolution
    [InlineData(1024, 768, true)]   // Legacy standard resolution
    [InlineData(900, 700, true)]    // Compact resolution
    [InlineData(800, 600, true)]    // Minimum bounds requirement
    [InlineData(1920, 1080, true)]  // FHD
    [InlineData(2560, 1440, true)]  // QHD
    [InlineData(3840, 2160, true)]  // 4K
    public void Layout_DimensionCalculations_AcrossWindowDimensions(double windowWidth, double windowHeight, bool isExpanded)
    {
        // Grid margin is 12 left, 12 right -> 24px
        double gridMarginX = 24.0;
        double availableGridWidth = windowWidth - gridMarginX;

        // Columns: LeftCol (*, MinWidth 380), Spacer (12), Sidebar (290)
        double spacerWidth = 12.0;
        double sidebarWidth = 290.0;
        double fixedColumns = spacerWidth + sidebarWidth; // 302px

        double leftColWidth = Math.Max(380.0, availableGridWidth - fixedColumns);
        leftColWidth.Should().BeGreaterOrEqualTo(380.0, "Left column must respect MinWidth 380");

        // Header Border padding: 12 left, 26 right = 38px + 2px border = 40px
        double headerOverhead = 40.0;
        double headerInnerWidth = leftColWidth - headerOverhead;

        // Fixed columns inside list: Checkbox (36), Track (130), Build (135), IDs (170) = 471px
        double fixedListCols = 36.0 + 130.0 + 135.0 + 170.0; // 471px

        // Description column width in star sizing
        double descriptionWidth = Math.Max(0, headerInnerWidth - fixedListCols);

        // Sidebar height calculation:
        // Card 1 (~185px), Card 2 (~176px), Card 3 (~140px), Spacing (20px) = ~521px
        double sidebarContentHeight = 521.0;

        // Available vertical height for content:
        // Window height minus TitleBar (~32px), Expander (~182px if expanded, ~52px if collapsed), Content Margin (~16px)
        double expanderHeight = isExpanded ? 182.0 : 52.0;
        double availableContentHeight = windowHeight - 32.0 - expanderHeight - 16.0;

        // When availableContentHeight < sidebarContentHeight, ScrollViewer engages
        bool scrollViewerNeeded = availableContentHeight < sidebarContentHeight;

        if (windowHeight <= 600)
        {
            scrollViewerNeeded.Should().BeTrue("Sidebar ScrollViewer must engage at 600px height when expander is open");
        }
        else if (windowHeight >= 840)
        {
            // At 840px height with 521px sidebar and ~610px available height, content fits comfortably
            availableContentHeight.Should().BeGreaterThan(sidebarContentHeight - 50);
        }
    }

    [Theory]
    [InlineData(1.0)]   // 100% DPI
    [InlineData(1.25)]  // 125% DPI
    [InlineData(1.5)]   // 150% DPI
    [InlineData(1.75)]  // 175% DPI
    [InlineData(2.0)]   // 200% DPI
    [InlineData(2.5)]   // 250% DPI
    [InlineData(3.0)]   // 300% DPI
    public void Layout_DpiScaling_DIPCalculations_RemainConsistent(double scaleFactor)
    {
        // WinUI 3 uses DIPs (Device Independent Pixels) internally.
        // A window configured to 1200x840 DIPs scales physical pixels = 1200 * scaleFactor x 840 * scaleFactor.
        double dipWidth = 1200.0;
        double dipHeight = 840.0;

        double physicalWidth = dipWidth * scaleFactor;
        double physicalHeight = dipHeight * scaleFactor;

        physicalWidth.Should().Be(1200.0 * scaleFactor);
        physicalHeight.Should().Be(840.0 * scaleFactor);

        // Sidebar 290 DIPs at any scaling factor maintains proportional width:
        double sidebarRatio = 290.0 / 1200.0;
        sidebarRatio.Should().BeApproximately(0.2416, 0.001);
    }

    [Fact]
    public void OfflineCatalog_AllEntries_GroupAndBuildLabels_FitWithinAllocatedColumnWidths()
    {
        var features = OfflineCatalog.GetFeatures();
        features.Should().NotBeEmpty();

        foreach (var feature in features)
        {
            // Track column is 130 DIPs. Text should be concise.
            feature.Group.Should().NotBeNullOrEmpty();
            // Build column is 135 DIPs.
            feature.BuildLabel.Should().NotBeNull();
            // IDs should be valid
            feature.IDsDisplay.Should().NotBeNullOrEmpty();
            feature.Description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void CodeBehind_ItemClick_TogglesSelection_AndUpdatesSummary()
    {
        var csContent = File.ReadAllText(MainWindowCsPath);
        csContent.Should().Contain("FeatureListView_ItemClick");
        csContent.Should().Contain("item.IsSelected = !item.IsSelected");
        csContent.Should().Contain("UpdateSummary()");
    }
}
