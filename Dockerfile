FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and all projects
COPY ZibStack.NET.slnx .
COPY packages/ packages/

# Restore
RUN dotnet restore packages/ZibStack.NET.UI/sample/SampleApi/SampleApi.csproj

# Build
RUN dotnet publish packages/ZibStack.NET.UI/sample/SampleApi/SampleApi.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Render uses PORT env var
ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
EXPOSE 10000

ENTRYPOINT ["dotnet", "SampleApi.dll"]
