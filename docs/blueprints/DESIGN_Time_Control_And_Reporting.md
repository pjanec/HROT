<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-08-21
current-answer: §2–§4 AS-IS (measured) · §6–§7 TARGET · §8 refactors · §9 the probes.
  ⛔ TC-3/TC-4 are BLOCKED on AS-12 — the editor's two buses. A decision is owed (§9).
stale-below: nothing.
known-rot: none.
known-conflict: none. ⚠ This is the TIME subsystem in full; DESIGN_Time_And_Write_Architecture.md
  is the WRITE path and cites this one for time. Where they overlap, this file is the time authority.
-->
# ⭐⭐⭐ Time — **control and reporting: how it works now, and how it should**

> ⭐⭐ **Why this exists** *(user, `2026-08-21`)*: *"we need design docs with mermaids about how the time
> reporting and time control works now (and what the apis look now) and how it should be working."*
>
> ⛔ **The write-path document answers "when may bytes land".** ⭐ **This one answers "who owns time, who
> may change it, and who may ask what it is."** 📌 The 12 pause notions are a **symptom**; this is the
> subsystem that produced them.

---

## 1. ⭐⭐ INVENTORY

Graph `home-user-HROT` @ `ac7860dd8`. ⭐ **Every claim below is from a read or a grep named here.**

| # | what | result |
|---|---|---|
| I1 | `search_graph(".*(IsPaused\|IsFrozen\|IsStopped\|PausedBy).*")` | **91** declarations → **12** production notions |
| I2 | `grep "TimeControllerFactory.Create\|new MasterSyncController\|new SlaveSyncController\|new SteppingTimeController\|SetTimeController"` | **the process topology — §2** |
| I3 | reads: `ITimeController` · `ISteppableTimeController` · `IEngineDebugTimeController` · `ITimeTransportFacade` · `ITimeControlGateway` · `MasterSyncController` · `SlaveSyncController` · `TimeControllerFactory` · `GlobalTime` · `ModuleHostKernel.UpdateInternal` · `ClusterMaster` · `ClusterUiCache` · the 3 transport implementations | **the API surface — §3** |
| I4 | `grep "SuspendGlobalTimePush\|SetSingletonUnmanaged(new GlobalTime"` | **4 suspend sites · 3+ singleton writers** |
| I5 | ⭐ **executable** — `ThePauseFlagOnTheClockIsFalseWhilePausedTests` *(4 tests)* | ⭐ the mode/delta claims are **measured**, not read |

---

## 2. ⭐⭐⭐ AS-IS — **who owns a clock**

```mermaid
graph TD
    subgraph Cluster["THE CLUSTER"]
        ORCH["Orchestrator<br/>MasterSyncController<br/>OrchestratorSubsystem:146"]
        SH["SimHost<br/>SlaveSyncController"]
        IG["IG<br/>SlaveSyncController<br/>IgApplication:726"]
        CGF["CGF<br/>SlaveSyncController<br/>CgfApplication:127"]
        EX["ExCon<br/>SlaveSyncController<br/>ExConSubsystem:192"]
        ORCH -->|"SwitchTimeModeEvent<br/>FrameOrderDescriptor (DDS)"| SH
        ORCH --> IG
        ORCH --> CGF
        ORCH --> EX
    end

    subgraph Ed["THE EDITOR - a SECOND master"]
        EDT["EditorSubsystem:715<br/>TimeControllerFactory.Create<br/>= MasterSyncController"]
    end

    EDT -.->|"no wire, no slaves"| EDT
```

> ⛔⛔ **THE EDITOR RUNS ITS OWN MASTER CLOCK.** 📐 `EditorSubsystem:715` casts the factory result to
> `MasterSyncController`. ⚠ **It has no slaves and publishes to no wire** — every editor pause is
> **local by construction**, which is exactly why 🔒 `R-126`'s *"debugger pause should force
> deterministic stepping cluster wide (now just inside the editor process)"* is a real gap and not an
> oversight.

### ⭐ The master's own states — **and a pause is TWO-PHASE**

