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
  - Unit tests for shared logic

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
dotnet test tests/App.Shared.Tests/App.Shared.Tests.csproj
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
- Unit tests:
  - `dotnet test tests/App.Shared.Tests/App.Shared.Tests.csproj`
- Linting/IDE diagnostics:
  - keep zero errors in changed files

## Current Limitations and Next Steps

- Current board model is simplified and demo-focused.
- Next planned expansions:
  - full domain tables for categories/habits/tasks/schedules/logs
  - recurrence engine persistence and advanced streak/statistics queries
  - integration tests for auth + per-user isolation + API flows
  - full notification and cross-platform sync implementation
