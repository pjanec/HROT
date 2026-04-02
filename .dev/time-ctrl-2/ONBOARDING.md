# Onboarding — Time Control Phase 2

Welcome to the `time-ctrl-2` workstream. This document gives a new developer everything needed to understand what we are changing, where the relevant code lives, and how to build and verify the work.

---

## What We Are Building

Three focused improvements to the distributed time-synchronisation subsystem:

| Feature | One-liner |
|---------|-----------|
| **A — Runtime Slave-Set Fix** | `MasterSyncController.SwitchToDeterministic` currently ignores the slave roster passed at call time. Fix it so lockstep actually waits for ACKs from the active nodes. |
| **B — Smooth SimTime UI** | The Time Control panel updates sim time only once per second (rate-limited network pulse). Fix `ClusterUiCache` to read directly from the local `ITimeController` for frame-rate updates. |
| **C — ExCon Lockstep** | `ExConSubsystem` (instructor station) has no time controller; it can never participate in lockstep. Add a `SlaveSyncController` so it ACKs steps and its UI runs at frame rate. |

Read [DESIGN.md](./DESIGN.md) for the full architecture and rationale.

---

## Where Things Live

```
d:\Work\IOS-IG-SimHost-FDP\
│
├── FDP/Toolkits/FDP.Toolkit.Time/
│   ├── Controllers/
│   │   ├── MasterSyncController.cs       ← Feature A: SwitchToDeterministic fix
│   │   └── SlaveSyncController.cs        ← Feature C: used as-is in ExCon
│   ├── TimeNetworkModule.cs              ← translator factories (used by Feature C)
│   └── FDP.Toolkit.Time.Tests/
│       └── MasterSyncControllerTests.cs  ← add Feature A unit tests here
│
├── Hrot.ClusterRunner/
│   └── Services/
│       ├── ClusterUiCache.cs             ← Feature B: inject ITimeController
│       ├── OrchestratorSubsystem.cs      ← Features A+B: order init, wire cache
│       └── ExConSubsystem.cs             ← Feature C: add SlaveSyncController
│
├── Hrot.ClusterRunner.Tests/
│   ├── ClusterUiCacheTests.cs            ← Feature B unit tests
│   └── ExConSubsystemTests.cs            ← Feature C unit tests
│
├── Hrot.ClusterRunner.Integration.Tests/
│   └── TimeControlIntegrationTests.cs    ← integration regression suite
│
└── IOS-IG-SimHost.sln                    ← main solution
```

---

## Design and Task Documents

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Full architecture, background, feature designs, files affected |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task instructions with exact success conditions (unit tests) |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Checkbox progress list — update as you go |

---

## How to Build

Open a terminal at `d:\Work\IOS-IG-SimHost-FDP\` and build the whole solution:

```powershell
dotnet build IOS-IG-SimHost.sln
```

Or build just the time toolkit and runner:

```powershell
dotnet build FDP/Toolkits/FDP.Toolkit.Time/FDP.Toolkit.Time.csproj
dotnet build Hrot.ClusterRunner/Hrot.ClusterRunner.csproj
```

---

## How to Run Tests

Unit tests (fast, no DDS, no window):

```powershell
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj
dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj
```

Integration tests (require DDS loopback — run on your dev machine, not in CI containers without DDS support):

```powershell
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
```

---

## Developer Guide

Read the developer workflow guide before writing code or submitting a batch report:

**[d:\Work\IOS-IG-SimHost-FDP\.dev\.guides\DEV-GUIDE.md](../.guides/DEV-GUIDE.md)**

Key rules:
- Every task must have passing unit tests with the exact success conditions described in [TASK-DETAIL.md](./TASK-DETAIL.md) before marking complete.
- Do not leave `// TODO` or commented-out code in production files.
- Update [TASK-TRACKER.md](./TASK-TRACKER.md) as you complete each task.

---

## Background Reading

If you are unfamiliar with the time synchronisation architecture, read these in order:

1. [DESIGN.md §2](./DESIGN.md#2-background--architecture) — Virtual wall clock, state machine, TimePulse role.
2. `MasterSyncController.cs` — Skim the class header doc and the `Update()` / `Step()` / `SwitchToDeterministic()` methods.
3. `SlaveSyncController.cs` — Skim `Update()`, `UpdateContinuous()`, `UpdateStepping()`, `ProcessTimePulses()`.
4. `TimeNetworkModule.cs` — Understand the translator factory methods.
5. `ClusterUiCache.cs` — Understand the DDS reader pattern and the existing `DrainTimePulse()` method.
