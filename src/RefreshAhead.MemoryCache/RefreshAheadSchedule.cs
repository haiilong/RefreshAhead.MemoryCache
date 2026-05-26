namespace RefreshAhead.MemoryCache;

/// <summary>
/// Describes when a refresh-ahead memory cache job should run.
/// </summary>
public abstract record RefreshAheadSchedule
{
    /// <summary>
    /// Creates a run-then-wait schedule.
    /// </summary>
    public static RefreshAheadSchedule FixedDelay(TimeSpan delay) => new FixedDelaySchedule(delay);

    /// <summary>
    /// Creates a fixed-rate schedule that skips missed ticks when refresh takes longer than the interval.
    /// </summary>
    public static RefreshAheadSchedule FixedRate(TimeSpan interval) => new FixedRateSchedule(interval);

    /// <summary>
    /// Creates a schedule that runs once per hour at the given offset from the top of the hour.
    /// </summary>
    public static RefreshAheadSchedule HourlyAt(TimeSpan offset, TimeZoneInfo? timeZone = null) =>
        new HourlyAtSchedule(offset, timeZone ?? TimeZoneInfo.Local);

    /// <summary>
    /// Creates a schedule that runs once per day at the given local time.
    /// </summary>
    public static RefreshAheadSchedule DailyAt(TimeOnly timeOfDay, TimeZoneInfo? timeZone = null) =>
        new DailyAtSchedule(timeOfDay, timeZone ?? TimeZoneInfo.Local);

    internal abstract DateTimeOffset GetNextRun(
        DateTimeOffset now,
        DateTimeOffset? previousScheduledRun,
        DateTimeOffset? previousCompletedRun);

    private sealed record FixedDelaySchedule : RefreshAheadSchedule
    {
        public FixedDelaySchedule(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be greater than zero.");
            }

            Delay = delay;
        }

        public TimeSpan Delay { get; }

        internal override DateTimeOffset GetNextRun(
            DateTimeOffset now,
            DateTimeOffset? previousScheduledRun,
            DateTimeOffset? previousCompletedRun) =>
            (previousCompletedRun ?? now).Add(Delay);
    }

    private sealed record FixedRateSchedule : RefreshAheadSchedule
    {
        public FixedRateSchedule(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than zero.");
            }

            Interval = interval;
        }

        public TimeSpan Interval { get; }

        internal override DateTimeOffset GetNextRun(
            DateTimeOffset now,
            DateTimeOffset? previousScheduledRun,
            DateTimeOffset? previousCompletedRun)
        {
            var scheduledFrom = previousScheduledRun ?? now;
            var elapsed = now - scheduledFrom;
            if (elapsed < TimeSpan.Zero)
            {
                return scheduledFrom.Add(Interval);
            }

            var intervalsToSkip = elapsed.Ticks / Interval.Ticks + 1;
            return scheduledFrom.AddTicks(intervalsToSkip * Interval.Ticks);
        }
    }

    private sealed record HourlyAtSchedule : RefreshAheadSchedule
    {
        public HourlyAtSchedule(TimeSpan offset, TimeZoneInfo timeZone)
        {
            if (offset < TimeSpan.Zero || offset >= TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    offset,
                    "Hourly offset must be greater than or equal to zero and less than one hour.");
            }

            Offset = offset;
            TimeZone = timeZone;
        }

        public TimeSpan Offset { get; }

        public TimeZoneInfo TimeZone { get; }

        internal override DateTimeOffset GetNextRun(
            DateTimeOffset now,
            DateTimeOffset? previousScheduledRun,
            DateTimeOffset? previousCompletedRun)
        {
            var localNow = TimeZoneInfo.ConvertTime(now, TimeZone).DateTime;
            var hourStart = new DateTime(
                localNow.Year,
                localNow.Month,
                localNow.Day,
                localNow.Hour,
                0,
                0,
                DateTimeKind.Unspecified);

            var nextLocal = hourStart.Add(Offset);
            if (nextLocal <= localNow)
            {
                nextLocal = hourStart.AddHours(1).Add(Offset);
            }

            return ConvertLocalToUtc(nextLocal, TimeZone);
        }
    }

    private sealed record DailyAtSchedule(TimeOnly TimeOfDay, TimeZoneInfo TimeZone) : RefreshAheadSchedule
    {
        internal override DateTimeOffset GetNextRun(
            DateTimeOffset now,
            DateTimeOffset? previousScheduledRun,
            DateTimeOffset? previousCompletedRun)
        {
            var localNow = TimeZoneInfo.ConvertTime(now, TimeZone).DateTime;
            var nextLocal = DateOnly.FromDateTime(localNow).ToDateTime(TimeOfDay);
            if (nextLocal <= localNow)
            {
                nextLocal = nextLocal.AddDays(1);
            }

            return ConvertLocalToUtc(nextLocal, TimeZone);
        }
    }

    private static DateTimeOffset ConvertLocalToUtc(DateTime localTime, TimeZoneInfo timeZone)
    {
        while (timeZone.IsInvalidTime(localTime))
        {
            localTime = localTime.AddMinutes(1);
        }

        return new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime)).ToUniversalTime();
    }
}
