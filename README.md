# Habitinator

<div align="center">

![.NET 11](https://img.shields.io/badge/.NET-11.0-3b82f6?logo=dotnet)
![.NET MAUI](https://img.shields.io/badge/.NET_MAUI-11.0-3b82f6?logo=dotnet)
![Blazor](https://img.shields.io/badge/Blazor-Interactive_WebAssembly-3b82f6?logo=blazor)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17.11-4169E1?logo=postgresql)
![SQLite](https://img.shields.io/badge/SQLite-Local--First-003B57?logo=sqlite)
![Platforms](https://img.shields.io/badge/Platforms-Android%20%7C%20Windows%20%7C%20iOS%20%7C%20macOS-blue)
[![GitHub Release](https://img.shields.io/github/v/release/kadato/Habitinator)](https://github.com/kadato/Habitinator/releases/latest)

A cross-platform productivity app built with .NET MAUI and Blazor. Manage habits, dailies, and to-dos with a focus timer, analytics, and reliable sync across web and mobile.

**Live Demo.** [habitinator.app](https://habitinator.app)

[Demo](https://habitinator.app) | [Preview](#preview) | [Download](#download-and-install) | [Features](#key-features) | [Tech stack](#technology-stack)

</div>

---

## Preview

<div align="center">

<p align="center">
<strong>Board</strong> | <strong>Statistics</strong> | <strong>Focus timer</strong><br><br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/board-dark.png">
  <img alt="Board with habits, dailies and to-dos" src="./docs/automation/screenshots/board-light.png" width="220">
</picture>
&nbsp;&nbsp;
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/statistics-dark.png">
  <img alt="Statistics with activity heatmap" src="./docs/automation/screenshots/statistics-light.png" width="220">
</picture>
&nbsp;&nbsp;
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/timer-dark.png">
  <img alt="Running focus timer" src="./docs/automation/screenshots/timer-light.png" width="220">
</picture>
</p>

<br>

<p align="center">
<strong>Edit daily</strong> | <strong>Activity day detail</strong> | <strong>Settings</strong><br><br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/edit-daily-dark.png">
  <img alt="Edit a daily" src="./docs/automation/screenshots/edit-daily-light.png" width="220">
</picture>
&nbsp;&nbsp;
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/activity-day-detail-dark.png">
  <img alt="Activity detail for one day" src="./docs/automation/screenshots/activity-day-detail-light.png" width="220">
</picture>
&nbsp;&nbsp;
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/settings-dark.png">
  <img alt="Settings page" src="./docs/automation/screenshots/settings-light.png" width="220">
</picture>
</p>

<br>

<p align="center">
<strong>Upcoming</strong> | <strong>AI assistant</strong><br><br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/upcoming-dark.png">
  <img alt="Upcoming todos and dailies" src="./docs/automation/screenshots/upcoming-light.png" width="220">
</picture>
&nbsp;&nbsp;
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./docs/automation/screenshots/ai-assistant-dark.png">
  <img alt="AI assistant drawer" src="./docs/automation/screenshots/ai-assistant-light.png" width="220">
</picture>
</p>

</div>

To regenerate these shots, run `pwsh ./tools/Habitinator.Screenshots/run.ps1` from the repo root. The script drives Playwright with a mobile viewport in light and dark themes.

---

## Download and install

Download the latest build from the [releases page](https://github.com/kadato/Habitinator/releases/latest), or use the direct links below. Every package ships with a SHA256 checksum file.

| Platform | Package | Size | Notes |
|----------|---------|------|-------|
| Android | [![APK](https://img.shields.io/badge/APK-3ddc84?style=for-the-badge&logo=android&logoColor=white)](https://github.com/kadato/Habitinator/releases/latest/download/Habitinator-android.apk) | 41 MB | Install on device. Enable unknown sources first |
| Windows | [![Installer](https://img.shields.io/badge/Installer-0078d6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/kadato/Habitinator/releases/latest/download/Habitinator-windows-x64-setup.exe) | 53 MB | Installer with auto-updates. Recommended |
| Windows | [![Portable ZIP](https://img.shields.io/badge/Portable%20ZIP-1f6feb?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/kadato/Habitinator/releases/latest/download/Habitinator-windows-x64.zip) | 78 MB | Portable. Extract and run |
| iOS and macOS | *Source only* | - | Build from source using the .NET MAUI workload |
| Web | [![Web App](https://img.shields.io/badge/Web%20App-6b46c1?style=for-the-badge&logo=web&logoColor=white)](https://habitinator.app) | - | Live demo, no install needed |

**Windows runtime note.** If your release is framework-dependent, install [.NET Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0) once.

#### Option 1: Prebuilt packages, recommended

1. Pick your platform from the table above
2. Download the package and its `.sha256` file
3. Verify the checksum, then follow the platform steps below

#### Option 2: Build from source

```powershell
# Windows
dotnet build -t:Run -f net11.0-windows10.0.19041.0

# Android, requires the Android SDK
dotnet build -t:Run -f net11.0-android
```

### Installation instructions

#### Android

1. Enable Install from unknown sources in system settings
2. Transfer the APK to your phone
3. Open the APK and install

#### Windows

**Installer.** Run `Habitinator-windows-x64-setup.exe` and follow the setup wizard. It keeps the app updated.

**Portable.** Extract the ZIP file, then run `Habitinator.exe`. If prompted, install .NET Desktop Runtime.

---

## Key features

- **Board planner.** Track habits, dailies, and to-dos. Organize them with tags, notes, checklists, archive, and streaks.
- **Upcoming view.** See overdue, due today, and due tomorrow todos in one digest.
- **Focus timer.** Run stopwatch or Pomodoro sessions with per-item targets. Log sessions to activity.
- **Statistics.** Browse the activity heatmap. Open any day for details.
- **Command palette.** Press Ctrl+K or Cmd+K to jump anywhere. Board shortcuts cover the rest.
- **AI assistant.** Chat with the board from the drawer. Pick provider and model. Set custom instructions.
- **External AI access.** Connect Claude, Cursor, VS Code, or OpenCode over MCP with OAuth or personal tokens.
- **Reminders.** Set a daily reminder, focus timer alerts, sync alerts, sound, and quiet hours.
- **Local-first sync.** Each device keeps a mirror and an outbox queue. Pause sync and inspect pending work. Conflicts resolve without data loss.

---

## Technology stack

| Layer | Technologies |
|-------|--------------|
| **Runtime** | .NET 11 |
| **Web UI** | Blazor Web App with Interactive WebAssembly, MudBlazor |
| **Native Apps** | .NET MAUI + Blazor Hybrid for Android, iOS, macOS, Windows |
| **Database, Server** | Neon Serverless PostgreSQL or any PostgreSQL via EF Core |
| **Database, Client** | SQLite via EF Core, IndexedDB in the browser |
| **Orchestration** | .NET Aspire AppHost |

---

## Solution graph

Here are the project dependencies. `scripts/Refresh-AutomationAssets.ps1` syncs the diagram below from `docs/automation/solution-graph.mmd`:

<!-- HABITINATOR_MERMAID_BEGIN:solution-graph -->
```mermaid
flowchart LR
%% Auto-generated by tools/Habitinator.Diagrams - project reference graph
  App_MAUI["App.MAUI"]
  App_MAUI_UITests["App.MAUI.UITests"]
  App_Shared_RCL["App.Shared.RCL"]
  App_Shared_RCL_Tests["App.Shared.RCL.Tests"]
  App_Shared_Tests["App.Shared.Tests"]
  App_Web["App.Web"]
  App_Web_Client["App.Web.Client"]
  App_Web_IntegrationTests["App.Web.IntegrationTests"]
  AppHost["AppHost"]
  App_MAUI --> App_Shared_RCL
  App_Shared_RCL_Tests --> App_Shared_RCL
  App_Shared_Tests --> App_Shared_RCL
  App_Web --> App_Shared_RCL
  App_Web --> App_Web_Client
  App_Web_Client --> App_Shared_RCL
  App_Web_IntegrationTests --> App_Web
  AppHost --> App_Web
```
<!-- HABITINATOR_MERMAID_END:solution-graph -->
