FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution, root build props, and all projects
COPY ZibStack.NET.slnx .
COPY Directory.Build.props .
COPY packages/ packages/
# shared/ holds DtoSemantics.cs which both Dto and TypeGen generators reference
# via <Compile Include="$(MSBuildProjectDirectory)\..\..\..\..\shared\DtoSemantics.cs">.
# Missing this directory caused publish to fail with CS2001 (file not found).
COPY shared/ shared/

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
