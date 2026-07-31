# Scheduling

Schedules are declared by each cache through its `Schedule` property.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(30));
```

The delegate builder uses the same schedule types behind its `Every`, `FixedRate`, `HourlyAt`, and `DailyAt` helpers.

## FixedDelay

`FixedDelay` means run, complete, then wait for the delay.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(30));
```

This is the safest default for refresh-ahead caches. It cannot pile up work, and a slow upstream naturally slows the refresh loop.

The delegate-builder alias is `Every`:

```csharp
refresh.Every<IPriceCache>(
    TimeSpan.FromSeconds(30),
    static (cache, ct) => cache.RefreshCacheAsync(ct));
```

## FixedRate

`FixedRate` aims for a regular cadence. If a refresh takes longer than the interval, missed ticks are skipped and the next run is scheduled in the future.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.FixedRate(TimeSpan.FromSeconds(5));
```

Use this when freshness is tied to clock cadence rather than spacing between refresh completions.

If the process is paused or a refresh runs long, missed ticks are skipped in one calculation.

## HourlyAt

`HourlyAt` runs once per hour at an offset from the top of the hour.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.HourlyAt(
        TimeSpan.FromMinutes(10),
        TimeZoneInfo.Local);
```

The offset must be greater than or equal to zero and less than one hour.

Hourly and daily caches often set:

```csharp
public override bool RunImmediately => false;
```

That keeps the first run aligned to the wall-clock schedule.

If such a cache also sets `WarmUpOnStart = true`, the first refresh still runs eagerly on startup so the host can wait for it; subsequent runs follow the wall-clock schedule. See [Warm-up and health](warm-up-and-health.md).

## DailyAt

`DailyAt` runs once per day at a local time.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.DailyAt(
        new TimeOnly(3, 30),
        TimeZoneInfo.Utc);
```

For daylight-saving gaps, invalid local times are advanced minute by minute until a valid local time is reached. During a fall-back, `HourlyAt` schedules both occurrences of an ambiguous local time so the hourly cadence is preserved; `DailyAt` runs once using the time zone's standard-offset occurrence.