```mermaid
stateDiagram-v2
    [*] --> Continuous
    Continuous --> BarrierPending : SwitchToDeterministic()<br/>barrier = now + Lookahead
    BarrierPending --> Stepping : wall clock crosses the barrier
    Stepping --> Continuous : SwitchToContinuous()
    BarrierPending --> Continuous : SwitchToContinuous()
    Stepping --> Stepping : Step(dt) then one advancing frame

    note right of BarrierPending
        GetMode() still answers Continuous
        DeltaTime is ALREADY zero
    end note
    note right of Stepping
        DeltaTime is zero between steps
        TimeScale is UNCHANGED throughout
    end note
```

⭐⭐ **Measured by rail.** ⇒ ⛔ **`GetMode()` is late; `DeltaTime` is prompt.** 📌 `M-42`.

---

## 3. ⭐⭐⭐ AS-IS — **the API surfaces**

```mermaid
classDiagram
    class ITimeController {
        <<interface>>
        +Update() GlobalTime
        +SetTimeScale(float)
        +GetTimeScale() float
        +GetMode() TimeMode
        +GetCurrentState() GlobalTime
        +SeedState(GlobalTime)
    }
    class ISteppableTimeController {
        <<interface>>
        +Step(float) GlobalTime
    }
    class IEngineDebugTimeController {
        <<interface>>
        +bool IsPausedByDebugger
        +RequestPause()
        +RequestResume()
        +RequestStepOneTick()
    }
    class ITimeTransportFacade {
        <<interface>>
        +bool IsPaused
        +double TotalTime
        +float TimeScale
        +TogglePlayPause()
        +Step()
        +Stop()
        +SetTimeScale(float)
    }
    class ITimeControlGateway {
        <<interface>>
        +RequestPause()
        +RequestResume()
        +RequestStep()
        +SetTimeScale(float)
    }
    class GlobalTime {
        <<ECS singleton>>
        +float DeltaTime
        +float TimeScale
        +double TotalTime
        +long FrameNumber
        +bool IsPaused
    }

    ITimeController <|-- ISteppableTimeController
    ISteppableTimeController <|.. MasterSyncController
    ITimeController <|.. SlaveSyncController
    ISteppableTimeController <|.. SteppingTimeController
    IEngineDebugTimeController <|.. MasterSyncTimeControllerAdapter
    MasterSyncTimeControllerAdapter --> MasterSyncController : wraps
    ITimeTransportFacade <|.. EditorTimeTransportAdapter
    ITimeTransportFacade <|.. EditorTimeTransportFacade
    ITimeTransportFacade <|.. ClusterTimeTransportAdapter
    ModuleHostKernel --> ITimeController : Update() each frame
    ModuleHostKernel --> GlobalTime : publishes (suspendable)
```

### ⛔ Five separate reporting surfaces, and they disagree

| # | surface | what it really answers | ⚠ |
|---|---|---|---|
| **①** | ⭐⭐ **`GlobalTime` on the live world** | **what the frame the simulation just ran did** | ⭐ **the only per-frame truth** — ⛔ and it had **zero production readers** until `2026-08-21` |
| **②** | `ITimeController.GetCurrentState()` | **cumulative position + settings** | ⛔ delta is **always 0** — it is a snapshot, not a reading |
| **③** | `ITimeTransportFacade.IsPaused` | **3 implementations, 2 of them byte-identical** — §4 | ⚠ editor arm reads `GetMode()` ⇒ **late by the barrier window** |
| **④** | `IEngineDebugTimeController.IsPausedByDebugger` | `GetMode() == Deterministic` | ⛔ a **MODE**, not a pause; true while merely planning |
| **⑤** | `ClusterUiCache.IsPaused` · `ClusterTimeTransportAdapter._isPaused` | **a REMOTE observation** off `SwitchTimeModeEvent` | ⭐ legitimately cached — ⚠ but **two independent caches of the same event** |

---

## 4. ⭐⭐⭐ AS-IS — **the control paths, and there are FOUR**

```mermaid
sequenceDiagram
    autonumber
    actor U as Operator
    participant CM as ClusterMaster
    participant BUS as FdpEventBus
    participant MSC as MasterSyncController
    participant DDS as DDS
    participant SL as Slave nodes

    rect rgb(238,246,255)
    Note over U,SL: PATH A - the CLUSTER path (correct shape)
    U->>CM: ClusterOpRequest PauseTime
    CM->>BUS: SlaveNodeSetUpdatedEvent + PauseTimeIntent
    Note over CM: time ops BYPASS 2PC by design
    BUS->>MSC: Update() drains the intents
    MSC->>MSC: SwitchToDeterministic(roster)
    MSC->>DDS: SwitchTimeModeEvent (barrier wall ticks)
    DDS->>SL: every node stops at the SAME wall tick
    end
```

