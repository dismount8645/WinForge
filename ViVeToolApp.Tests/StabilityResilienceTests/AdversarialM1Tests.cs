using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using ViVeToolApp.Models;
using ViVeToolApp.Services;
using Xunit;

namespace ViVeToolApp.Tests.StabilityResilienceTests;

/// <summary>
/// Comprehensive adversarial stress tests for Milestone 1:
/// PureinfotechScraper, OfflineCatalog, and FeatureFilterService.
/// </summary>
public class AdversarialM1Tests
{
    private readonly PureinfotechScraper _scraper = new();
    private readonly FeatureFilterService _filterService = new();

    #region Category 1: PureinfotechScraper Adversarial HTML Stress Tests

    [Fact]
    public void ParseHtml_DeceptiveScriptAndStyleBlocks_StripsDeceptiveContentWithoutExtractingFakes()
    {
        var html = """
            <div class="entry-content">
                <script>
                    var fakeFeature = "<code>99999991</code> Fake Script Feature";
                    function test() { return '<code>99999992</code> Another script feature'; }
                </script>
                <style>
                    .someClass:before { content: "<code>99999993</code> CSS code feature"; }
                </style>
                <h3>GA 2026</h3>
                <p><strong>September 2026</strong></p>
                <ul>
                    <li><code>61754985</code> Real feature that should be parsed</li>
                </ul>
            </div>
            <!-- CONTENT END -->
            """;

        var results = _scraper.ParseHtml(html);

        results.Should().HaveCount(1);
        results[0].IDs.Should().Equal(61754985);
        results[0].Description.Should().Be("Real feature that should be parsed");
        results.Should().NotContain(f => f.IDs.Contains(99999991));
        results.Should().NotContain(f => f.IDs.Contains(99999992));
        results.Should().NotContain(f => f.IDs.Contains(99999993));
    }

    [Fact]
    public void ParseHtml_HeavilyCorruptedAndBrokenTags_DoesNotThrowAndParsesValidContent()
    {
        var corruptedHtml = """
            <div class="entry-content" <broken attribute>>>
                <h3 GA 2026 Update features <unclosed tag
                <p><strong<strong>September 2026</strong>:
                <ul>
                    <li class='broken" <tag><code>61754985</code> Unclosed list item with nested <b>bold</b> and <i>italic</i> formatting
                    <li><code>62762248</code> Missing closing li tag
                    <li><code>27829265, 61457898</code> Double code blocks <code>59213768</code> in same item
                    <li><code without closing tag 99999999
                    <li>No code tag in this item at all
                    <li><code>abcdef, !@#$%^&*()</code> Non-numeric code
                </ul>
            """;

        var act = () => _scraper.ParseHtml(corruptedHtml);
        act.Should().NotThrow();

        var results = act();
        results.Should().NotBeEmpty();
        results.Should().Contain(f => f.IDs.Contains(61754985));
        results.Should().Contain(f => f.IDs.Contains(62762248));
    }

    [Fact]
    public void ParseHtml_MissingEntryContentMarker_ParsesWholeDocumentGracefully()
    {
        var html = """
            <html>
            <body>
                <header>Some site header</header>
                <main>
                    <h3>26H2 Insider</h3>
                    <strong>Build 26300.8697:</strong>
                    <li><code>61267302, 61344081</code> Search web toggle</li>
                </main>
            </body>
            </html>
            """;

        var results = _scraper.ParseHtml(html);

        results.Should().HaveCount(1);
        results[0].Group.Should().Be("26H2 Insider");
        results[0].BuildLabel.Should().Be("Build 26300.8697");
        results[0].IDs.Should().Equal(61267302, 61344081);
    }

    [Fact]
    public void ParseHtml_EntityBombardmentAndSurrogatePairs_DecodesSafelyWithoutThrowing()
    {
        var html = """
            <div class="entry-content">
                <h3>GA 2026 &amp; &lt;Special&#8217;s&gt;</h3>
                <strong>Build 26300:</strong>
                <li><code>61754985</code> Feature with &#0; &#99999999; &#xZZZZ; &invalid; &#x1F600; 😀 &#8216;Smart Quotes&#8217; &quot;quoted&quot; &amp; &lt;&gt;</li>
            </div>
            """;

        var act = () => _scraper.ParseHtml(html);
        act.Should().NotThrow();

        var results = act();
        results.Should().HaveCount(1);
        results[0].Description.Should().Contain("Smart Quotes");
        results[0].Description.Should().Contain("\"quoted\"");
    }

