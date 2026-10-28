# ChessMvp API — .NET 10 multi-stage build.
# Build context is the repository root (see docker-compose.yml) so this Dockerfile
# can COPY the whole server/ solution tree in one context.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore from the project graphs alone so source edits never invalidate the NuGet cache.
COPY server/src/ChessMvp.Domain/ChessMvp.Domain.csproj server/src/ChessMvp.Domain/
COPY server/src/ChessMvp.Infrastructure/ChessMvp.Infrastructure.csproj server/src/ChessMvp.Infrastructure/
COPY server/src/ChessMvp.Api/ChessMvp.Api.csproj server/src/ChessMvp.Api/
RUN dotnet restore server/src/ChessMvp.Api/ChessMvp.Api.csproj

# Publish the API project (pulls in Domain + Infrastructure via ProjectReference).
COPY server/ server/
RUN dotnet publish server/src/ChessMvp.Api/ChessMvp.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The aspnet runtime image is Debian-slim based and ships neither curl nor wget, so the
# compose HEALTHCHECK's tool must be installed explicitly.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 5112
ENTRYPOINT ["dotnet", "ChessMvp.Api.dll"]
