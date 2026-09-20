using FluentAssertions;
using PChabit.Core.Sync;
using Xunit;

namespace PChabit.Tests.Sync;

public class BrowserDeletionDetectionTests
{
    private static FlatItem Bookmark(string url, string title = "t") => new()
    {
        Type = FlatItemType.Bookmark,
        Title = title,
        Url = url,
        Path = new List<string> { "书签栏" },
        Key = "b:" + BookmarkMergePlanner.NormalizeUrl(url)
    };

    [Fact]
    public void MissingFromExport_DetectedAsDeletion()
    {
        var last = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem> { Bookmark("https://a.com"), Bookmark("https://b.com") }
        };
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem> { Bookmark("https://a.com") }
        };
        var deleted = BookmarkMergePlanner.DetectBrowserDeletions(now, last);
        deleted.Should().Contain(BookmarkMergePlanner.ItemKey(Bookmark("https://b.com")));
    }

    [Fact]
    public void FirstExport_NoDeletions()
    {
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem> { Bookmark("https://a.com") }
        };
        BookmarkMergePlanner.DetectBrowserDeletions(now, null).Should().BeEmpty();
        BookmarkMergePlanner.DetectBrowserDeletions(null, now).Should().BeEmpty();
    }

    [Fact]
    public void FolderNeverDeleted()
    {
        var folder = new FlatItem
        {
            Type = FlatItemType.Folder,
            Title = "Dev",
            Path = new List<string> { "书签栏" },
            Key = "f:书签栏/Dev"
        };
        var last = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chrome"] = new List<FlatItem> { folder, Bookmark("https://ok.com") }
        };
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chrome"] = new List<FlatItem> { Bookmark("https://ok.com") }
        };
        BookmarkMergePlanner.DetectBrowserDeletions(now, last).Should().BeEmpty();
    }

    [Fact]
    public void SpecialUrls_NotDeleted()
    {
        var last = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chrome"] = new List<FlatItem> { Bookmark("chrome://bookmarks"), Bookmark("https://normal.com") }
        };
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chrome"] = new List<FlatItem>()
        };
        var deleted = BookmarkMergePlanner.DetectBrowserDeletions(now, last);
        deleted.Should().Contain(BookmarkMergePlanner.ItemKey(Bookmark("https://normal.com")));
        deleted.Should().NotContain(k => k.Contains("chrome://"));
    }

    [Fact]
    public void BrowserNotInCurrentExport_Skipped()
    {
        var last = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem> { Bookmark("https://x.com") }
        };
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chrome"] = new List<FlatItem> { Bookmark("https://keep.com") }
        };
        var deleted = BookmarkMergePlanner.DetectBrowserDeletions(now, last);
        deleted.Should().NotContain(BookmarkMergePlanner.ItemKey(Bookmark("https://x.com")));
    }

    [Fact]
    public void UnreliableExport_DoesNotDetectDeletions()
    {
        BookmarkMergePlanner.IsExportReliableForDeletions(10, 200).Should().BeFalse();
        BookmarkMergePlanner.IsExportReliableForDeletions(100, 200).Should().BeTrue();
        BookmarkMergePlanner.IsExportReliableForDeletions(5, 10).Should().BeTrue();

        var last = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = Enumerable.Range(0, 100)
                .Select(i => Bookmark($"https://e{i}.com")).ToList()
        };
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem> { Bookmark("https://e0.com") }
        };
        BookmarkMergePlanner.DetectBrowserDeletions(now, last).Should().BeEmpty();
    }
}
