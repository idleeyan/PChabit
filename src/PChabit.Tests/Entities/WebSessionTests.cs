using PChabit.Core.Entities;
using Xunit;

namespace PChabit.Tests.Entities;

public class WebSessionTests
{
    [Fact]
    public void FocusRatio_ShouldBeZero_WhenNoDuration()
    {
        var session = new WebSession
        {
            StartTime = DateTime.Now,
            ActiveDuration = TimeSpan.Zero
        };

        Assert.Equal(0, session.FocusRatio, 3);
    }

    [Fact]
    public void FocusRatio_ShouldClampToUnitInterval()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0);
        var session = new WebSession
        {
            StartTime = start,
            EndTime = start.AddMinutes(10),
            Duration = TimeSpan.FromMinutes(10),
            ActiveDuration = TimeSpan.FromMinutes(15)
        };

        Assert.Equal(1.0, session.FocusRatio, 3);
    }

    [Fact]
    public void FocusRatio_ShouldEqualActiveOverWall()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0);
        var session = new WebSession
        {
            StartTime = start,
            EndTime = start.AddMinutes(10),
            Duration = TimeSpan.FromMinutes(10),
            ActiveDuration = TimeSpan.FromMinutes(4)
        };

        Assert.Equal(0.4, session.FocusRatio, 3);
    }

    [Fact]
    public void IdleDuration_DefaultsToZero()
    {
        var session = new WebSession();
        Assert.Equal(TimeSpan.Zero, session.IdleDuration);
    }

    [Fact]
    public void CategoryMaterialization_Fields_AreOptional()
    {
        var session = new WebSession();
        Assert.Null(session.CategoryId);
        Assert.Null(session.CategoryName);
        Assert.Null(session.CategorySource);
        Assert.False(session.IsLegacy);
        Assert.False(session.IsPersisted);
    }
}