    [Theory]
    [InlineData("<code>0</code> Zero ID", 0)]
    [InlineData("<code>999999</code> 6-digit ID (below 1M)", 0)]
    [InlineData("<code>1000000000</code> 10-digit ID (above 999M)", 0)]
    [InlineData("<code>9999999999999999999999999999999999999999</code> Huge numeric overflow", 0)]
    [InlineData("<code>1000000</code> Minimum valid ID (1M)", 1)]
    [InlineData("<code>999999999</code> Maximum valid ID (999.999M)", 1)]
    [InlineData("<code>61754985, 61754985, 61754985</code> Duplicate IDs in same code tag", 1)]
    public void ParseHtml_BoundaryAndOverflowIds_EnforcesValidRangeStrictly(string liInnerHtml, int expectedFeatureCount)
    {
        var html = $"<div class=\"entry-content\"><li>{liInnerHtml}</li></div>";
        var results = _scraper.ParseHtml(html);

        results.Should().HaveCount(expectedFeatureCount);
    }

    [Fact]
    public void ParseHtml_NestedMarkupInHeadingsAndLabels_ExtractsCleanGroupAndBuild()
    {
        var html = """
            <div class="entry-content">
                <H3><span>Windows 11 <i>version</i> <b>26H2</b> (Dev / Beta)</span></H3>
                <p><strong><span style="color:red;">Build 26300.8697</span>:</strong></p>
                <ul>
                    <li><code class="language-bash" data-attr="test">61267302</code> <b>Bold</b> and <i>Italic</i> feature description with <a href="https://example.com">link</a></li>
                </ul>
            </div>
            <!-- CONTENT END -->
            """;

        var results = _scraper.ParseHtml(html);

        results.Should().HaveCount(1);
        results[0].Group.Should().Be("26H2 Insider");
        results[0].BuildLabel.Should().Be("Build 26300.8697");
        results[0].Description.Should().Be("Bold and Italic feature description with link");
        results[0].IDs.Should().Equal(61267302);
    }

    [Fact]
    public void ParseHtml_MultipleCodeBlocksInSingleListItem_CombinesAndParsesAllIds()
    {
        var html = """
            <div class="entry-content">
                <h3>GA 2026</h3>
                <p><strong>September 2026</strong></p>
                <ul>
                    <li>Primary ID: <code>61754985</code> Secondary IDs: <code>62762248, 59213768</code> - Multiple codes feature</li>
                </ul>
            </div>
            """;

        var results = _scraper.ParseHtml(html);

        results.Should().HaveCount(1);
        results[0].IDs.Should().Equal(61754985, 62762248, 59213768);
        results[0].IDsDisplay.Should().Be("61754985, 62762248, 59213768");
        results[0].Description.Should().Be("Primary ID: Secondary IDs: - Multiple codes feature");
    }

    [Fact]
    public void ParseHtml_NullCharactersAndBOM_HandlesSafely()
    {
        var html = "\uFEFF<div class=\"entry-content\"><h3>GA 2026\0</h3><li><code>61754985</code> Feature\0 with null bytes</li></div>";

        var act = () => _scraper.ParseHtml(html);
        act.Should().NotThrow();

        var results = act();
        results.Should().HaveCount(1);
        results[0].IDs.Should().Equal(61754985);
    }