```mermaid
sequenceDiagram
    autonumber
    actor D as Designer
    participant TB as Toolbar / EditorTimeTransportAdapter
    participant DBG as MasterSyncTimeControllerAdapter
    participant MSC as MasterSyncController (editor-local)

    rect rgb(255,240,240)
    Note over D,MSC: PATHS B and C - the EDITOR paths, both LOCAL and DIRECT
    D->>TB: TogglePlayPause
    TB->>MSC: SwitchToDeterministic(empty roster)
    Note over TB,MSC: no intent, no bus, no wire
    end

    rect rgb(255,248,235)
    D->>DBG: breakpoint hit -> RequestPause
    DBG->>MSC: SwitchToDeterministic(empty roster)
    Note over DBG: same call, different caller,<br/>and neither tells the cluster
    end
```

| path | who | shape | ⚠ |
|---|---|---|---|
| **A** ⭐ | ExCon / operator UI → `ITimeControlGateway` → `ClusterMaster` | ⭐⭐ **intents on the bus**, drained by the master, fanned out over DDS with a **wall-clock barrier** | ⭐ **the right shape** |
| **B** ⛔ | editor toolbar → `EditorTimeTransportAdapter` | ⛔ **direct method call** on the editor's own controller | ⛔ **bypasses the intent bus entirely** |
| **C** ⛔ | breakpoint / debugger → `IEngineDebugTimeController` | ⛔ **direct method call**, empty slave roster | ⛔ 🔒 `R-126`: this is the one that must go cluster-wide |
| **D** ⚠ | `AiTracerCoordinator.RequestPause/Continue/StepOneTick` *(BTree · HSM)* | ⛔⛔ **virtual NO-OPS** — production builds the base class *(`EditorSubsystem:750`)* | 🔴 📌 `AS-9` / `R-67` |

### ⛔⛔ `AS-11` — **a measured duplicate: `EditorTimeTransportFacade`**

📐 `diff` of `EditorTimeTransportFacade` vs `EditorTimeTransportAdapter`: **identical apart from the
name, the accessibility, and three null-guards.** ⭐ **Only the Adapter is constructed**
*(`TimeControlStatusBarSection:31`)*.
⚠ **Checked `.dev/` before saying so** *(the `2026-08-15` rule)* — the record is `.dev/main-toolbar-1/`
Batch 24, which introduced the facade; ⛔ **no record explains why both survive.**
⇒ ⭐ **A ruling-9 duplicate. The Facade is dead** — but 📌 **the right verdict is ROUTE-OR-DELETE decided
by name**: the *public* one has the better name and the null-guards; the *internal* one is the one
wired. ⛔ **Do not delete the wrong half.**

---

## 5. ⭐⭐⭐ WHY IT PRODUCED TWELVE NOTIONS — **the root cause, stated once**

| ⭐ | |
|---|---|
| **①** | ⛔ **there is no READ API.** ① is a raw ECS singleton lookup; every consumer that wanted a *question answered* — *"is it stopped?"* — **invented its own** |
| **②** | ⛔ **the one convenience flag that exists is wrong.** `GlobalTime.IsPaused` is `TimeScale == 0`, and no pause path sets that ⇒ **everyone who tried the obvious thing got a dead flag and wrote their own** |
| **③** | ⛔ **control is not uniform.** Path A goes through intents; B, C and D call methods directly ⇒ **a state change on one path is invisible to observers of another** |
| **④** | ⚠ **two legitimate remote observations** *(⑤)* look like the same kind of thing as the four local guesses ⇒ **"do not cache" gets applied to the wrong ones** |

⇒ ⭐⭐⭐ **The twelve are not carelessness. They are what happens when a subsystem has a WRITE API and no
READ API.**

---

## 6. ⭐⭐⭐ TARGET — **the APIs**

