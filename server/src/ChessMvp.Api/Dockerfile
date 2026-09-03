# syntax=docker/dockerfile:1.7

# --- build stage ---
# Restores NuGet packages and publishes the ChessMvp.Api web app.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy the solution and all project files first so restore can be cached independently
# of source changes (only re-runs when project references/versions change).
COPY server/ChessMvp.slnx ./
COPY server/src/ChessMvp.Domain/ChessMvp.Domain.csproj src/ChessMvp.Domain/
COPY server/src/ChessMvp.Infrastructure/ChessMvp.Infrastructure.csproj src/ChessMvp.Infrastructure/
COPY server/src/ChessMvp.Api/ChessMvp.Api.csproj src/ChessMvp.Api/

RUN dotnet restore "src/ChessMvp.Api/ChessMvp.Api.csproj"

# Copy the rest of the source and publish.
COPY server/src/ ./src/
RUN dotnet publish "src/ChessMvp.Api/ChessMvp.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is not present in the aspnet:10.0 base image; install it so the
# HEALTHCHECK below has a tool to probe the OpenAPI endpoint.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

# ASP.NET Core reads ASPNETCORE_URLS for the bind address. The API listens on
# 5112 to match the client's configured VITE_API_BASE_URL.
ENV ASPNETCORE_URLS=http://0.0.0.0:5112
EXPOSE 5112

HEALTHCHECK --interval=15s --timeout=5s --start-period=200s --retries=12 \
  CMD curl --fail http://127.0.0.1:5112/openapi/v1.json || exit 1

ENTRYPOINT ["dotnet", "ChessMvp.Api.dll"]
