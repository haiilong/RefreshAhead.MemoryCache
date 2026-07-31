# AOT and Source Generation

The package is `IsAotCompatible=true` and `IsTrimmable=true`.

The important design choice is compile-time discovery. The bundled Roslyn source generator finds concrete `IRefreshAheadMemoryCache<TSnapshot>` implementations — `class` or `record` types — and emits registration code into your app. There is no runtime `Assembly.GetTypes()` scan.

## Generated Method

In a consuming project with this cache:

```csharp
public sealed class PriceCache()
    : RefreshAheadMemoryCache<IReadOnlyDictionary<string, decimal>>(
        FrozenDictionary<string, decimal>.Empty)
{
    // ...
}
```

the generator emits a method shaped like this:

```csharp
namespace RefreshAhead.MemoryCache;

[System.CodeDom.Compiler.GeneratedCode(
    "RefreshAhead.MemoryCache.SourceGenerator",
    "1.1.1")]
internal static class RefreshAheadMemoryCacheGeneratedRegistration
{
    internal static IServiceCollection AddRefreshAheadMemoryCaches(
        this IServiceCollection services)
    {
        return services.AddRefreshAheadMemoryCaches(refresh =>
        {
            refresh.Add<PriceCache, IReadOnlyDictionary<string, decimal>>(name: "PriceCache");
        });
    }
}
```

The method is `internal` because each app gets its own generated copy.

The generator emits the method only when the current compilation contains at least one concrete cache class. If a project only uses the delegate builder, use the public overload that accepts `Action<IRefreshAheadMemoryCacheBuilder>`.

## Why This Is AOT-Friendly

- Cache discovery happens at compile time.
- Generated registration references concrete cache types directly.
- Cache names are emitted as compile-time string literals derived from concrete type names.
- The runtime registration method carries the constructor-preservation annotation required by `Microsoft.Extensions.DependencyInjection`.
- The package does not need reflection-based activation for discovering caches.

Your cache implementation can still introduce trim warnings if it uses reflection, dynamic serialization, or unannotated activation. Those warnings belong to the cache code and should be fixed there.

## First Build

The generated method does not exist until the generator runs.

If the IDE reports this:

```text
CS1061: 'IServiceCollection' does not contain a definition for 'AddRefreshAheadMemoryCaches'
```

run:

```sh
dotnet build
```

After the first build, the generated extension should resolve.

## ProjectReference Development

NuGet consumers receive the source generator automatically from the package's `analyzers/dotnet/cs` folder.

When developing this repository through `ProjectReference`, analyzers do not flow through the library reference. Samples and tests therefore reference the generator project explicitly:

```xml
<ProjectReference Include="..\..\src\RefreshAhead.MemoryCache.SourceGenerator\RefreshAhead.MemoryCache.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Real package consumers do not need that extra reference.

## Caches in Class Libraries

The generator scans the current compilation's source. It does not scan referenced assemblies.

If your caches live in a class library, create a public wrapper inside that class library:

```csharp
public static class ReferenceDataCacheRegistration
{
    public static IServiceCollection AddReferenceDataCaches(
        this IServiceCollection services) =>
        services.AddRefreshAheadMemoryCaches();
}
```

The generated method is internal, but the wrapper is in the same assembly, so it can call it. Your app references the library and calls the wrapper.

## Inspecting Generated Code

To make generated files visible during a build, add this to your app temporarily:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Then look under `obj/<Configuration>/<TargetFramework>/generated` for the generated registration file.
