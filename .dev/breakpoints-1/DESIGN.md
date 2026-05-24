# Universal Breakpoints — Design

**Scope:** Slice 2 universal-breakpoint diagnostic substrate for the HROT/FDP engine.
**Source talk:** [design-talk.md](./design-talk.md), refined against [soft-pause.md](./soft-pause.md), [universal-breakpoints-idea.md](./universal-breakpoints-idea.md), and [Blueprint_Subsystem_Slice2_Candidates.md](../blueprints-1/Blueprint_Subsystem_Slice2_Candidates.md).
**Non-goals (explicitly excluded after architect review):**
- Replay-browser "Frankenstein" merged-view feature (separate design).
- `MultiplexingProbeSink` (single-subscriber static field remains sufficient).
- CLR-debugger / Visual Studio source-line sync.
- Exception interception / torn-state snapshot (deferred follow-up).
- Cluster-wide deterministic rewind (single-node use is the supported workflow; see §11).

---

## 1. Goal

Transform the engine's debugging surface from narrow execution-flow pauses (Slice 1 Blueprint nodes) into a **single data-driven diagnostic substrate** that can halt the simulation on:

- arbitrary ECS component-data conditions,
- transient FdpEventBus payload constraints,
- behavior-tree / FastHSM lifecycle opcodes (trap on Enter / Exit / Abort / Transition / Guard),
- dynamic-partition Blueprint variable conditions,
- structural archetype mutations,
- spatial bounding-box transitions,
- entity-lifecycle (birth/death) transitions,
- Blueprint node activations (Slice 1 surface, kept on the managed probe path — see §6).

All of these route through one polymorphic [`SearchPredicateDto`](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs) tree, are JIT-compiled by the existing [`IPredicateCompiler`](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs) / `IEventScannerCompiler`, evaluated zero-allocation against unmanaged chunk memory using `EntityRepository.QueryDelta`, and halt time via the engine's existing **soft-pause** semantics.

---

## 2. Architectural overview

```
┌─────────────────────────────────────────────────────────────┐
│  Editor / Graph UI (per perspective)                        │
│  ├─ Data Breakpoint Manager window  (StructEdit predicate)  │
│  ├─ BTree / HSM / Blueprint context menus  (auto-synthesise)│
│  └─ Watch panel (persists to watches.json)                  │
└──────────────────────────────┬──────────────────────────────┘
                               │  SearchPredicateDto (JSON-friendly)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  IDataBreakpointManager  (per subsystem)                    │
│  ├─ Breakpoint registry  (Breakpoint records + DTOs)        │
│  ├─ Active count → mounts/unmounts DebugSnapshotProvider    │
│  ├─ JIT compile via IPredicateCompiler / EventScannerCompiler│
│  ├─ Hot-reload rebind                                       │
│  └─ Orchestrator: triple-buffer rewind + deferred mutations │
└──────────────────────────────┬──────────────────────────────┘
                               │  compiled Func<EntityRepository,Entity,bool>
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  DataBreakpointSystem  (IEcsModuleSystem @ PostSimulation)  │
│  └─ QueryDelta over dirty chunks + FdpEventBus scans        │
│                                                             │
│  DebugSnapshotProvider  (IEcsModuleSystem @ BeforeSync)     │
│  └─ _preTickSnapshot.SyncFrom(live) when gate=on            │
└──────────────────────────────┬──────────────────────────────┘
                               │  RequestPause / RequestStepOneTick
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  IEngineDebugTimeController  (was IBlueprintTimeController) │
│  └─ MasterSyncTimeControllerAdapter (SwitchToDeterministic) │
└─────────────────────────────────────────────────────────────┘
```

Two new engine systems (per-subsystem), one new manager (per-subsystem), one interface rename + extension, gizmo signature change, plus presentation work. **All other Slice 1 debug surface (callstack, breakpoint list, watch panel, structure-hash safety check, breakpoint reconciliation across hot reload) is preserved unchanged.**

---

## 3. Phase plan

| Phase | Goal | Headline deliverables |
|---|---|---|
| **P0 — Foundation rename** | Generalize Slice 1 time-controller interface | Rename `IBlueprintTimeController` → `IEngineDebugTimeController`; preserve old name as alias for one batch |
| **P1 — Snapshot orchestration** | Triple-buffer infrastructure with zero-cost gate | `DebugSnapshotProvider`, `_postTickSnapshot` allocator, `IDataBreakpointManager` skeleton |
| **P2 — Universal substrate** | JIT predicate evaluation against live ECS | `DataBreakpointSystem` (component + event paths), QueryDelta integration, structural/spatial/lifecycle handlers |
| **P3 — Virtual-snapshot UI swap** | Editor inspects rewound state without mutating live memory | Refactor `IEntityStatefulGizmo.UpdateAndDraw(ISimulationView, ...)`, update `DataDrivenGizmoSystem` + `BehaviorGizmoManagerSystem`, repoint inspection adapters |
| **P4 — Deferred mutation** | `StructEdit` edits applied at N+1 boundary via ECB | `PendingDebugMutation` queue, `StageMutation` API, ECB drain pipeline |
| **P5 — Trace-buffer integration** | BTree / HSM execution breakpoints | Predicate compiler emits ring-buffer scans over `BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024` |
| **P6 — Blueprint variable integration** | Dynamic-partition memory breakpoints | New `BlueprintVariablePredicateDto`, slot-table-aware compiler path |
| **P7 — Graph-editor synthesis** | BTree / HSM / Blueprint context menus auto-build predicates | Context menu actions; gutter renderers reuse existing breakpoint glyphs |
| **P8 — Manager UI** | Data Breakpoint Manager window (StructEdit host) | Predicate Builder modes, JSON clipboard, enable/disable, temporal status banner |
| **P9 — Resilience polish** | Hot-reload rebind, watches.json, step-abandoned UX | Subscribe to `OnReloadCompleted`, `OnHotReloadBegin` preemption, watch persistence |

Tasks within phases run mostly independently; later phases depend on earlier ones (P3 depends on P1, P5 depends on P2, etc.).

---

## 4. Time-control surface (Phase P0)

### 4.1 Rename

Rename the existing [`IBlueprintTimeController`](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs) to **`IEngineDebugTimeController`** in `Hrot.Blueprints.Core.Debug` (or move to `Fdp.ModuleHost.Time.Debug` if the rename also pulls it out of the Blueprint package). Existing methods retained verbatim:

