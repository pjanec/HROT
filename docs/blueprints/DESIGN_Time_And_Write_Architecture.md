<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-21
current-answer: §3 findings · §4 the probe results · §5 the target · §6 the refactor list.
  ⭐ Dispatched as Batch 104 (RF-1, RF-2, RF-3, RF-5, RF-6). ⛔ RF-4 is NOT in it — §7.
stale-below: nothing above HISTORY.
known-rot: none. ⚠ Two claims from this file's FIRST edition were corrected by the probes and
  are marked CORRECTED inline — the pause barrier (§3 AS-2) and the threading premise (§4 P4).
known-conflict: none. Q48 §5 is the RULING and wins on intent; this file measures what it costs.
-->
# ⭐⭐⭐ Time, Pause and the Write Path — **as-is, target, and the refactors between**

> ⭐⭐ **Why:** `R-126` settled the intent — one source (the clock), the tick loop drains, running is not
> a refusal. ⛔ **This document measures what that costs.**
>
> ⭐⭐⭐ **All six probes are now RUN.** ⚠ **Three of them corrected this document's own first edition**,
> and one of those corrections changes the design — §4 `P6`.

---

## 1. ⭐⭐ INVENTORY — **the queries behind every claim** *(`R-74`)*

Graph `home-user-HROT` @ `ac7860dd8` — **175 663 nodes / 438 004 edges**.

| # | query | total |
|---|---|---|
| Q1 | `search_graph(name_pattern=".*(IsPaused\|IsFrozen\|IsStopped\|PausedBy).*")` | **91** → 12 production notions *(`Q48` §0)* |
| Q2 | `search_graph(name_pattern=".*(StageFieldMutation\|StageMutation\|DrainStaged\|PendingMutations\|ApplyStaged\|FlushStaged).*")` | **50** |
| Q3 | `trace_path("DataBreakpointManager.DrainPendingMutations", inbound, 3)` | **3** production callers |
| Q4 | `search_graph(name_pattern=".*(SetComponentFieldRaw\|SetComponentRaw\|SetManagedComponentRaw\|WriteLive\|TryWriteWorkingState\|ApplyEdit\|CommitEdit).*", label="Method")` | **37** |
| Q5 | reads in full or in the relevant part: `GlobalTime` · `MasterSyncController` · `MasterSyncTimeControllerAdapter` · `DataBreakpointManager` · `DebugSnapshotProvider` · `SystemScheduler` · `ModuleHostKernel` · `ExecutionPolicy` · `SubsystemOrchestrator` · `ClusterUiCache` · `AiTracerCoordinator` · `AiDebugSessionBase` | — |
| Q6 | ⭐⭐ **an executable rail** — `ThePauseFlagOnTheClockIsFalseWhilePausedTests`, 4 tests | ⭐ **the only claims here that are MEASURED rather than READ** |

---

## 2. ⭐⭐⭐ THE AS-IS

```mermaid
graph TD
    subgraph Clock["TIME"]
        MSC["MasterSyncController<br/>Continuous | BarrierPending | Stepping"]
        GT["GlobalTime (ECS singleton)<br/>DeltaTime - TimeScale - FrameNumber"]
        MSC -->|"Update() returns"| GT
    end

    subgraph Kernel["ModuleHostKernel.UpdateInternal"]
        K1["push GlobalTime"] --> K2["ExecutePhase Input<br/>~25 state-mutating systems"]
        K2 --> K3["ExecutePhase BeforeSync<br/>DebugSnapshotProvider captures pre-tick"]
        K3 --> K4["DISPATCH modules<br/>ShouldRunThisFrame ignores dt"]
        K4 --> K5["PostSimulation / Export"]
    end

    subgraph Bp["BREAKPOINTS - a repo rewind"]
        DBM["DataBreakpointManager<br/>_isPaused - pre/post snapshots<br/>_pendingMutations"]
    end

    subgraph Editor["EDITOR - twelve private answers"]
        A1["BlueprintDebugSession._isPaused"]
        A2["IsPausedByDebugger = GetMode()==Deterministic"]
        A3["PerspectiveWorkspaceServices.IsFrozen"]
        A4["ClusterUiCache.IsPaused"]
    end

    MSC --> Kernel
    Kernel --> DBM
    DBM -->|"OnHit: rewind + RequestPause"| MSC
    A2 -.->|"asks MODE - late and wrong"| MSC
    A1 -.->|"asks nobody"| A1
    A3 -.->|"ORs A1 and A2"| A1
    A4 -.->|"observes the WIRE"| MSC
    GT -.->|"ZERO production readers"| A3
```

