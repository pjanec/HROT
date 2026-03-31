# BUG1 — Bug-Fix Design Document

**Source:** [design-talk.md](./design-talk.md)  
**Task Detail:** [TASK-DETAIL.md](./TASK-DETAIL.md)  
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## Overview

This design document covers a batch of bug fixes and small features uncovered during interactive
testing of the IOS / IG / SimHost federated simulation runtime. The issues span four
concern areas:

1. **Infrastructure & Configuration** — DDS domain ID mis-handling and missing node-ID CLI flag
2. **Network Correctness** — ACK spam from non-authoritative nodes and orphaned DDS topic instances
3. **IG Feature: Continuous Drag Mode** — testing tool for latency observation during entity drag
4. **Mission System Fixes** — mission not advancing between waypoints, and a version conflict after
   abort

One item from the design talk was found to be *already implemented* in the current codebase and is
recorded here for completeness (see [Section 1.4](#14-ios-context-menu-delete-action--already-done)).

---

## Phase 1 — Infrastructure & Configuration

### 1.1 Fix SimHost DDS Domain Zero Guard

**Files:** `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`

`SimHostSubsystem.Initialize()` extracts the CLI domain flag with:

```csharp
int? domainOverride = config.DomainId > 0 ? config.DomainId : (int?)null;
```

DDS Domain **0** is the default and most commonly used domain. The `> 0` guard silently discards it,
causing `SimHostApp` to fall back to its local `config.json`. When that file is not found (wrong
CWD) it falls back to the hardcoded default of **domain 42** (`NodeConfiguration.cs`). The IG and
IOS accept `DomainId = 0` directly, so the three processes silently end up on different DDS domains
and can never exchange messages.

**Fix:** Replace the guard with a direct pass-through:

```csharp
int? domainOverride = config.DomainId;
```

Zero is a valid domain. If the runner does not need to differentiate "unspecified" from "0",
`RunnerConfiguration.DomainId` should use a **nullable int** (or a sentinel value like `-1`) as the
default and the guard should be rewritten to check `hasValue` instead of `> 0`.

---

### 1.2 Add `--node-id` CLI Option to Runner

**Files:**  
- `FDP/Framework/FDP.Framework.Runner/RunnerConfiguration.cs`  
- `FDP/Framework/FDP.Framework.Runner/RunnerOptions.cs`  
- `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs`  
- `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`  
- `Hrot.ClusterRunner/Program.cs`  
- `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`  
- `Hrot.ClusterRunner/Services/IgSubsystem.cs`

Currently all node identifiers are **static constants** (`SimHostNetworkConstants.LocalNodeId = 1`,
`IgNetworkConstants.InstanceId = 300`). This prevents running multiple instances of the same
subsystem type on the same machine.

A new `--node-id` (short `-n`) CLI argument is added as a **base ID**. The orchestrator applies
type-specific deterministic offsets to each subsystem so every instance gets a unique network
identity:

| Subsystem | Offset | Default resolved ID (base=0) |
|---|---|---|
| SimHost   | +0     | 1 (legacy constant)            |
| IG        | +100   | 300 (legacy constant)          |
| IOS       | +200   | 500 (legacy constant)          |
| Other     | +300   | 1000                           |

When `--node-id` is **not supplied** (`0`), the orchestrator falls back to the existing legacy
constants so no existing launch scripts break.

Running two IGs requires two separate runner processes, each with a distinct `--node-id`:

```
Hrot.ClusterRunner.exe -m ig --node-id 300
Hrot.ClusterRunner.exe -m ig --node-id 301
```

These resolve to internal IDs 400 and 401 respectively (applying the IG offset of +100).

#### Why single-process multi-IG is not supported

`RunMode` is a `[Flags]` enum; passing `-m ig,ig` is a no-op because bitwise-OR produces a single
`IG` flag. The runner will instantiate exactly one `IgSubsystem`. This is a deliberate constraint
and is acceptable for the current scope.

---

### 1.3 Fix Batch Script Working Directory

**Files:** `run_all_standalone.bat`, `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat`

When the batch scripts run from the solution root, all relative asset paths (e.g.
`Assets/sample_road.json`, `config.json`) fail silently. Two symptoms:
- SimHost falls back to domain 42 (see §1.1)
- Road network is not rendered (silent `FileNotFoundException` swallowed in `SimHostApp.cs`)

**Fix:** `cd` into the compiled output directory before launching the processes:

```bat
@echo off
setlocal

set DOMAIN=0
cd /d "%~dp0Hrot.ClusterRunner\bin\Debug\net8.0"
set RUNNER=Hrot.ClusterRunner.exe

start "SimHost" %RUNNER% -d %DOMAIN% -m simhost --no-wait
start "IG"      %RUNNER% -d %DOMAIN% -m ig      --no-wait
start "IOS"     %RUNNER% -d %DOMAIN% -m ios     --no-wait
```

This matches the behaviour of Visual Studio's `commandName: Project` launch profile which
automatically sets CWD to the output directory.

---

### 1.4 IOS Context Menu — Delete Action ✅ Already Done

**Files:** `Hrot.ExCon/Logic/ContextMenuLogic.cs`

The design talk identified that the "Delete" context menu action (`ContextMenuActions.Delete`,
ID = 10) was restricted to `MenuStrategy.Admin`. The current code already has it in
`MenuStrategy.Standard`:

```csharp
MenuStrategy.Standard => new List<ContextMenuItem>
{
    new() { Id = ContextMenuActions.CenterOnEntity, ... },
    new() { Id = ContextMenuActions.Properties,     ... },
    new() { Id = ContextMenuActions.Delete,   Label = "DELETE", Style = "destructive" },
},
```

The `Admin` strategy's Delete line is commented out. No further work is required.

---

## Phase 2 — Network Correctness

### 2.1 Enforce Silent Bystander Rule in `UpdateEntityDescriptorRequestSystem`

**Files:** `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs`

The BDC SST architecture mandates the **Silent Bystander Rule**: a node that is not authoritative
for a resource must not emit an ACK — it must silently discard the request. This rule is correctly
implemented in `UpdateEntityAttributeRequestSystem` but is violated by
`UpdateEntityDescriptorRequestSystem`.

Current anti-patterns:

| Location | Current behaviour | Correct behaviour |
|---|---|---|
| `ProcessRequest` — entity not found | `WriteAck(EntityNotFound)` | Silent return |
| `ProcessRequest` — unsupported type | `WriteAck(NotSupported)` | Silent return |
| `ProcessWorldPosUpdate` — not authoritative | `WriteAck(NotOwner)` | Silent return |
| `ProcessMapVisualOverlayUpdate` — not authoritative | `WriteAck(NotOwner)` | Silent return |

The practical consequence of the current behaviour: with two SimHost instances accidentally running
(zombie process in standalone testing), **both** believe they hold authority (`LocalNodeId == 1`),
both apply the mutation and both emit a `Success` ACK. The IOS
`RequestTransactionManager` receives two identical success correlations for the same `RequestId`.

**Fix:** Remove the failure-path `WriteAck` calls. Only the true owner of the descriptor should
ever call `WriteAck(Success)`.

---

### 2.2 Fan-Out Entity Descriptor Disposal in `CycloneNetworkCleanupSystem`

**Files:**  
- `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs`  
- Wherever `CycloneNetworkCleanupSystem` / `NetworkCleanupModule` is constructed (SimHost bootstrap)

When an entity is deleted, only the `EntityMaster` DDS topic instance is disposed. Every other
descriptor topic (`WorldPos`, `EntityInfo`, `MapVisualOverlay`, etc.) retains a live
`TransientLocal` sample in the DDS middleware queue. Late-joining IGs accumulate these as **ghost
descriptors** — partial records waiting for a master that will never arrive — burning CPU and memory.

**Architectural note:** Per BDC SST contract, entity existence is dictated solely by `EntityMaster`.
The fan-out disposal is therefore a *pragmatic cleanup* measure, not a correctness requirement. All
egress translators already implement `IDescriptorTranslator.Dispose(long networkEntityId)` which
calls `Writer.DisposeInstance`.

**Fix:** Refactor `CycloneNetworkCleanupSystem` to accept `IEnumerable<IDescriptorTranslator>`
instead of a single master translator. On entity destruction, iterate the collection and call
`Dispose(netId)` on each, swallowing individual exceptions so one bad translator cannot prevent
the others from running.

Update the `NetworkCleanupModule` wrapper and the SimHost bootstrap registration accordingly.

---

## Phase 3 — IG Continuous Drag Mode

### 3.1 Add Continuous Drag Update Toggle

**Files:**  
- `Hrot.IG/Systems/MapUserConfig.cs`  
- `Hrot.IG/IgApplication.cs`

Currently the IG only sends a `UpdateEntityDescriptorRequest` for a `WorldPos` update when the
operator **drops** the entity (fires `OnEntityDragEnded`). The SimHost ghost-preview mechanism
works correctly because the drag sends nothing and the drop sends the final position.

To test how the system behaves under various network latencies, a **Continuous Backbone** mode is
needed: the IG sends throttled position updates *during* the drag, allowing the SimHost to mutate
the authoritative ECS state and egress the WorldPos confirmation back in real-time.

#### Throttling requirement

`UpdateEntityDescriptorRequest` uses **Reliable** DDS QoS. Broadcasting at 60 Hz would flood the
reliable queue and create TCP-like backpressure, artificially inducing the latency it is meant to
measure. The update rate must be throttled (design talk specifies **10 Hz**).

#### Design

1. **`MapUserConfig.ContinuousDragUpdates`** (bool, default `false`) — toggled via the Debug Panel.
2. **`IgApplication.SendWorldPosUpdate(Entity, Vector2)`** — extracted helper that contains the
   full request-building and publishing logic currently inlined in `OnEntityDragEnded`. Both the
   drag-move path and the drag-end path call this helper.
3. **`IgApplication._continuousDragTimer`** (float) — per-frame accumulator; fires the update and
   resets when it exceeds 0.1 s (10 Hz).
4. **`OnEntityMoved` subscription** — updated to use the entity parameter, accumulate the timer,
   and call `SendWorldPosUpdate` when throttle fires (only when `ContinuousDragUpdates == true`).
5. **`OnEntityDragEnded`** — simplified to delegate to `SendWorldPosUpdate` using the tracked
   `_lastDragWorldPos`; resets `_continuousDragTimer`.

---

## Phase 4 — Mission System Fixes

### 4.1 Default `DoctrineFinished` Trigger on Task Creation

**Files:** `Hrot.ExCon/Panels/MissionPanel.cs`

When the IOS `HandleAddTask()` method creates a new `MissionTask`, it populates `Triggers` with an
**empty list**. On the SimHost, `MissionControlRequestSystem.ResolveTrigger()` receives zero
triggers and deliberately assigns the fallback state
`(EcsMissionTrigger.TimerElapsed, float.MaxValue)`. This causes the `MissionDirectorSystem` to wait
**forever** after the first task completes — the entity reaches its destination but never advances
to the next waypoint.

**Root cause chain:**

```
HandleAddTask() → Triggers = [] 
  → ResolveTrigger() fallback → TimerElapsed(float.MaxValue) 
  → MissionDirectorSystem waits indefinitely
  → entity stops at waypoint 1, never continues
```

**Fix:** Inject `DoctrineFinished` as the default trigger in `HandleAddTask()`:

```csharp
Triggers = new List<MissionTrigger> { new MissionTrigger { Type = "DoctrineFinished" } }
```

`DoctrineFinished` is the architecturally correct default. The backend pipeline already supports it:
- `BTreeTickSystem` publishes `DoctrineFinishedEvent` when a behavior tree reaches a terminal state.
- `MissionDirectorSystem` natively evaluates `EcsMissionTrigger.DoctrineFinished`.
- `MissionControlRequestSystem` already parses the string `"DoctrineFinished"` from the DDS payload.

No SimHost-side changes are required.

---

### 4.2 Track Control Commands for OCC Version Sync

**Files:**  
- `Hrot.ExCon/Services/IMissionEditorService.cs`  
- `Hrot.ExCon/Services/MissionEditorService.cs`  
- `Hrot.ExCon/Panels/MissionPanel.cs`

When the operator clicks **ABORT**, the IOS sends `CMD_ABORT_ALL` via `SendControlCommand()`. The
SimHost processes the command, increments the OCC version (e.g. 1 → 2), and returns a
`MissionControlAck` containing the new version. However, `SendControlCommand` is **fire-and-forget**
— it does not track the `RequestId`, so the ACK is discarded. The IOS UI retains `_draftBaseVersion
= 1`. A subsequent COMMIT sends `BaseVersion = 1`; the SimHost correctly rejects it with
`ERR_VERSION_CONFLICT (errorCode = 7)`.

**Fix:** Replace `SendControlCommand` with an **async tracked transaction** that mirrors the existing
`CommitMissionAsync` pattern:

1. Add `Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId)` to `IMissionEditorService`.
2. Implement it in `MissionEditorService` using a `TaskCompletionSource<MissionCommitResult>` stored
   in `_pendingCommits`, just like `CommitMissionAsync`. Control commands do not perform version
   checks, so `BaseVersion = 0` is sent in the request.
3. In `MissionPanel.HandleAbort()` and `HandleJump()`, replace the fire-and-forget call with the
   async version and assign the returned `Task<MissionCommitResult>` to `_pendingCommit`, setting
   `_commitInFlight = true`.
4. The existing `PollCommitCompletion()` mechanism already extracts `result.NewVersion` and assigns
   it to `_draftBaseVersion` — no changes required there.

The UI is already designed to serialise operations via `_commitInFlight`; routing control commands
through the same pipeline therefore locks the UI buttons until the ACK is received, preventing
double-clicks and ensuring version coherence.