```csharp
public interface IEngineDebugTimeController
{
    bool IsPausedByDebugger { get; }
    void RequestPause();
    void RequestResume();
    void RequestStepOneTick();
}
```

Backwards-compatibility: a one-line interface inheritance (`IBlueprintTimeController : IEngineDebugTimeController`) can be retained for a single batch so the Slice 1 `BlueprintDebugSession` and the existing `MasterSyncTimeControllerAdapter` continue to compile untouched.

### 4.2 Adapter is unchanged

The concrete adapter [`MasterSyncTimeControllerAdapter`](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs) already implements all four methods over `MasterSyncController.SwitchToDeterministic(new HashSet<int>())` (empty slave roster → instant local pause). No mechanism changes; the **rewind-aware methods originally floated** (`BeginObservationalRewind`, `EndObservationalRewind`, `IsInRewoundState`) are **not** added to the time controller — that state belongs in the orchestrator, since the clock must remain a dumb time-advancement primitive. (See §10 — "Orchestrator is one level above the clock".)

---

## 5. Triple-buffer snapshot architecture (Phase P1)

### 5.1 The three repositories

| Repo | Lifetime | Owner | Populated by |
|---|---|---|---|
| `_liveRepo` | Always | `ModuleHostKernel` | Engine simulation (existing) |
| `_preTickSnapshot` | Allocated once per subsystem | `DebugSnapshotProvider` | `SyncFrom(_liveRepo)` at the start of every tick **while the gate is open** |
| `_postTickSnapshot` | Allocated once per subsystem | `IDataBreakpointManager` | `SyncFrom(_liveRepo)` exactly when a predicate fires |

Both auxiliary repos are pre-allocated at subsystem init to avoid GC pressure. They're not registered with the kernel; the manager mutates them via `EntityRepository.SyncFrom`.

### 5.2 `DebugSnapshotProvider`

Implements `IEcsModuleSystem`. Scheduled in `SystemPhase.BeforeSync` (executes before any module ticks). Holds:

- a reference to `_preTickSnapshot` (its own `EntityRepository` instance),
- a `volatile int _isEnabled` flag.

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    if (_isEnabled == 0) return;                   // zero-cost dormant path
    _preTickSnapshot.SyncFrom((EntityRepository)view);
}
```

When the flag is 0, `Execute` is a single CPU branch and returns immediately. **No SyncFrom occurs; production cost stays at exactly zero.**

### 5.3 `IDataBreakpointManager` reference-counted gate

The manager tracks `_activeBreakpointCount` (count of `Enabled == true` breakpoints across the subsystem).

- **0 → 1 transition (Mount):** call `_snapshotProvider.SetEnabled(true)`. The next tick begins capturing `_preTickSnapshot`.
- **1 → 0 transition (Unmount):** call `_snapshotProvider.SetEnabled(false)`. SyncFrom ceases on the next tick.

This guarantees the snapshot's ~2ms cost is paid **only while the developer has at least one breakpoint armed**, satisfying Success Condition #2 ("Zero-Cost Dormant State").

### 5.4 On-demand `_postTickSnapshot`

`_postTickSnapshot` is captured **only** in the exact tick a JIT-compiled predicate evaluates true (and the hit-count threshold is satisfied). The manager does:

```csharp
_postTickSnapshot.SyncFrom(_liveRepo);   // capture exact post-execution state
_liveRepo.SyncFrom(_preTickSnapshot);    // rewind live world to start-of-tick
_timeController.RequestPause();          // halt clock on next frame boundary
```

The pause is a **soft pause**: the kernel finishes any in-flight phase work; the OS thread keeps spinning so the editor UI remains responsive (see [soft-pause.md](./soft-pause.md)).

### 5.5 Clean Step (observation-only fast path)

When the operator clicks Step/Continue and no `_pendingDebugMutations` are queued:

```csharp
_liveRepo.SyncFrom(_postTickSnapshot);   // byte-for-byte restoration of tick N end-state
_timeController.RequestStepOneTick();    // engine advances normally
```

**Zero resimulation. Zero replay logic. Zero `EventAccumulator` injection.** Components flagged `DataPolicy.NoRecord` or `DataPolicy.NoSnapshot` cannot diverge because the past is never re-executed.

> **Why the talk's earlier "destructive SyncFrom" concern was real:** the snapshot's `EntityHeader.ComponentMask` strips `NoSnapshot`/`Transient` bits. Restoring it into `_liveRepo` would orphan transient memory. The forward-snapshot approach sidesteps this entirely by capturing the post-tick state (which *includes* the transient bits as they exist at end-of-tick) and writing it back byte-for-byte. The pre-tick snapshot is used only by **the editor UI for inspection**, never assigned back to live memory while transient data could be lost — see §7 (Virtual Snapshot).

---

## 6. Universal predicate substrate (Phase P2)

### 6.1 Re-used DTO hierarchy

The existing [`SearchPredicateDto`](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs) tree (verified present in v228) is the data contract. The polymorphic `[JsonDerivedType]` discriminators already cover:

| Discriminator | DTO | Mode |
|---|---|---|
| `Compound` | `CompoundPredicateDto` | AND / OR aggregation tree |
| `PropertyMatch` | `PropertyMatchDto` | ECS component data threshold |
| `Numeric`, `String` | `NumericPredicateDto`, `StringPredicateDto` | scalar value predicates |
| `TransientEvent` | `TransientEventPredicateDto` | `FdpEventBus` payload scan |
| `Lifecycle` | `LifecyclePredicateDto` | entity birth / death |
| `SpatialBounding` | `SpatialBoundingPredicateDto` | 2D bounding-box entry/exit |
| `Structural` | `StructuralPredicateDto` | archetype mutation + authority filter |
| `BehaviorParam` | `BehaviorParamPredicateDto` | typed projection over `BrainBlackboard` / `Blackboard1024` |

**New polymorphic node introduced by this design:**

| Discriminator | DTO | Mode (rationale) |
|---|---|---|
| `BlueprintVariable` | `BlueprintVariablePredicateDto` | dynamic-partition Blueprint memory (§6.5) |

No new DTO is added for B-Tree or HSM execution breakpoints. The graph editors **synthesise** a `PropertyMatchDto` (or `CompoundPredicateDto`) targeting the existing trace-buffer components (§6.4).

### 6.2 `Breakpoint` record (orchestration state)

```csharp
public sealed record Breakpoint(
    BreakpointId Id,
    SearchPredicateDto Condition,    // polymorphic payload
    Entity? FilterEntity,            // optional scope; null = global
    int HitCount,                    // incremented on each predicate-true
    int OccurrenceThreshold,         // pause only on Nth+ hit; 0 = every hit
    bool Enabled,
    string DisplayName);
