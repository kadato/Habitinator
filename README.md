# Habitinator

Habitinator is a gamification-free productivity app inspired by Habitica's structure (Habits, Dailies, To-Dos) without RPG visuals.

This document is the single source of truth for current architecture, features, requirements, and run/debug workflows.

## Product Scope

- Platforms:
  - Web (`App.Web`)
  - Android-ready MAUI Blazor Hybrid host (`App.MAUI`)
- Main UX:
  - Three productivity sections: Habits, Dailies, To-Dos
  - Responsive board:
    - Desktop: three columns
    - Mobile/tablet: tabs
  - One global stopwatch-style timer (start/pause/stop + log)
- Non-gamified direction:
  - No RPG mechanics
  - Keep practical productivity tracking and analytics foundation

## Core Requirements

- Habit and task management:
  - CRUD operations for board items
  - Habit increment/check behavior
  - Daily/To-Do completion toggle behavior
- Time tracking:
  - Single global timer per session
  - Timer log attachment to selected target
- User management:
  - ASP.NET Core Identity
  - Multi-user model
  - Cookie auth for web + JWT token response for API/mobile usage
- Data persistence:
  - PostgreSQL via EF Core
  - Startup migration + seed support
- Demo readiness:
  - Guest account seeded automatically with demo board data

### Cross-platform sync and local-first requirements

- PostgreSQL remains the server source of truth for synchronized user data.
- **Web (`App.Web`)** is **online-only**: the board is read and written through `WebBoardDataService` against PostgreSQL in the same process.
- **MAUI (`App.MAUI`)** is **local-first** for the productivity board:
  - board data is mirrored in **SQLite** on the device
  - edits apply locally immediately and append to an **outbox** for the existing REST board API (`GET/POST/PUT/DELETE` under `/api/board`)
  - a background **sync coordinator** drains the outbox when online (exponential backoff on failures), then **pulls** incremental changes (`GET /api/board/sync?cursor=`) when a cursor is stored, otherwise a **full snapshot** (`GET /api/board`)
  - **SignalR** can still prompt a refresh; the refresh path pulls into SQLite before the Blazor UI reloads
  - signing **out** clears the local mirror and outbox for that device
- **Board API reliability contract** (native clients and tests should assume this behavior):
  - **`Idempotency-Key`** (optional on web; **sent from MAUI** using the outbox `OperationId`): duplicate requests with the same key and the same request **fingerprint** replay the stored status/body without re-running side effects. The same key with a **different** body returns **409** with `problem: idempotency_key_reuse`.
  - **Optimistic concurrency**: **`X-Board-Expected-Updated-At-Utc`** or **`If-Match`** with the item’s `ServerUpdatedAtUtc` (ISO-8601). If the server row’s `UpdatedAtUtc` does not match, the API returns **409** with `problem: version_conflict` and the **current** `item` JSON. **Conflict policy** for MAUI: the outbox operation is **dropped** after 409 and a **resync** is requested (**server wins** for conflicting mutations).
  - **Incremental sync**: **`GET /api/board/sync?cursor=`** returns upserts (`BoardSyncItem`: section + `BoardItem`), **`deletedItemIds`** (soft-delete tombstones), and **`nextCursor`**. Deletes use **`DeletedAtUtc`** on the server; the full snapshot excludes tombstones. Invalid or rejected cursors return **400**; MAUI clears the cursor and falls back to a full snapshot.
  - **HTTP resilience**: the MAUI **`api`** `HttpClient` uses **`Microsoft.Extensions.Http.Resilience`** (timeouts + transient retries). **401** still clears the session via the existing handler.
  - **Maintenance**: a hosted service **purges** old idempotency rows and **physically deletes** aged tombstones per configured retention.
- Security and isolation:
  - sync scope is always user-bound (JWT on MAUI, cookies on web)
  - existing auth model (cookie/JWT + Identity) is used for API routes

MAUI API base URL is unchanged: configure `Api:BaseUrl` / `HABITINATOR_API_BASE_URL` and `MauiAppSettings` as before (see run/debug sections below).

## Technology Stack

- .NET 10
- Blazor Web App + MAUI Blazor Hybrid
- MudBlazor (UI components)
- ASP.NET Core Identity
- Entity Framework Core + Npgsql provider
- .NET Aspire AppHost for orchestration

## Solution Structure

- `src/AppHost`
  - .NET Aspire orchestrator
  - Starts PostgreSQL and `App.Web`
- `src/App.Web`
  - ASP.NET Core host
  - Blazor UI + API + Identity + EF Core
- `src/App.MAUI`
  - MAUI Blazor host
  - Uses shared components/services
- `src/App.Shared.RCL`
  - Shared Razor components, models, services
- `tests/App.Shared.Tests`
  - Unit tests for shared logic (services, schedules, notifications)
- `tests/App.Shared.RCL.Tests`
  - bUnit component smoke tests (Razor test project)
- `tests/App.Web.IntegrationTests`
  - API + PostgreSQL integration tests (Docker required — Testcontainers)
