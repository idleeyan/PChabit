using FluentAssertions;
using PChabit.Core.Sync;
using Xunit;

namespace PChabit.Tests.Sync;

public class BookmarkFolderSyncTests
{
    private static FlatItem Folder(string title, params string[] parentPath)
        => new()
        {
            Type = FlatItemType.Folder,
            Title = title,
            Path = parentPath.ToList(),
            Key = "f:" + string.Join("/", parentPath.Append(title))
        };

    private static FlatItem Bookmark(string url, params string[] path)
        => new()
        {
            Type = FlatItemType.Bookmark,
            Title = url,
            Url = url,
            Path = path.ToList(),
            Key = "b:" + BookmarkMergePlanner.NormalizeUrl(url)
        };

    [Fact]
    public void PruneOrphanFolders_RemovesEmptyKeepsUsed()
    {
        var items = new List<FlatItem>
        {
            Folder("书签栏"),
            Folder("Dev", "书签栏"),
            Folder("Empty", "书签栏"),
            Bookmark("https://a.com", "书签栏", "Dev")
        };
        var pruned = BookmarkMergePlanner.PruneOrphanFolders(items);
        pruned.Any(i => i.Type == FlatItemType.Folder && i.Title == "Empty").Should().BeFalse();
        pruned.Any(i => i.Type == FlatItemType.Folder && i.Title == "Dev").Should().BeTrue();
        pruned.Any(i => i.Type == FlatItemType.Folder && i.Title == "书签栏").Should().BeTrue();
    }

    [Fact]
    public void CanonicalSegment_MapsRootAliases()
    {
        BookmarkMergePlanner.CanonicalSegment("收藏夹栏").Should().Be("@bar");
        BookmarkMergePlanner.CanonicalSegment("书签栏").Should().Be("@bar");
        BookmarkMergePlanner.CanonicalSegment("工具").Should().Be("工具");
    }

    [Fact]
    public void ComputeFoldersToRemove_AliasPathWithContent_NotDeleted()
    {
        var local = new List<FlatItem> { Folder("工具", "收藏夹栏") };
        var merged = new List<FlatItem>
        {
            Folder("工具", "书签栏"),
            Bookmark("https://tool.com", "书签栏", "工具")
        };
        var remove = BookmarkMergePlanner.ComputeFoldersToRemove(local, merged, null);
        remove.Should().BeEmpty();
    }

    [Fact]
    public void ComputeFoldersToRemove_ExplicitKeyOnly()
    {
        var local = new List<FlatItem> { Folder("Empty", "书签栏"), Folder("Keep", "书签栏") };
        var merged = new List<FlatItem>
        {
            Folder("Keep", "书签栏"),
            Bookmark("https://z.com", "书签栏", "Keep")
        };
        var emptyKey = BookmarkMergePlanner.EnsureKey(Folder("Empty", "书签栏"));
        var remove = BookmarkMergePlanner.ComputeFoldersToRemove(local, merged, new[] { emptyKey });
        remove.Should().ContainSingle(f => f.Title == "Empty");
        remove.Should().NotContain(f => f.Title == "Keep");
    }

    [Fact]
    public void DetectFolderDeletions_AliasRoot_NotFalsePositive()
    {
        // 上次 Edge 用「收藏夹栏」，本次仍导出同逻辑文件夹（别名可能变化）→ 不判删除
        var last = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem> { Folder("工具", "收藏夹栏") }
        };
        var now = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"] = new List<FlatItem>
            {
                Folder("工具", "书签栏"),
                Bookmark("https://x.com", "书签栏", "工具")
            }
        };
        BookmarkMergePlanner.DetectBrowserFolderDeletions(now, last).Should().BeEmpty();
    }
}