```

One record type covers every variant — the polymorphic `Condition` field absorbs the variation, satisfying Success Condition #8 (Open-Closed Decoupling).

### 6.3 `DataBreakpointSystem`

`IEcsModuleSystem`, runs in `SystemPhase.PostSimulation` (after all domain mutations, before the recorder finalizes the tick's deltas — order critical for Success Condition #5 below).

Pseudocode:

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    if (!_manager.HasMountedDelegates) return;                  // zero-cost gate

    foreach (var (bp, compiled) in _manager.MountedComponentPredicates)
    {
        var mandatory = compiled.MandatoryComponents;           // pre-extracted
        repo.QueryDelta(mandatory, (in entity) =>               // skip clean chunks
        {
            if (bp.FilterEntity is { } e && e != entity) return;
            if (!compiled.Delegate(repo, entity)) return;
            _manager.OnHit(bp, entity);
        });
    }

    foreach (var (bp, scanner) in _manager.MountedEventScanners)
    {
        if (scanner.Evaluate(bus))
            _manager.OnHit(bp, Entity.Null);
    }
}
```

`OnHit` increments `HitCount`, checks `OccurrenceThreshold`, and on a confirmed pause:
1. Captures `_postTickSnapshot.SyncFrom(_liveRepo)`,
2. Rewinds `_liveRepo.SyncFrom(_preTickSnapshot)`,
3. Calls `_timeController.RequestPause()`.

### 6.4 Trace-buffer execution breakpoints (BTree / HSM)

No bespoke DTO. The graph editor synthesises a `PropertyMatchDto` targeting `BTreeTraceWorkingMemory1024.Buffer` (verified at [BTreeTraceWorkingMemory1024.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BTreeTraceWorkingMemory1024.cs)) or its HSM counterpart, with a special path syntax describing "scan all records, match opcode + node index + status".

Compilation extension: `IPredicateCompiler` gets a new branch that recognises the trace-buffer component types and emits a tight loop over `RecordCount` records (16-byte stride) instead of a single-field read. Records to scan:

- **BTree** — `BTreeTraceRecord` (16 bytes; opcode at offset 0, `NodeIndex` at 8, `Status` at 10).
- **HSM** — `TraceRecord` (16 bytes; opcode at offset 0, `StateIndex`/`EventId`/`ActionId`/`GuardId` at 8, `TargetStateIndex`/`GuardResult` at 10, `TriggerEventId` at 12).

Both components are decorated `[DataPolicy(DataPolicy.NoSave)]` and have `RecordCount` headers, so the JIT-compiled scan loops `i = 0 .. RecordCount` with `bufferPtr + (i * 16)` pointer arithmetic. Output is `true` iff any record matches.

### 6.5 Blueprint variable breakpoints

The new `BlueprintVariablePredicateDto`:

```csharp
public sealed class BlueprintVariablePredicateDto : SearchPredicateDto
{
    public Guid TargetBlueprintAssetId { get; set; }     // BlueprintId hash recomputed at compile
    public string VariableName { get; set; }             // looked up via BlueprintDefinition.StateFields
    public SearchOperator Operator { get; set; }
    public SearchPredicateDto Predicate { get; set; }    // NumericPredicateDto / StringPredicateDto
}
```

Compiler emits IL that:
1. probes the entity for *any* of `BlueprintBlackboard1024` / `BlueprintBlackboard4096` / `BlueprintBlackboard16384`,
2. calls `BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset)`,
3. short-circuits to `false` if not found,
4. otherwise reads at `memory + payloadOffset + fieldOffset` (where `fieldOffset` is baked at compile time from `BlueprintDefinition.StateFields[VariableName].OffsetBytes`),
5. casts via `Unsafe.AsRef<T>` to the field type, evaluates the predicate.

The partition allocator's tier-upgrade / defragmentation never invalidates the *delegate* — the delegate re-runs the slot scan every evaluation. Hot reload still invalidates it because `BlueprintId` and field offsets can change; that's handled in §12.

### 6.6 Blueprint **node-execution** breakpoints (Slice 1 surface)

**Per user decision: kept on the managed `DebugProbe.Sink` path.** Slice 1's `BlueprintDebugSession.OnNodeEnter(self, nodeId)` continues to fire when generated Blueprint code hits a probe call. No predicate-engine integration.

The integration point with Universal Breakpoints is the **orchestrator**: when a Slice 1 probe-driven breakpoint fires, instead of calling `_timeController.RequestPause()` directly (current Slice 1 behavior), `BlueprintDebugSession` routes the hit through `IDataBreakpointManager.OnExternalHit(...)`, which then performs the triple-buffer rewind so the developer inspects the **pre-execution** state — fixing the Slice 1 "one-tick drift" via the same `_preTickSnapshot` machinery used by data breakpoints.

**Rationale:** the design talk consistently described Blueprint node breakpoints as `BlueprintLatentCursor.NodeIdAtEntry == <guid>`, but the actual `BlueprintLatentCursor` struct (16 bytes, verified at [BlueprintLatentCursor.cs](../../FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs)) has only `ResumeAt`/`WaitUntilTime`/`InstanceVersion` — no `NodeIdAtEntry`. Adding such a field would be ABI-breaking; meanwhile the probe path already works and is well-tested. Slice 1's structure-hash safety check, breakpoint list, callstack, and watch panel all keep working unchanged.

### 6.7 Mandatory-components optimisation

`IPredicateCompiler.ExtractMandatoryComponents(SearchPredicateDto root)` already walks the predicate tree and pulls components required by `AND`-branch `PropertyMatch` leaves. The manager pre-computes this set when a breakpoint is added; the `DataBreakpointSystem` passes it to `QueryDelta` so entire chunks lacking the mandatory components are skipped in O(chunks).

For OR-branches the engine cannot guarantee component presence — those branches don't contribute to the mandatory set (correct per existing compiler implementation).

### 6.8 Structural / Spatial / Lifecycle paths

These three modes don't reduce to simple chunk-memory threshold checks. The compiler emits a pass-through `(_, _) => true` for the QueryDelta filter. The `DataBreakpointSystem` then evaluates per-tick state-tracking machinery directly:

- **`StructuralPredicateDto`** — maintains a `HashSet<Entity>` of entities currently carrying the target component (honouring `AuthorityRequirement`). Per tick, diff against previous-tick set; fire on `Added`/`Removed`/`AnyChange` matching the DTO's `ModificationType`.
- **`SpatialBoundingPredicateDto`** — maintains a `HashSet<Entity>` of entities inside the bounds; per tick read the position component, evaluate inside/outside, fire on `Entry`/`Exit`/`EntryOrExit`.
- **`LifecyclePredicateDto`** — for births: iterate newly-active entities, evaluate the identifier; for deaths: iterate `EntityRepository.GetDestructionLog()`.

All three replicate the existing replay-browser scanner architecture (already in production for offline search).

---

## 7. Virtual Snapshot — UI rendering during pause (Phase P3)

### 7.1 The view-pointer swap

When the orchestrator engages a pause, it does **not** mutate the editor windows' bindings. Instead, the inspection adapters (e.g. the system driving `EntityInspectorPanel`, the `SimulationViewAdapter` used by data-driven gizmos) consult the manager for the "active view":

```csharp
ISimulationView ActiveView => _manager.IsPaused ? _preTickSnapshot : _liveRepo;
```

Because `EntityRepository` natively implements `ISimulationView`, the swap is trivial; no UI component is rebuilt.

### 7.2 `IEntityStatefulGizmo` signature change

Verified at [IStatefulGizmo.cs](../../FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs): the current interface caches view + entity at construction and `UpdateAndDraw(float, IDebugDrawBuilder)` does not receive a view. This must change:

```csharp
void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder drawBuilder);
```

The cached references in concrete gizmo constructors are dropped; the active view is passed every frame by `DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem`. The view passed is the manager's `ActiveView`. This guarantees a paused session renders the **frozen** pre-tick state and resumes against the live state without recreating gizmo instances.

> **Note:** This is the explicit abandonment of the "no per-call bloat" design rule originally codified in [gizmo-input-focus-design.md](../_DONE/gizmos-1/gizmo-input-focus-design.md). The trade-off (one extra pointer-argument per gizmo per frame vs. correct historical rendering during a pause) is unambiguously worth it.

### 7.3 Temporal status banner

Editor windows (specifically `EntityInspectorPanel` and the map canvas) render a global banner whenever the manager reports `IsPaused == true`:

> `PAUSED — Pre-Execution State (Tick N)`   `[ 2 Pending Mutations ]`

The banner is rendered by a small global panel reading `_manager.PausedTick` and `_manager.PendingMutationsCount`. It's a single ImGui widget; no per-window plumbing.

---

## 8. Deferred mutation contract (Phase P4)

### 8.1 The data envelope

```csharp
public readonly struct PendingDebugMutation
{
    public readonly Entity Target;
    public readonly int    ComponentTypeId;
    public readonly bool   IsManaged;
    public readonly object Payload;       // boxed unmanaged struct OR managed reference
    public readonly int    SizeBytes;
}
```

### 8.2 Staging API

```csharp
// IDataBreakpointManager
void StageMutation(Entity target, Type componentType, object payload);
```

Wired into the `StructEdit` commit pipeline: when a user clicks "Apply" in the inspector while paused, `IEditSession.Commit()` returns the boxed component; the panel routes it to `StageMutation` instead of writing to memory. The manager resolves `ComponentTypeId` via `ComponentTypeRegistry`, classifies managed vs. unmanaged, captures `Marshal.SizeOf` for unmanaged structs, and enqueues into an internal `Queue<PendingDebugMutation>`.

### 8.3 Drain on Step / Continue

When the operator clicks Step or Continue:

```csharp
private unsafe void DrainPendingMutations(EntityRepository repo)
{
    if (_pendingMutations.Count == 0) return;

    var ecb = ((ISimulationView)repo).GetCommandBuffer();
    while (_pendingMutations.TryDequeue(out var m))
    {
        if (m.IsManaged)
        {
            ecb.SetManagedComponentRaw(m.Target, m.ComponentTypeId, m.Payload);
        }
        else
        {
            var handle = GCHandle.Alloc(m.Payload, GCHandleType.Pinned);
            try
            {
                ecb.SetComponentRaw(m.Target, m.ComponentTypeId, (void*)handle.AddrOfPinnedObject(), m.SizeBytes);
            }
            finally { handle.Free(); }
        }
    }
}
```

Both `SetComponentRaw` and `SetManagedComponentRaw` are verified to exist on `IEntityCommandBuffer` ([IEntityCommandBuffer.cs:36-41](../../FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs)).

### 8.4 Full sequence

1. Pause triggered → `_postTickSnapshot.SyncFrom(_liveRepo)`, then `_liveRepo.SyncFrom(_preTickSnapshot)`.
2. Operator inspects rewound state, edits component via `StructEdit` → `StageMutation(...)` enqueues.
3. Operator clicks Step.
4. `_liveRepo.SyncFrom(_postTickSnapshot)` → byte-for-byte restoration.
5. `DrainPendingMutations(_liveRepo)` → ECB records the staged writes.
6. `_timeController.RequestStepOneTick()` → engine advances. ECB plays back as normal at the upcoming tick boundary; the recorder sees a clean tick-N → tick-(N+1) delta.

**This is the 1-tick latency compromise** — edits take effect at the boundary of tick N+1, not retroactively during tick N. Per the talk's analysis: acceptable for 99% of debugging use cases.

---

## 9. Manager API (`IDataBreakpointManager`)

Per-subsystem singleton. Lifetime tied to the subsystem's `ModuleHostKernel`.

```csharp
public interface IDataBreakpointManager
{
    // Registration
    BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                                int occurrenceThreshold = 0, string displayName = "");
    void RemoveBreakpoint(BreakpointId id);
    void SetEnabled(BreakpointId id, bool enabled);
    void UpdateCondition(BreakpointId id, SearchPredicateDto newCondition);

    // Inspection
    IReadOnlyDictionary<BreakpointId, Breakpoint> AllBreakpoints { get; }
    bool IsPaused { get; }
    long? PausedTick { get; }
    int PendingMutationsCount { get; }

    // Deferred mutation
    void StageMutation(Entity target, Type componentType, object payload);

    // Step controls (delegates to time controller after triple-buffer fast path)
    void RequestStep();
    void RequestContinue();

    // External hit injection (Slice 1 Blueprint probe path)
    void OnExternalHit(string sourceTag, Entity entity);

    // Event hooks
    event Action<Breakpoint, Entity> OnBreakpointHit;
    event Action OnPauseStateChanged;
}
```