---

## 3. ⭐⭐⭐ FINDINGS

| # | finding | 📐 evidence | ⚠ |
|---|---|---|---|
| **`AS-1`** | ⛔⛔ **`GlobalTime.IsPaused` (`TimeScale == 0`) is FALSE while paused.** A pause never touches `TimeScale` | ⭐⭐ **RAIL** `APausedClockReportsZeroDeltaAndYetIsPausedIsFalse` | 🔴🔴 |
| **`AS-1b`** | ⛔⛔ **`GetCurrentState()` is `BuildGlobalTime(0f, 0f)`** — it hard-codes the delta ⇒ **a delta predicate read through it says "halted" FOREVER** | ⭐⭐ **RAIL** `GetCurrentStateHardCodesTheDeltaToZero…` | 🔴🔴 **the second landmine, in the same family** |
| **`AS-2`** ⚠ **CORRECTED** | ⛔ **A pause is a two-phase BARRIER.** `SwitchToDeterministic` enters `BarrierPending`, **not** `Stepping`, and `GetMode()` answers **`Continuous`** for the whole lookahead window. ⭐⭐ **But `DeltaTime` goes to zero on the FIRST frame** *("sim time is logically frozen from the moment SwitchToDeterministic() is called")* | ⭐⭐ **RAIL** `TheDeltaGoesToZeroBeforeTheModeChanges` | 🔴 **`IsPausedByDebugger` is LATE as well as wrong** — ⭐ and this **strengthens** the delta |
| **`AS-3`** | ⛔ **`BlueprintDebugSession._isPaused` asks nobody** | `:1109`, `:920` | 🔴 **the gate that refused the user** |
| **`AS-4`** | ⛔⛔ **the breakpoint pause is a repo-REWIND protocol** — `OnHit`: post ← live; **live ← pre-tick**; halt. Resume: live ← post-tick; drain; advance | `DataBreakpointManager:470-473`, `:495`, `:514` | 🔴🔴 |
| **`AS-5`** | ⛔ **one drain, three production callers, none reachable from any UI** | Q3 + grep | 🔴🔴 |
| **`AS-6`** | ⭐ **the drain system has an exact precedent** — `DebugSnapshotProvider` | source | ✅ |
| **`AS-7`** | ⭐ **cluster-wide time control already exists** — intents + `SwitchTimeModeEvent` over DDS | `MasterSyncController:112-124` | ✅ |
| **`AS-8`** | ⭐ **intra-phase ordering is expressible**, and the scheduler is **phase-generic** *(`Dictionary<SystemPhase, …>`)* | `SystemScheduler:16,46,178-203` | ✅ **adding a phase is 1 enum member + 1 kernel line** |
| **`AS-9`** ⭐ NEW | ⛔⛔ **BTree/HSM "pause" never touches time.** `AiTracerCoordinator.RequestPause/RequestContinue/RequestStepOneTick` are **virtual no-ops**, and production constructs the **BASE class** — `EditorSubsystem:750` — while holding `_bpTimeAdapter` a few lines away | grep: no subclass, no override | 🔴 📌 **`R-67` again** |

---

## 4. ⭐⭐⭐ THE PROBES — **all six run**

| probe | verdict |
|---|---|
| **`P1`** — can the restore leave `RequestStep`/`RequestContinue`? | ⭐⭐ **YES, and it must go EARLIER than `BeforeSync`.** 📐 `Input` runs **first** and holds ~25 state-mutating systems, so a rewound repo must be restored **before** it ⇒ ⭐ **a new `PreFrame` phase**, which `AS-8` makes a 2-line engine change. ⭐⭐ **And `_isPaused` must stay TRUE until the restore runs**, because it also selects `ActiveView` |
| **`P2`** — is a repo available where `RunStateSource` is built? | ✅ **YES.** `_kernel = new ModuleHostKernel(_world, …)` *(`EditorSubsystem:661`)* ⇒ **`_world` IS the kernel's live world**, in scope at `:2226`. ⛔ No new plumbing |
| **`P3`** — what is `ClusterUiCache.IsPaused` for? | ⭐ **A REMOTE OBSERVATION, not a local cache.** Fed by `SwitchTimeModeEvent` off the bus *(`:173-181`)* — there is no local object to ask. ⇒ ⛔ **do NOT delete it**; ⚠ it stores **mode**, so it inherits `AS-2` and should be renamed/refined. 📌 `R-126`'s "don't cache" is about locally-derivable state |
| **`P4`** — is there a THREADING reason anything is unwritable? | ⛔⛔ **NO.** 📐 The runner is one loop — `while (_running) { Update(dt); DrawWorldAll(); DrawUIAll(); }` *(`SubsystemOrchestrator:105-114`)*; `DataStrategy.Direct` is **`Synchronous`-only and ENFORCED** *(`ExecutionPolicy.Validate:148-157`, called at `ModuleHostKernel:246`)*; async modules run on **leased views** and play back at harvest. ⇒ ⭐⭐ **the UI writes between frames with nothing else touching the live repo** |
| **`P5`** — the BTree/HSM twins | ⛔ **`AS-9`.** Their pause is a UI flag over a no-op coordinator. ⭐ **Good news: no rewind either**, so they are the SIMPLE case once they get a write path |
| **`P6`** ⭐⭐⭐ NEW | **Do modules tick while paused?** ⛔⛔ **YES.** `ShouldRunThisFrame` **never consults `deltaTime`** — a module at ≥60 Hz runs **every frame**, with `moduleDelta == 0` *(`ModuleHostKernel:614`, `:624`, `ShouldRunThisFrame`)* |

