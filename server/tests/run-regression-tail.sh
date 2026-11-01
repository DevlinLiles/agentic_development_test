#!/usr/bin/env sh
# Regression tail runner (sub-5 of the Computer-opponent QA epic).
#
# Runs the ENTIRE pre-existing GameServiceTests suite unmodified, together with the
# newly added test files from sub-2 (GameServiceComputerGameReplyFlowTests), sub-3
# (GameServiceComputerGameFlowTests), and sub-4 (GameServiceHumanModeIsolationTests),
# in one single run.
#
# Why docker: the local dotnet SDK is 8.x while every project in server/ targets
# net10.0 and pulls EF Core 10 / .NET 10 refs, so any local `dotnet test` dies with
# NETSDK1045 before compiling a single test. docker/tests.Dockerfile restores, builds
# AND executes the container-free Domain unit-test suite under the matching
# mcr.microsoft.com/dotnet/sdk:10.0 image, so a red test fails the build outright.
#
# Usage (from the repository root):
#   sh server/tests/run-regression-tail.sh
#
# The `tests` service lives under the `verify` profile (declared in
# docker-compose.override.yml) so plain `docker compose up` never sees it. Naming the
# service on `build` selects it directly, so the --profile flag is not required.
#
# Exit status is docker compose's: 0 = every test passed, non-zero = at least one
# failure (which the regression tail reports, never fixes).
set -eu

LOG_DIR="server/tests/logs"
LOG_FILE="${LOG_DIR}/regression-tail.log"

mkdir -p "${LOG_DIR}"

# --no-cache forces a fresh restore+build+test pass so the log reflects the current
# working tree rather than a stale image layer. The unittests stage prints every test
# name via the detailed console logger, so the log doubles as the regression evidence.
echo "=== Regression tail: full Domain test suite (pre-existing + new) ===" | tee "${LOG_FILE}"
docker compose build --no-cache tests 2>&1 | tee -a "${LOG_FILE}"

echo "=== Full suite log written to ${LOG_FILE} ===" | tee -a "${LOG_FILE}"
