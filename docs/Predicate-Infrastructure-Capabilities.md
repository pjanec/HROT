# Predicate Infrastructure — Capabilities & Reuse Surface

**Purpose.** Frame what is already available in the codebase for **building, serialising, JIT-compiling, and editing condition trees over entity state, AI memory, and the event bus**. Two systems already consume this infrastructure end-to-end (the Replay Browser's search panel and the Universal Breakpoints subsystem). A third consumer — for example a "When …" BTree node that fires when an entity-state condition becomes true — can be built on top with no new compiler, DTO, or UI plumbing.

This is a capabilities reference, not an architecture-from-scratch design. It tells you **what you can take for granted** and **where the limits sit**.

---

## 1. The four reusable layers

```
┌─────────────────────────────────────────────────────────────────┐
│  1.  DTO hierarchy   (data contract; JSON-friendly)             │
│      SearchPredicateDto + subclasses                            │
│      [Fdp.Toolkit/ReplayBrowser/Search/SearchPredicateDto.cs]   │
└─────────────────────────────────────────────────────────────────┘
                            │
              ┌─────────────┴─────────────┐
              ▼                           ▼
┌─────────────────────────────┐  ┌─────────────────────────────┐
│  2.  JIT compiler           │  │  3.  UI builder             │
│      IPredicateCompiler →   │  │      StructEdit + drawers   │
│      Func<repo,e,bool>      │  │      → ImGui property tree  │
│      IEventScannerCompiler  │  │      Specialised drawers    │
│      → EventScannerDelegate │  │      drive type / path /    │
│      Zero-allocation hot    │  │      behaviour / bounding-  │
│      path; chunk-version    │  │      box pickers            │
│      aware via QueryDelta   │  │                             │
└─────────────────────────────┘  └─────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  4.  JSON round-trip   (clipboard / file persistence)           │
│      [JsonPolymorphic] + [JsonDerivedType] discriminators       │
│      System.Text.Json out of the box                            │
└─────────────────────────────────────────────────────────────────┘
```

Each layer is independently consumable. A new feature can pick a subset (e.g. the DTO + the compiler without the UI; or the UI + JSON without the engine evaluation).

---

## 2. DTO hierarchy — what you can express

Defined in [`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`](../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs). All concrete classes inherit from the abstract `SearchPredicateDto` base, which carries the `[JsonPolymorphic]` discriminator so trees round-trip as JSON without custom converters.

### 2.1 Composition

| DTO | Purpose |
|---|---|
| **`CompoundPredicateDto`** | AND / OR aggregation over a `List<SearchPredicateDto>`. Recursive — children can be any predicate type including further compounds. Carries `ReadOnlyChildIndices` so callers can mark specific branches "locked" (used by graph editors to inject structural conditions that must not be edited away). |

### 2.2 Value matchers (scalar comparison)

| DTO | Matches |
|---|---|
| `NumericPredicateDto` | `MinValue` / `MaxValue` (double); range or exact via `Min == Max`. |
| `StringPredicateDto` | `Substring`, `StartsWith`, `ExactMatch` flags. |
| `EnumPredicateDto<TEnum>` | Allow-list of enum values. **Not** registered in the JSON polymorphic chain (generic type parameter); usable directly in code, not round-trippable. |

These are normally nested inside a parent matcher's `Predicate` field, never used alone.

### 2.3 ECS state matchers

| DTO | What it inspects | How it compiles |
|---|---|---|
| **`PropertyMatchDto`** | A single field on an unmanaged or managed ECS component, addressed by `ComponentType` + dot-notation `PropertyPath` (e.g. `"Position.X"`, `"Locomotion.ActiveAction"`). | `Func<EntityRepository, Entity, bool>` emitted via expression trees; reads `GetComponentRO<T>` (`ref readonly`) and walks the property path. No boxing. |
| **`StructuralPredicateDto`** | Component-mask transitions: `Added`, `Removed`, `AnyChange`. Filter via `AuthorityRequirement` so split-authority ghosts don't fire phantom hits. | Per-tick state tracker (HashSet of entities currently carrying the bit) rather than a chunk-data delegate. |
| **`SpatialBoundingPredicateDto`** | 2D-box entry / exit / both, using a configurable `PositionComponentType` + `PositionXPath` / `PositionYPath`. Bounds can be authored via a map-canvas picker (`[MapPickableBoundingBox]` attribute). | Per-tick state tracker (HashSet of entities currently inside the box). |
| **`LifecyclePredicateDto`** | Birth / death of an entity, identified by `EcsHandle`, `NetworkId`, or a `NameSubstring` (in which case `NameComponentType` + `NamePropertyPath` tell the evaluator where the string lives). | Per-tick scan of newly-active entities + `EntityRepository.GetDestructionLog()`. |

### 2.4 AI / behaviour matchers

| DTO | What it inspects |
|---|---|
| **`BehaviorParamPredicateDto`** | A typed field projected over the untyped `BrainBlackboard.BehaviorParameters` buffer (60 B inline) **or** the `Blackboard1024.Memory` buffer (1024 B). Carries a `BehaviorId` hash so the compiler can short-circuit when the active behaviour doesn't match, then `Unsafe.AsRef<T>` over the correct DTO type. |
| **`BlueprintVariablePredicateDto`** | A named variable inside a dynamically-allocated slot of `BlueprintBlackboard1024/4096/16384`. Compiler emits IL that walks the slot table at `BlueprintBlackboardPartitions.TryGetSlotOffset`, short-circuits on miss, then reads at `payloadOffset + fieldOffset`. Tier upgrades don't invalidate the compiled delegate — the slot lookup runs every evaluation. |
| **`TraceBufferScanPredicateDto`** | Any record in `BTreeTraceWorkingMemory1024` or `HsmTraceWorkingMemory1024` (16-byte stride ring buffer, up to 63 records). Matches on `OpCode` plus optional `IndexField` (node/state index), `StatusField` (NodeStatus or GuardResult), `TriggerEventId`. Lets you express "did B-Tree node X enter Running this tick?", "did HSM exit state Y?", "was transition Z fired?". |

### 2.5 Event-bus matchers

| DTO | What it inspects | How it compiles |
|---|---|---|
| **`TransientEventPredicateDto`** | A `FdpEventBus` payload (`bus.Read<T>()` for unmanaged, `bus.ReadManaged<T>()` for managed). Carries `EventType`, `AnyOccurrence` (bool — when true, O(1) `bus.HasEvent(type)`), `PropertyPath`, `Operator`, `TargetValue` (string parsed at JIT time). | Compiled to `EventScannerDelegate` by a separate compiler (§3.2). Does **not** flow through the entity-predicate compiler. |

### 2.6 External-hit bridge

| DTO | What it does |
|---|---|
| `ExternalHitTagPredicateDto` | Synthetic marker — the entity-predicate compiler always returns `static (_, _) => false` for this type. The Universal Breakpoint manager treats it specially: a `Tag` string is matched against external probe calls (e.g. Slice 1 Blueprint `OnNodeEnter`). Useful when an external event source (probe, network, hot-reload, etc.) is the trigger and the rest of the compound is a guard. |

### 2.7 Extension points

Adding a new evaluation shape requires only:

1. A new subclass of `SearchPredicateDto` + a `[JsonDerivedType]` registration on the base.
2. A new `case` branch inside `PredicateCompiler.Compile(...)` (or `EventScannerCompiler` if it scans the bus).
3. *Optionally* a UI drawer if the new DTO has a field with custom interaction (otherwise StructEdit reflects it automatically).

No `DataBreakpointSystem`, no manager, no UI window code needs to change. This is the architectural Open-Closed property that the system was deliberately built around.

---

## 3. JIT compilation — `DTO → delegate`

Defined in [`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`](../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs) and the sibling `EventScannerCompiler.cs`.

### 3.1 `IPredicateCompiler`

```csharp
public interface IPredicateCompiler
{
    Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root);
    IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto root);
}
```

- **Input:** any `SearchPredicateDto` tree (compounds resolved recursively).
- **Output:** a single delegate. Short-circuit semantics in compounds: `And` returns `false` on first failing child; `Or` returns `true` on first succeeding child.
- **Mandatory components:** the compiler walks `AND` branches and lists the component types that every match *must* have. Callers can use this to pre-filter via `EntityRepository.Query().WithComponentId(...)` so chunks lacking the mandatory components are skipped in O(populated_chunks). `OR` branches do not contribute (they don't guarantee component presence).
- **Hot path:** the emitted IL uses `ref readonly` chunk pointers, no boxing, no managed allocations *within the delegate*. (Callers can still allocate around it — see "Limits" below.)
- **Construction:** `new PredicateCompiler(editService, behaviorRegistry?, blueprintRegistry?)`. The behaviour registry is needed to type-resolve `BehaviorParamPredicateDto` paths; the blueprint registry to bake field offsets for `BlueprintVariablePredicateDto`.

### 3.2 `IEventScannerCompiler`

```csharp
public interface IEventScannerCompiler
{
    EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate);
}

public delegate void EventScannerDelegate(
    FdpEventBus bus, int frame, long ticks,
    List<SearchResultDto> results,
    EntityRepository repo,
    TargetEntityFilter? entityFilter);
```

- **Output:** a stateless, thread-safe delegate. Appends matches to the caller's `results` list.
- **Three internal fast paths** based on the DTO:
  - `AnyOccurrence == true` → O(1) `bus.HasEvent(type)`.
  - Unmanaged event payload → tight loop over `bus.Read<T>()` reading via `PropertyEvaluator`.
  - Managed event payload → loop over `bus.ReadManaged<T>()`, null-skip.
- **TargetValue** is intentionally a string; the compiler parses it at JIT time to match the property type.

### 3.3 Where the compiled delegates run

The compiled output is just a delegate — **the call site is yours**. Existing call sites:

- Replay Browser: invoked once per recorded frame during a search pass over the loaded `.fdp`.
- Universal Breakpoints: invoked every tick in `SystemPhase.PostSimulation` via `DataBreakpointSystem`.
- A "When" BTree node would invoke the same delegate every tick of the node's evaluation against its owning entity.

There is no constraint that says these delegates can only run inside a system; any code that has an `EntityRepository` (or the appropriate snapshot view) and an `Entity` handle can evaluate them.

---

## 4. UI building — `StructEdit` + specialised drawers

### 4.1 What StructEdit gives you for free

The `StructEdit` package ([`FDP/ExtDeps/StructEdit/`](../FDP/ExtDeps/StructEdit/)) is a polymorphic ImGui editor that takes **any class graph** and renders it as a two-column **(Property | Value)** tree, generating the right controls from reflection + attributes.

Specifically:
- Polymorphic types (`[JsonDerivedType]`) get a `$type` dropdown that swaps the active payload to a different subclass; the rest of the tree re-renders automatically.
- Collections (`List<>`) get `+ Add` / `Remove` row controls.
- Enums get a combo box.
- Primitives get their natural ImGui input.
- Custom field attributes route specific fields to specialised drawers (§4.2).

You open it via:

```csharp
IEditSession session = editService.Open(dto, dto.GetType());
session.DrawInPlace();          // somewhere inside an ImGui frame
if (session.IsDirty) { ... }
var commitedDto = session.Commit();   // boxed; same type as input
```

**Outcome:** any new `SearchPredicateDto` subclass gets a working editor with zero new UI code. You only write a drawer if a field needs domain-aware behaviour (e.g. "show valid property paths for the currently selected component").

### 4.2 The five specialised drawers already in the codebase

Located in [`FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/Drawers/`](../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/Drawers/):

| Drawer | Triggered by | Behaviour |
|---|---|---|
| **`FilteredTypeComboFieldDrawer`** | Field of type `Type` + a mode hint (`Component` / `Event`) | Reflection scan over `ComponentTypeRegistry` (or the event registry) populates a searchable dropdown. Result is stored as a `Type` reference, serialised by short name. |
| **`PropertyPathFieldDrawer`** | `[PropertyPathPicker]` attribute on a `string` field | Reads the sibling row's `ComponentType` (or in `BehaviorParamPredicateDto` mode, resolves the active behaviour's `ParamsDtoType`), reflects its layout, and offers a typeahead dropdown of *valid* dot-notation paths. Prevents typos that would break the JIT. |
| **`BehaviorHashFieldDrawer`** | `[BehaviorHashPicker]` attribute on an `int` field | Queries the live `BehaviorRegistry` to present a list of human-readable behaviour names; stores the stable integer hash. |
| **`BoundingBoxFieldDrawer`** | `[MapPickableBoundingBox]` attribute on a `BoundingBox2D` field | Unrolls `Min` / `Max` to two `DragFloat2` rows and injects a `[...]` picker button that opens a `BoundingBoxPickerGizmo` on the map canvas; the operator drags a rectangle, releases, and the coordinates land back in the session. |
| **`PredicateValueFieldDrawer`** | Polymorphic `SearchPredicateDto Predicate` field on a parent matcher | Reads the parent's `Operator`, casts the abstract `Predicate` row to the right concrete value type (`Numeric` / `String` / etc.), and unrolls its scalar inputs inline. |

### 4.3 Hosting the editor

Two production hosts already wrap StructEdit:

- **`ReplaySearchPanel`** in `FDP.Engine.Fdp.Presentation` — the original Replay Browser predicate authoring surface. Reads from / writes to a single `SearchPredicateDto`, exposes a mode dropdown (Component / Event / Compound / Spatial / Lifecycle / Structural / Behavior Param), and persists presets to JSON.
- **`DataBreakpointManagerPanel`** in `Hrot.Presentation.Panels.Breakpoints` — the Universal Breakpoints manager. Wraps StructEdit identically but lives alongside a grid of registered breakpoints; the Predicate Builder section is the same StructEdit host opened against the currently-selected breakpoint's `Condition`.

A third host (e.g. an inspector tab on a BTree "When" node) follows the same pattern: hand a `SearchPredicateDto` field to `IComponentEditService.Open(...)`, draw it inline, capture `Commit()`.

### 4.4 The `ReadOnlyChildIndices` mechanism

For compounds where some branches must be locked (e.g. an auto-generated structural guard the user shouldn't be able to drift), set `CompoundPredicateDto.ReadOnlyChildIndices = [0]` (or whichever indices). The Universal Breakpoint menu populators already use this to inject a fixed BTree trace-scan branch alongside an editable variable branch.

> Note (factual): at the time of writing the current `DataBreakpointManagerPanel` does not yet honour `ReadOnlyChildIndices` when rendering — the field is populated by callers but the renderer needs a small update to disable matching rows. Treat the mechanism as "designed and stored, partially wired" rather than fully enforced.

---

## 5. JSON round-trip

`System.Text.Json` round-trips any `SearchPredicateDto` tree out of the box because the polymorphic discriminator and all derived types are statically registered:

```csharp
string json = JsonSerializer.Serialize(rootDto, options);
var loaded = JsonSerializer.Deserialize<SearchPredicateDto>(json, options);
```

Existing uses:
- Replay Browser preset save / load.
- Universal Breakpoints "Copy to Clipboard" / "Paste from Clipboard" toolbar buttons.
- `WatchPersistence.Save(...)` / `TryLoad(...)` for editor-restart watch survival.

Persisting to disk, clipboard, or wire is therefore a one-liner. The only thing not round-trippable is the generic `EnumPredicateDto<TEnum>` — encode enum allow-lists as `StringPredicateDto.Substring` if you need wire serialisation.

---

## 6. Evaluation modes — what's possible without writing new compiler code

A condition over entity state and AI memory can be expressed as one of these shapes today, no library extension:

| Question the operator wants to ask | DTO shape |
|---|---|
| "Did `Health.Current` drop below 10?" | `PropertyMatchDto(Health, "Current", Numeric{Max=9.999})` |
| "Did the entity enter `LocomotionAction.Flee`?" | `PropertyMatchDto(Locomotion, "ActiveAction", Numeric{Min=Max=(int)Flee})` |
| "Did B-Tree node 7 just enter Running?" | `TraceBufferScanPredicateDto(BTreeTraceWorkingMemory1024, OpCode=NodeEvaluated, IndexField=7, StatusField=Running)` |
| "Did HSM transition fire on event 42?" | `TraceBufferScanPredicateDto(HsmTraceWorkingMemory1024, OpCode=Transition, TriggerEventId=42)` |
| "Is `BehaviorState.ActiveBehaviorHash` == HillAttack AND `BehaviorParameters.Aggression > 0.8`?" | `BehaviorParamPredicateDto(BrainBlackboard, BehaviorId=HillAttack.Hash, "Aggression", Numeric{Min=0.8})` (the behaviour-hash check is implicit) |
| "Did a Blueprint variable `AmmoCount` reach 0?" | `BlueprintVariablePredicateDto(MyAsset.Guid, "AmmoCount", Numeric{Min=Max=0})` |
| "Has a `HitEvent` with `Damage > 50` been published this tick?" | `TransientEventPredicateDto(HitEvent, AnyOccurrence=false, "Damage", >, "50")` |
| "Did the entity enter the polygon over there?" | `SpatialBoundingPredicateDto(SimTransform, "Position.X", "Position.Y", bounds=…, Entry)` |
| "Was `EntityInfo.Name` 'EnemyTank' born this tick?" | `LifecyclePredicateDto(NameSubstring, "EnemyTank", EntityInfo, "Name")` — fires on births and deaths |
| "Multiple of the above together" | `CompoundPredicateDto(And, [...])` / `Or` |

The combinations are open-ended; a compound can mix data, event, blackboard, and trace-buffer children freely. **All of this is doable with the existing compiler, with no new code.**

---

## 7. Limits and gotchas

These are the real edges where a new consumer needs to do work or accept a constraint.

### 7.1 Evaluation cadence and tick boundary

- The entity-data delegate (`Func<EntityRepository, Entity, bool>`) evaluates **instantaneously** against whatever repo / entity you pass in. It has no concept of "across ticks".
- "Did X change?" semantics require **two-tick edge detection** at the call site. The existing consumers (Replay Browser, breakpoints) handle this in different ways:
  - **Replay Browser:** invokes the predicate per recorded frame; the delta is implicit (each frame is a discrete time step).
  - **Universal Breakpoints (component-data path):** uses `EntityRepository.QueryDelta(..., sinceVersion)` to evaluate only against entities whose chunks have *changed*. This is presence detection (the predicate is now true), not transition detection.
  - **Universal Breakpoints (structural/spatial/lifecycle):** maintains a `HashSet<Entity>` per breakpoint capturing "was this entity in the matching set last tick" — explicit two-tick tracking lives in the orchestrator, not in the compiled delegate.
- A "When …" BTree node that wants strict edge semantics (fire when the condition transitions `false → true`) needs to remember last-tick state itself. The compiled delegate gives the current evaluation; you build the edge from two consecutive evaluations.

### 7.2 The event-scanner path is per-tick, not per-event

`EventScannerDelegate` scans the *current frame's* bus payloads in one go and appends every matching event. It does not deliver individual events synchronously. A consumer reacting to events therefore inherits the same tick-granularity that the rest of the engine works at. This matches `SystemPhase.PostSimulation` ordering and the soft-pause architecture.

### 7.3 Where the compiled delegate is *not* zero-allocation

The delegate body itself is allocation-free. But the call site can leak:

- The current `DataBreakpointSystem` allocates a `List<Entity>` and a closure per breakpoint per tick (a known hot-path defect tracked as `UBP-P11T1`). A new consumer should avoid that pattern: cache the callback once, reuse a `List<>` buffer, or use a struct-typed `QueryDelta` visitor if/when one is added.
- The mandatory-components list (`ExtractMandatoryComponents`) allocates a `List<Type>` per call — fine if called once at predicate-mount time, expensive if called per tick.
- Reflection-driven helpers like `LifecyclePredicateDto.NamePropertyPath` reading currently use `FieldInfo.GetValue`; consumers should plan to either build a compiled accessor or accept the cost for low-frequency state trackers.

In short: the **inner** evaluation is fast and clean; the **outer loop** is the consumer's responsibility.

### 7.4 Blueprint node entry is not a `PropertyMatchDto` candidate

`BlueprintLatentCursor` (16 bytes) does **not** carry a "current node id" field. Blueprint **node** breakpoints route through `ExternalHitTagPredicateDto` plus a probe callback into `DebugProbe.Sink.OnNodeEnter(...)`. A BTree "When" node that wants to fire on entry to a *Blueprint* node should expect to consume the same probe surface — the universal predicate substrate alone cannot observe Blueprint node execution.

### 7.5 Multi-node / cluster behaviour

The compiled delegates evaluate against a single subsystem's `EntityRepository`. They are subsystem-local; no cluster broadcast happens. A BTree on the Brain (CGF) node can use any cognitive predicate freely; the Muscle (SimHost) repo doesn't physically hold cognitive components and `QueryDelta`'s component-mask filter naturally skips them.

### 7.6 Hot reload

`Func<EntityRepository, Entity, bool>` delegates are JIT-emitted against the layout of the assemblies that were loaded when they were compiled. After a Roslyn hot reload, the layout may shift; consumers **must** invalidate and recompile their delegates from the retained DTO. The Universal Breakpoints manager subscribes to `AiHotReloadCoordinator.OnReloadCompleted` and does this; any new consumer needs an equivalent hook (or accept that it will live on a code path that doesn't hot-reload).

### 7.7 Snapshot semantics

The compiled entity-data delegate is purely a function of the `EntityRepository` reference passed in. If you pass the live repo it sees live state; if you pass a snapshot (e.g. the Universal Breakpoints `_preTickSnapshot`) it sees the historical state. Consumers that want to differentiate "what was true last tick" from "what is true now" can call the same delegate against two repos.

---

## 8. Consumer comparison (where existing wiring helps you copy patterns)

| Aspect | Replay Browser | Universal Breakpoints | A BTree "When" node (hypothetical) |
|---|---|---|---|
| Authoring UX | `ReplaySearchPanel` (`StructEdit`) | `DataBreakpointManagerPanel` (`StructEdit`) | Same — open `IEditService.Open(dto)` in the node's inspector tab |
| Persisted as | `.json` preset files | `watches.json` + clipboard | Inline in the BTree asset (serialised as part of the tree's node payload) |
| Compile timing | Once per search invocation | At `AddBreakpoint` / `UpdateCondition` / hot-reload | At tree-asset load + on hot-reload |
| Evaluation timing | Per recorded frame during a search pass | Every tick in `PostSimulation`, gated by snapshot reference count | Every tick the node is visited (BTree evaluation cadence) |
| Edge detection | N/A — discrete frame scan | Per-tracker `HashSet` for structural/spatial/lifecycle; "presence" only for component-data path | Node holds last-tick result; emits "fired" on `false → true` |
| Mandatory-components optimisation | Used to short-circuit chunks during scan | Used to short-circuit `QueryDelta` filter | Trivial — the BTree visits a single entity, so chunk filtering is irrelevant; just call the delegate |

---

## 9. Practical reuse checklist for a new consumer

Anything that wants to add a predicate-driven feature can copy this shape:

1. **Reference** `Fdp.Toolkits` (DTO + compiler) and `Fdp.Presentation` (drawers + `StructEdit` hosting), plus `BehaviorRegistry` / `BlueprintRegistry` if behaviour-param or blueprint-variable predicates are in scope.
2. **Store** the operator-authored `SearchPredicateDto` somewhere durable (asset bytes, JSON, watches file, your own component).
3. **Compile once** via `IPredicateCompiler.CompileComponentPredicate(dto)` (and / or `IEventScannerCompiler.CompileScanner(dto)`); cache the delegate.
4. **Re-compile** on hot reload (`AiHotReloadCoordinator.OnReloadCompleted`) and on edit (commit from the inspector session).
5. **Evaluate** the delegate against the right `(EntityRepository, Entity)` pair on the cadence you need. Reuse buffers; avoid lambdas in the call site.
6. **Edge-detect** at the call site if you need transition semantics — the compiled delegate is stateless.
7. **Author** through a `StructEdit`-hosted panel rooted in your DTO; the polymorphic discriminator + reflection do the rest. Mark structurally-locked branches with `CompoundPredicateDto.ReadOnlyChildIndices`.
8. **Lock-icon** any compound branches you don't want operators to drift, via `ReadOnlyChildIndices` (today, render this as disabled rows yourself; the shared panel's enforcement of this attribute is in flight).
9. **Persist** to JSON when needed — no custom converters required.

---

## 10. File map

| Layer | Project / file |
|---|---|
| DTOs | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` |
| Entity-data compiler | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs` + `IPredicateCompiler.cs` |
| Event-bus compiler | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/EventScannerCompiler.cs` + `IEventScannerCompiler.cs` |
| StructEdit (UI core) | `FDP/ExtDeps/StructEdit/` |
| Specialised drawers | `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/Drawers/` (PropertyPath, FilteredTypeCombo, BehaviorHash, BoundingBox, PredicateValue) |
| Replay-browser host (reference UI) | `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` |
| Universal-Breakpoints host (reference UI) | `Hrot/Engine/Hrot.Presentation/Panels/Breakpoints/DataBreakpointManagerPanel.cs` |
| Universal-Breakpoints manager (compile + evaluate + edge-detect reference) | `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` |
| Trace-buffer components | `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BTreeTraceWorkingMemory1024.cs` (HSM sibling alongside) |
| Blueprint partition allocator | `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` |
