# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files first to cache restore.
COPY src/ChessMvp.Domain/ChessMvp.Domain.csproj src/ChessMvp.Domain/
COPY src/ChessMvp.Infrastructure/ChessMvp.Infrastructure.csproj src/ChessMvp.Infrastructure/
COPY src/ChessMvp.Api/ChessMvp.Api.csproj src/ChessMvp.Api/

RUN dotnet restore src/ChessMvp.Api/ChessMvp.Api.csproj

# Copy the rest of the source and publish.
COPY src/ src/

RUN dotnet publish src/ChessMvp.Api/ChessMvp.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Install curl for the healthcheck (aspnet slim images do not ship it by default).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:5112
EXPOSE 5112

HEALTHCHECK --interval=10s --timeout=5s --retries=6 --start-period=40s \
  CMD curl --fail http://127.0.0.1:5112/openapi/v1.json || exit 1

ENTRYPOINT ["dotnet", "ChessMvp.Api.dll"]
