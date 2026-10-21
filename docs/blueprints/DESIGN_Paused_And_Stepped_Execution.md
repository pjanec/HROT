<!--STATUS
state: LIVE
build-state: BUILT (2026-10-21: S1–S5, §7a–§7e; the gate is on — FdpConfig.TickModeGate)
updated: 2026-10-21 (§10 F5–F14 leans APPROVED by the user and folded into §1/§7; READY-TO-BUILD) · 2026-10-21 (§10 F1/F2/F3/F4/F9/F13 resolved by measurement and R-3038; Appendix A: BehaviorIngressSystem + RoeSystem → EveryFrame) · 2026-10-21 (§10 critical review: 14 findings, build-state back to DESIGN) · 2026-10-21 (created — backend; CE-3293, R-3036, R-3037)
current-answer: §1 the rules (they carry §10's resolutions) · §7 the build order · §10 the review record · §2–§4 the diagrams · §7 the build order. Appendix A/B are the per-system and per-module verdicts.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/DESIGN_Time_Architecture.md — owns time: who changes it, the zero-delta pause, §17 stepped-mode determinism
    (sim-time cadence, the step wait). Its §17 OPEN item "event visibility across paused frames" is what THIS document settles.
  - docs/designs/mgmt-1/DESIGN.md — owns editing while paused ("Entities placed/modified", :431) and Always Recording (:1941):
    the reasons ~40 % of systems must run while paused.
  - docs/DESIGN_Film_Director.md — a CONSUMER: needs stepped runs to repeat (R-3035); paused until this lands (R-3034).
  - FDP/Engine/Fdp.ModuleHost/docs/UserGuide/05_EventBus.md — owns the bus and its one-frame double buffer.
-->
# ⭐⭐⭐ PAUSED AND STEPPED EXECUTION — **every system says when it runs; a step-only system reads every event since the last step**

> 🔒 **User, `2026-10-21`:** *"if it's 40% of those that need to run in pause, then we need to explicitly tell for every
> system what mode it is in. There shouldn't be any default."* · *"System should simply read event, and the framework should
> make sure that the system that shouldn't be ticked when time is paused is not ticked when time is paused, meaning that when
> the system reads the event from its bus using a normal and simple read event, will get those accumulated events."*
> ⇒ **R-3036.** Tracker [`CE-3293`](Blueprint_Issues_Tracker.md).

## INVENTORY *(run `2026-10-21`)*

| query | total |
|---|---|
| Roslyn `roslyn_find_implementations("IEcsModuleSystem")`, workspace `Hrot.Core.csproj` (`is_msbuild_workspace: true`, root solution) | **251** → 202 production, 48 test/example, 1 abstract base |
| Roslyn `roslyn_find_implementations("IEcsModule")`, same workspace | **85** → 40 production |
| the only project outside the root solution, `Stride/HrotStrideApp.Windows` | Roslyn could not load it (never restored); its one source file is a 51-line launcher, no system |
| prior art: `search_code` for `TickWhenPaused · RunsWhilePaused · PausePolicy · TickMode · RunWhenPaused` over code and docs | **0** |
| `repo.Bus.Read*` in production files · `view.ReadEvents*` · casts of the view to `EntityRepository` | **43 · 70 · 158** files (grep) |

Every production system and module was read, its `Execute`/`Tick` body included (Appendix A, B). ⚠ `check_index_coverage` is not
available through the codebase-memory CLI; the counts above come from Roslyn, not the graph.

## 0. The problem, in one table

| today | consequence |
|---|---|
| pause = a **zero delta**; every system is still called every rendered frame | work scaled by `dt` stops, ⛔ but reacting to an EVENT does not — it happens at full strength on a paused frame |
| an event is readable for **one frame** (`FdpEventBus` double buffer) | a system that skips paused frames (the brain, `BrainTickSystem.cs:135`) **misses** every event that became readable on one |
| 8 systems skip paused frames **by hand** (`if (deltaTime <= 0f) return;`) | the rest run while paused by accident, not by design — 📐 no design says simulation reacts while paused |

⇒ stepped runs depend on how many frames were drawn between steps (the §17 OPEN item of
[`DESIGN_Time_Architecture.md`](DESIGN_Time_Architecture.md)).

## 1. ⭐⭐ The rules

| # | rule | why |
|---|---|---|
| **1** | ⭐⭐⭐ **Every system and every module carries exactly one `[TickMode(...)]`: `WhenTimeAdvances` or `EveryFrame`.** ⛔ No default. The attribute is **not inherited** (`AttributeUsage(Inherited = false)`): a base class cannot hand its mode down. Checked at registration, on the **first `ExecuteSystem` of a type** (group members, manual systems) and by a reflection rail over the loaded assemblies — each throws or fails naming the type (§10 F10). | 📐 82 of 202 production systems must run while paused, 116 must not (Appendix A) — neither is a safe default (R-3036) |
| **2** | ⭐⭐ **The gate is `SystemScheduler.ExecuteSystem`**: on a frame whose delta is 0, a `WhenTimeAdvances` system is not called — **in every mode**, stepped or continuous (time scale 0 included). The kernel gates a module's own `Tick` and a background module's dispatch the same way (§10 F11). A module with a **snapshot view** has one mode for all its systems — its own — and a `WhenTimeAdvances` snapshot module is not dispatched on a paused frame (§10 F5). | the one method every scheduled call passes (`SystemScheduler.cs:147`) |
| **3** | ⭐⭐ **No system is called around the gate.** The 34 direct calls (§10 F3, Roslyn) go through it: modules tick their systems through `RegisterManualSystem` wrappers, the wrapper and the framework's groups call `ExecuteSystem`, and hosts outside the kernel (the replay browser, `IgApplication`, the Stride physics bracket) call its public form **with their own advancing flag** (the replay clock's; the kernel's, replacing `simRunning`). | a gate a call can bypass is not a gate |
| **4** | ⭐⭐⭐ **A system reads world events only through its view** (`view.ReadEvents<T>()` / `ReadManagedEvents<T>()`), never `Bus.Read*` / `HasManagedEvent` / `ReadRawByTypeId` — nor does any code it calls, the blueprint generator's output included (§10 F4: 96 files, Roslyn). Reads of the orchestration and interaction buses are unaffected. | the view is the designed per-reader window: *"Events include history since module's last run"* (`ISimulationView.cs:53-57`) |
| **5** | ⭐⭐⭐ **A system's view read returns every event since THAT SYSTEM'S LAST RUN** (§10 F6). For an `EveryFrame` system that is the current frame (the bus span itself); for a `WhenTimeAdvances` system it is everything since its previous step; for a reduced-rate system, everything since its previous run — in live play too. The system calls the same plain read. | the framework delivers what a skipped system missed; the system does not know (user, above) |
| **6** | ⭐⭐ **The accumulator keeps an event until its last reader has had it** — every system's window start and every background module's last-seen frame. An **empty frame takes no slot**; consecutive paused frames share one (§10 F9). A reader that has not read for the cap's length **expires** — counted and logged — and a removed module unregisters (§10 F8). What the cap drops is counted (`Overflow`), never silent. | 📐 `EventAccumulator.cs:16` keeps 60 frames today; 📐 paused frames carry almost nothing (§10 F9) |
| **7** | ⭐ **`SnapAndPause` restarts every window and the history** (a load, a preview Stop, a seek): nothing from before it is delivered after it (§10 F7). | the ONE time discontinuity (Time Architecture §12a), not an inference from the clock |
| **8** | ⭐ **The 8 hand-written `deltaTime <= 0` returns go** once the mark gates them. | one mechanism (ruling 9) |
| **9** | ⭐⭐⭐ **No allocation on the hot path** — capture every frame, the step-window read, the flush to a background module: **0 bytes per frame in steady state.** A buffer grows only when a new high-water mark is reached (a new event type, a busier frame than ever), and growth is counted. | 🔒 user, `2026-10-21`: *"The accumulator must be free of allocations on the hot path."* ⚠ today's accumulator is not (§1a) |
| **10** | ⭐ **Replay is orthogonal** (§10 F14): what may run while a recording plays stays with `mgmt-1` §8.10's gates, in place; a replayed frame whose `dt` > 0 counts as advancing. | `docs/designs/mgmt-1/DESIGN.md` §8.10 owns replay |


### 1a. ⭐⭐ Zero allocation — **what allocates today, and what replaces it**

📐 Read in the code `2026-10-21` (not yet measured — rail ⑤ in §7 measures it, red first):

| today, every frame | allocates | replaced by |
|---|---|---|
| `SnapshotCurrentBuffers` makes two `new List<…>` per frame (`FdpEventBus.cs:607-609`) | 2 lists + their growth | a **fixed ring of frame slots**, created once at the cap; a slot is reused, never freed |
| it walks `_nativeStreams.Values` / `_managedStreams.Values` — `ConcurrentDictionary.Values` builds a new collection on every call (`FdpEventBus.cs:612, 625`) | 2 snapshots per frame | an **append-only array of streams**, filled when a stream is created (rare), walked by index |
| managed events copied through `InspectReadBuffer()`, a `yield` iterator returning `object` (`ManagedEventStream.cs:43-49`) | an iterator per stream with events · ⛔ **a box per event** for a managed struct (`PublishManaged` allows structs) | each managed stream owns a **typed history** (`StreamHistory<T>`): the stream copies its read buffer into it by a virtual call — no iterator, no `object`, no box |
| native bytes copied into `ArrayPool` rentals and returned on trim | none after warm-up, but a rent and return per type per frame | a **byte arena per stream in each slot**, grown to its high-water mark and kept |
| the flush to a background snapshot goes through `InjectManagedIntoCurrent`: `GetMethod` + `MakeGenericMethod` + `Invoke(new object[] …)` per managed type (`FdpEventBus.cs:580-590, 712`) | reflection + an argument array per type per flush | the typed history **injects itself** into the replica's stream of the same `T` — a virtual call, no reflection |

**The step-window read** (`ReadSince`, rule 5):
- **When the window is only the current frame** (live play, and a step with no paused frame before it), it returns **the bus span itself**: zero copy.
- **Otherwise** it concatenates the window into the stream history's **reusable contiguous buffer** (`T[]` for native events, a reused `List<T>` for managed), once per type per frame. Every step-only reader of that type shares it.

### 1b. ⭐⭐ Systems that run while paused AND on steps — **where their events come from**

| | `EveryFrame` system | `WhenTimeAdvances` system |
|---|---|---|
| called | every frame, paused or not (`dt` = 0 while paused) | only on a frame that advances |
| `view.ReadEvents<T>()` returns | ⭐ **the current frame only** — the bus's read buffer, exactly as today; the step window is never open for it | everything since the previous step |
| an event published at step N is read | once, on frame N+1 — whether N+1 is paused or a step | once, at the next step |

⭐ An `EveryFrame` system runs on every frame, so it never misses an event and never sees one twice — it needs no accumulator. On a
step frame both kinds read the same frame's events; the step-only one also gets the paused frames before it.
⚠ It still receives `dt` = 0 while paused, so its `dt`-scaled work stands still exactly as today.

⚠ **A system whose work is HALF paused-time, half sim-time is split, not given a third mode.** The readers found three, all
`WhenTimeAdvances`, each with a small duty an operator expects to see while paused (§8):

| system | the paused-time duty it carries today | split into |
|---|---|---|
| `CarKinematicsSystem` | ground-clamps Z after a vehicle is dragged (`CarKinematicsSystem.cs:623-624`) | an `EveryFrame` clamp of moved entities |
| `AirMotionSystem` | recompiles the `AirPath` when a route is edited (`AirMotionSystem.cs:48`) | an `EveryFrame` route compile, beside `RouteTrajectorySyncSystem` |
| `UnitCentroidSystem` | moves a commander to his members' centroid after a drag (`UnitCentroidSystem.cs:20-45`) | an `EveryFrame` centroid on a member's move |

⭐ **In live play every frame advances** ⇒ the window is always the current frame ⇒ `ReadEvents` returns the bus span itself
(zero-copy). Nothing changes for live play.

⭐ **Why an `EveryFrame` system stays deterministic:** it reads each event exactly once, on the first frame after it was
published, ⛔ always before the next step. The number of paused frames in between changes nothing.

## 2. ⭐⭐ Who calls what each frame — **the module relationship**

```mermaid
graph TD
  K["ModuleHostKernel.UpdateInternal<br/>every rendered frame"]
  PH["Scheduler.ExecutePhase<br/>PreFrame · Input · BeforeSync · PostSimulation · Export"]
  DI["module dispatch"]
  SS["sync module SimulationSystems"]
  MT["sync module Tick<br/>gated by the module's TickMode"]
  AJ["async module job<br/>snapshot view, flushed by EventAccumulator"]
  G{{"ExecuteSystem<br/>THE GATE + the step window"}}
  MW["RegisterManualSystem wrappers"]
  X1["direct Execute today:<br/>NavigationSolverModule · PhysicsQueryModule ·<br/>GizmoInteractionModule · EditorSystemsModule · EqsModule"]
  X2["NetworkLifecycleSystemGroup<br/>sys.Execute directly"]
  X3["TogglablePostSimulationGroup.Execute<br/>called directly by GizmoInteractionModule"]
  X4["StridePhysicsBracket<br/>BulletReverseSyncSystem directly"]
  K --> PH --> G
  K --> DI
  DI --> SS --> G
  DI --> MT
  DI --> AJ --> G
  MT --> X1
  MT --> X2
  MT --> X3
  X1 -. "rerouted" .-> MW
  X3 -. "rerouted" .-> MW
  X2 -. "rerouted" .-> G
  X4 -. "rerouted" .-> MW
  MW --> G
  classDef bypass fill:#fdd,stroke:#c00,color:#000
  class X1,X2,X3,X4 bypass
```

*What the picture shows that prose hid:* **call sites that never reach `ExecuteSystem` today** (§10 F3, Roslyn: 34 in 14 files — the replay browser alone runs 12 systems itself) (red; file:line in
Appendix B). A gate placed only in the scheduler would not see them — `BulletReverseSyncSystem` (drag while paused) and
`DeferredTakeoverSystem` (ownership, reachable only through `NetworkLifecycleSystemGroup`) among them.

## 3. ⭐⭐ The classes

```mermaid
classDiagram
  direction LR
  class TickPolicy {
    <<enumeration>>
    Forwarding
    WhenTimeAdvances
    EveryFrame
  }
  class TickModes {
    <<new>>
    +OfSystem(type) TickPolicy
    +OfModule(type) TickPolicy
  }
  class IForwardingSystem {
    <<interface>>
  }
  class TickModeAttribute {
    <<new>>
    +TickPolicy Policy
  }
  class IEcsModuleSystem {
    <<interface>>
    +Execute(view, dt)
  }
  class IEcsModule {
    <<interface>>
    +Tick(view, dt)
  }
  class SystemScheduler {
    <<existing>>
    +RegisterSystem(system)
    +RegisterManualSystem(system)
    +ExecutePhase(phase, view, dt)
    ~ExecuteSystem(system, view, dt, advancing)
    -_policies Dictionary
  }
  class ProfiledManualSystemWrapper {
    <<existing>>
    +Execute(view, dt)
  }
  class ModuleHostKernel {
    <<existing>>
    -_advancingFrame bool
    -_lastStepFrame uint
    +UpdateInternal()
  }
  class EventAccumulator {
    <<existing>>
    +CaptureFrame(bus, frame)
    +FlushToReplica(bus, lastSeen)
    +ReadSince(frame) span
    +Release(frame)
    +Overflow long
  }
  class StreamHistory~T~ {
    <<new>>
    -slots T[] per frame slot
    -window T[] reused
    +Capture(slot, readBuffer)
    +ReadSince(frame) span
    +InjectInto(replicaStream)
  }
  class EntityRepository {
    <<existing>>
    +ReadEvents() span
    +ReadManagedEvents() list
    ~OpenStepWindow(fromFrame)
    ~CloseStepWindow()
  }
  class ISimulationView {
    <<interface>>
    +ReadEvents() span
    +ReadManagedEvents() list
  }
  TickModeAttribute --> TickPolicy
  TickModes ..> TickModeAttribute : reads, throws when absent
  SystemScheduler ..> TickModes : at registration and first run
  IEcsModuleSystem ..> TickModeAttribute : exactly one
  IEcsModule ..> TickModeAttribute : exactly one
  ModuleHostKernel --> SystemScheduler : ExecuteSystem with advancing
  ModuleHostKernel --> EventAccumulator : CaptureFrame every frame
  SystemScheduler --> EntityRepository : opens the window around a WhenTimeAdvances system
  ProfiledManualSystemWrapper --> SystemScheduler : ExecuteSystem
  EntityRepository ..|> ISimulationView
  EntityRepository --> EventAccumulator : ReadSince while the window is open
  EventAccumulator "1" *-- "many" StreamHistory~T~ : one per event stream, slots in a fixed ring
```

*What the picture shows that prose hid:* **only two types are new** (`TickPolicy`, `TickModeAttribute`). The accumulator, the
view's event reads, the manual wrapper and the gate's home all exist; each gains one member. ⭐ The window is a field on the
**live** `EntityRepository` instance — a background module's snapshot is a different instance and keeps reading its own,
already-flushed bus.

## 4. ⭐⭐ A pause, then a step

```mermaid
sequenceDiagram
  participant K as ModuleHostKernel
  participant B as FdpEventBus
  participant A as EventAccumulator
  participant S as SystemScheduler
  participant E as EveryFrame system
  participant W as WhenTimeAdvances system
  participant R as EntityRepository live
  Note over K: frame N+1 is paused (dt = 0)
  K->>B: SwapBuffers()
  K->>A: CaptureFrame(bus, N+1)
  K->>S: ExecuteSystem(E, advancing = false)
  S->>E: Execute(view, 0)
  E->>R: ReadEvents
  R-->>E: frame N+1 only
  K->>S: ExecuteSystem(W, advancing = false)
  S--xW: not called
  Note over K: frame N+k is a step (dt > 0)
  K->>B: SwapBuffers()
  K->>A: CaptureFrame(bus, N+k)
  K->>S: ExecuteSystem(W, advancing = true)
  S->>R: OpenStepWindow(from N)
  S->>W: Execute(view, dt)
  W->>R: ReadEvents
  R->>A: ReadSince(N)
  A-->>W: frames N+1 to N+k
  S->>R: CloseStepWindow()
  K->>A: Release(N+k) once every reader is past it
  Note over K: lastStepFrame = N+k
```

*What the picture shows that prose hid:* the step-only system's read covers **N+1 … N+k inclusive** — events published late
in step N (readable on N+1, a paused frame) are delivered once, at N+k. Paced and HTTP stepping therefore deliver the same
events to the same step.

## 5. ⭐ Claim table — what the rules rest on

| claim | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| pause is a zero delta; every system is still called | ✅ `ModuleHostKernel.cs:517-805` (phases on `_liveWorld` with `deltaTime`) | ✅ `Blueprint_Issues_Tracker.md:3633` *"That is the intent — a paused world should not animate"* (a tracker row, not a design) · ⛔ searched `docs/`+`.dev/`: no design says simulation reacts while paused |
| every scheduled call passes `ExecuteSystem` | ✅ `SystemScheduler.cs:134-197`; kernel module systems `ModuleHostKernel.cs:735-738` | ⛔ searched `docs/`+`.dev/` (`TickWhenPaused`, `RunsWhilePaused`, `PausePolicy`, `TickMode`): no record of a pause gate |
| 7 call sites bypass it | ✅ Appendix B (module reader, 40/40 modules) | — |
| main-thread systems share the live world as their view | ✅ `ModuleHostKernel.cs:700-703` (`view = _liveWorld`) | — |
| the view's event read is the designed per-reader window | ✅ `EntityRepository.View.cs:96-103` (today it forwards to `Bus.Read`) | ✅ `ISimulationView.cs:53-57` *"Events include history since module's last run"* |
| 43 production files read `repo.Bus.Read` directly; 70 use the view | ✅ grep, `2026-10-21` | — |
| the accumulator captures every frame, keeps 60 | ✅ `ModuleHostKernel.cs:567`, `EventAccumulator.cs:16,34` | ✅ its class doc: *"Enables slow modules to see accumulated events since last run"* |
| 82 / 116 / 4 split | ✅ Appendix A — Roslyn `find_implementations(IEcsModuleSystem)` = 251, 202 production, every one read | ✅ `docs/designs/mgmt-1/DESIGN.md:431, :1941` (editing while paused, Always Recording) |

## 6. Rejected — one line each

- **A default (either way)** — 82 vs 116 (Appendix A): whichever default, a new system silently gets the wrong one; the user ruled no default.
- **Decide by phase** — `RecentSensesSystem` is Input, `BallisticsSystem` PostSimulation, `BehaviorIngressSystem` Input: both kinds live in every phase.
- **Decide by event type** — every type the brain reads also has paused-frame readers (`SensorChangedEvent`: `RecentSensesSystem`, `CivilianFearSystem`, `SquadSensing`).
- **A mark on the module only** — mixed packs (the editor's input group holds ingress and edit systems) — a module mark would be a default for its members.
- **Systems register the event types they want** — the system would know about pausing; the user ruled it must not.
- **A different bus object per kind of system** — 158 production files cast the view back to `EntityRepository`; a wrapper view fails the cast and those systems silently do nothing.
- **A switch inside `FdpEventBus.Read`** — leaves two event-reading APIs and cements the 43 bypassing sites.
- **Keep paused-frame events alive for everyone** — an `EveryFrame` reader would see the same event on every paused frame.

## 7. ⭐⭐ Build order — each slice green before the next

| slice | what | behaviour change |
|---|---|---|
| **S1** | `TickPolicy` + `TickModeAttribute` (not inherited); marks on all 202 production systems (Appendix A), the 48 test/example systems and every module (Appendix B); the three checks of rule 1; the F2 producer→consumer rail with its allow-list | none — not yet read |
| **S2** | reroute the 34 direct calls (§10 F3) through `ExecuteSystem`; its public form for hosts outside the kernel; the Stride bracket reads the kernel's advancing flag | none |
| **S3** | the 96 files' world-bus reads (§10 F4, Roslyn list) → the view; the blueprint generator emits a view read; rail: no world-bus read outside the framework | none |
| **S4** | the allocation-free accumulator (§1a); retention by last reader, empty frames free, paused frames merged, readers expire, cap + `Overflow`; per-system windows (rule 5); `SnapAndPause` restart | none yet (no window opened); background modules lose the per-frame garbage |
| **S5** | ⭐ turn the gate on (rule 2) and open the windows (rule 5), behind an **`FdpConfig` switch, on by default**, so a red can be bisected (§10 F12); remove the 8 hand gates; count the test fallout first (8 test files drive the kernel with `dt = 0`) | ⭐ step-only systems skip paused frames and read every event since their last run |

**Acceptance (S5), each red before and green after:**
① a `SensorChangedEvent` published at a step and followed by a paused frame reaches `BrainTickSystem` at the next step ·
② an entity deleted while paused gets its `DestructionOrder` cleanup in `WeaponDispatcherSystem`, `AnimationDispatcherSystem`
and `PhysicsBodyLifecycleSystem` at the next step (no body leak) ·
③ a background module held over 120 paused frames still sees every event (today: the 60-frame trim) ·
④ *(narrowed by §10 F1)* the three-reload probe of §17 at 15 Hz HTTP-stepped: identical including `RecentSenses.NearMiss` / `UnderFire.LastTime`, and
paced vs HTTP stepping agree ·
⑤ **zero allocation** (S4, red today): after a warm-up, `GC.GetAllocatedBytesForCurrentThread()` across 1 000 frames of capture +
`ReadSince` over a 30-frame window + a flush to a replica, native and managed-struct events in every frame, is **0** — the
precedent is `BlueprintAssetTickTests.cs:116`.

## 7a. ⭐ AS-BUILT — S1 (`2026-10-21`)

| | as built |
|---|---|
| the type | `TickPolicy { Forwarding = 0, WhenTimeAdvances, EveryFrame }` + `[TickMode]` (not inherited; declaring `Forwarding` throws) + `TickModes.OfSystem/OfModule` (`SystemAttributes.cs`) |
| ⚠ deviation: `Forwarding` | a group (`ISystemGroup`) and the manual-system wrapper (new marker `IForwardingSystem`) answer `Forwarding` — they carry no mark; their members do. ⇒ the "4 GROUP" rows of Appendix A, made a type rule instead of a list |
| the checks | `SystemScheduler.RegisterSystem` and the first `ExecuteSystem` of a type (`_tickPolicies`); the kernel's three module-entry sites (`RegisterModule`, `InstallModuleAsync`, the batch install) → `ModuleEntry.TickPolicy` |
| the marks | 198 production systems + 40 modules exactly as Appendix A/B (the §8 leans as listed there) · test and example systems `EveryFrame` (today's behaviour) · test modules by their policy: background ⇒ `WhenTimeAdvances`, else `EveryFrame` · done by four parallel agents on disjoint files, then swept: every Roslyn-listed implementation resolved to its declaration, **0 unmarked** |
| the rails | `TickModeGuardTests` (registration, first run of a group member, not inherited, the manual wrapper, the module, `Forwarding` refused) · `EverySystemDeclaresItsTickModeTests` in `Hrot.ClusterRunner.Tests` **and** `HrotStrideApp.Game.Tests` over the shared `FDP/TestShared/TickModeProbe.cs` (every concrete system and module in the production assemblies beside the test) |
| the F2 chain gate | ⭐ `scripts/tickmode-chains.py --check` — modes read from the `[TickMode]` marks in source, per **enclosing class** (a file-level tag made `EditorSubsystem`'s orchestration-bus read look like a step-only reader); **36 chains, all on the reviewed `ALLOW` list** with a reason each; an unreviewed or stale one fails |
| ⛔ not yet | nothing reads the policy to gate — S5 |

## 7b. ⭐ AS-BUILT — S2 (`2026-10-21`)

| | as built |
|---|---|
| ⚠ deviation: `RegisterManualSystem` | it registered into the system's OWN phase (so a manually ticked system would also run there); it now registers into `Manual` as its doc always said, keeps an existing registration, and its wrapper calls `ExecuteSystem` (no second profiler). It had no production caller before S2 |
| `SystemScheduler.Run(system, view, dt)` | the gate for a host with no kernel phase: first sight reads the `TickPolicy`; from S5 it takes the host's advancing flag |
| concurrency | `_profileData` and `_tickPolicies` are `ConcurrentDictionary` — a background module's systems pass `ExecuteSystem` on its worker thread |
| modules (wrappers from `RegisterSystems`; the raw systems until a kernel registers the module, so a test driving `Tick` directly still works) | `PhysicsQueryModule` (raycast, hit resolution) · `NavigationSolverModule` (⚠ **one solver for the module's life** — it built a new one every tick, which would be "first seen" on the worker thread each time; the solver resolves its graph per run, so nothing goes stale) · `EqsModule` (grid, solver) · `EditorSystemsModule` (perception setup, zone authoring) · `GizmoInteractionModule` (ingress, context ingress, every interaction system incl. the post-simulation gizmo group, egress) |
| `NetworkLifecycleSystemGroup` | `RouteThrough(registry)` from `NedReplicationModule.RegisterSystems`; `ExecuteGroup` runs the wrappers (the originals kept, so a second registration does not wrap wrappers) |
| hosts outside the kernel | `ReplayBrowserSubsystem` — its 12 systems through a private `SystemScheduler.Run` · `StridePhysicsBracket` — lifecycle, vehicle navigation, reverse sync through `Run` (its `simRunning` hand gate stays until S5) |
| ⚠ mark corrected | `PhysicsBodyLifecycleSystem` → **`EveryFrame`**: its host runs it ALWAYS so bodies follow a drag while paused (`StridePhysicsBracket`, CE-219) — the S1 reader had it step-only. Appendix A updated (STEP 115 · EVERY 83) |
| left as they are, on purpose | `CgfThreatEvaluationSystem` calls its inner `ThreatEvaluationSystem` — both step-only, the outer call is gated · `IgApplication.TestHook_DirectCreationToolClick` — a test hook · the toggle groups' own `Execute` — reachable only by a direct call, and none remains in production |

## 7c. ⭐ AS-BUILT — S3 (`2026-10-21`)

| | as built |
|---|---|
| the reads | `EntityRepository` gained the public `ReadEvents<T>` · `ReadManagedEvents<T>` · `HasManagedEvents<T>` · `ReadRawEventsByTypeId` · `HasEventsByTypeId` · `HasEvents(Type)`; the explicit `ISimulationView` reads forward to them. Today they answer the current frame (= the bus); **S4 makes them the window** — that is the one place it changes |
| ⚠ deviation: *"→ the view"* | code that holds an `EntityRepository` (most of it — `view is EntityRepository repo`) calls the repository's reads, not `ISimulationView`: same method, no cast back, and the helpers that never see a view (`Custody`, `Charges`, `DoorCommands`, `TrafficPanic`) can use it too |
| migrated | **45 files** of the 96: every `<repo>.Bus.Read*` / `ReadManaged*` / `HasManagedEvent` *(4 Sonnet agents over the Roslyn list, reviewed)* + `BlueprintEventDispatch` (takes the repository) + **the blueprint emitter** (`InstanceEmitter`: `world.HasEventsByTypeId` / `world.ReadRawEventsByTypeId`; one emit golden, 2 lines) |
| ⭐ found by the sweep: `DataBreakpointSystem` | **step-only, and it handed the raw world bus to its event scanners** — a breakpoint on an event published while paused would never fire. ⇒ `EventScannerDelegate` takes the repository (not a bus); the replay search feeds playback into its repository's own bus; the system's bus argument is now only the event-path switch |
| left on a bus, on purpose | the orchestration, time-control, interaction and replay buses *(separate instances; never windowed)* · world buses injected into **every-frame** code — `BusEntityCreationRequestSource` (drained by `CreateEntityRequestSystem`, EveryFrame), the three map egress translators (net), `ScenarioMissionService` / `MenuShortcuts` / `MapViewModeLayer` (editor UI), the gizmo systems' `_interactionBus ?? world.Bus` fallback (EveryFrame) — an every-frame reader's window IS the frame. ⚠ A step-only reader of any of them would be a defect; the sweep found none (step-only files holding an `FdpEventBus` and reading it: only `EditorSubsystem`'s orchestration bus) |
| the rail | `NoWorldBusReadOutsideTheFrameworkTests` *(ClusterRunner.Tests)* — no `X.Bus.Read*` / `HasEvent` / `HasManagedEvent` / `ReadRawByTypeId` in production source outside `Fdp.Core` and `Fdp.ModuleHost`; covers the emitter's string too. ⚠ It cannot see a bus held in a field — that is what the sweep above was for |

## 7d. ⭐ AS-BUILT — S4 (`2026-10-21`)

| | as built |
|---|---|
| the bus | every stream also in an append-only array (`FdpEventBus.NativeStreamAt`/`ManagedStreamAt`), created under a lock, walked by index — `SwapBuffers`, `ClearAll`, `ClearCurrentBuffers` and the `Populate*` helpers no longer touch `ConcurrentDictionary.Values`. `SnapshotCurrentBuffers` / `InjectEvents` / `FrameEventData` are gone (the old capture path, replaced) |
| `EventStreamHistory` | ⚠ deviation from §3's `StreamHistory<T>`: a **native** stream keeps BYTES (`NativeStreamHistory`) — one class serves typed and replayed (untyped) streams, and a window read is `MemoryMarshal.Cast`; a **managed** stream keeps a typed `List<T>` per slot (`ManagedStreamHistory<T>`, made by the stream itself — `IManagedEventStream.CreateHistory`) |
| `EventAccumulator` | rewritten: a ring of slots over PHYSICAL slot ids reused from a free stack (⚠ found by rail ⑤ — a strict ring walks all 256 positions and grows each once) · an empty frame takes no slot · a run of frames shares the tail slot while no active reader's position lies inside it (a pause) and it holds < `maxRetainedEvents / 8` events · a slot is freed once every active reader has had it · the cap (256 slots, 65 536 events) drops the oldest: what an active reader still needed counts in `Overflow` and that reader EXPIRES (logged once) until it reads again · keys that go back restart the timeline · `Restart()` for `SnapAndPause` |
| readers | `RegisterReader` / `RegisterReaderAtCurrentFrame` / `MarkRead` / `UnregisterReader`; the window: `WindowIsCurrentFrame`, `ReadWindow<T>`, `ReadManagedWindow<T>`, `ReadRawWindow`, `HasInWindow` (one reused window buffer per stream, shared by readers of the same window); the background flush: `FlushUnreadTo(bus, reader)` — ⚠ NOT an overload of `FlushToReplica(bus, uint)`: an integer literal bound to it and a legacy rail went red. The stateless `FlushToReplica(bus, uint)` stays for callers that hold no reader |
| providers | GDB, SoD and Shared providers are readers (`EventReaderHandle`: registered on first flush) — they no longer track `GlobalVersion` themselves. ⚠ A provider the topology REPLACES (convoy promoted or widened) is not disposed, so it now releases its reader (`IEventHistoryReader`) — otherwise it would pin the history until the ring overflowed |
| the repository | `EntityRepository.AttachEventHistory` (the kernel, once) · `OpenStepWindow(reader)` / `CloseStepWindow(previous)` — a window that is only the current frame opens nothing · the six reads route to the window while it is open. Nothing opens one yet (S5) |
| rule 7 | `ITimeController.TimelineEpoch` (default 0); `MasterSyncController.SnapAndPause` bumps it. The kernel restarts the history on a changed epoch OR on sim time going back (the backstop — a slave sees only the master's snapped time), and does so at the TOP of the frame, before the capture, so a load's own events are kept |
| ⑤ zero allocation | ✅ `EventHistoryTests.Capture_WindowRead_AndFlush_AllocateNothing_InSteadyState` — 1 000 frames, 3 native + 2 managed-struct events each, a 30-frame window, a flush every 10: **0 bytes**. 🔴 Red before: the same measurement on the S3 accumulator **1 289 600 bytes**. ⚠ Found on the way: `ManagedEventStream.Write`'s `evt is null` boxed every managed STRUCT in unoptimised code (32 bytes a publish) |

## 7e. ⭐ AS-BUILT — S5 (`2026-10-21`) — **the gate is ON**

| | as built |
|---|---|
| the switch | `FdpConfig.TickModeGate` (default **true**). ⚠ Off is not byte-for-byte the old behaviour: the eight hand gates are gone |
| the gate | `SystemScheduler.ExecuteSystem(system, view, dt, advancing, moduleGoverns)`: a `WhenTimeAdvances` system is not called when `advancing` is false (`dt > 0` for every kernel phase and every manual wrapper; the host's flag through `Run(…, advancing)`). On the LIVE world it runs inside its step window: a reader registered on first sight at the current frame, `OpenStepWindow` / `CloseStepWindow` around the call, `MarkRead` after it (also on a throw). Groups pass the flags to their members. `Stopwatch.StartNew()` per system per frame replaced by `GetTimestamp` (no object) |
| modules (rule 2, §10 F5/F11) | a `WhenTimeAdvances` module on a SNAPSHOT is not dispatched on a frame that does not advance, in every mode (time scale 0 included) — ⇒ its next flush carries the pause; its systems then run under the module's mode (`moduleGoverns`). A DIRECT (live) module keeps running its simulation systems, each under its own mode; only its `Tick` follows the module's. A held frame is not counted as a frame since its last run |
| hosts | the Stride bracket passes `simRunning` as "advances" for all three of its runs; its hand gate on vehicle navigation is gone. The replay browser's runs gate on the delta its Update is handed (its step-only effect systems stand down in replay anyway — rule 10) |
| the 8 hand gates (rule 8) | gone from `BallisticsSystem`, `BehaviorFrameSystem`, `BlueprintTickSystem`, `BrainTickSystem`, `DroneImpactSystem`, `GuidedRoundSystem`, `NavigationExecutionSystem`, `ResumeAndDrainSystem` |
| the splits (§1b, §8 approved) | `GroundClampWhilePausedSystem` (EVERY): a drag lands on the surface — the movement model's bodies and clamping flag, shared (`CarKinematicsSystem.MovedBodies`/`IsClamped`) · `AirRouteCompileSystem` (EVERY): a route edited while paused is compiled at once — the motion system's own `EnsurePath` (`AirMotionSystem.CompilePaths`), one implementation. Both act only on a frame that does not advance (on a step the half they split from does it). ⚠ deviation: **`UnitCentroidSystem` is re-marked EVERY instead of split** — its whole work is the paused duty, it reads no event and keeps no state, so a paused frame computes exactly what the next step would; a split would have an empty step half |
| the test fallout | 6 of ~12 000 in the suites run *(ModuleHost, Core, ClusterRunner, Common, SimHost, IG, Editor, ReplayBrowser, Toolkits, Breakpoints, Blueprints, Presentation, NodeComposition)*: 4 called a step-only system DIRECTLY with `dt = 0` and relied on its hand gate — now through `SystemScheduler.Run` (`ResumeAndDrainSystemTests` ×2, `TheBehaviorFramePulseTests`, and `BlueprintTestFixture.TickFrame` for `AssetTickCounterTests`) · `Continuous_TheCadenceStaysFrameCounted` asserted a step-only background module RUNS at time scale 0 — the approved F11 says it must not: split into an advancing-cadence test and `Continuous_AtTimeScaleZero_AStepOnlyBackgroundModuleIsHeld` · `GroundKinematicsModule_RegistersAllKinematicSystems` counts the new split |
| ⚠ found by ④ (live): readers are registered UP FRONT | 📐 the four-load probe (below) had the first run after boot differ from every reload from the first cut: a step-only system's reader was registered lazily at its first step, so the first run missed what the load published while paused and every reload (reader already there) got it. ⇒ `EventAccumulator.RegisterReaderAtTimelineStart` (owed everything since the last `Restart`) and `SystemScheduler.RegisterStepReaders`, called by the kernel for every new or swapped topology (each phase, group members, direct modules' simulation systems). Rail `TheFirstRun_ReadsTheLoadsEvents_LikeEveryLaterRun` — red with lazy registration (`[]` vs `[10, 11, 12]`) |
| acceptance | ① ② `PausedAndSteppedExecutionTests.AStepOnlySystem_ReadsEverythingSinceItsLastRun_AtTheNextStep_Once` (1, 7, 120 paused frames: an event published at the step and one during the pause reach the step-only system once, in order, at the next step; it is never called paused; the every-frame system reads each once) — ⚠ generic, not through `BrainTickSystem`/the dispatchers: the mechanism is the same for every step-only reader · `AStepOnlySystem_SeesTheSameStepsWhateverThePausedFrames` (0, 1, 5 paused frames between steps) · `ASnap_ForgetsTheEventsBeforeIt` (rule 7) · ③ `ABackgroundModule_HeldOverALongPause_GetsEveryEvent` (120 paused frames, an event each, `Overflow` 0) · ④ see below · ⑤ S4 |

## 8. ✅ Leans — APPROVED by the user `2026-10-21` (*"Approved."*) — the marks in Appendix A carry them

| question | lean |
|---|---|
| `UnitHierarchySystem`, `TacticalIntentResolutionSystem` are fed by the operator while paused AND by the simulation on steps | **`EveryFrame`** — each event applied once before the next step (§1 determinism note); the ORBAT shows the change at once |
| `AnimationRuntimeBridgeSystem` — must a character placed while paused register and show its pose before the first step? | **`EveryFrame`** |
| `PossessionSystem` — must a possess/release clicked while paused take effect before Resume? | **`WhenTimeAdvances`** — the window delivers the click at the next step |
| the three half-and-half systems of §1b — split, or leave their paused duty undone until the next step? | **split** — each paused duty becomes its own small `EveryFrame` system; the sim work stays `WhenTimeAdvances`. ⛔ Not a third mode: a mode that means "partly" brings back the per-system `dt` checks |
| `HierarchyOrderSystem` has no production registration | **`EveryFrame`** (map ordering, UI) — marked so the rule holds when it is registered |
| frame-counted service timeouts in `EveryFrame` systems (`NetworkGatewaySystem`, `DeferredConstructionParticipant`, `LifecycleSystem`, `MissionControlExecutionSystem`) and `GhostPromotionSystem`'s 2 ms budget | **keep** — service plumbing, not simulation; they already must move while paused |

## 10. ✅ CRITICAL REVIEW `2026-10-21` — **gaps and flaws, each measured; ALL RESOLVED (the record — §1 and §7 carry the result)**

📐 The measurements behind this section are grep and code reads made the same day. Each row gives its evidence.

| # | finding | evidence | fix (lean) |
|---|---|---|---|
| **F1** | ✅ **RESOLVED by R-3038 (user, `2026-10-21`).** Paced and HTTP stepping are not equivalent: an `EveryFrame` system reacts on the frame after a step, so whether a paused frame comes before the next step changes the run. ⇒ **every run that must repeat steps EXACTLY ONCE PER RENDERED FRAME** — never zero, never two; real time is kept by holding the frame, not by skipping or doubling steps. The scout, the live show and the capture all follow it. | `VehicleCommandSystem` (STEP) → `CmdAssignSubordinate` → `UnitHierarchySystem` (EVERY) | ⛔ rejected: the kernel forbidding steps on consecutive frames — it halves the top step rate (a 60 Hz live show would need 120 fps) |
| **F2** | ✅ **RESOLVED — chain pass done** (`chains.py`: every event type, its publishers and readers, each tagged with its mode). 📐 **38** types have a STEP reader and a non-STEP publisher: network ingress (sim events from other nodes — the window delivers them at the next step, correct) · editor code that publishes **only while running** and applies directly while paused (`EntityAiDetailsView.cs:170-188`, `DebugApiService.Reuse.cs:86`) · sim helpers called from STEP systems · EVERY systems whose events a STEP system should take at the next step (`MissionControlExecution`/`TacticalIntentResolution` → `ClearBehaviorEvent`; `UnitHierarchy` → `CmdAssignSubordinateRejected`; `ClusterCapabilityIngest` → `NodeDeparted`; `EmbarkedState` → `DamageAssessedEvent`). ⛔ **Two broke:** `InitialBrainMaterializationSystem` (load) and the unit type's default SOP at creation (`BehaviorTkbTranslator.cs:114`) publish while paused into `BehaviorIngressSystem` and `RoeSystem` ⇒ a unit loaded or placed while paused would have no AI until the first step | `InitialBrainMaterializationSystem.cs:36-43` | ✅ `BehaviorIngressSystem`, `RoeSystem` → **EVERY** (Appendix A updated). `ReportingRequirementsSystem` and `BlueprintEventIngressSystem` stay STEP (their publishers are sim code, or the editor only while running). ⭐ The chain pass becomes a rail: an EVERY/editor publisher with a STEP reader must be on a reviewed allow-list |
| **F3** | ✅ **MEASURED by Roslyn** (`find_references(IEcsModuleSystem.Execute)`: 1 801 refs, 52 production, of which 34 are code calls in 14 files). Besides the scheduler itself: the framework's own groups (`NetworkLifecycleSystemGroup.cs:115`, `TogglableInputGroup.cs:71`, `TogglableSimulationGroup.cs:70`, `TogglablePostSimulationGroup.cs:83`, the manual wrapper `SystemScheduler.cs:451`) · modules (`NavigationSolverModule.cs:130`, `PhysicsQueryModule.cs:42-43`, `GizmoInteractionModule.cs:70,79,84,87`, `EditorSystemsModule.cs:53-54`, `EqsModule.cs:124,127`) · a system calling a system (`CgfThreatEvaluationSystem.cs:27`) · ⭐ **hosts outside the kernel**: `ReplayBrowserSubsystem.cs:439-469` (**12** systems ticked by the replay browser itself), `IgApplication.cs:1948`, `StridePhysicsBracket.cs:199, 205, 219` (its own `simRunning` gate). ⚠ `StrideViewBracket.cs:118` is NOT one — `StrideAnimationBridge` is not a system | Roslyn, root solution | Every call goes through one runner: `ExecuteSystem` gains a public form taking the frame's advancing flag; the replay browser passes its replay clock's, the Stride bracket asks the kernel instead of keeping `simRunning` |
| **F4** | ✅ **MEASURED by Roslyn**: **96 production files** read `FdpEventBus` directly — `Read` 100 sites / 54 files, `ReadManaged` 122 / 53, `HasManagedEvent` 1 (`IdAllocationMonitorSystem.cs:59`), `ReadRawByTypeId` 2 (`BlueprintEventDispatch.cs:47` and a generated `.g.cs` — **the blueprint generator emits it**), `HasEvent` 0. 43 of the 96 declare a system; the rest are helpers, translators and the orchestration/time controllers on their OWN buses | Roslyn `find_references` on each `FdpEventBus` read member | S3 works from this list: world-bus reads in or under a system → the view; the generator emits a view read; reads of the orchestration/interaction buses stay (never windowed — no step-only reader). The rail bans world-bus reads outside the framework |
| **F5** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **Rule 5 covers only the live world.** A module with a snapshot view (`FastReplica`, a GDB or SoD strategy) acquires a fresh view every frame it is dispatched. A step-only system inside it would get only the step frame's events: the paused frames were flushed into views it never read. | `ExecutionPolicy.cs:66-74`; production has none today (all 40 production modules are `Synchronous` or `SlowBackground`), tests and examples use it | **Rule:** a module with a snapshot view has ONE mode for all its systems, which is the module's. A `WhenTimeAdvances` snapshot module is not dispatched on a paused frame, so its next flush covers the pause |
| **F6** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **"Since the previous step" is the wrong unit; "since this system's last run" is the right one.** `ISimulationView.ReadEvents`' own contract is *"since module's last run"*. A main-thread module running below 60 Hz (`FrameSynced(targetHz)`) misses the events of the frames it skips, in LIVE play, today. | `ISimulationView.cs:53-57`; `ExecutionPolicy.FrameSynced(targetHz)` exists (1 use in the tree) | **Lean:** the window is **since this system's last run**. At 60 Hz it equals "since the last step"; it also fixes reduced-rate modules in live play. The zero-copy fast path still holds when the last run was the previous frame |
| **F7** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **Rule 7's trigger is inferred.** "The clock went back" misses a load at the same sim time (both 0), and an operator click from before the load would then reach the new world's first step. | Time Architecture §12a: *"`SnapAndPause` is the ONE time discontinuity"* | Restart the window and the history on **`SnapAndPause`**, the explicit discontinuity, not on a comparison of clocks |
| **F8** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **"Kept until its last reader" never ends for a reader that stops reading**: a module tripped by the circuit breaker, disabled or uninstalled ⇒ history runs to the cap and overflows every frame. | `ExecutionPolicy` `FailureThreshold` / `CircuitResetTimeoutMs` | A reader **expires** after it has not read for the cap's length (counted, logged); a module that is removed unregisters |
| **F9** | ✅ **MEASURED** (`--mode editor`, `ua-tank-ammo`, preview paused for 60 s after 240 steps, `GET /events`): **at most 54 frames of 3 600 carried any event — 287 events, 258 of them `EqsResultEvent`** — an upper bound, since the first read also returns the last steps' events. An idle edit world: 35 events in 30 s (the load). ⇒ the world bus is all but silent while paused | `paused_traffic.py` | An empty frame takes no slot; consecutive paused frames merge. A 10-minute pause costs a few hundred events, not 36 000 slots |
| **F10** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **"Registration throws" does not reach every system.** Group members never pass `RegisterSystem` (`ExecuteGroup` adds them lazily, `SystemScheduler.cs:188-196`), nor do directly executed systems (F3). And `[TickMode]` on `DispatcherSystemBase<TChannel>` would be **inherited**, which is a default by the back door. | `SystemScheduler.cs:30-46, 188-196` | Also check on the first `ExecuteSystem` of a type (cached), plus the reflection rail over loaded assemblies. `AttributeUsage(Inherited = false)` |
| **F11** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **Background modules are held only in STEPPED mode** (CE-3292). A `dt = 0` frame in continuous play (time scale 0) still dispatches them. | `ModuleHostKernel.cs:600-603` | Rule 2 gates async dispatch on `dt > 0` in every mode |
| **F12** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **S5 is a big bang**: 118 systems change behaviour at once, 8 test files drive the kernel with `dt = 0`, and there is no way back short of a revert. | grep: 8 test files call `Update(0)` | An `FdpConfig` switch for the gate (on by default) so a red can be bisected, and S5's test fallout counted before it lands |
| **F13** | ✅ **DONE with F2**: the chain pass found the two contradictions; the rest of Appendix A holds along its chains | F2 | the F2 allow-list rail keeps it true |
| **F14** | ✅ **APPROVED `2026-10-21` (user: *"Approved"*) — folded into §1 / §7.** ⚠ **Replay is not expressed.** `TickMode` says nothing about what runs while a recording plays; `mgmt-1` §8.10 owns that and gates systems in place. Is a replayed frame "advancing"? | `docs/designs/mgmt-1/DESIGN.md` §8.10 (current-answer) | **Lean:** orthogonal. The §8.10 gates stay; a replayed frame is advancing (its `dt` > 0). Write the interplay into both documents |

## 9. Related, not built here — **one way to mark an event** (R-3037)

🔒 **User, `2026-10-21`:** *"we need to unify the event. It's not acceptable that some has the attributes and some doesn't. And the
event type must be found by reflection scan at the beginning, no other way."* 📐 Measured: 40 of 76 managed event types carry no
`[EventId]` (their id is a per-process string hash), and ids 9059 collide (`MergeLogsIntent`, `StagingRemapPublishedEvent`, both
managed so the clash is dormant). ⭐ Independent of this design: the step window works on whatever id the bus uses. Its own design
is pending.

## Appendix A — every production system *(Roslyn: 251 implementations, 202 production; all read `2026-10-21`)*

**Legend:** **STEP** = `WhenTimeAdvances` · **EVERY** = `EveryFrame` (tag: edit · net · ui · rec) · **GROUP** = forwards to
members, carries no mark of its own (members decide). ⚠ = reads events a paused frame can carry — rule 5 delivers them.
Paths abbreviated; each resolves under `FDP/` `Hrot/` or `Stride/`.

| system | phase | mode | why | evidence |
|---|---|---|---|---|
| ActiveSensorTracksUpdateSystem | Simulation | STEP ⚠ | brain track memory fed by DDS events | ActiveSensorTracksUpdateSystem.cs:35-50 |
| AirMotionSystem | PostSimulation | STEP | moves air bodies | AirMotionSystem.cs:32,48 |
| AirRouteCompileSystem *(S5 split)* | PostSimulation | EVERY (edit) | AirMotion's paused half: a route edited while paused is compiled at once | AirMotionSystem.cs |
| AnimationBackendCleanupSystem | PostSimulation | EVERY edit | a delete while paused must free the backend slot | AnimationBackendCleanupSystem.cs:43-56; StrideAnimationBackend.cs:178 |
| AnimationCapabilityChangeReactorSystem | Input | STEP | capability edges — same mode as CognitiveInterrupt/Cleanup | AnimationCapabilityChangeReactorSystem.cs:29,56 |
| AnimationDispatcherSystem | Simulation | STEP ⚠ | montage executors; DestructionOrder teardown | AnimationDispatcherSystem.cs:62-75 |
| AnimationRuntimeBridgeSystem | Simulation | EVERY edit (lean, §8) | a character placed while paused registers and shows its pose | AnimationRuntimeBridgeSystem.cs:53-61,79 |
| AnimationStateReporterSystem | PostSimulation | STEP | montage/stance completion | AnimationStateReporterSystem.cs:94-145 |
| AreaEffectCloudSystem | Simulation | STEP | cloud drift, irritation, expiry | AreaEffectCloudSystem.cs:39-69 |
| AreaEffectSystem | Simulation | STEP ⚠ | area damage from detonations | AreaEffectSystem.cs:49-76 |
| BallisticsSystem | PostSimulation | STEP | bullets (hand gate today) | BallisticsSystem.cs:48 |
| BehaviorFrameSystem | Simulation | STEP | brain pulse (hand gate today) | BehaviorFrameSystem.cs:31 |
| BehaviorGizmoManagerSystem | PostSimulation | EVERY ui | redraws behaviour gizmos each frame | BehaviorGizmoManagerSystem.cs:89-104 |
| BehaviorIngressSystem | Input | EVERY edit (§10 F2) | applies the AI a unit is loaded or created with (InitialBrainMaterialization, the default SOP) while paused | InitialBrainMaterializationSystem.cs:36-43; BehaviorTkbTranslator.cs:114 |
| BleedOutSystem | Simulation | STEP | health drain by dt | BleedOutSystem.cs:43-48 |
| BlueprintApplicationSystem | BeforeSync | EVERY edit | Add Entity in Preview injects template components | BlueprintApplicationSystem.cs:33-40 |
| BlueprintEventIngressSystem | Input | STEP ⚠ | paused attach/detach applies directly | EntityBlueprintsEditModel.cs:160-175 |
| BlueprintMaterializationSystem | Input | EVERY edit | slots for entities added/loaded while paused | BlueprintMaterializationSystem.cs:67 |
| BlueprintTickSystem | Simulation | STEP | blueprint instances (hand gate today) | BlueprintTickSystem.cs:51 |
| BrainHandOverSystem | Input | EVERY net | a paused ownership transfer runs the intent on the gainer | BrainHandOverSystem.cs:43-51 |
| BrainTickSystem | Simulation | STEP ⚠ | the brain (hand gate today) | BrainTickSystem.cs:129-135,157-229 |
| BulletReverseSyncSystem | PostSimulation | EVERY edit | drag/reposition while paused (bypass, §2) | StridePhysicsBracket.cs:177,196,219 |
| CanvasMenuUpdateSystem | PostSimulation | EVERY ui | the right-click menu; the cluster boots paused | CanvasMenuUpdateSystem.cs:37,52 |
| CarKinematicsSystem | PostSimulation | STEP | vehicle motion | CarKinematicsSystem.cs:107 |
| GroundClampWhilePausedSystem *(S5 split)* | PostSimulation | EVERY (edit) | CarKinematics' paused half: a drag lands on the surface | GroundClampWhilePausedSystem.cs |
| CarryStationSystem | Simulation | STEP ⚠ | RestationEvent from brain behaviours | CarryStations.cs:126-128 |
| CenterOnEntitySystem | PostSimulation | EVERY ui | camera "center on" while paused | CenterOnEntitySystem.cs:64-72 |
| CgfThreatEvaluationSystem | Simulation | STEP | threat decay/boost by dt | CgfThreatEvaluationSystem.cs:27 |
| ChannelArbitrationSystem | Simulation | STEP | stale channels before the brain tick | ChannelArbitrationSystem.cs:44-48 |
| ChargeSystem | Simulation | STEP ⚠ | charges detonated by behaviours | Charges.cs:126-128 |
| CivilianFearSystem | Simulation | STEP ⚠ | fear from sounds and hits | CivilianFearSystem.cs:59-70 |
| ClusterCapabilityIngestSystem | Input | EVERY net | membership, heartbeats, init barrier | ClusterCapabilityIngestSystem.cs:22-24,97-134 |
| CognitiveCleanupSystem | Simulation | STEP | clears interrupt bits — ⚠ today erases bits no brain tick saw | CognitiveCleanupSystem.cs:43-45 |
| CognitiveInterruptSystem | Input | STEP | capability edges (same mode as Cleanup) | CognitiveInterruptSystem.cs:105-116 |
| CombatTraceSystem | PostSimulation | STEP | shot logs grow only on steps | CombatTraceSystem.cs:24-25 |
| ContextActionIngressSystem | Input | EVERY ui | context-menu clicks | ContextActionIngressSystem.cs:72-85 |
| ContextMenuSystem | PostSimulation | EVERY ui | right-click menus | ContextMenuSystem.cs:53-121 |
| CoordinateTransformSystem | PostSimulation | STEP | Position → geodetic | CoordinateTransformSystem.cs:28-51 |
| CorridorPreviewSystem | Simulation | STEP | path look-ahead for the brain | CorridorPreviewSystem.cs:61-87 |
| CountermeasureSystem | PostSimulation | STEP | decoys on sim time | CountermeasureSystem.cs:37-48 |
| CreateEntityRequestSystem | Input | EVERY edit | Add Entity in Preview, ExCon/DDS creates | CreateEntityRequestSystem.cs:158-165 |
| CrowdAgentUpdateSystem | Simulation | STEP | Detour crowd by dt | CrowdAgentUpdateSystem.cs:81,120 |
| CrowdSystem | Simulation | STEP | crowd formation, contagion | CrowdSystem.cs:86-129 |
| CustodySystem | Simulation | STEP ⚠ | custody timers on sim time | Custody.cs:138-160 |
| CycloneEgressSystem | Export | EVERY net | paused edits reach other nodes | CycloneEgressSystem.cs:93-98 |
| CycloneIngressSystem | Input | EVERY net | polls translators (no production constructor found) | CycloneIngressSystem.cs:51-56 |
| CycloneNetworkCleanupSystem | Export | EVERY net | wire deletes for paused deletes | CycloneNetworkCleanupSystem.cs:105-120 |
| CycloneNetworkIngressSystem | Input | EVERY net | control plane while frozen | CycloneNetworkIngressSystem.cs:33-36,97-109 |
| DamageCalculationSystem | Simulation | STEP ⚠ | damage from detonations | DamageCalculationSystem.cs:44-55 |
| DamageSystem | Simulation | STEP | hit damage to Health | DamageSystem.cs:38-83 |
| DangerAreaSensorSystem | Simulation | STEP ⚠ | brain sensor | DangerAreaSensorSystem.cs:36-41 |
| DataBreakpointSystem | PostSimulation | STEP | a hit rewinds to the pre-tick snapshot | DataBreakpointManager.cs:557-563 |
| DataDrivenGizmoSystem | PostSimulation | EVERY ui | gizmos redrawn each frame | DataDrivenGizmoSystem.cs:268-272 |
| DeadReckoningSyncSystem | PostSimulation | STEP | replica blend scaled by dt | DeadReckoningSyncSystem.cs:113 |
| DebugPrimitivesBatchPublisherSystem | Export | EVERY ui | gizmo buffer to remote viewers | DebugPrimitivesBatchPublisherSystem.cs:34-46 |
| DebugPrimitivesBatchSubscriberSystem | Input | EVERY ui | remote gizmos into the local buffer | DebugPrimitivesBatchSubscriberSystem.cs:40-86 |
| DebugSnapshotProvider | BeforeSync | STEP | pre-tick image only for advancing ticks | DataBreakpointManager.cs:560,673 |
| DebugStatePatchSystem | Input | EVERY ui | gizmo pins, AI-trace toggles | GizmoPins.cs:53 |
| DeferredConstructionParticipant | BeforeSync | EVERY net | acks constructions made while paused | DeferredConstructionParticipant.cs:70,90 |
| DeferredTakeoverSystem | BeforeSync | EVERY net | ownership handover (bypass, §2) | DeferredTakeoverSystem.cs:76-125 |
| DeleteEntityRequestSystem | Input | EVERY edit | deletes while paused | DeleteEntityRequestSystem.cs:79-104 |
| DescentLandingSystem | PostSimulation | STEP | landing wait in sim time | DescentModels.cs:514-521 |
| DetonationMemorySystem | Simulation | STEP ⚠ | brain memory of bursts | RecentDetonations.cs:86-88 |
| DisposalMonitoringSystem | PostSimulation | EVERY net | NetworkEntityMap after paused deletes | DisposalMonitoringSystem.cs:44-45 |
| DoorCommandSystem | Simulation | STEP ⚠ | doors from sim/ingress only | OpenDoorExecutor.cs:89 |
| DoorPassageSystem | Simulation | STEP | door holds by dt | DoorPassageSystem.cs:68-80 |
| DroneImpactSystem | PostSimulation | STEP | flyer collisions (hand gate today) | DroneImpactSystem.cs:34 |
| EditorPerceptionSetupSystem | Simulation | EVERY edit | "seed target" while paused (bypass, §2) | EditorSubsystem.cs:2536 |
| EditorZoneAuthoringSystem | Simulation | EVERY edit | obstacle placement click (bypass, §2) | EditorZoneAdapter.cs:100 |
| EffectSpawnSystem | PostSimulation | STEP ⚠ | effects from fire/hit events | EffectSystems.cs:71,137 |
| EjectionSystem | Simulation | STEP ⚠ | ejection on request or low health | EjectionSystem.cs:43-52 |
| EmbarkationSystem | Simulation | EVERY edit | embark/disembark from menu and ORBAT | EmbarkOrders.cs:18,27 |
| EmbarkedStateSystem | Input | EVERY net | passenger side from the replicated buffer | EmbarkedStateSystem.cs:51-73 |
| EngagementAreaSystem | Input | EVERY edit | sector edits from the API | DebugApiService.Sector.cs:53 |
| EngineBackedPathResponseSystem | Input | STEP ⚠ | path answers for sim requests | NavigationIntentBridgeSystem.cs:509 |
| EntityAttributeSchemaPublisherSystem | BeforeSync | EVERY net | schema once at startup (boots paused) | EntityAttributeSchemaPublisherSystem.cs:40-45 |
| EntityRequestFinalizationSystem | PostSimulation | EVERY net | final ack for create/delete | EntityRequestFinalizationSystem.cs:137 |
| EqsResultUpdateSystem | Simulation | STEP ⚠ | EQS answers | EqsResultUpdateSystem.cs:57,97 |
| EqsSolverSystem | Simulation | STEP | driven by EqsModule.Tick (bypass, §2) | EqsModule.cs:102,127 |
| EventHistoryCaptureSystem | PostSimulation | EVERY rec | bus history incl. paused-time commands | EventHistoryCaptureSystem.cs:32 |
| EventToEffectSystem | PostSimulation | STEP ⚠ | legacy IG effects from sim events | EventToEffectSystem.cs:46,54 |
| FireProcessingSystem | Input | STEP ⚠ | fire intents | FireProcessingSystem.cs:54 |
| FormationTargetSystem | Simulation | STEP | formation slots | FormationTargetSystem.cs:36-55 |
| GenesisMaterializationSystem | Input | EVERY edit | scenario-load passengers, hierarchy, routes | GenesisMaterializationSystem.cs:49-54 |
| GeodeticSmoothingSystem | Input | STEP | lerp by dt | GeodeticSmoothingSystem.cs:46 |
| GhostCreationSystem | BeforeSync | STEP | Execute is empty | GhostCreationSystem.cs:48 |
| GhostPromotionSystem | BeforeSync | EVERY net | ghosts of entities created while paused | GhostPromotionSystem.cs:132-199 |
| GhostTimeoutSystem | — | STEP | unregistered; ⚠ stamps frames, ages steps | GhostTimeoutSystem.cs:26,39 |
| GlobalActionDispatchSystem | Input | EVERY ui | map-menu actions | GlobalActionDispatchSystem.cs:26-29 |
| GlobalGizmoManager | PostSimulation | EVERY ui | placement/picker tools | GlobalGizmoManager.cs:212-224 |
| GuidedRoundSystem | PostSimulation | STEP | guided rounds (hand gate today) | GuidedRoundSystem.cs:91 |
| HandsFuzeSystem | Simulation | STEP | fuze in hand by dt | RoundPickUpSystem.cs:97,106 |
| HealthApplicationSystem | Simulation | STEP ⚠ | damage events | HealthApplicationSystem.cs:60 |
| HierarchyOrderSystem | BeforeSync | EVERY ui (lean, §8) | unregistered map ordering | HierarchyOrderSystem.cs:39-44 |
| HistoryRecordingSystem | Simulation | STEP | trail sampled by dt | HistoryRecordingSystem.cs:59 |
| HitResolutionSystem | Input | STEP | sim raycast hits (bypass, §2) | HitResolutionSystem.cs:67 |
| IGCapabilitiesPublisherSystem | PostSimulation | EVERY net | capabilities once at startup | IGCapabilitiesPublisherSystem.cs:30-39 |
| IdAllocationMonitorSystem | — | EVERY net | refills id blocks (unregistered) | IdAllocationMonitorSystem.cs:59-92 |
| InitialBrainMaterializationSystem | Input | EVERY edit | loaded unit's AI → editor events | InitialBrainMaterializationSystem.cs:36-43 |
| InteractionDispatcherSystem | Simulation | STEP ⚠ | interaction executors | InteractionDispatcherSystem.cs:28-78 |
| LifecycleSystem | BeforeSync | EVERY edit | entity lifecycle for paused creates/deletes | LifecycleSystem.cs:52-76 |
| LifespanSystem | PostSimulation | STEP | expiry by sim time | LifespanSystem.cs:39-43 |
| LightweightRoundIdentitySystem | PostSimulation | STEP | ids for rounds (steps only) | LightweightRoundIdentitySystem.cs:51-58 |
| LinearKinematicsSystem | PostSimulation | STEP | projectiles by dt | LinearKinematicsSystem.cs:89 |
| LocalAuthorityYieldSystem | BeforeSync | EVERY net | yields grants on paused creates | NedReplicationModule.cs:725 |
| LocalGridBuilderSystem | Manual | STEP | perception grid | LocalGridBuilderSystem.cs:63 |
| LocomotionDispatcherSystem | Simulation | STEP ⚠ | locomotion executors | LocomotionDispatcherSystem.cs:16,30 |
| LookAtDispatcherSystem | Simulation | STEP ⚠ | look-at executors | LookAtDispatcherSystem.cs:19,42 |
| LookFollowSystem | PostSimulation | STEP | head yaw on sim time | LookFollowSystem.cs:21,32 |
| MapCullingSystem | PostSimulation | EVERY ui | pan/zoom while paused | MapCullingSystem.cs:55 |
| MapLayerAssignmentSystem | PostSimulation | EVERY ui | layers for entities placed while paused | MapLayerAssignmentSystem.cs:66 |
| MapSelfCheckSystem | PostSimulation | EVERY ui | checks the drawn frame | MapSelfCheckSystem.cs:92 |
| MenuOrders | — | EVERY ui | the clicked order | MenuOrders.cs:186 |
| MenuShortcuts | — | EVERY ui | key → menu action | MenuShortcuts.cs:46 |
| MissileWarningSensingSystem | Simulation | STEP | missile warning | MissileWarningSensingSystem.cs:17 |
| MissionAdapterSystem | Simulation | STEP | phase changes (state) | MissionAdapterSystem.cs:30 |
| MissionControlExecutionSystem | Input | EVERY edit | mission commits acked | MissionControlExecutionSystem.cs:162 |
| MissionDirectorSystem | Simulation | STEP ⚠ | mission phases by dt | MissionDirectorSystem.cs:62,153 |
| MontageQueueAdvanceSystem | Simulation | STEP | montage queue | MontageQueueAdvanceSystem.cs:25 |
| NavigationExecutionSystem | Simulation | STEP | arrival/stall (hand gate today) | NavigationExecutionSystem.cs:84 |
| NavigationIntentBridgeSystem | Simulation | STEP | intent → path request | NavigationIntentBridgeSystem.cs:42 |
| NavigationPathDetailsUpdateSystem | Input | STEP ⚠ | path details from sim | NavigationPathDetailsUpdateSystem.cs:45 |
| NearMissSensingSystem | Simulation | STEP | near miss | NearMissSensingSystem.cs:14 |
| NetworkGatewaySystem | BeforeSync | EVERY net | construction barrier | NetworkGatewaySystem.cs:30,247 |
| NetworkPollingSystem | Input | EVERY net | DDS into the cluster cache | NetworkPollingSystem.cs:8,20 |
| NetworkSpawningSystem | BeforeSync | EVERY edit | spawn/destroy commands | NetworkSpawningSystem.cs:99,105 |
| NetworkTranslatorEgressSystem | PostSimulation | EVERY ui | gizmo state to remote terminals | GizmoNetworkTransportModule.cs:55,106 |
| NetworkTranslatorIngressSystem | Input | EVERY ui | remote gizmo interactions | GizmoNetworkTransportModule.cs:55,96 |
| NotifyEventEmitterSystem | PostSimulation | STEP | animation notifies | NotifyEventEmitterSystem.cs:17 |
| OffMeshLinkDetectionSystem | Simulation | STEP | off-mesh traversal | OffMeshLinkDetectionSystem.cs:31 |
| OwnershipEgressSystem | Export | EVERY net | ownership changes | OwnershipEgressSystem.cs:10,29 |
| OwnershipIngressSystem | Input | EVERY net | remote ownership updates | OwnershipIngressSystem.cs:42 |
| OwnershipRecomputeSystem | Input | EVERY net | record follows claim | OwnershipRecomputeSystem.cs:101,116 |
| OwnershipTransferInitiationSystem | Input | EVERY net | transfer requests | OwnershipTransferInitiationSystem.cs:63 |
| PartialOwnerReclaimSystem | Input | EVERY net | a node leaves at any time | PartialOwnerReclaimSystem.cs:62 |
| PathTraceSystem | PostSimulation | STEP | path trace | PathTraceSystem.cs:17 |
| PathfindingResultMaterializationSystem | Input | STEP | solver results to the BTree | PathfindingResultMaterializationSystem.cs:18 |
| PathfindingSolverSystem | Simulation | STEP | sim requests only (bypass, §2) | NavigationSolverModule.cs:128-130 |
| PersonalRouteAuthoringSystem | Input | EVERY edit | route authoring click (pairs with RouteTrajectorySync) | PersonalRouteAuthoringSystem.cs:91 |
| PhysicsBodyLifecycleSystem | Simulation | EVERY edit (corrected in S2) | Bullet bodies must follow a drag while paused — its host runs it ALWAYS (`StridePhysicsBracket.cs`, CE-219); also frees bodies of deletes made while paused | StridePhysicsBracket.cs:192-199; PhysicsBodyLifecycleSystem.cs:64,166 |
| PlaybackTickSystem | PostSimulation | EVERY rec | replay seek while paused | PlaybackTickSystem.cs:103 |
| PossessionSystem | Input | STEP (lean, §8) | possess/release at the next step | Possession.cs:185,219 |
| ProficiencyFillSystem | Simulation | STEP | proficiency copy | ProficiencyFillSystem.cs:15 |
| ProfiledManualSystemWrapper | — | GROUP | forwards to its inner system | SystemScheduler.cs:445-451 |
| RaycastResultMaterializationSystem | Input | STEP | raycast hits to the BTree | RaycastResultMaterializationSystem.cs:18 |
| RaycastSolverSystem | Input | STEP | sim requests only (bypass, §2) | RaycastSolverSystem.cs:46,100 |
| RecentSensesSystem | Input | STEP ⚠ | stamps sensor changes | RecentSensesSystem.cs:13 |
| RecorderTickSystem | PostSimulation | EVERY rec | Always Recording | RecorderTickSystem.cs:60,65 |
| ReportingRequirementsSystem | Input | STEP ⚠ | SetReportingEvent | ReportingRequirementsSystem.cs:36 |
| ResumeAndDrainSystem | PreFrame | STEP | staged writes on advancing frames | ResumeAndDrainSystem.cs:63 |
| RoeSystem | Input | EVERY edit (§10 F2) | applies the ROE a unit is loaded with while paused | InitialBrainMaterializationSystem.cs:36-37 |
| RoundPickUpSystem | Simulation | STEP ⚠ | grenade pick-up | RoundPickUpSystem.cs:18 |
| RouteContextSystem | Simulation | STEP | waypoint interrupts by dt | RouteContextSystem.cs:81 |
| RouteTrajectorySyncSystem | BeforeSync | EVERY edit | compiles routes authored while paused | RouteTrajectorySyncSystem.cs:30 |
| SeatPresetSystem | Input | EVERY edit | crews a host placed while paused | SeatPresets.cs:66-80 |
| SelectionEgressSystem | PostSimulation | EVERY net | selection to remote observers | SelectionEgressSystem.cs:76,99 |
| SelectionInteractionSystemAdapter | PostSimulation | EVERY ui | map selection | SelectionInteractionSystem.cs:85 |
| SelectionNotificationSystem | PostSimulation | EVERY ui | selection announcements | SelectionNotificationSystem.cs:88 |
| SelectionRequestSystem | PostSimulation | EVERY ui | the one place selection changes | SelectionRequestSystem.cs:81,93 |
| SimMapRenderSystem | Export | EVERY ui | draws the map | SimMapRenderSystem.cs:44 |
| SimulatedInitReadinessParticipant | BeforeSync | EVERY edit | joins every construction | SimulatedInitReadinessParticipant.cs:75 |
| SmartEgressSystem | Export | STEP | caches the world only; no caller | SmartEgressSystem.cs:16-20 |
| SoundEmissionSystem | Simulation | STEP ⚠ | sound ranges | SoundEmissionSystem.cs:39-60 |
| SpatialHashSystem | Simulation | STEP | collider grid | SpatialHashSystem.cs:57-118 |
| SpawnLevelSystem | PostSimulation | EVERY edit | ground level the frame an entity is born | SpawnLevelSystem.cs:12,26 |
| SquadCoordinationSystem | Simulation | STEP | squad perception and fire | SquadCoordinationSystem.cs:56-85 |
| StaffComposerSystem | Simulation | STEP ⚠ | command-chain reports | StaffSystems.cs:75,89 |
| StaffInboxSystem | Simulation | STEP ⚠ | reports into the CommandPicture | StaffSystems.cs:426 |
| StanceTransitionSystem | Simulation | STEP | stance changes | StanceTransitionSystem.cs:25-79 |
| StatelessGizmoSystem | PostSimulation | EVERY ui | stateless gizmos each frame | StatelessGizmoSystem.cs:107,160 |
| StaticObstacleBakeSystem | BeforeSync | EVERY edit | obstacle bake "must also fire while paused" (R-244) | StaticObstacleBake.cs:37,201 |
| StyleResolutionSystem | PostSimulation | EVERY ui | map symbol styles | StyleResolutionSystem.cs:57-160 |
| SubEntityCleanupSystem | PostSimulation | EVERY edit | parts of a parent deleted while paused | SubEntityCleanupSystem.cs:21-25 |
| TacticalIntentResolutionSystem | Simulation | EVERY edit (lean, §8) | operator orders while paused + mission/commander intents | EntityOrders.cs:167,265 |
| TerrainQueryInitializationSystem | Input | STEP | ground-clamp batch (quartet, test-installed) | TerrainQueryInitializationSystem.cs:24-41 |
| TerrainQueryResolutionSystem | PostSimulation | STEP | clamp HitZ (quartet) | TerrainQueryResolutionSystem.cs:38-83 |
| TerrainQuerySolverSystem | Simulation | STEP | solves the batch (quartet) | TerrainQuerySolverSystem.cs:34-44 |
| TerrainQuerySubmitSystem | Input | STEP | per-entity clamp queries (quartet) | TerrainQuerySubmitSystem.cs:36-76 |
| ThermalHeatSystem | Simulation | STEP | heat from speed | ThermalHeatSystem.cs:40-49 |
| ThreatEvaluationSystem | — | STEP ⚠ | inner of CgfThreatEvaluationSystem | ThreatEvaluationSystem.cs:72,91 |
| TogglableInputGroup | Input | GROUP | members decide | SystemScheduler.cs:176-196 |
| TogglablePostSimulationGroup | PostSimulation | GROUP | members decide (direct call today, §2) | MapInteractionPack.cs:212 |
| TogglableSimulationGroup | Simulation | GROUP | members decide | SystemScheduler.cs:176-196 |
| ToolActivationDrainSystem | PostSimulation | EVERY ui | tool presses | ToolActivationDrainSystem.cs:111 |
| TraceBufferLifecycleSystem | BeforeSync | STEP | trace buffers from DebugState | TraceBufferLifecycleSystem.cs:37-111 |
| TrafficBodyFeedSystem | Input | STEP | bodies to SUMO | TrafficBodies.cs:49-53 |
| TrafficHandoverSystem | Input | STEP | traffic hand-over | TrafficHandover.cs:31-36 |
| TrafficMirrorSystem | Input | STEP | SUMO/DDS cars | TrafficModules.cs:172 |
| TrafficPanicSystem | Input | STEP ⚠ | shots frighten pedestrians | TrafficPanic.cs:86,90 |
| TrafficShadowFollowSystem | Simulation | STEP | body follows its shadow | TrafficShadowFollowSystem.cs:37-91 |
| UnderFireMoveSystem | Simulation | STEP | under-fire reflex | UnderFireMoveSystem.cs:335-345 |
| UnitCentroidSystem | Simulation | EVERY (edit) *(S5: was STEP — its whole work is the paused duty, no events, no state)* | commander at the centroid | UnitCentroidSystem.cs:20-45 |
| UnitHierarchySystem | Simulation | EVERY edit (lean, §8) | ORBAT assigns while paused + sim assigns on steps | ScenarioOrbatAdapter.cs:213; UnitHierarchySystem.cs:54-147 |
| UpdateEntityAttributeRequestSystem | Input | EVERY edit | attribute patches | UpdateEntityAttributeRequestSystem.cs:200 |
| UpdateEntityDescriptorRequestSystem | Input | EVERY edit | position/overlay requests | UpdateEntityDescriptorRequestSystem.cs:91 |
| VehicleCommandSystem | Simulation | STEP ⚠ | VehicleAPI from sim executors | VehicleCommandSystem.cs:37 |
| VehicleNavigationIntentSystem | Simulation | STEP | vehicle path following | VehicleNavigationIntentSystem.cs:173-317 |
| VisualEffectCleanupSystem | PostSimulation | STEP | effects aged by dt | EventToEffectSystem.cs:152 |
| WeaponAmmoMirrorSystem | Simulation | STEP | ammo for the HUD | WeaponAmmoMirrorSystem.cs:26-66 |
| WeaponDeploySystem | Simulation | STEP | deploy deadlines | WeaponSystemComponents.cs:109-123 |
| WeaponDispatcherSystem | Simulation | STEP ⚠ | weapon executors; DestructionOrder | WeaponDispatcherSystem.cs:32-129 |
| WeaponTaskDispatcherSystem | Input | STEP | turret slew by dt | WeaponTaskDispatcherSystem.cs:21-31 |
| WorldSurveyApplySystem | Simulation | STEP ⚠ | survey answers | WorldSurveyApplySystem.cs:33 |
| WreckSpawnSystem | PostSimulation | STEP | flames at the health crossing | EffectSystems.cs:359-385 |

**Totals:** STEP 114 · EVERY 86 (edit 33 · net 24 · ui 26 · rec 3; S5: 2 split systems added, `UnitCentroidSystem` moved; 4 of them leans, §8; 2 moved by §10 F2, 1 corrected in S2) · GROUP 4 = **204**. Plus `DispatcherSystemBase<TChannel>`
(abstract; its subclasses are marked) and 48 test/example systems (S1 marks them too). ⚠ Same-mode sets: the three
capability-interrupt systems · `PersonalRouteAuthoringSystem` + `RouteTrajectorySyncSystem` · the four terrain-query systems.

## Appendix B — every production module *(Roslyn: 85 implementations of `IEcsModule`, 40 production; all read)*

Only these eight have a `Tick` that does work; the other 32 have an empty `Tick` and take **`WhenTimeAdvances`** (their systems
carry their own marks).

| module | policy | Tick | mode | bypassed systems (rerouted in S2) |
|---|---|---|---|---|
| GizmoInteractionModule | Synchronous | gizmo DDS ingress/egress, interaction systems | **EVERY** ui | all of them — GizmoInteractionModule.cs:67-88; `TogglablePostSimulationGroup.Execute` called directly (MapInteractionPack.cs:212) |
| EditorSystemsModule | Synchronous | two editor-authoring systems | **EVERY** edit | EditorPerceptionSetupSystem, EditorZoneAuthoringSystem — EditorSystemsModule.cs:51-55 |
| NedReplicationModule | Synchronous | descriptor pairings + the lifecycle group | **EVERY** net | GhostCreationSystem, DeferredTakeoverSystem via `NetworkLifecycleSystemGroup` — NedReplicationModule.cs:637-642, NetworkLifecycleSystemGroup.cs:111-116 |
| OrchestrationLogicPack | Synchronous | `ClusterSlave.Tick` — capability, 1 Hz heartbeat | **EVERY** net | — |
| PhysicsQueryModule | Synchronous | raycasts + hits | **STEP** | RaycastSolverSystem, HitResolutionSystem — PhysicsQueryModule.cs:40-44 |
| NavigationSolverModule | SlowBackground 10 Hz | path solver | **STEP** | PathfindingSolverSystem — NavigationSolverModule.cs:126-131 |
| EqsModule | SlowBackground 10 Hz | grid + solver (already gated on dt) | **STEP** | LocalGridBuilderSystem, EqsSolverSystem — EqsModule.cs:105-128 |
| TrafficSteppingModule | SlowBackground 20 Hz | SUMO stepping toward a target time | **STEP** | — |

Outside the solution: `Stride/HrotStrideApp.Windows` — one 51-line launcher, no system (Roslyn could not load it: never restored).
