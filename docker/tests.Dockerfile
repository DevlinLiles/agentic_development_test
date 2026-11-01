# ChessMvp tests — .NET 10 multi-stage verification build.
# Build context is the repository root (like docker/api.Dockerfile) so this Dockerfile can
# COPY the whole server/ solution tree in one context. Its sole purpose is to compile (and,
# optionally, run) the test projects under the .NET 10 SDK, because the local SDK is 8.x and
# cannot restore the EF Core 10 packages. No product behavior is defined here.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore from the project graphs alone so source edits never invalidate the NuGet cache.
COPY server/src/ChessMvp.Domain/ChessMvp.Domain.csproj server/src/ChessMvp.Domain/
COPY server/src/ChessMvp.Infrastructure/ChessMvp.Infrastructure.csproj server/src/ChessMvp.Infrastructure/
COPY server/src/ChessMvp.Api/ChessMvp.Api.csproj server/src/ChessMvp.Api/
COPY server/tests/ChessMvp.Domain.Tests/ChessMvp.Domain.Tests.csproj server/tests/ChessMvp.Domain.Tests/
COPY server/tests/ChessMvp.Api.Tests/ChessMvp.Api.Tests.csproj server/tests/ChessMvp.Api.Tests/
RUN dotnet restore server/tests/ChessMvp.Domain.Tests/ChessMvp.Domain.Tests.csproj \
    && dotnet restore server/tests/ChessMvp.Api.Tests/ChessMvp.Api.Tests.csproj

# Build (without running) both test projects and everything they reference. Failing this
# layer fails the docker build, so a green build is the compile signal.
COPY server/ server/
RUN dotnet build server/tests/ChessMvp.Domain.Tests/ChessMvp.Domain.Tests.csproj -c Release --no-restore \
    && dotnet build server/tests/ChessMvp.Api.Tests/ChessMvp.Api.Tests.csproj -c Release --no-restore

# Optional test execution: built with --target runtests (default target only compiles).
FROM build AS runtests
ENTRYPOINT ["dotnet", "test", "server/tests/ChessMvp.Domain.Tests/ChessMvp.Domain.Tests.csproj", "-c", "Release", "--no-restore", "--no-build"]
