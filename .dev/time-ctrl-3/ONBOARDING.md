# Onboarding — Time Control Phase 3

Welcome to the `time-ctrl-3` workstream.  This document gives a new developer everything
needed to understand what we are changing, where the relevant code lives, and how to build
and verify the work.

---

## What We Are Building

The distributed time-synchronisation toolkit (`FDP.Toolkit.Time`) currently works only on a
single machine because it compares raw OS `Stopwatch.GetTimestamp()` values across nodes — and
those values start at OS-boot time, so different machines (or even different boot sessions)
have completely different origins.

This workstream upgrades the `MasterSyncController` and `SlaveSyncController` to an NTP-style
two-way handshake that computes the exact OS-clock offset between the master node and each
slave, making the **Future Barrier** (pause) and **lockstep stepping** work correctly across
physically separate computers.

| Feature | One-liner |
|---------|-----------|
| **A — NTP Message Types** | New `TimeSyncRequest` / `TimeSyncResponse` DDS structs + `TimeConfig` settings |
| **B — Master Bug Fixes** | Two single-line fixes: initialise `_totalWallTicks` in constructor; populate `TargetSimTime` in `Step()` |
| **C — Slave NTP Handshake** | Slave computes `_masterWallClockOffset`; all barrier and PLL comparisons use `SyncedWallTicks` |
| **D — Translators** | `MasterTimeSyncTranslator` + `SlaveTimeSyncTranslator` + `TimeNetworkModule` factory methods |
| **E — Autonomous Tests** | Five test classes that simulate separate OS-clock domains and prove correctness entirely in-process, without DDS or external processes |
| **F — Integration Validation** | Verify the application layer (`Hrot.ClusterRunner`) still builds and all existing integration tests pass |

Read [DESIGN.md](./DESIGN.md) for the full architecture, background, and rationale.

---

## Where Things Live

```
d:\Work\IOS-IG-SimHost-FDP\
│
├── FDP/Toolkits/FDP.Toolkit.Time/           ← PRIMARY WORK AREA (Phases 1–4)
│   ├── Messages/
│   │   └── TimeMessages.cs                  ← Phase 1: add TimeSyncRequest/Response
│   ├── Controllers/
│   │   ├── TimeConfig.cs                    ← Phase 1: add MaxRttTicks etc.
│   │   ├── MasterSyncController.cs          ← Phase 2: constructor + Step() fixes + logging
│   │   └── SlaveSyncController.cs           ← Phase 3: NTP handshake + barrier/PLL fix + logging
│   ├── Translators/
│   │   ├── MasterTimeSyncTranslator.cs      ← Phase 4: NEW FILE
│   │   └── SlaveTimeSyncTranslator.cs       ← Phase 4: NEW FILE
│   └── TimeNetworkModule.cs                 ← Phase 4: add factory methods
│
├── FDP/Toolkits/FDP.Toolkit.Time.Tests/     ← TEST AREA (Phases 1–5)
│   ├── TimeMessagesTests.cs                 ← Phase 1 tests (add to existing file)
│   ├── MasterSyncControllerTests.cs         ← Phase 2 tests (add to existing file)
│   ├── SlaveSyncControllerTests.cs          ← Phase 3 tests (add to existing file)
│   ├── TimeSyncOffsetTests.cs               ← Phase 5: NEW FILE
│   ├── PauseBarrierSyncTests.cs             ← Phase 5: NEW FILE
│   ├── LockstepSimTimeAccuracyTests.cs      ← Phase 5: NEW FILE
│   ├── FullCycleMultiComputerSim.cs         ← Phase 5: NEW FILE
│   └── ClockSkewDriftTests.cs               ← Phase 5: NEW FILE
│
├── Hrot.ClusterRunner.Integration.Tests/    ← REGRESSION GUARD (Phase 6)
│   └── TimeControlIntegrationTests.cs       ← Must stay green; no modifications needed
│
└── IOS-IG-SimHost.sln                       ← Main solution
```