- `tests/App.Web.E2E`
  - Playwright browser smoke tests (requires running `App.Web`; set `E2E_BASE_URL`)
- `tests/App.MAUI.UITests`
  - **Android** UI smoke tests (Appium + UiAutomator2); skipped unless you opt in (see below)

## Aspire and PostgreSQL

Aspire is the recommended way to run locally.

- `AppHost` provisions:
  - PostgreSQL container resource: `postgres`
  - Database resource: `habitinatordb`
  - Web project: `app-web` (depends on `habitinatordb`)
- `App.Web` accepts either:
  - `ConnectionStrings:habitinatordb` (from Aspire), or
  - `ConnectionStrings:DefaultConnection` (standalone mode)

## Run and Debug

### Recommended: run via Aspire

```powershell
dotnet build Habitinator.slnx
dotnet test Habitinator.slnx
dotnet run --project src/AppHost/AppHost.csproj
```

### Visual Studio debugging

- To auto-start PostgreSQL through Aspire:
  - set `AppHost` as startup project
  - run/debug from `AppHost`
- If `App.Web` is startup project:
  - PostgreSQL must already be running externally on `127.0.0.1:5432` (or matching connection string)

### Standalone web (without Aspire)

```powershell
dotnet run --project src/App.Web/App.Web.csproj
```

## Demo Guest User

Seeded at startup (if missing):

- Email: `guest@habitinator.local`
- Password: `Guest123!`
- Timezone: `Europe/Budapest`

Guest login endpoint:

- `POST /api/auth/guest-login`

Behavior:

- DB migration is applied at startup
- guest user is created if missing
- demo board items are seeded if none exist for guest

## API Surface (Current)

### Auth API

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/guest-login`
- `POST /api/auth/logout`

### Board API

- `GET /api/board`
- `POST /api/board/{section}`
- `PUT /api/board/{section}/{itemId}`
- `DELETE /api/board/{section}/{itemId}`
- `POST /api/board/{section}/{itemId}/toggle`
- `POST /api/board/habits/{itemId}/increment`

`section` values:

- `Habit`
- `Daily`
- `Todo`

## Configuration

Main configuration file:

- `src/App.Web/appsettings.json`

Important sections:

- `ConnectionStrings`
- `Jwt`
- `DemoUser`

## Testing and Quality

- Build:
  - `dotnet build Habitinator.slnx`
- Automated tests (solution — unit, bUnit smoke, Testcontainers integration, Android UI tests **skipped** by default):
  - `dotnet test Habitinator.slnx`
  - Integration tests need **Docker** (Linux/macOS/Windows with Docker Desktop).
- TRX files for CI / IDE:
  - `dotnet test Habitinator.slnx --results-directory ./TestResults --logger trx`
- Playwright E2E (optional, not in the solution file):
  1. Start PostgreSQL and run `App.Web` (e.g. `dotnet run --project src/App.Web/App.Web.csproj --urls http://127.0.0.1:5050`).
  2. Install browsers once: `pwsh tests/App.Web.E2E/bin/Debug/net10.0/playwright.ps1 install chromium` (path matches your configuration).
  3. `dotnet test tests/App.Web.E2E/App.Web.E2E.csproj` (defaults to `E2E_BASE_URL=http://127.0.0.1:5050` if unset).
- **Android UI tests (Appium)** — opt-in so normal `dotnet test` stays fast:
  1. Build the app: `dotnet build src/App.MAUI/App.MAUI.csproj -f net10.0-android -c Debug`
  2. Start an **Android emulator** (or device) with `adb devices` showing it as connected.
  3. Install and run **Appium 2** with the **UiAutomator2** driver (`npm i -g appium`, `appium driver install uiautomator2`, then `appium`).
  4. Run tests:  
     `ANDROID_UI_TESTS=1 dotnet test tests/App.MAUI.UITests/App.MAUI.UITests.csproj`  
     On Windows PowerShell: `$env:ANDROID_UI_TESTS='1'; dotnet test tests/App.MAUI.UITests/App.MAUI.UITests.csproj`  
     Optional: `ANDROID_APP_PATH` (full path to APK), `APPIUM_SERVER_URL` (default `http://127.0.0.1:4723`).
  5. Manual CI: workflow [`.github/workflows/android-uitest.yml`](.github/workflows/android-uitest.yml) (`workflow_dispatch`).
- **GitHub Actions**: [`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds the solution, runs tests with TRX, publishes results, runs E2E against a job service PostgreSQL.
- Linting/IDE diagnostics:
  - keep zero errors in changed files

## Current Limitations and Next Steps

- Current board model is simplified and demo-focused.
- Next planned expansions:
  - full domain tables for categories/habits/tasks/schedules/logs
  - recurrence engine persistence and advanced streak/statistics queries
  - more bUnit coverage for MudBlazor dialogs (JSInterop / provider setup)
  - More Android UI coverage (WebView context, flows) and optional device matrix in CI
  - full notification and cross-platform sync implementation
