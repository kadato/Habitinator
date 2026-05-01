# Habitinator Thesis Documentation

## Diagrams

### 1. System Architecture (`01-system-architecture.mmd`)

**Purpose:** High-level overview of the complete system architecture

**Includes:**

- Client applications (Web + MAUI)
- Shared components (Razor Class Library)
- Server infrastructure (API, Authentication, Business Logic)
- Data layer (PostgreSQL + SQLite)
- DevOps components (Aspire, Testing, Azure)

**Key Insight:** Demonstrates the cross-platform architecture with shared UI
components running on both Blazor Server and .NET MAUI WebView.

---

### 2. Outbox Sync Sequence (`02-outbox-sync-sequence.mmd`)

**Purpose:** Detailed flow of the local-first synchronization pattern

**Includes:**

- User action → Local SQLite + Outbox queue
- Background sync with exponential backoff
- Conflict detection and resolution
- Operation coalescing (delete immediately after create)

**Key Insight:** Shows how the app achieves instant UI responsiveness while
maintaining server consistency through queued operations.

---

### 3. Authentication Flow (`03-authentication-flow.mmd`)

**Purpose:** Dual authentication strategy for web and mobile clients

**Includes:**

- Cookie authentication for web browsers
- JWT Bearer tokens for MAUI app
- `BoardOrJwt` authorization policy combining both

**Key Insight:** Demonstrates a single API serving multiple client types with
appropriate authentication mechanisms.

---

### 4. Timer State Machine (`04-timer-state-machine.mmd`)

**Purpose:** State transitions for the global focus timer

**States:**

- Stopped → Running → Paused → Running
- Running → AwaitingPrompt (milestone reached)
- AwaitingPrompt → Running (not done) or Stopped (done)

**Key Insight:** Shows the state machine pattern with accumulated duration
calculation that survives pauses.

---

### 5. Recurrence Algorithm (`05-recurrence-algorithm.mmd`)

**Purpose:** Decision tree for determining if a recurring task is scheduled

**Patterns Covered:**

- Daily every N days
- Weekly on same day-of-week every N weeks
- Monthly on same calendar day (handling month-end)
- Yearly on same month/day

**Key Insight:** Modular arithmetic with edge case handling for variable month
lengths.

---

### 6. Testing Pyramid (`06-testing-pyramid.mmd`)

**Purpose:** Comprehensive testing strategy visualization

**Layers:**

- **Unit Tests:** bUnit component tests, algorithm tests
- **Integration Tests:** TestContainers + PostgreSQL, API integration, SignalR
- **E2E Tests:** Playwright (web), Appium (mobile)

**Key Insight:** Shows modern .NET testing stack with containerized databases
and automated documentation generation.

---

### 7. Heatmap Generation (`07-heatmap-generation.mmd`)

**Purpose:** GitHub-style contribution graph algorithm

**Process:**

- ISO week alignment (Monday start)
- 2D grid generation (columns = weeks, rows = days)
- Non-linear intensity quantization (0-4 levels)
- Handling of out-of-range and future dates

**Key Insight:** 2D visualization algorithm with efficient O(n) complexity for
n = weeks × 7 days.

---

### 8. Database Schema (`08-database-schema.mmd`)

**Purpose:** Entity relationship diagram for core domain

**Entities:**

- AspNetUsers (ASP.NET Core Identity)
- BoardItems (Habits, Dailies, To-Dos via TPH)
- UserActivityEvents (event sourcing)
- BoardOutboxOperations (local sync queue)
- BoardRequestIdempotencies (exactly-once processing)

**Key Insight:** Shows Table-Per-Hierarchy pattern for polymorphic board items
and append-only event sourcing.

---

### 9. Streak Calculation (`09-streak-calculation.mmd`)

**Purpose:** Algorithm for computing consecutive completion streaks

**Approach:**

- Backward-walking from today through history
- Event sourcing with "last event wins" semantics
- Safety guards (20,000 iteration limit)

**Key Insight:** Event sourcing approach enables accurate streak calculation
even with multiple toggles per day.

---

### 10. SignalR Real-Time (`10-signalr-realtime.mmd`)

**Purpose:** Multi-device synchronization via WebSocket

**Flow:**

- Device connection with group assignment
- Mutation → Server → Broadcast to user's group
- Settings change propagation
- Disconnection handling with auto-reconnect

**Key Insight:** User-scoped SignalR groups enable targeted real-time updates
across all user devices.

---

### 11. Exponential Backoff (`11-exponential-backoff.mmd`)

**Purpose:** Retry algorithm visualization

**Wait Times:**

- Attempt 0: 1s (2⁰)
- Attempt 1: 2s (2¹)
- Attempt 2: 4s (2²)
- ...
- Attempt 8+: 256s (capped at 300s max)

**Key Insight:** Mathematical backoff formula with safety caps prevents
overwhelming servers.

---

### 12. Project Dependencies (`12-project-dependencies.mmd`)

**Purpose:** Visual representation of project reference graph

**Structure:**

- Applications (Web, MAUI)
- Shared Libraries (RCL)
- Test Projects (Unit, Integration, E2E, UI)
- Orchestration (AppHost)

**Key Insight:** Clean separation with shared Razor components reused across
web and mobile platforms.