```mermaid
classDiagram
    class ISimClock {
        <<interface - READ>>
        +bool IsAdvancing
        +bool IsHalted
        +double TotalTime
        +float TimeScale
        +long FrameNumber
        +HaltReason Reason
    }
    class SimClock {
        <<static - the view-side facade>>
        +Of(ISimulationView) ISimClock
    }
    class ITimeCommands {
        <<interface - WRITE>>
        +Pause()
        +Resume()
        +StepOneTick()
        +SetTimeScale(float)
    }
    class IntentTimeCommands {
        publishes PauseTimeIntent etc.
    }
    class ITimeController {
        <<interface - PRODUCER, unchanged>>
        +Update() GlobalTime
        +GetCurrentState() GlobalTime
        +SeedState(GlobalTime)
    }
    class GlobalTime {
        <<ECS singleton - THE STORAGE>>
        +float DeltaTime
        +bool IsAdvancing
    }
    class ITimeTransportFacade {
        <<interface - UI only>>
    }

    SimClock ..> GlobalTime : reads the VIEW's singleton
    ISimClock <|.. SimClock
    ITimeCommands <|.. IntentTimeCommands
    IntentTimeCommands ..> ITimeController : via the intent bus, never directly
    ModuleHostKernel --> ITimeController : Update()
    ModuleHostKernel --> GlobalTime : publishes
    ITimeTransportFacade ..> ISimClock : reads
    ITimeTransportFacade ..> ITimeCommands : writes
```

| ⭐⭐ the three-way split | |
|---|---|
| ⭐⭐⭐ **PRODUCER — `ITimeController`** | ⭐ **unchanged.** It computes this node's frame time and hands it to the kernel. ⛔ **Nobody outside the kernel calls `Update()`, and nobody asks it questions** |
| ⭐⭐⭐ **STORAGE + READ — `GlobalTime` behind `ISimClock`** | ⭐ **one named read surface**, taking an `ISimulationView` so a caller cannot accidentally ask the wrong world. ⛔ **`IsAdvancing`, never `IsPaused`** |
| ⭐⭐⭐ **CONTROL — `ITimeCommands`, intents only** | ⭐ **paths B, C and D become path A.** ⛔ No direct `SwitchToDeterministic` outside the controller ⇒ 🔒 **`R-126`'s cluster-wide debugger pause comes free**, because the intent already fans out |

### ⭐ `HaltReason` — **why it is stopped, not just that it is**

⭐ `Running` · `PausedByOperator` · `SteppingHeld` · `HeldByBreakpoint` · `NotPublishing` *(replay
preparation — 📌 `AS-10`)*. ⛔ **Derived, never latched.** ⚠ **This is the field that would have made
every one of the twelve notions unnecessary**: each of them existed to answer *"why"*, and `bool` could
not.

---

## 7. ⭐⭐ TARGET — **one pause, one shape**

```mermaid
sequenceDiagram
    autonumber
    actor A as Any actor (toolbar, debugger, ExCon, BTree/HSM)
    participant TC as ITimeCommands
    participant BUS as FdpEventBus
    participant MSC as MasterSyncController
    participant K as ModuleHostKernel
    participant GT as GlobalTime on the live world
    participant R as any reader

    A->>TC: Pause()
    TC->>BUS: PauseTimeIntent
    BUS->>MSC: Update() drains it
    MSC->>MSC: SwitchToDeterministic(roster)
    MSC-->>K: Update() returns DeltaTime = 0
    K->>GT: publish
    R->>GT: SimClock.Of(view).IsAdvancing -> false
    Note over A,R: one path in, one path out.<br/>The cluster fan-out is the SAME intent.
```

⭐⭐⭐ **The property this buys:** ⛔ **there is no way to change time that a reader cannot see**, because
there is exactly one way to change it and exactly one place it is reported.

---

## 8. ⭐⭐ THE REFACTORS *(this document's own; the write-path list is separate)*