**Application files to watch but NOT modify in this workstream:**

| File | Why |
|------|-----|
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Hosts `MasterSyncController`; wiring of new translators is deferred to a follow-on |
| `Hrot.IG/IgApplication.cs` | Hosts `SlaveSyncController`; translator wiring deferred |
| `Hrot.ClusterRunner/Services/ExConSubsystem.cs` | Same |

---

## Design and Task Documents

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Full architecture, clock concepts, feature designs, files affected |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task instructions with exact success conditions and test method names |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Checkbox progress list — update as you complete each task |

---

## Key Concepts to Understand Before Coding

1. **`Stopwatch.GetTimestamp()` is OS-boot-relative** — comparing this value across machines
   without an offset will yield garbage results ranging from seconds to days.

2. **`SyncedWallTicks = _getTick() + _masterWallClockOffset`** — after a successful NTP
   handshake this expression gives the slave a clock that lives in the master's OS-tick domain.

3. **The NTP formula:**
   ```
   RTT    = (t4 - t1) - (t3 - t2)
   Offset = ((t2 - t1) + (t3 - t4)) / 2
   ```
   where t1 = slave send tick, t2 = master receive tick, t3 = master transmit tick, t4 = slave
   receive tick.  The formula is mathematically identical to the standard NTP clock-offset
   calculation.

4. **All barrier comparisons must use `SyncedWallTicks`, not `_virtualWallTicks`** — the barrier
   value (`BarrierWallTicks`) is an absolute master-domain tick, so the slave must compare it
   against its master-domain view.

5. **`TargetSimTime = 0` was a bug** — the slave was accumulating `_totalTime += delta` from its
   own locally-drifted starting point, causing the sim-time discrepancy to be locked in on every
   step rather than corrected.

Read the [design talk](./design_talk.md) for the full interactive diagnosis that led to these
conclusions.

---

## How to Build

Open a terminal at `d:\Work\IOS-IG-SimHost-FDP\` and build the full solution:

```powershell
dotnet build IOS-IG-SimHost.sln
```

Or build just the time toolkit and its tests:

```powershell
dotnet build FDP/Toolkits/FDP.Toolkit.Time/FDP.Toolkit.Time.csproj
dotnet build FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj
```

---

## How to Run Tests

Unit tests (fast, no DDS, no window — the primary validation target for Phases 1–5):

```powershell
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj
```

Integration regression guard (Phase 6):

```powershell
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
```

Run a specific new test class by filter:

```powershell
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj \
  --filter "FullyQualifiedName~PauseBarrierSyncTests"
```

---

## Developer Guide

Read the developer workflow guide before writing code or submitting a batch report:

**[d:\Work\IOS-IG-SimHost-FDP\.dev\.guides\DEV-GUIDE.md](../.guides/DEV-GUIDE.md)**

Follow the batch-based workflow: receive instructions → plan → implement → write a batch report →
submit for review.  Do not merge until all success conditions for your batch pass.

---

## Implementation Order

The phases have explicit dependencies — implement them in order:

```
Phase 1  (TC3-P1)  Messages + TimeConfig          ← no dependencies
    ↓
Phase 2  (TC3-P2)  MasterSyncController fixes      ← depends on Phase 1
    ↓
Phase 3  (TC3-P3)  SlaveSyncController NTP         ← depends on Phase 1 + 2
    ↓
Phase 4  (TC3-P4)  Translators + NetworkModule     ← depends on Phase 1 + 3
    ↓
Phase 5  (TC3-P5)  Multi-computer unit tests       ← depends on Phase 2 + 3
    ↓
Phase 6  (TC3-P6)  Integration validation          ← depends on Phase 2 + 3 + 4
```

**Important:** Phase 5 (the autonomous tests) must reach green before any pull request is raised.
The tests are designed to be the primary evidence of correctness — reviewers will look at them
first.
