using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.LayoutTests;

public class LayoutAdversarialScenariosTests
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
        var content = File.ReadAllText(MainWindowXamlPath);
        return XDocument.Parse(content);
    }

    [Fact]
    public void All_Xaml_EventHandlers_MustExistInCodeBehind()
    {
        var doc = LoadXaml();
        var csCode = File.ReadAllText(MainWindowCsPath);

        // Find all event handlers assigned in XAML attributes
        var eventAttributes = new[]
        {
            "Click", "TextChanged", "SelectionChanged", "ItemClick"
        };

        var handlerNames = new HashSet<string>();
        foreach (var desc in doc.Descendants())
        {
            foreach (var attr in desc.Attributes())
            {
                if (eventAttributes.Contains(attr.Name.LocalName))
                {
                    handlerNames.Add(attr.Value);
                }
            }
        }

        handlerNames.Should().NotBeEmpty();
        foreach (var handler in handlerNames)
        {
            csCode.Should().Contain(handler, $"Event handler '{handler}' declared in MainWindow.xaml must be implemented in MainWindow.xaml.cs");
        }
    }

    [Fact]
    public void ColumnAlignment_HeaderPadding_CompensatesForListViewScrollbarGutter()
    {
        var doc = LoadXaml();

        // Header Border padding: "12,6,16,6" (responsive fix)
        var headerBorder = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "Border" && (string?)d.Attribute("Grid.Row") == "1");
        headerBorder.Should().NotBeNull();
        var headerPadding = headerBorder!.Attribute("Padding")?.Value;
        headerPadding.Should().Be("12,6,16,6");

        // Parse header padding components
        var headerPaddings = headerPadding!.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
        double headerLeftPad = headerPaddings[0];
        double headerRightPad = headerPaddings[2];

        // ListView item padding: "12,4,16,4"
        var listView = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "ListView" && (d.Attribute(XamlNamespace + "Name")?.Value == "FeatureListView" || d.Attribute("Name")?.Value == "FeatureListView"));
        listView.Should().NotBeNull();

        var itemStyleSetter = listView!.Descendants().FirstOrDefault(d => d.Name.LocalName == "Setter" && (string?)d.Attribute("Property") == "Padding");
        itemStyleSetter.Should().NotBeNull();
        var itemPadding = itemStyleSetter!.Attribute("Value")?.Value;
        itemPadding.Should().Be("12,4,16,4");

        var itemPaddings = itemPadding!.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
        double itemLeftPad = itemPaddings[0];
        double itemRightPad = itemPaddings[2]; // "12,4,16,4" → right = 16

        // Alignment check:
        // Left alignment must match exactly
        headerLeftPad.Should().Be(itemLeftPad, "Header left padding must match ListView item left padding for column alignment");

        // Right gutter check:
        // Both header and item now reserve 16px for scrollbar gutter — difference 0 but both 16
        headerRightPad.Should().Be(itemRightPad, "Header and item right padding must both reserve 16px for scrollbar gutter");
        headerRightPad.Should().BeInRange(12.0, 16.0, "Header right padding must match standard WinUI 3 ScrollBar width (12-16px)");
    }

    [Fact]
    public void StressTest_ExtremelyLongFeatureDescription_And_ManyIds_DoNotCorruptModelOrCalculations()
    {
        var longDesc = new string('A', 5000);
        var manyIds = Enumerable.Range(10_000_000, 100).Select(i => (long)i).ToArray();
        var idsDisplay = string.Join(", ", manyIds);

        var item = new FeatureItem
        {
            Group = "Stress Test Group That Is Very Long To Verify Layout",
            BuildLabel = "Build 99999.99999.ExtraLongSubBuild",
            Description = longDesc,
            IDsDisplay = idsDisplay,
            IDs = manyIds,
            IsSelected = true
        };

        item.Group.Should().NotBeNullOrEmpty();
        item.BuildLabel.Should().NotBeNullOrEmpty();
        item.Description.Length.Should().Be(5000);
        item.IDs.Length.Should().Be(100);

        var filterService = new FeatureFilterService();
        var list = new List<FeatureItem> { item };

        // Test filtering against long strings
        var filteredByDesc = filterService.Filter(list, "AAAA", null).ToList();
        filteredByDesc.Should().HaveCount(1);

        var filteredById = filterService.Filter(list, "10000050", null).ToList();
        filteredById.Should().HaveCount(1);

        var summary = filterService.CalculateSummary(list, list);
        summary.SelectedCount.Should().Be(1);
        summary.TotalCount.Should().Be(1);
        summary.UniqueSelectedIdCount.Should().Be(100);
        summary.SelectionPercentage.Should().Be(100.0);
    }

    [Fact]
    public void ExpanderHeaderToolbar_MaintainsProgressAndClearButtonAccess_WhenCollapsed()
    {
        var doc = LoadXaml();
        var expander = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "Expander" && (d.Attribute(XamlNamespace + "Name")?.Value == "LogExpander" || d.Attribute("Name")?.Value == "LogExpander"));
        expander.Should().NotBeNull();

        var header = expander!.Element(expander.Name.Namespace + "Expander.Header");
        header.Should().NotBeNull();

        // Verify ProgressBar remains in Expander.Header for persistent visibility (a11y fix keeps it there)
        var progressBar = header!.Descendants().FirstOrDefault(d => d.Name.LocalName == "ProgressBar" && (d.Attribute(XamlNamespace + "Name")?.Value == "RunProgress" || d.Attribute("Name")?.Value == "RunProgress"));
        progressBar.Should().NotBeNull();

        // A11y fix: ClearLogBtn must NOT be nested inside Expander.Header (nested interactive)
        var headerClearBtn = header.Descendants().FirstOrDefault(d => d.Name.LocalName == "Button" && (d.Attribute(XamlNamespace + "Name")?.Value == "ClearLogBtn" || d.Attribute("Name")?.Value == "ClearLogBtn"));
        headerClearBtn.Should().BeNull("ClearLogBtn must not be nested inside Expander.Header for a11y - moved to Expander content footer");

        var clearBtn = expander.Descendants().FirstOrDefault(d => d.Name.LocalName == "Button" && (d.Attribute(XamlNamespace + "Name")?.Value == "ClearLogBtn" || d.Attribute("Name")?.Value == "ClearLogBtn"));
        clearBtn.Should().NotBeNull("ClearLogBtn must be in Expander content footer");

        // Expander Content should contain the scrollable log text with bounded height (responsive: Min 80, Max 200, auto)
        var contentGrid = expander.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid");
        contentGrid.Should().NotBeNull("Expander must have content Grid");
        var maxHeight = contentGrid!.Attribute("MaxHeight")?.Value;
        var minHeight = contentGrid.Attribute("MinHeight")?.Value;
        // Accept either legacy fixed Height 130 or new responsive bounds
        var isResponsive = maxHeight == "200" && minHeight == "80";
        var isLegacy = (string?)contentGrid.Attribute("Height")?.Value == "130";
        (isResponsive || isLegacy).Should().BeTrue("Expander body must have bounded height (responsive Min 80 Max 200 or legacy Height 130) to prevent layout distortion");
    }

    [Fact]
    public void SidebarCardConsolidation_VerifiesAllThreeCardsArePresentWithProperSpacing()
    {
        var doc = LoadXaml();
        var scrollViewer = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == "ScrollViewer" && (string?)d.Attribute("Grid.Column") == "2");
        scrollViewer.Should().NotBeNull();

        var stackPanel = scrollViewer!.Element(scrollViewer.Name.Namespace + "StackPanel");
        stackPanel.Should().NotBeNull();
        stackPanel!.Attribute("Spacing")?.Value.Should().Be("10");

        var cards = stackPanel.Elements().Where(e => e.Name.LocalName == "Border").ToList();
        cards.Should().HaveCount(3, "Right sidebar must contain exactly 3 consolidated cards");

        // Card 1: ViVeTool Engine
        var card1 = cards[0];
        card1.Descendants().Any(d => d.Name.LocalName == "TextBlock" && (string?)d.Attribute("Text") == "ViVeTool Engine").Should().BeTrue();
        card1.Descendants().Any(d => d.Name.LocalName == "Button" && (d.Attribute(XamlNamespace + "Name")?.Value == "EnableBtn" || d.Attribute("Name")?.Value == "EnableBtn")).Should().BeTrue();
        card1.Descendants().Any(d => d.Name.LocalName == "Button" && (d.Attribute(XamlNamespace + "Name")?.Value == "DisableBtn" || d.Attribute("Name")?.Value == "DisableBtn")).Should().BeTrue();

        // Card 2: Selection Overview
        var card2 = cards[1];
        card2.Descendants().Any(d => d.Name.LocalName == "TextBlock" && (string?)d.Attribute("Text") == "Selection Overview").Should().BeTrue();
        card2.Descendants().Any(d => d.Name.LocalName == "ProgressBar" && (d.Attribute(XamlNamespace + "Name")?.Value == "SelectionProgress" || d.Attribute("Name")?.Value == "SelectionProgress")).Should().BeTrue();

        // Card 3: Execution Options
        var card3 = cards[2];
        card3.Descendants().Any(d => d.Name.LocalName == "TextBlock" && (string?)d.Attribute("Text") == "Execution Options").Should().BeTrue();
        card3.Descendants().Any(d => d.Name.LocalName == "ToggleSwitch" && (d.Attribute(XamlNamespace + "Name")?.Value == "WhatIfToggle" || d.Attribute("Name")?.Value == "WhatIfToggle")).Should().BeTrue();
        card3.Descendants().Any(d => d.Name.LocalName == "ToggleSwitch" && (d.Attribute(XamlNamespace + "Name")?.Value == "RestartExplorerToggle" || d.Attribute("Name")?.Value == "RestartExplorerToggle")).Should().BeTrue();
    }
}
