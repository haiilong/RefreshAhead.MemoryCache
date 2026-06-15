namespace RefreshAhead.MemoryCache;

/// <summary>
/// Options for the refresh-ahead memory cache hosted service. Configure with
/// <c>services.Configure&lt;RefreshAheadMemoryCacheOptions&gt;(...)</c>.
/// </summary>
public sealed class RefreshAheadMemoryCacheOptions
{
    /// <summary>
    /// Gets or sets the maximum time to wait during host startup for caches with
    /// <see cref="IRefreshAheadMemoryCache{TSnapshot}.WarmUpOnStart"/> set to <c>true</c> to complete their
    /// first successful refresh.
    /// </summary>
    /// <remarks>
    /// When <c>null</c> (the default), startup waits indefinitely for warm-up, bounded only by the host's
    /// startup cancellation token. Set a timeout when an upstream dependency may be unavailable: if warm-up
    /// does not complete in time, startup fails with a <see cref="TimeoutException"/>.
    /// </remarks>
    public TimeSpan? WarmUpTimeout { get; set; }
}