The manager owns:
- the `_pendingMutations` queue,
- the `_postTickSnapshot` repository,
- the active-count tracker that gates `DebugSnapshotProvider`,
- compiled-delegate cache `(SearchPredicateDto → Func<EntityRepository,Entity,bool>)`,
- subscription to `OnReloadCompleted` / `OnHotReloadBegin`.

### 9.1 Routing during a pause

`RequestStep`:
1. If `_pendingMutations.Count == 0` (Clean Step): `_liveRepo.SyncFrom(_postTickSnapshot)`; release the rewind; `_timeController.RequestStepOneTick()`.
2. Else (Dirty Step): `_liveRepo.SyncFrom(_postTickSnapshot)`; drain queue into ECB; `_timeController.RequestStepOneTick()`.

`RequestContinue`:
- Restore `_postTickSnapshot`, drain queue, `_timeController.RequestResume()`.

---

## 10. Orchestration separation of concerns

The talk repeatedly emphasises this and the design enshrines it:

| Layer | Responsibility | Knows about |
|---|---|---|
| `MasterSyncController` (engine) | Advance `_totalTime` / `_frameNumber`; lockstep network ACKs | Time, frames, nothing else |
| `MasterSyncTimeControllerAdapter` (engine→debug adapter) | Translate `RequestPause/Resume/Step` into `SwitchToDeterministic/SwitchToContinuous/Step` | Time controller calls |
| `IDataBreakpointManager` (orchestrator) | Triple-buffer state, ECB drain, hit-count, snapshot lifecycle | ECS, ECB, predicates, snapshots |
| `DataBreakpointSystem` (evaluator) | Walk QueryDelta, invoke compiled delegates, signal hits to manager | Compiled delegates, EntityRepository |
| `DebugSnapshotProvider` (capture) | Pre-tick `SyncFrom` when gated on | Just SyncFrom + a flag |

**The time controller never learns about ECS data, snapshots, or pending mutations.** This mirrors the proven Slice 1 pattern.

---

## 11. Subsystem scoping & multi-node behaviour

### 11.1 Per-subsystem isolation

The `ClusterRunner` architecture hosts multiple subsystems (SimHost, IG, CGF, ExCon, Editor) within one OS process; each owns an isolated `EntityRepository`, `FdpEventBus`, and `ModuleHostKernel`. Consequently:

- The manager, `DataBreakpointSystem`, and `DebugSnapshotProvider` are instantiated **per subsystem**.
- Compiled `Func<EntityRepository, Entity, bool>` delegates evaluate strictly against the local memory of that subsystem.
- A halt issued by one subsystem's manager calls **that subsystem's** time-controller adapter; it does not broadcast across the cluster.

Cognitive components (`BrainBlackboard`, `BehaviorState`, `BTreeTraceWorkingMemory1024`, `HsmTraceWorkingMemory1024`, all `BlueprintBlackboard*`) physically live only on Brain/CGF and Editor subsystems. If a developer accidentally targets them on the SimHost (Muscle) manager, `QueryDelta`'s mandatory-component filter skips every chunk in O(populated_chunks) and the predicate costs zero — natural filtering.

### 11.2 Multi-node consequences (single-node is supported workflow)

Per the talk's accepted trade-off: **breakpoints are designed for single-node usage** (Editor running in-process, headless test harness, or one isolated subsystem within a cluster). In a live distributed cluster:

