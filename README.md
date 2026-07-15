# Chess MVP

A two-player online chess app. Player 1 creates a game and gets a shareable link; Player 2 joins via that link. The C#/.NET API is the sole authority on game state and move legality; the React frontend renders the board and relays moves.

## Prerequisites

- .NET 10 SDK
- Node 20+
- Docker Desktop (for the local SQL Server container)

## One-time setup

```
docker compose up -d
```

This starts a local SQL Server container. The API applies EF Core migrations automatically on startup in the Development environment, so no manual migration step is required for local dev.

## Running

Two terminals, from the repo root:

```
# Terminal 1 — API (REST + SignalR hub)
cd server/src/ChessMvp.Api
dotnet run
```

```
# Terminal 2 — client
cd client
npm install
npm run dev
```

The API listens on the URL printed by `dotnet run` (see `server/src/ChessMvp.Api/Properties/launchSettings.json`). The client dev server runs at `http://localhost:5173` by default and is configured via `client/.env` (see `client/.env.example`) to call that API URL. CORS on the API is restricted to the client's origin (`Cors:ClientOrigin` in `appsettings.Development.json`).

## Playing a game locally with two players

Open the app in two separate browser sessions — e.g. one normal window and one incognito/private window, or two different browsers. Both player sessions store their game token in `localStorage` under the same origin, so two tabs in the *same* browser profile will overwrite each other's session.

1. Window A: create a game, copy the join link.
2. Window B (different profile/incognito): paste the join link to join as the second player.
3. Play moves by clicking a piece, then a destination square.

## Testing

```
# Backend
cd server
dotnet test

# Frontend (unit/component)
cd client
npm test
```

### Automated test-suite execution & coverage verification (QA gate)

A deterministic gate covers regression for legal move generation, terminal-state detection, evaluation/search, and AI engine legality. It runs the full backend suite, collects line coverage, and **gates on three conditions**:

1. the test suite exits with code 0,
2. line coverage meets the configured threshold (default **80%**) for **every** implementation module, and
3. no skipped or xfailed tests remain in the run.

Run it from the repo root:

```
./server/run-qa-coverage.sh             # uses the default 80% line-coverage threshold
THRESHOLD=85 ./server/run-qa-coverage.sh # raise the threshold
```

The gate is driven by `server/coverage.runsettings` (coverlet instrumentation + an aggregate threshold) and `server/run-qa-coverage.sh`, which parses the emitted `coverage.cobertura.xml` and `.trx` artifacts with `python3` (stdlib only) to enforce the per-module coverage and no-skipped-tests conditions. A self-checking `NoSkippedTestsGuardTests` also runs inside the suite to forbid any `Skip`/`Explicit` xUnit attributes deterministically.

> The API integration tests (`ChessMvp.Api.Tests`) use Testcontainers (a real SQL Server container), so this gate needs `docker compose up -d` first, exactly like the E2E tests below.

### End-to-end tests (Playwright)

`client/e2e/` drives the real app in a real browser against the real running backend — two independent browser contexts play full games against each other, covering the SignalR-synced golden path, promotion, and the join-link/spectator-denial screens. These need the full stack up first:

```
docker compose up -d                          # from repo root
cd server/src/ChessMvp.Api && dotnet run       # leave running

cd client
npx playwright install chromium                # one-time
npm run test:e2e                               # starts the Vite dev server itself
```

## Troubleshooting

- "Connection refused" / DB errors on API startup: the SQL Server container may still be starting up. Check `docker compose ps` and wait for the healthcheck to pass, then restart the API.
- To reset the local database entirely: `docker compose down -v` (removes the data volume), then `docker compose up -d`.
