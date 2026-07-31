using FluentAssertions;

namespace RefreshAhead.MemoryCache.Tests;

public sealed class RefreshAheadScheduleTests
{
    [Fact]
    public void FixedDelay_SchedulesFromPreviousCompletion()
    {
        // Arrange
        var schedule = RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(5));
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var completed = now.AddSeconds(2);

        // Act
        var nextRun = schedule.GetNextRun(now, previousScheduledRun: now, previousCompletedRun: completed);

        // Assert
        nextRun.Should().Be(completed.AddSeconds(5));
    }

    [Fact]
    public void FixedRate_SkipsMissedTicks()
    {
        // Arrange
        var schedule = RefreshAheadSchedule.FixedRate(TimeSpan.FromSeconds(1));
        var previousScheduledRun = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var now = previousScheduledRun.AddSeconds(5.2);

        // Act
        var nextRun = schedule.GetNextRun(now, previousScheduledRun, previousCompletedRun: now);

        // Assert
        nextRun.Should().Be(previousScheduledRun.AddSeconds(6));
    }

    [Fact]
    public void FixedRate_SkipsLargeMissedWindowsWithoutIteratingPerTick()
    {
        // Arrange
        var interval = TimeSpan.FromMilliseconds(1);
        var schedule = RefreshAheadSchedule.FixedRate(interval);
        var previousScheduledRun = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var now = previousScheduledRun.AddDays(1).AddTicks(1);

        // Act
        var nextRun = schedule.GetNextRun(now, previousScheduledRun, previousCompletedRun: now);

        // Assert
        nextRun.Should().Be(previousScheduledRun.AddDays(1).AddMilliseconds(1));
    }

    [Fact]
    public void HourlyAt_SchedulesNextHourlyOffset()
    {
        // Arrange
        var schedule = RefreshAheadSchedule.HourlyAt(TimeSpan.FromSeconds(10), TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 11, TimeSpan.Zero);

        // Act
        var nextRun = schedule.GetNextRun(now, previousScheduledRun: null, previousCompletedRun: null);

        // Assert
        nextRun.Should().Be(new DateTimeOffset(2026, 5, 26, 11, 0, 10, TimeSpan.Zero));
    }

    [Fact]
    public void HourlyAt_DstFallBack_SchedulesBothAmbiguousOccurrences()
    {
        // Arrange
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var schedule = RefreshAheadSchedule.HourlyAt(TimeSpan.Zero, timeZone);
        var beforeFirstOccurrence = new DateTimeOffset(2025, 11, 2, 4, 0, 0, TimeSpan.Zero);

        // Act
        var firstOccurrence = schedule.GetNextRun(beforeFirstOccurrence, null, null);
        var secondOccurrence = schedule.GetNextRun(firstOccurrence, firstOccurrence, firstOccurrence);

        // Assert
        firstOccurrence.Should().Be(new DateTimeOffset(2025, 11, 2, 5, 0, 0, TimeSpan.Zero));
        secondOccurrence.Should().Be(new DateTimeOffset(2025, 11, 2, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void FixedRate_WhenNextTickWouldOverflow_ClampsToMaximumValue()
    {
        // Arrange
        var schedule = RefreshAheadSchedule.FixedRate(TimeSpan.FromDays(2));
        var previousScheduledRun = DateTimeOffset.MaxValue.AddDays(-1);

        // Act
        var nextRun = schedule.GetNextRun(previousScheduledRun, previousScheduledRun, previousScheduledRun);

        // Assert
        nextRun.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public void DailyAt_SchedulesNextDailyTime()
    {
        // Arrange
        var schedule = RefreshAheadSchedule.DailyAt(new TimeOnly(3, 30), TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2026, 5, 26, 4, 0, 0, TimeSpan.Zero);

        // Act
        var nextRun = schedule.GetNextRun(now, previousScheduledRun: null, previousCompletedRun: null);

        // Assert
        nextRun.Should().Be(new DateTimeOffset(2026, 5, 27, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void FixedDelay_RejectsZeroDelay()
    {
        // Act
        var act = () => RefreshAheadSchedule.FixedDelay(TimeSpan.Zero);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FixedRate_RejectsZeroInterval()
    {
        // Act
        var act = () => RefreshAheadSchedule.FixedRate(TimeSpan.Zero);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HourlyAt_RejectsOffsetAtOneHour()
    {
        // Act
        var act = () => RefreshAheadSchedule.HourlyAt(TimeSpan.FromHours(1), TimeZoneInfo.Utc);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
