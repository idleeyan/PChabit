using FluentAssertions;
using PChabit.Core.Sync;
using Xunit;

namespace PChabit.Tests.Sync;

public class BookmarkMergePlannerTests
{
    private static FlatItem Bookmark(string url, string title = "t", long modified = 100, string? id = null)
    {
        var item = new FlatItem
        {
            Type = FlatItemType.Bookmark,
            Id = id ?? "",
            Title = title,
            Url = url,
            Path = new List<string> { "书签栏" },
            DateModified = modified
        };
        item.Key = BookmarkMergePlanner.ItemKey(item);
        return item;
    }

    [Fact]
    public void NormalizeUrl_StripsTrailingSlash()
    {
        BookmarkMergePlanner.NormalizeUrl("https://example.com/foo/").Should().Be("https://example.com/foo");
        BookmarkMergePlanner.NormalizeUrl("  not-a-url  ").Should().Be("not-a-url");
    }

    [Fact]
    public void ItemKey_BookmarkAndFolder()
    {
        BookmarkMergePlanner.ItemKey(Bookmark("https://example.com/foo")).Should().Be("b:https://example.com/foo");
        var folder = new FlatItem { Type = FlatItemType.Folder, Title = "Dev", Path = new List<string> { "书签栏" } };
        BookmarkMergePlanner.ItemKey(folder).Should().Be("f:书签栏/Dev");
    }

    [Theory]
    [InlineData("chrome://bookmarks")]
    [InlineData("edge://settings")]
    [InlineData("javascript:alert(1)")]
    public void ShouldSkipUrl_Internal(string url)
        => BookmarkMergePlanner.ShouldSkipUrl(url).Should().BeTrue();

    [Fact]
    public void ShouldSkipUrl_NormalHttp_False()
        => BookmarkMergePlanner.ShouldSkipUrl("https://example.com").Should().BeFalse();

    [Fact]
    public void FirstSync_Union_NoDeletes()
    {
        var local = new List<FlatItem> { Bookmark("https://a.com", "A") };
        var cloud = new List<FlatItem> { Bookmark("https://b.com", "B") };
        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last: null);
        plan.MergedCloud.Should().HaveCount(2);
        plan.ToRemoveLocal.Should().BeEmpty();
        plan.ToAddLocal.Should().ContainSingle(i => i.Url == "https://b.com");
    }

    [Fact]
    public void EmptyLast_StillUnion()
    {
        var local = new List<FlatItem> { Bookmark("https://a.com") };
        var cloud = new List<FlatItem> { Bookmark("https://b.com") };
        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last: Array.Empty<FlatItem>());
        plan.MergedCloud.Should().HaveCount(2);
    }

    [Fact]
    public void BothHave_KeepsItem()
    {
        var local = new List<FlatItem> { Bookmark("https://a.com", "A") };
        var cloud = new List<FlatItem> { Bookmark("https://a.com", "A") };
        var last = new List<FlatItem> { Bookmark("https://a.com", "A") };
        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last);
        plan.MergedCloud.Should().ContainSingle(i => i.Url == "https://a.com");
        plan.ToRemoveLocal.Should().BeEmpty();
    }

    [Fact]
    public void TitleConflict_LocalNewer_WinsInMergedCloud()
    {
        var url = "https://rename-me.com";
        var local = new List<FlatItem> { Bookmark(url, "新名字", modified: 2000, id: "id1") };
        var cloud = new List<FlatItem> { Bookmark(url, "旧名字", modified: 1000) };
        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last: null);
        var merged = plan.MergedCloud.Single(i => i.Type == FlatItemType.Bookmark);
        merged.Title.Should().Be("新名字");
        plan.ToRenameLocal.Should().BeEmpty();
    }

    [Fact]
    public void CloudPartialVsLargeBaseline_NoMassDelete()
    {
        // 事故场景：云端只剩 2 条，本地/基线 10 条 → 不得 ToRemoveLocal
        var local = new List<FlatItem>();
        var last = new List<FlatItem>();
        var cloud = new List<FlatItem>
        {
            Bookmark("https://a.com", "A", 1),
            Bookmark("https://b.com", "B", 1)
        };
        for (var i = 0; i < 10; i++)
        {
            var item = Bookmark($"https://site{i}.com", $"S{i}", 10);
            local.Add(item);
            last.Add(Bookmark($"https://site{i}.com", $"S{i}", 10));
        }

        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last);
        plan.ToRemoveLocal.Should().BeEmpty();
        // union: 10 local + 2 cloud-only
        plan.MergedCloud.Count(i => i.Type == FlatItemType.Bookmark).Should().Be(12);
    }

    [Fact]
    public void CloudEmptyWithBaseline_DoesNotMassDelete()
    {
        var url = "https://keep-me.com";
        var local = new List<FlatItem> { Bookmark(url, "A", 100) };
        var last = new List<FlatItem> { Bookmark(url, "A", 100) };
        var cloud = new List<FlatItem>(); // 云端读空

        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last);
        plan.ToRemoveLocal.Should().BeEmpty();
        plan.MergedCloud.Should().Contain(i => i.Url == url);
    }

    [Fact]
    public void TitleConflict_CloudNewer_RenamesLocal()
    {
        var url = "https://rename-me.com";
        var local = new List<FlatItem> { Bookmark(url, "旧名字", modified: 1000, id: "id1") };
        var cloud = new List<FlatItem> { Bookmark(url, "云端新名", modified: 3000) };
        var plan = BookmarkMergePlanner.PlanThreeWayMerge(local, cloud, last: null);
        plan.MergedCloud.Single(i => i.Type == FlatItemType.Bookmark).Title.Should().Be("云端新名");
        plan.ToRenameLocal.Should().ContainSingle(r => r.Title == "云端新名");
    }

    [Fact]
    public void IndexByKey_PrefersNewerDateModified()
    {
        var url = "https://same.com";
        var older = Bookmark(url, "旧", modified: 1);
        var newer = Bookmark(url, "新", modified: 9);
        var plan = BookmarkMergePlanner.PlanThreeWayMerge(
            new List<FlatItem> { older, newer },
            new List<FlatItem> { older },
            last: null);
        plan.MergedCloud.Single(i => i.Type == FlatItemType.Bookmark).Title.Should().Be("新");
    }
}
