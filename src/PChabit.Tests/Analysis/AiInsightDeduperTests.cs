using FluentAssertions;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Formatters;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AiInsightDeduperTests
{
    [Fact]
    public void Filter_DropsOverlappingFinding()
    {
        var findings = new List<AnalyticsAiResponseParser.AiFinding>
        {
            new("夜间使用偏多", "你晚上用了很久电脑，注意休息"),
            new("周四专注断崖", "周四下午专注只有 20 分钟，可能是会议打断")
        };
        var rules = new List<(string, string)>
        {
            ("夜间使用偏多", "23 点后活跃 90 分钟，高于上周")
        };

        var filtered = AiInsightDeduper.FilterFindings(findings, rules);
        filtered.Should().HaveCount(1);
        filtered[0].Title.Should().Contain("周四");
    }

    [Fact]
    public void Filter_KeepsDistinctFinding()
    {
        var findings = new List<AnalyticsAiResponseParser.AiFinding>
        {
            new("浏览器娱乐挤占午后", "视频站占比上升 12%")
        };
        var rules = new List<(string, string)>
        {
            ("数据不足", "有效天数偏少")
        };
        AiInsightDeduper.FilterFindings(findings, rules).Should().HaveCount(1);
    }

    [Fact]
    public void IsDuplicate_ExactTitleContained()
    {
        AiInsightDeduper.IsDuplicate(
                "发现：夜间使用偏多需要调整作息",
                "详情……",
                new List<(string, string)> { ("夜间使用偏多", "23点后 90 分钟") })
            .Should().BeTrue();
    }
}