    [Fact]
    public void ParseHtml_LargeDocumentPerformanceAndReDoSStress_CompletesQuickly()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"entry-content\">");
        for (int i = 0; i < 500; i++)
        {
            sb.AppendLine($"<h3>Section {i} Windows 11 2026 Update features</h3>");
            sb.AppendLine($"<p><strong>September 2026 - Batch {i}:</strong></p>");
            sb.AppendLine("<ul>");
            for (int j = 0; j < 5; j++)
            {
                var id1 = 10000000 + (i * 5) + j;
                var id2 = 20000000 + (i * 5) + j;
                sb.AppendLine($"<li><code>{id1}, {id2}</code> Stress feature {i}-{j} with long descriptive text explaining the Windows feature toggle mechanism in detail</li>");
            }
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("</div><!-- CONTENT END -->");

        var largeHtml = sb.ToString();

        var sw = Stopwatch.StartNew();
        var results = _scraper.ParseHtml(largeHtml);
        sw.Stop();

        results.Should().HaveCount(2500);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "Parsing 2,500 features across 500 sections should take under 2 seconds");
    }

    #endregion

    #region Category 2: FeatureFilterService Adversarial Input Stress Tests

    [Theory]
    [InlineData(".*")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("\\d+")]
    [InlineData("$")]
    [InlineData("^")]
    [InlineData("{}")]
    [InlineData("{0,5}")]
    [InlineData("\\")]
    [InlineData("?")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("|")]
    [InlineData("(?=.*[a-z])")]
    [InlineData("[a-z0-9_-]+")]
    [InlineData("\\p{L}+")]
    public void Filter_RegexSpecialCharactersInSearchQuery_DoesNotThrowAndTreatsAsLiteralSubstring(string regexPattern)
    {
        var features = new List<FeatureItem>
        {
            new() { Description = $"Feature with literal {regexPattern} in text", Group = "GA 2026", IDsDisplay = "61754985", IDs = new long[] { 61754985 } },
            new() { Description = "Standard feature without special chars", Group = "GA 2026", IDsDisplay = "62762248", IDs = new long[] { 62762248 } }
        };

        var act = () => _filterService.Filter(features, regexPattern, "All Tracks").ToList();
        act.Should().NotThrow();

        var results = act();
        results.Should().ContainSingle().Which.Description.Should().Contain(regexPattern);
    }

    [Theory]
    [InlineData("🔥 Unicode emoji test 🚀")]
    [InlineData("Тестирование поиска на русском")]
    [InlineData("Windows 11 功能测试")]
    [InlineData("اختبار ميزة ويندوز")]
    public void Filter_MultiLanguageAndUnicodeSearch_MatchesCorrectlyWithoutCrashing(string unicodeQuery)
    {
        var features = new List<FeatureItem>
        {
            new() { Description = $"Prefix {unicodeQuery} Suffix", Group = "GA 2026", IDsDisplay = "61754985", IDs = new long[] { 61754985 } },
            new() { Description = "Non-matching feature", Group = "GA 2026", IDsDisplay = "62762248", IDs = new long[] { 62762248 } }
        };

        var results = _filterService.Filter(features, unicodeQuery, "All Tracks").ToList();
        results.Should().ContainSingle().Which.Description.Should().Contain(unicodeQuery);
    }

    [Fact]
    public void Filter_ExtremelyLongSearchQuery_ExecutesQuicklyWithoutCrashing()
    {
        var longSearch = new string('a', 50000);
        var features = new List<FeatureItem>
        {
            new() { Description = "Feature description", Group = "GA 2026", IDsDisplay = "61754985", IDs = new long[] { 61754985 } }
        };

        var act = () => _filterService.Filter(features, longSearch, "All Tracks").ToList();
        act.Should().NotThrow();

        var results = act();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Filter_NullAndEmptyInputs_HandlesAllCombinationsGracefully()
    {
        var features = new List<FeatureItem>
        {
            new() { Description = "Item 1", Group = "GA 2026", IDsDisplay = "61754985", IDs = new long[] { 61754985 } }
        };

        // Null collection
        _filterService.Filter(null!, "query", "All Tracks").Should().BeEmpty();

        // Null/Empty search query and track
        _filterService.Filter(features, null, null).Should().HaveCount(1);
        _filterService.Filter(features, "", "").Should().HaveCount(1);
        _filterService.Filter(features, "   ", "   ").Should().HaveCount(1);

        // Non-matching track
        _filterService.Filter(features, null, "NonExistentTrack").Should().BeEmpty();
    }

    [Fact]
    public void CalculateSummary_ZeroTotalCountAndEmptyCollection_AvoidsZeroDivision()
    {
        var empty = Enumerable.Empty<FeatureItem>();

        var summary = _filterService.CalculateSummary(empty, empty);

        summary.TotalCount.Should().Be(0);
        summary.VisibleCount.Should().Be(0);
        summary.SelectedCount.Should().Be(0);
        summary.CheckedCount.Should().Be(0);
        summary.SelectedPercentage.Should().Be(0.0);
        summary.SelectionPercentage.Should().Be(0.0);
        summary.UniqueSelectedIdsCount.Should().Be(0);
        summary.UniqueSelectedIdCount.Should().Be(0);
        summary.FormattedSummary.Should().Be("Visible 0 of 0  ·  Checked: 0");
        double.IsNaN(summary.SelectedPercentage).Should().BeFalse();
        double.IsInfinity(summary.SelectedPercentage).Should().BeFalse();
    }

    [Fact]
    public void CalculateSummary_NullCollections_HandledGracefullyWithoutCrashing()
    {
        var summary = _filterService.CalculateSummary(null!, null!);

        summary.TotalCount.Should().Be(0);
        summary.VisibleCount.Should().Be(0);
        summary.SelectedCount.Should().Be(0);
        summary.SelectedPercentage.Should().Be(0.0);
        double.IsNaN(summary.SelectedPercentage).Should().BeFalse();
    }

    [Fact]
    public void GetDistinctSelectedFeatureIds_NullAndEmptyInputs_ReturnsEmptyList()
    {
        _filterService.GetDistinctSelectedFeatureIds(null!).Should().BeEmpty();
        _filterService.GetDistinctSelectedFeatureIds(Enumerable.Empty<FeatureItem>()).Should().BeEmpty();

        var featuresWithEmptyIds = new List<FeatureItem>
        {
            new() { IsSelected = true, IDs = Array.Empty<long>() },
            new() { IsSelected = false, IDs = new long[] { 61754985 } },
            new() { IsSelected = true, IDs = new long[] { 0, -1 } }
        };

        _filterService.GetDistinctSelectedFeatureIds(featuresWithEmptyIds).Should().BeEmpty();
    }

    [Fact]
    public void GetDistinctSelectedFeatureIds_MultiItemWithSharedIds_DeduplicatesAndSortsAscending()
    {
        var features = new List<FeatureItem>
        {
            new() { IsSelected = true, IDs = new long[] { 62000000, 61000000 } },
            new() { IsSelected = true, IDs = new long[] { 61000000, 63000000 } },
            new() { IsSelected = false, IDs = new long[] { 99999999 } }
        };

        var ids = _filterService.GetDistinctSelectedFeatureIds(features);
        ids.Should().Equal(61000000, 62000000, 63000000);
    }

    [Fact]
    public void GetDistinctGroups_NullAndEmptyInputs_HandlesWhitespaceAndDuplicatesCorrectly()
    {
        _filterService.GetDistinctGroups(null!).Should().BeEmpty();
        _filterService.GetDistinctGroups(Enumerable.Empty<FeatureItem>()).Should().BeEmpty();

        var features = new List<FeatureItem>
        {
            new() { Group = "" },
            new() { Group = "   " },
            new() { Group = "GA 2026" },
            new() { Group = "ga 2026" }, // Case variation
            new() { Group = "26H2 Insider" }
        };

        var groups = _filterService.GetDistinctGroups(features);
        groups.Should().Equal("26H2 Insider", "GA 2026");
    }

    [Theory]
    [InlineData("All Tracks")]
    [InlineData("all tracks")]
    [InlineData("ALL TRACKS")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SetGroupSelection_AllTracksVariants_SelectsAllFeatures(string? trackVariant)
    {
        var features = new List<FeatureItem>
        {
            new() { Group = "GA 2026", IsSelected = false },
            new() { Group = "26H2 Insider", IsSelected = false },
            new() { Group = "Canary", IsSelected = false }
        };

        _filterService.SetGroupSelection(features, trackVariant!, true);

        features.Should().OnlyContain(f => f.IsSelected == true);
    }

    [Fact]
    public void SetSelection_And_SetGroupSelection_NullCollectionSafety()
    {
        var act1 = () => _filterService.SetSelection(null!, true);
        act1.Should().NotThrow();

        var act2 = () => _filterService.SetGroupSelection(null!, "GA 2026", true);
        act2.Should().NotThrow();
    }

    [Fact]
    public void FilterService_HighVolumeThroughput_Filters50KItemsQuickly()
    {
        var largeList = new List<FeatureItem>(50000);
        for (int i = 0; i < 50000; i++)
        {
            largeList.Add(new FeatureItem
            {
                Group = i % 2 == 0 ? "GA 2026" : "26H2 Insider",
                BuildLabel = $"Build {26000 + (i % 100)}",
                Description = $"Feature description number {i} for Windows 11 testing",
                IDsDisplay = $"{60000000 + i}",
                IDs = new long[] { 60000000 + i },
                IsSelected = i % 3 == 0
            });
        }

        var sw = Stopwatch.StartNew();
        var filtered = _filterService.Filter(largeList, "number 123", "GA 2026").ToList();
        var summary = _filterService.CalculateSummary(filtered, largeList);
        var ids = _filterService.GetDistinctSelectedFeatureIds(filtered);
        sw.Stop();

        filtered.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(250, "Filtering and summarizing 50,000 items should take under 250ms");
    }

    #endregion

    #region Category 3: OfflineCatalog Adversarial & Concurrency Tests

    [Fact]
    public void OfflineCatalog_GetFeatures_ReturnsFreshInstancesOnEveryCall()
    {
        var list1 = OfflineCatalog.GetFeatures();
        var list2 = OfflineCatalog.GetFeatures();

        list1.Should().NotBeSameAs(list2);
        list1[0].Should().NotBeSameAs(list2[0]);

        // Mutating list1 must NOT affect list2
        list1[0].IsSelected = false;
        list1[0].Description = "Mutated Description";
        list1[0].IDs = new long[] { 99999999 };

        list2[0].IsSelected.Should().BeTrue();
        list2[0].Description.Should().NotBe("Mutated Description");
        list2[0].IDs.Should().NotEqual(new long[] { 99999999 });
    }

    [Fact]
    public void OfflineCatalog_GetFallbackFeaturesAlias_MatchesGetFeaturesExactly()
    {
        var primary = OfflineCatalog.GetFeatures();
        var fallback = OfflineCatalog.GetFallbackFeatures();

        primary.Should().HaveCount(fallback.Count);
        for (int i = 0; i < primary.Count; i++)
        {
            primary[i].Group.Should().Be(fallback[i].Group);
            primary[i].BuildLabel.Should().Be(fallback[i].BuildLabel);
            primary[i].Description.Should().Be(fallback[i].Description);
            primary[i].IDsDisplay.Should().Be(fallback[i].IDsDisplay);
            primary[i].IDs.Should().Equal(fallback[i].IDs);
        }
    }

    [Fact]
    public async Task OfflineCatalog_ConcurrentAccess_IsThreadSafe()
    {
        var tasks = new Task[50];
        var allResults = new List<FeatureItem>[50];

        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                allResults[index] = OfflineCatalog.GetFeatures();
            });
        }

        await Task.WhenAll(tasks);

        for (int i = 0; i < 50; i++)
        {
            allResults[i].Should().HaveCount(15);
            allResults[i].Should().OnlyContain(f => f.IDs.Length > 0);
        }
    }

    [Fact]
    public void OfflineCatalog_AllCatalogEntriesHaveValidTracksAndIds()
    {
        var features = OfflineCatalog.GetFeatures();

        features.Should().HaveCount(15);
        var validTracks = new HashSet<string>
        {
            "GA 2026",
            "GA 2025",
            "26H2 Insider",
            "25H2 Insider",
            "Canary / Feature Platforms"
        };

        foreach (var item in features)
        {
            validTracks.Should().Contain(item.Group);
            item.BuildLabel.Should().NotBeNullOrWhiteSpace();
            item.Description.Should().NotBeNullOrWhiteSpace();
            item.IDs.Should().NotBeEmpty();
            item.IDs.Should().OnlyContain(id => id >= 1_000_000 && id <= 999_999_999);
            item.IsSelected.Should().BeTrue();
        }
    }

    #endregion
}