- The Brain pauses immediately on a soft pause. Remote Muscle/IG nodes continue simulating for a few ticks before they observe the deterministic-mode switch, then stop where their own clock catches the future barrier.
- This causes (a) Causality Inversion (Muscle's egress events arrive at a paused Brain from "the future"), (b) Visual Disconnect (IG renders Muscle's later-tick positions while developer inspects Brain's tick N), (c) Split-Authority Chaos (intents staged at tick N reach Muscle that already simulated past tick N).
- Forced corrective-state broadcast was evaluated and rejected — it would destroy continuation of the remote simulation.

**Design recommendation:** for deep AI debugging, run the HROT Editor's single-node `OfflineNetworkFactory` mode (Brain + Muscle + IG share one `EntityRepository` over null DDS translators) or use a headless test runner with one subsystem.

### 11.3 Wall-tick annotation for post-mortem analysis

When a breakpoint fires, the manager captures `GlobalTime.TotalWallTicks` (already stamped by `TimeSystem` at frame start) and exposes it via `Breakpoint.LastHitWallTicks`. This lets a future post-mortem analysis use the replay browser's `PlaybackController.SeekToWallClockTicks` to align all node recordings to the breakpoint moment for offline diff inspection (separate feature, out of scope for this design).

### 11.4 Window scope (presentation)

Per established FDP pattern (e.g. `FdpEntityInspectorWindow`, `FdpEventBrowserWindow`), the Data Breakpoint Manager window is registered with `WindowScope.PerspectiveBound`. Each subsystem's perspective shows its own breakpoints; the operator interacts with one subsystem's local manager at a time. No global multiplexing facade is built.

---

## 12. Hot-reload resiliency (Phase P9)

### 12.1 Auto-rebind on `OnReloadCompleted`

The Blueprint subsystem already raises `OnReloadCompleted` via `AiHotReloadCoordinator` with the set of affected asset IDs. The manager subscribes:

1. For every breakpoint whose `Condition` references types from the reloaded assemblies (or whose `BlueprintVariablePredicateDto.TargetBlueprintAssetId` is in the reload set):
   - drop the cached compiled delegate aggressively (avoid stale unmanaged pointer evaluation);
   - re-feed the retained `SearchPredicateDto` to `IPredicateCompiler`;
   - if compilation succeeds (component schema / property paths still structurally valid via `StructureHash`), remount the new delegate;
   - if compilation fails, mark `Breakpoint.IsBroken = true` in the manager (rendered as a red error glyph in the UI). The DTO is retained so the developer can fix and recompile.

### 12.2 "Step abandoned" preemption on `OnHotReloadBegin`

If the simulation is soft-paused when a hot reload starts:

1. The manager force-calls `RequestContinue()` — time unfreezes, snapshot lock released, `_pendingDebugMutations` flushed (their byte offsets may be invalid against the new layout).
2. Active watch-panel variables flagged `IsStale = true`.
3. `IEditorIndicators.Notify("Step abandoned due to reload")` toast emitted.
4. After `OnReloadCompleted`, watches are re-validated (cleared `IsStale` for structurally-matching components).

### 12.3 Watch persistence (`watches.json`)

Watch expressions reuse the same `PropertyMatchDto` / `BlueprintVariablePredicateDto` shapes. The manager serializes `(BreakpointId, Condition)` pairs of watch-flagged entries to `<editor-data>/watches.json` on shutdown and on explicit Save; deserializes on init.

Validation: every watch with a `ComponentType` is reachable via `ComponentTypeRegistry`; mismatched watches are flagged and not re-mounted, leaving the user free to edit and re-arm.

---

## 13. UI design (Phase P8)

### 13.1 Data Breakpoint Manager window

Registered per perspective. Wireframe (preserved from talk):

```
+-----------------------------------------------------------------------------+
| Data Breakpoints                                                      [x]   |
+-----------------------------------------------------------------------------+
| [ + Add ] [ - Remove ]  |  [ Enable All ] [ Disable All ]  |  [ { } JSON ]  |
+-----------------------------------------------------------------------------+
| [ ] | Target Scope      | Type        | Condition Summary      | Hits       |
|-----------------------------------------------------------------------------|
| [x] | Global            | Component   | CurrentHealth < 10     | 4          |
| [ ] | Entity 42         | Event       | HitEvent.Damage > 50   | 0          |
| [x] | Global            | B-Tree      | Node [3] == Success    | 12         |
| [x] | Entity 104        | HSM         | State == 'Fleeing'     | 1          |
| [x] | Global            | Blueprint   | NodeId == 'a1b2c3d4'   | 0          |
+-----------------------------------------------------------------------------+
|  ...details panel hosting StructEdit on selected breakpoint...              |
+-----------------------------------------------------------------------------+
| [PAUSED: Pre-Execution State (Tick 4502)]    [ 2 Pending Mutations ]        |
+-----------------------------------------------------------------------------+
```

JSON clipboard reuses the `ReplaySearchPanel`'s preset infrastructure (serialize → clipboard / paste → deserialize).

### 13.2 Predicate Builder (Details Inspector)

Hosts an `IEditSession` rooted in the selected breakpoint's `Condition`. Modes (mode selector dropdown discards & re-opens the session against a fresh root DTO):

| Mode | Root DTO | UI specifics |
|---|---|---|
| Component Data | `PropertyMatchDto` | `FilteredTypeComboFieldDrawer` for ComponentType; `PropertyPathFieldDrawer` for path; operator + nested numeric/string predicate |
| Transient Event | `TransientEventPredicateDto` | event-type combo (TypeComboMode.Event); `AnyOccurrence` toggle hides payload rows when set |
| Behavior Param | `BehaviorParamPredicateDto` | `TargetBlackboard` enum; `BehaviorHashFieldDrawer` for BehaviorId; path drawer reflects the resolved `ParamsDtoType`/`HeavyDtoType` |
| Compound Logic | `CompoundPredicateDto` | `LogicalOperator` (And/Or); list of nested polymorphic conditions, each row a `$type` dropdown |
| Structural | `StructuralPredicateDto` | ComponentType, `ModificationType`, `AuthorityRequirement` |
| Spatial | `SpatialBoundingPredicateDto` | Position component + X/Y paths; `BoundingBoxFieldDrawer` with `[MapPickableBoundingBox]` injecting map-canvas picker via `GlobalGizmoManager` |
| Lifecycle | `LifecyclePredicateDto` | `IdentifierType`, `TargetValue`, optional `NameComponentType`/`NamePropertyPath` |
| Blueprint Variable | `BlueprintVariablePredicateDto` | `BlueprintPickerDrawer` for asset; variable dropdown from `BlueprintDefinition.StateFields` |

`[Compile & Apply]` invokes `_manager.UpdateCondition(id, dto)`, which routes the DTO through `IPredicateCompiler` / `IEventScannerCompiler` and remounts the new delegate.

### 13.3 Graph-editor context menus (Phase P7)

**BTree canvas** — right-click a node:
```
Add Breakpoint
├─ Break on Activation (Enter)
├─ Break on Completion (Exit)
└─ Break on Interruption (Abort)
Add Conditional Data Breakpoint...
```

Selecting any of the first three synthesises a `PropertyMatchDto` scanning `BTreeTraceWorkingMemory1024` for `(OpCode, NodeIndex, Status)` matching the operator's choice. The conditional variant produces a `CompoundPredicateDto[And]` with the trace-buffer scan as a `[EditReadOnly]` Branch A and an empty `BehaviorParamPredicateDto`/`PropertyMatchDto` for Branch B, opens the Details Inspector with the AND already set, lets the user configure Branch B against blackboard data.

**HSM canvas** — same pattern, on states / transitions / guards, synthesising scans over `HsmTraceWorkingMemory1024`.

**Blueprint canvas** — right-click a node:
```
Add Breakpoint                         ← Slice 1 probe-driven (kept as-is)
Add Conditional Data Breakpoint...     ← Compound: probe-driven node match + variable predicate
```

Visual indicators: existing `BTreeBreakpointGutterRenderer`, `HsmBreakpointGutterRenderer`, and Blueprint canvas red-gutter glyphs continue to read the manager's `AllBreakpoints` set, comparing `ElementId` to node `VisualId`. Auto-synthesised breakpoints carry a `SourceElementId` field on the orchestration `Breakpoint` record so the renderer can locate them; the structural branches are flagged `[EditReadOnly]` in the inspector to prevent the operator from drifting the trace-buffer pathing away from the visual node.

### 13.4 Compound conditions across modes

The user can copy a synthesised B-Tree breakpoint's JSON (clipboard), create a new Compound breakpoint, paste the JSON as a child, and add a sibling `BehaviorParamPredicateDto` — yielding "break only when Action_Wander is running AND AmmoCount == 0" with zero custom-window code. The compiler emits short-circuit IL (AND evaluates the cheaper trace-buffer scan first; OR returns true on first child success).

### 13.5 Hit-count threshold

`Breakpoint.OccurrenceThreshold` exposes "break on Nth hit" in the manager UI as a small integer field per row. The `DataBreakpointSystem` increments `HitCount` on every predicate-true; the manager pauses only when `HitCount >= OccurrenceThreshold`. Zero (default) = pause on every hit.

---

## 14. Flight Recorder invariance

The recorder (`AsyncRecorder` + `RecorderTickSystem`) runs in `SystemPhase.PostSimulation`. Two ordering constraints satisfy Success Condition #5 (Linear Flight Recorder Invariance):

1. **Recorder runs before manager evaluation finishes mutating snapshots.** The recorder writes the post-tick delta of the *natural* execution of tick N. Soft pause happens *after* this; the manager's `_postTickSnapshot.SyncFrom` and `_liveRepo.SyncFrom(_preTickSnapshot)` happen after the recorder has already captured.
2. **No frame is re-simulated.** Tick N is recorded once. Resume from rewound state restores `_postTickSnapshot` (byte-identical to what the recorder captured) and steps to tick N+1; the recorder sees a normal N→N+1 delta. Deferred mutations applied at the N+1 boundary appear as standard ECB writes.

Recorder needs zero awareness of the debugger. `.fdp` files remain perfectly linear.

> **Implementation note:** if the recorder happens to be scheduled *after* the breakpoint system within `PostSimulation`, ordering must be enforced via system priorities so the recorder always serializes the *natural* tick-N state before the manager rewinds `_liveRepo`. This is a single registration constant (or `[ExecuteAfter]`-style attribute on `DataBreakpointSystem`).

---

## 15. Success conditions (verbatim from the talk's final list)

| # | Condition | How this design satisfies it |
|---|---|---|
| 1 | Zero-Allocation Hot-Path Evaluation | Reuses existing `IPredicateCompiler` (emits `ref readonly T` IL) + `EventScannerCompiler` (loops over `bus.Read<T>()`). QueryDelta skips chunks by version. Validated by a CLR profiler test asserting 0 bytes/frame in `DataBreakpointSystem.Execute`. |
| 2 | Zero-Cost Dormant State | `DebugSnapshotProvider._isEnabled` volatile flag gated by manager's `_activeBreakpointCount` reference count. Validated by profiler test: heavy scenario, no breakpoints, snapshot provider reports 0.00 ms. |
| 3 | Resimulation-Free Forward-Snapshot Rewind | `_postTickSnapshot.SyncFrom(live)` captures forward; clean step restores it byte-for-byte. No EventAccumulator injection. Validated: orchestrator unit test asserts only `EntityRepository.SyncFrom` calls between pause and step (no kernel re-run). |
| 4 | Deferred Mutation Determinism | Edits queued in `_pendingDebugMutations`; drained into ECB at N+1 boundary. Validated: end-to-end test pauses on tick N, edits a value, asserts the new value lands at tick N+1 (not N). |
| 5 | Linear Flight Recorder Invariance | Recorder runs to natural completion before pause engages; no resimulation; deferred edits captured as standard ECB delta. Validated: `.fdp` file written across a paused-and-stepped session has strictly monotonic frame indices and no duplicates. |
| 6 | Subsystem-Isolated Execution | Manager + system + snapshot provider instantiated per subsystem. Validated: integration test pauses CGF subsystem, asserts SimHost subsystem continues advancing locally. |
| 7 | Torn-State Exception Interception | **Deferred** — not in this design's scope per user decision. |
| 8 | Open-Closed Decoupling via Polymorphic DTOs | Adding a new breakpoint mode requires only (a) new `SearchPredicateDto` subclass + `[JsonDerivedType]`, (b) new branch in `IPredicateCompiler`. No changes to manager, `DataBreakpointSystem`, UI plumbing, or recorder. |
| 9 | Hot-Reload Memory Resiliency | `OnReloadCompleted` discards compiled delegates, recompiles retained DTOs. Compilation failures mark breakpoint invalid (non-crashing). Validated: structural hot reload while breakpoint armed never throws / never AVs. |
| 10 | Backward-Compatible UI Synthesis | Slice 1 Blueprint node breakpoints unchanged; existing breakpoint list / callstack / watch panel keep working; **new managed-probe-to-manager bridge** wires Slice 1 hits through the triple-buffer rewind so users get pre-execution inspection from existing UI. |

---

## 16. Final-idea coverage map (from design talk)

| Talk topic | Section | Status |
|---|---|---|
| Triple-buffer pause (pre/post/live) | §5 | Designed |
| Soft pause via `SwitchToDeterministic` | §4 | Re-used (Slice 1) |
| Forward snapshot rewind (clean step) | §5.5 | Designed |
| Deferred mutation queue + ECB drain | §8 | Designed |
| Why `SyncFrom` would wipe transient data (avoided via Virtual Snapshot) | §5.5 callout, §7 | Designed |
| Virtual-snapshot UI repointing | §7 | Designed |
| `IEntityStatefulGizmo.UpdateAndDraw` signature change | §7.2 | Designed |
| Stateless gizmos (already take view per-call) | §7 | No change |
| Generalize `IBlueprintTimeController` → `IEngineDebugTimeController` | §4 | Designed |
| `DataBreakpointSystem` in `PostSimulation` | §6.3 | Designed |
| `DebugSnapshotProvider` in `BeforeSync` | §5.2 | Designed |
| Zero-cost gate (active-count reference) | §5.3 | Designed |
| QueryDelta chunk-skipping | §6.3, §6.7 | Designed |
| Mandatory components extraction | §6.7 | Re-used |
| Polymorphic `SearchPredicateDto` substrate | §6.1 | Re-used (verified extant) |
| `PropertyMatchDto` | §6.1 | Re-used |
| `TransientEventPredicateDto` | §6.1 | Re-used |
| `BehaviorParamPredicateDto` | §6.1 | Re-used |
| `CompoundPredicateDto` | §6.1 | Re-used |
| `StructuralPredicateDto` + `AuthorityRequirement` | §6.8 | Re-used |
| `SpatialBoundingPredicateDto` | §6.8 | Re-used |
| `LifecyclePredicateDto` | §6.8 | Re-used |
| BTree execution breakpoints (Enter/Exit/Abort) | §6.4 | Designed (reuses `PropertyMatchDto` over trace buffer) |
| HSM execution breakpoints (State/Transition/Guard) | §6.4 | Designed (reuses `PropertyMatchDto` over trace buffer) |
| Blueprint node breakpoints | §6.6 | Slice 1 probe path retained; routed through new manager for triple-buffer rewind |
| **NEW** `BlueprintVariablePredicateDto` for dynamic-partition memory | §6.5 | Designed |
| Compound: BTree node + blackboard variable | §13.4 | Designed (no custom UI) |
| "Add Conditional Data Breakpoint..." graph context menu | §13.3 | Designed |
| `[EditReadOnly]` on auto-synthesised structural branches | §13.3 | Designed |
| Break after N-th hit (HitCount threshold) | §13.5, §6.2 | Designed |
| `Breakpoint` record (single unified type) | §6.2 | Designed |
| `IDataBreakpointManager` API | §9 | Designed |
| Subsystem-isolated execution | §11.1 | Designed |
| `WindowScope.PerspectiveBound` per-subsystem | §11.4 | Designed |
| Multi-node consequences accepted (single-node workflow) | §11.2 | Documented |
| Wall-tick annotation for replay-browser alignment | §11.3 | Designed (capture only; merged-view explicitly out of scope per user) |
| Hot-reload auto-rebind | §12.1 | Designed |
| "Step abandoned" preemption on hot-reload | §12.2 | Designed |
| Watch persistence to `watches.json` | §12.3 | Designed |
| **EXCLUDED** `MultiplexingProbeSink` | n/a | Excluded (single subscriber sufficient) |
| **EXCLUDED** CLR-debugger sync | n/a | Excluded |
| **DEFERRED** Exception interception / torn-state | n/a | Out of scope (#7 above) |
| **EXCLUDED** "Frankenstein" merged replay-browser view | n/a | Separate design |
| Stack-frame context reconstruction (call stack on ECS) | n/a | Out of scope; reusing Slice 1 callstack window unchanged. Trace-buffer rendering already gives execution callstack. |
| Flight Recorder invariance | §14 | Designed |
| All 10 success conditions | §15 | Mapped |

---

## 17. Project-dependency check

| Component this design touches | Project | Existing? | Change |
|---|---|---|---|
| `IBlueprintTimeController` | `Hrot.Blueprints.Core` | yes | Rename + relocate (alias kept one batch) |
| `MasterSyncTimeControllerAdapter` | `Hrot.Blueprints.Editor` | yes | No behavioural change |
| `BlueprintDebugSession` | `Hrot.Blueprints.Editor` | yes | Add `OnExternalHit` wiring to `IDataBreakpointManager` |
| `SearchPredicateDto` family | `Fdp.Toolkit/ReplayBrowser/Search` | yes | Add `BlueprintVariablePredicateDto` + `[JsonDerivedType]` entry |
| `IPredicateCompiler` | `Fdp.Toolkit/ReplayBrowser/Search` | yes | Extend with trace-buffer scan branch + blueprint-variable path |
| `IEventScannerCompiler` | `Fdp.Toolkit/ReplayBrowser/Search` | yes | No change |
| `BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024` | `Fdp.Toolkit/Behavior/Diagnostics` | yes | No change (read-only consumer) |
| `BlueprintLatentCursor` | `Fdp.Toolkit/Blueprints` | yes | No change (Blueprint node BPs stay on probe path) |
| `EntityCommandBuffer.SetComponentRaw / SetManagedComponentRaw` | `Fdp.Core` | yes | No change |
| `EntityRepository.SyncFrom` / `.QueryDelta` | `Fdp.Core` | yes | No change |
| `IEntityStatefulGizmo` | `Fdp.Toolkit/Diagnostics/Gizmos` | yes | **Signature change**: add `ISimulationView view` param |
| `DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem` | `Fdp.Toolkit/Diagnostics/Gizmos` | yes | Pass active view from manager into each `UpdateAndDraw` call |
| `EntityInspectorPanel` / `SimulationViewAdapter` | `Hrot.Presentation` (or equivalent) | yes | Read view from `IDataBreakpointManager.ActiveView` |
| `StructEdit` commit pipeline | (existing) | yes | Route writes to `StageMutation(...)` during pause |
| **NEW** `IDataBreakpointManager` + concrete impl | new file in `Hrot.Blueprints.Core.Debug` or shared `Hrot.Diagnostics.Breakpoints` | no | New |
| **NEW** `DataBreakpointSystem` (`IEcsModuleSystem`) | same | no | New |
| **NEW** `DebugSnapshotProvider` (`IEcsModuleSystem`) | same | no | New |
| **NEW** Data Breakpoint Manager window | `Hrot.Presentation` (per-perspective registration) | no | New |
| **NEW** BTree / HSM / Blueprint canvas context-menu extensions | existing graph editors | yes | Add menu entries |

**No new external package dependencies.** No new DDS message types. No new ECS component types (the talk's "BlueprintNodeEntryFlag1024" alternative was rejected in favor of the probe-path approach). No recorder changes. No network-protocol changes.

**Risk:** the `IEntityStatefulGizmo` signature change is breaking — every concrete gizmo (data-driven, behavior, picker variants) gets a one-line ctor cleanup. Compile-time error, easily found and fixed; no silent breakage.

---

## 18. Single-source-of-truth referenced files

- [SearchPredicateDto.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs) — full DTO hierarchy
- [IPredicateCompiler.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs) — compiler interface
- [IBlueprintTimeController.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs) — interface to rename
- [MasterSyncTimeControllerAdapter.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs) — soft-pause adapter
- [BlueprintDebugSession.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs) — Slice 1 session to bridge
- [BlueprintLatentCursor.cs](../../FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs) — verified shape (no NodeIdAtEntry)
- [BTreeTraceWorkingMemory1024.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BTreeTraceWorkingMemory1024.cs) — trace buffer
- [IEntityCommandBuffer.cs](../../FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs) — `SetComponentRaw` / `SetManagedComponentRaw` confirmed
- [IStatefulGizmo.cs](../../FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs) — signature to extend
- [SystemPhase.cs](../../FDP/Engine/Fdp.ModuleHost/Abstractions/SystemPhase.cs) — `BeforeSync` / `PostSimulation` confirmed
- [Blueprint_Subsystem_Slice2_Candidates.md](../blueprints-1/Blueprint_Subsystem_Slice2_Candidates.md) — original D1 + related items
- [Blueprint_Subsystem_Architecture_v1.2.md](../blueprints-1/Blueprint_Subsystem_Architecture_v1.2.md) — Blueprint runtime overview
- [HROT architecture.md](../../docs/HROT%20architecture.md) — engine architecture
