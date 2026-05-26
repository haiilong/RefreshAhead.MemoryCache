FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/RefreshAhead.MemoryCache/RefreshAhead.MemoryCache.csproj src/RefreshAhead.MemoryCache/
COPY src/RefreshAhead.MemoryCache.SourceGenerator/RefreshAhead.MemoryCache.SourceGenerator.csproj src/RefreshAhead.MemoryCache.SourceGenerator/
COPY samples/RefreshAhead.MemoryCache.Sample/RefreshAhead.MemoryCache.Sample.csproj samples/RefreshAhead.MemoryCache.Sample/
RUN dotnet restore samples/RefreshAhead.MemoryCache.Sample/RefreshAhead.MemoryCache.Sample.csproj

COPY . .
RUN dotnet publish samples/RefreshAhead.MemoryCache.Sample/RefreshAhead.MemoryCache.Sample.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "RefreshAhead.MemoryCache.Sample.dll"]