### ⛔⛔⛔ `P6` is the one that changes the design — **and it answers the user's question**

> 🔒 **User:** *"I do not understand how comes that something can be unwritable. The only real reason
> might be threading issues…"*

⭐⭐ **`P4` says the threading answer is NO — there is no race.** ⛔⛔ **But there is a second mechanism,
and it is real:** the simulation **keeps ticking while paused** *(dt = 0)*, so anything that **recomputes**
a value writes it again on the very next frame. ⇒ ⛔ **a direct write to the live repo is overwritten
before the designer's next frame**, with time still stopped.

| ⭐ consequence | |
|---|---|
| ⭐⭐⭐ **STAGE, do not write direct** | ⛔ **and not for the reason anyone assumed** — not threading, not the rewind: **because the tick never stops** |
| ⭐⭐ **drain at the top of an ADVANCING tick** | ⭐ the edit becomes the **input** to the tick that will use it — which is the semantics a designer expects |
| ⚠⚠ **AND THE HONEST UX COST** | ⛔ **for a RECOMPUTED variable, the new value cannot be shown while paused** — the next dt=0 frame overwrites it. ⭐ For a stored value nobody recomputes, a direct write would stick. ⇒ ⚠ **the panel must say "queued, applies on the next tick" rather than pretend** — 📌 this is a UX decision the design owes, not a defect |

⇒ ⭐⭐ **The old refusal sentence — *"the edit would be overwritten by the next tick"* — was describing a
REAL mechanism.** ⛔ What was wrong was **refusing** instead of **staging**.

---

## 5. ⭐⭐⭐ THE TARGET

```mermaid
classDiagram
    class GlobalTime {
        <<ECS singleton>>
        +float DeltaTime
        +float TimeScale
        +bool IsAdvancing
    }
    class SimClock {
        <<static, Fdp.Core>>
        +IsAdvancing(ISimulationView) bool
    }
    class ResumeAndDrainSystem {
        <<UpdateInPhase PreFrame>>
        +Execute(view, dt)
    }
    class IStagedWrites {
        <<interface>>
        +Stage(write)
        +bool IsRewound
        +RestorePostTick()
        +DrainInto(repo)
    }
    class DataBreakpointManager {
        -Queue _pendingMutations
        -bool _isPaused
        +DrainPendingMutations(repo)
    }
    class BlueprintDebugSession {
        +TryWriteWorkingStateField(...)
    }
    class RunStateSource {
        <<static>>
        +Resolve(...)
    }

    GlobalTime <.. SimClock : reads DeltaTime
    IStagedWrites <|.. DataBreakpointManager
    ResumeAndDrainSystem --> IStagedWrites : restore-then-drain
    ResumeAndDrainSystem ..> SimClock : only when advancing
    BlueprintDebugSession ..> IStagedWrites : stages, no time gate
    RunStateSource ..> SimClock : reads through
```

```mermaid
sequenceDiagram
    autonumber
    participant K as ModuleHostKernel
    participant D as ResumeAndDrainSystem
    participant Q as DataBreakpointManager
    participant I as Input phase
    participant R as live EntityRepository

    K->>K: push GlobalTime (DeltaTime known)
    K->>D: ExecutePhase(PreFrame)  [NEW - before Input]
    alt DeltaTime == 0
        D--)K: nothing - the edit waits
    else advancing
        opt IsRewound (a breakpoint pause is ending)
            D->>Q: RestorePostTick()
            Q->>R: live <- post-tick snapshot
        end
        D->>Q: DrainPendingMutations(repo)
        Q->>R: the designer's bytes land
    end
    K->>I: ExecutePhase(Input) - sees the restored, edited state
    Note over I,R: BeforeSync then captures pre-tick WITH the edit
```