| # | | feasibility | 📐 |
|---|---|---|---|
| **`TC-1`** | ⭐⭐ **`ISimClock` + `SimClock.Of(view)`**, and `GlobalTime.IsAdvancing` | ✅ **PROVEN** | one property + one static; 📌 `RF-1` in the write-path doc is its first half |
| **`TC-2`** | ⭐ **retire the duplicate** `EditorTimeTransportFacade`/`Adapter` → one class | ✅ **PROVEN** | `AS-11`; ⚠ keep the better name + the null-guards |
| **`TC-3`** | ⭐⭐ **`ITimeCommands`, intents only** — path B onto it | ⛔⛔ **BLOCKED — probed, §9 `AS-12`** | ⚠ **the editor has TWO buses and the intents are on the wrong one.** ⭐ Needs a wiring decision first |
| **`TC-4`** | ⭐⭐⭐ **path C (debugger) onto `ITimeCommands`** ⇒ cluster-wide pause | ⛔ **blocked behind `TC-3`** | 🔒 `R-126`; 📌 UX Ruling 62 |
| **`TC-5`** | ⭐ **path D — hand the BTree/HSM coordinator a real controller** | ✅ **PROVEN** | `AS-9`; the caller holds it |
| **`TC-6`** | ⭐ **`HaltReason`** | ⚠ **design owed** | needs `AS-10`'s `NotPublishing` exposed from the kernel |
| **`TC-7`** | ⚠ **collapse the two remote caches** *(`ClusterUiCache` · `ClusterTimeTransportAdapter`)* | ⚠ **UNKNOWN** | ⛔ **not probed** — they may serve different processes |
| **`TC-8`** | ⚠ **`IsPausedByDebugger` retires into `ISimClock`** | ✅ **PROVEN** | it is a `GetMode()` read with 3 call sites |

---

## 9. ⭐⭐⭐ THE TWO PROBES — **run, and one of them BLOCKS `TC-3`**

### ⛔⛔⛔ `AS-12` — **the editor has TWO buses, and the time intents are on the wrong one**

📐 **Measured:**

| | |
|---|---|
| `EditorSubsystem:685-686` | `_orchestrationBus = new FdpEventBus();` — a **separate control-plane bus** — and `OrchestrationEventRegistry.RegisterAll(_orchestrationBus)` registers `PauseTimeIntent` / `ResumeTimeIntent` / `StepTimeIntent` / `SetTimeScaleIntent` **there** |
| `EditorSubsystem:715` | `TimeControllerFactory.Create(**_world.Bus**, timeConfig)` — ⛔ **the editor's clock drains intents from the WORLD bus** |

⇒ ⛔⛔ **A `PauseTimeIntent` published the cluster way would never reach the editor's clock.** ⭐ **That is
why paths B and C call methods directly** — 📌 **not laziness: the intent road does not connect.**

| ⭐ the three ways out — **a decision, not a discovery** | |
|---|---|
| **①** | construct the editor's controller on **`_orchestrationBus`** ⇒ ⚠ it then shares a bus with cluster management; **smallest diff, widest blast radius** |
| **②** | ⭐ **register the four time intents on `_world.Bus` too** and publish there ⇒ ⛔ **two buses carrying the same intent type** — a second notion by another name |
| **③** | ⭐⭐ **one explicit bridge** that forwards *only* the four time intents orchestration → world ⇒ ⭐ **the seam is named and one-way**, ⚠ and it is the shape the cluster hop will need anyway |
| ⭐ **my lean** | **③**, for approval — ⛔ but **`TC-3` must not be scheduled until this is chosen** |

⚠ **And a corroboration worth recording:** `Role = TimeRole.Standalone`, with the comment *"Start in
Deterministic mode so authoring starts paused (dt == 0 every frame)"* ⇒ ⭐ **the editor deliberately
boots HALTED**, which is why `IsPausedByDebugger` reads true while merely planning *(`AS-2`)*.

### ⭐ `TimeSystem` *(`Fdp.Core`)* — **LEGACY, not a competing writer**

📐 `grep "new TimeSystem("` ⇒ **one non-test construction: `Fdp.Examples.Showcase`.**
⇒ ⭐⭐ **It is NOT in the HROT path.** ⛔ Correcting an earlier note of mine that listed it among the
singleton's authors: **true across the repo, false for HROT.** ⚠ In HROT the singleton has exactly
**two** writers — `ModuleHostKernel.UpdateInternal` and `SimHostNodeBootstrapper`'s seed.

---

## 10. ⛔ WHAT THIS DOCUMENT STILL HAS NOT MEASURED — **named, not implied**

| ⛔ | |
|---|---|
| **`SlaveSyncController` internals** | the PLL, NTP offset handshake and ACK protocol. ⭐ Its **surface** is in §3; ⛔ its **behaviour** is not |
| **the replay/playback clock** | `PlaybackTickSystem` · `EcsRecordReplayController` · `ReplayProcessManager` — ⚠ **a third time authority**, touched only at `AS-10` |
| **`TC-7`** | what each of the two remote caches is for, and whether they serve different processes |

⇒ ⭐ **Suggested next: the replay clock**, because `AS-10` already showed it interacts with the write
path and it is the only remaining authority nobody has mapped.
