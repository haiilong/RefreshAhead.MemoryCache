using RefreshAhead.MemoryCache;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRefreshAheadMemoryCaches();

await builder.Build().RunAsync();