⭐⭐⭐ **Why `PreFrame` and not `BeforeSync`:** `Input` runs first and mutates state. ⛔ Draining in
`BeforeSync` would let ~25 Input systems run against a rewound repo.

---

## 6. ⭐⭐⭐ THE REFACTORS

| # | refactor | feasibility | 📐 |
|---|---|---|---|
| **`RF-0`** ⭐ NEW | ⛔ **fix the arm that can never fire** — `EditorSubsystem`'s third `isFrozen` arm read `GetCurrentState().IsPaused` | ✅ **DONE** *(this commit)* + rails | `AS-1`, `AS-1b` |
| **`RF-1`** | **`GlobalTime.IsAdvancing => DeltaTime > 0`**, and mark `IsPaused` for retirement | ✅ **PROVEN** | one computed property |
| **`RF-2`** | **`SystemPhase.PreFrame` + one `ExecutePhase` line** | ✅ **PROVEN** | `AS-8` — the scheduler is phase-generic |
| **`RF-3`** | **`ResumeAndDrainSystem`** in `PreFrame`, gated, registered beside `DebugSnapshotProvider` | ✅ **PROVEN by precedent** | `AS-6` + `P1` |
| **`RF-4`** | **`RequestStep`/`RequestContinue` stop restoring+draining**; `_isPaused` clears in the system | ⚠ **PLAUSIBLE→LIKELY** | `P1`. ⛔ **Only 10 `_isPaused` sites in the DBM, all inside its own protocol**; every external consumer is display or per-frame |
| **`RF-5`** | **delete the time-shaped refusals** — running ⇒ stage | ✅ **PROVEN — mechanical** | `R-126` |
| **`RF-6`** | **`BlueprintDebugSession._isPaused` stops gating the write** | ✅ **PROVEN — one line** | `AS-3`; safe after `RF-3`+`RF-4` |
| **`RF-7`** | **`RunStateSource` reads the clock through** | ✅ **PROVEN** | `P2` — `_world` is right there |
| **`RF-8`** | **debugger pause publishes `PauseTimeIntent`** ⇒ cluster-wide by construction | ✅ **PROVEN** | `AS-7` |
| **`RF-9`** | ⛔ **`ClusterUiCache.IsPaused` — KEEP, refine to a mode name** | ✅ **decided** | `P3` — it observes the wire |
| **`RF-10`** | **BTree/HSM: hand the coordinator the real time controller** | ✅ **PROVEN — the caller holds it** | `AS-9` |
| **`RF-11`** | ⚠ **the "queued, applies next tick" affordance** in the edit dialog / panel | ⛔ **DESIGN OWED** | `P6` — ⚠ a UX decision, not a mechanism |

### ⭐ Order

```mermaid
graph LR
    RF0["RF-0 done"] --> RF1["RF-1 IsAdvancing"]
    RF1 --> RF2["RF-2 PreFrame phase"]
    RF2 --> RF3["RF-3 drain system"]
    RF3 --> RF4["RF-4 move restore out of resume"]
    RF4 --> RF6["RF-6 ungate the write"]
    RF5["RF-5 delete time refusals"] --> RF6
    RF3 --> RF5
    RF1 --> RF7["RF-7 RunStateSource"]
    RF6 --> RF11["RF-11 queued affordance"]
    RF8["RF-8 cluster pause"]
    RF10["RF-10 BTree/HSM coordinator"]
```

⭐⭐ **The spine is `RF-1 → RF-2 → RF-3 → RF-4 → RF-5 → RF-6`, then `RF-11`** — that is what the designer
feels. ⭐ `RF-7`–`RF-10` are independent and can go in any batch.

---

## 7. ⭐ WHAT IS STILL NOT PROVEN — **one item, named**

⚠ **`RF-4` is LIKELY, not proven.** ⭐ What is measured: the DBM has **only 10 `_isPaused` sites**, all
inside its own protocol, and **every external consumer is display or a per-frame system** — so a
one-frame deferral is invisible **provided `_isPaused` stays true until the restore runs.**
⛔ **What is NOT measured:** whether `BlueprintDebugSession`'s **own** step machinery *(temp
breakpoints, `_nodePointer`, the recorder)* tolerates the DBM resuming a frame later than the session.
⇒ ⭐⭐ **`Q48-E`'s end-to-end rail is what settles it**, and it should be written **before** `RF-4`.

⛔ **Everything else in §6 is either PROVEN or a decision.**
