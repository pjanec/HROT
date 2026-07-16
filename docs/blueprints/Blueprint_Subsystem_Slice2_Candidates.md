# Blueprint Subsystem — Slice 2 Candidates and Forward-Look

> **Status:** Forward-looking sketch. NOT a design. Captures everything flagged as "Slice 2" or deferred across the Slice 1 design corpus (148 references across 17 documents).
> **Purpose:** Three uses — (1) checkpoint that Slice 1's architectural decisions extend cleanly to obvious Slice 2 needs, (2) backlog so deferred items aren't forgotten during Slice 1 implementation, (3) sense of "where this is heading" for engine team and AI-behaviors author.
> **What this is NOT:** an architecture document, a design with reviewable code shapes, or a commitment to specific Slice 2 contents. The actual Slice 2 architecture pass happens after Slice 1 ships, when implementation telemetry can inform priorities.
> **Caveats:** Every item here was sketched before Slice 1 was built. Some will turn out to be wrong priorities; some will turn out trivial; some will turn out to need different shape than imagined. Telemetry will sort them.

---

## Table of Contents

1. Reading guide and forecast confidence
2. Theme A — Authoring scale and ergonomics
3. Theme B — Capability surface
4. Theme C — Performance and runtime polish
5. Theme D — Debugging depth
6. Theme E — Architecture extensions
7. Theme F — Catalog evolution to attribute-driven
8. Theme G — Editor UX polish
9. Theme H — Operational concerns
10. Cross-theme dependencies
11. What Slice 1 explicitly preserves for Slice 2

---

## 1. Reading guide and forecast confidence

Each item below carries a tag indicating how confident I am that it'll actually land in Slice 2:

- **[HIGH]** — Architectural design space is mapped; Slice 1 explicitly preserves the shape. Almost certain to ship in Slice 2 if telemetry supports it.
- **[MED]** — Reasonable Slice 2 candidate; some design work needed; may shift in scope based on what users do with Slice 1.
- **[LOW]** — Speculative or "if telemetry shows X is painful, do Y." Listed for completeness; may not survive prioritization.

Items also carry a rough size:
- **[XS]** — under a week, single dev.
- **[S]** — a week or two.
- **[M]** — multi-week or multi-dev.
- **[L]** — a milestone-sized effort comparable to one of Slice 1's larger DDs.

These are gut-feel pre-implementation estimates; treat them as priorities for prioritization, not commitments.

---

## 2. Theme A — Authoring scale and ergonomics

### A1. Cross-entity event dispatcher binding **[HIGH | M]**

The single most important deferred capability. Slice 1 says: "Blueprint A on entity X cannot subscribe to Blueprint B on entity Y's custom event; same-self dispatchers only."

Slice 2 routes cross-entity events through the engine's deferred event bus. The architect ruled in early design rounds: cross-entity must cross a frame boundary (same-frame sync writes from a Blueprint on entity A to entity B would violate the ECS parallel-execution model). The trade-off: cross-entity dispatch loses Unreal-style same-frame execution but gains correctness.

Architecture sketch (already locked in early design discussions):
- One generic `BlueprintDeferredEvent` struct: `(Entity target, ulong eventNameHash, byte[16] inlinePayload)`. No per-custom-event struct generation.
- Reserved `[EventId]` value in the engine event registry (engine-side change).
- Compiler emits `ecb.PublishEvent(new BlueprintDeferredEvent { ... })` for cross-entity binds; runtime dispatcher routes by `eventNameHash`.

Why this is HIGH: every architecture review of Slice 1 referenced it; the design space is well-mapped; it's the obvious next capability.

### A2. Latent execution in BehaviorAction graphs **[MED | S]**

Slice 1's AiPrimitive *conditions* cannot be latent (the validator rejects Wait nodes in Condition-intent graphs). For Slice 2, BehaviorAction graphs hosted via BTreeAction could also opt into latent Wait semantics, mapping cleanly onto BTree's existing `Running` return value. Architecturally simple — just a validator relaxation plus the same phase-byte lowering Slice 1 already uses for AiPrimitive Actions.

### A3. Macro nodes and graph inlining **[MED | M]**

Authoring convenience: a user-defined "macro" is a named sub-graph that can be dropped into other graphs as a single node. The compiler inlines macros at Stage 3 (Normalize) — Compiler DD §6 already has the "macro expansion" hook stub.

Macros don't change the runtime model; they're a pre-compile transformation. Slice 1 reserved the slot but didn't implement it.

### A4. Collapsed-graph inlining (refactoring tool) **[MED | S]**

The inverse of A3: select a region of a graph, "collapse" into a sub-graph, replace with a single node. Authoring-time refactoring; no compiler impact since it's editor-driven. Useful when graphs get large.

### A5. Cross-asset rename and refactoring **[MED | M]**

If a user renames a variable in asset A, references from asset B's `CallPeerBlueprint` should update automatically. Currently the user must hand-edit. Slice 2 adds:
- Refactor-rename across `.bp.json` files (find all references, update atomically).
- Promote-local-to-shared (extract a variable from one Blueprint into a shared definition).
- Inline-shared (the inverse).

### A6. Multi-Blueprint Quick Reload **[HIGH | S]**

Slice 1 ships per-asset Quick Reload only. If the user has 5 dirty assets and wants to reload them all atomically, they currently do 5 separate Quick Reloads. Slice 2 compiles the dirty set into one combined assembly + applies via a single `coordinator.ApplyQuickReload` call. Editor DD §10.6 sketched this.

### A7. Multi-graph-editor mode **[MED | S]**

Slice 1 allows one Graph Editor showing one graph at a time. Slice 2 allows multiple Graph Editor instances showing different graphs simultaneously. Requires per-window selection state (currently in `EditorSelectionStore` as shared singletons). Editor DD §2.5 sketched the constraint.

### A8. Search and navigation across assets **[MED | S]**

"Find all uses of channel command MoveTo" / "Find all assets that call asset X" / "Find all variables named CurrentHealth". A search index over the asset catalog plus an editor UI surface. Editor DD §1.6 explicitly out-of-scope for Slice 1.

### A9. Construction script (graph kind) **[LOW | S]**

Compiler DD §3 references a `GraphKind.Construction` deferred to Slice 2. Concept borrowed from Unreal: a script that runs once when an entity is constructed, separate from BeginPlay. Slice 1 doesn't need it — BeginPlay covers the common case. Slice 2 only if telemetry shows a need.

### A10. Refactor: promote-local-to-shared / collapse-into-macro **[MED | M]**

Authoring tools that perform structural transformations across multiple assets. No compiler impact; pure editor concern. Architecture v1.2 §14 listed.

### A11. Visual conceptual documentation for the shared-state / working-state model **[HIGH | S]**

> **Raised by the user (2026-07-16) from hands-on Windows testing of the composed-blueprint + GetShared/SetShared authoring path.** Verbatim concern: *"all the variable and scope and default values and GetShared/SetShared and params vs working state, this is a lot to digest for a user … beyond the capability of an ordinary user (needs a programmer mindset). Without [visual documentation] the system is incomprehensible."*

The Slice-1/Slice-2 authoring surface exposes several concepts that are individually reasonable but collectively opaque to a non-programmer author:

- **Params vs WorkingState** — sync-in inputs (baked into `BehaviorParameters`, `Role=Input`) versus per-tick mutable state (`Role=State`), and why a composed node auto-creates *two* blackboard variables.
- **`WorkingStateScope` = Node / Behavior / Entity** — private-per-node vs shared-across-co-bound-nodes vs shared-across-behavior-switches/entities, and the slot-key math each implies (`FNV(assetId++nodeVisualId)` / `FNV(assetId++variableId)` / `FNV(variableId)`).
- **`GetShared`/`SetShared`** — the second-slot accessor, its named-`variableId` keying, the `[BlackboardDtoStruct]` Category-1 shared struct contract, cross-entity read (target-`Entity` pin), and the owner-provisions / members-read / not-ready→`false` protocol.
- **Default values** — where blueprint Param defaults come from vs host-BTree variable defaults.

**Deliverable:** author-facing conceptual documentation that leans on **visuals (SVG and/or Mermaid diagrams, schemas, memory-layout illustrations)** rather than prose. Candidate diagrams:
1. A memory-layout schematic: entity → `BrainBlackboard.BehaviorParameters` (Params, Input) vs `BlueprintBlackboard{1024,4096,16384}` partition slots (WorkingState, State), keyed by scope.
2. A scope decision tree / matrix: "I want state that is private to this node / shared between these nodes / shared with another entity → pick this Scope."
3. A `GetShared`/`SetShared` data-flow diagram across two entities (commander provisions, member reads, ≤1-frame latency).
4. A "Params vs WorkingState" side-by-side (sync-in vs per-tick-mutable, who writes, when it resets).
5. The end-to-end authoring flow: author blueprint → host in BTree as action/condition → bind + scope the WorkingState variable → optional GetShared/SetShared.

This is **HIGH** because the feature is now shipped and Windows-verified but effectively unusable by the target (non-programmer) author without it — comprehensibility, not capability, is the current blocker. Sized **[S]**: documentation + diagrams, no code. Diagrams should live under `docs/blueprints/` and be embeddable in the editor's help surface later. *(Chat pending: agree on diagram set + tooling — Mermaid inline in Markdown vs hand-authored SVG — during a long-running background task.)*

---

## 3. Theme B — Capability surface

### B1. Map/Set containers as variable types **[MED | M]**

Slice 1 supports unmanaged value types as Blueprint variables (`int`, `float`, `Vector3`, `Entity`, etc.). Slice 2 may add `Dictionary<K,V>` and `HashSet<T>` for select unmanaged K/V combinations. Architecturally a validator + emitter change; non-trivial because the storage layout has to accommodate managed-collection-like semantics in an unmanaged component.

Possible Slice 2 approach: use existing engine map/set primitives if they exist; otherwise defer further.

### B2. String formatting as a coercion path **[LOW | XS]**

Compiler DD §4 mentions Slice 2 could add string formatting (numeric → string) as an implicit cast in TypeResolve. Slice 1 has no string variables at all; this would land when strings do.

### B3. Polymorphic pure nodes **[LOW | S]**

Compiler DD mentions: Slice 2 may expand wildcard handling to support `Math.Op(T, T) → T` polymorphic pure-function nodes (e.g., generic Add that works on int/float/Vector3). Currently every overload is a separate catalog entry.

### B4. Async asset loads as a latent primitive **[LOW | M]**

The `BlueprintLatentCursor.WaitEventMask` field has a Slice 2 marker for async asset loads ("wait until this asset finishes loading"). Speculative — depends on whether async loading becomes a common Blueprint use case.

### B5. WaitForEvent latent primitive **[MED | S]**

A general "wait until any subscribed event fires" primitive in latent execution. Slice 1's `WaitForChannel` is the channel-specific version; Slice 2 generalizes. The `WaitEventMask` field on `BlueprintLatentCursor` is already there for this.

### B6. UI/Presentation-phase Blueprints **[LOW | M]**

Slice 1's `BlueprintTickSystem` runs in `SystemPhase.Simulation`. The architect mentioned in early review: if Slice 2 needs UI/Presentation-phase Blueprints (for HUD logic, in-world UI), introduce a separate `BlueprintPresentationTickSystem` tagged with `SystemPhase.Presentation` that queries only assets explicitly authored for that phase.

Speculative; depends on game's needs.

### B7. Animation Blueprints **[LOW | L]**

Architecture v1.2 §14 lists animation Blueprints as Slice 2/3. Their own design pass — likely needs its own state-machine layer plus animation-specific node kinds. Out of scope for any near-term planning.

### B8. RPC/multiplayer Blueprints **[LOW | L]**

Same as B7 — listed in §14 but not yet in any concrete planning. Replay-safety is what Slice 1 got correct; full networking is much more.

---

## 4. Theme C — Performance and runtime polish

### C1. AiPrimitive concurrent working-state per entity **[HIGH | M]**

> **Note:** the original wording of this item conflated two different `Blackboard1024` components and over-stated the constraint. The corrected framing below is narrower and more precise.
>
> **Design pass (2026-06):** the per-node case is realized by Slice-2 (`BTree_AiActionParameterBinding_Detailed_Design.md §4.1–4.3`); its generalization to **scoped local/shared working state** (`Node`/`Behavior`/`Entity`, plus the `GetShared/GetSharedRW` accessor for cross-entity/commander sharing) is designed in **`§4.4`** of that doc — which also subsumes §A10 (promote-local-to-shared). MVP = `Behavior` scope. Pending architect review.

#### The two `Blackboard1024`s in play

The system has two ECS components with similar names and overlapping size, but different ownership and different roles:

| Component | Owner | Purpose | Has partition allocator? |
|---|---|---|---|
| **`Blackboard1024`** *(engine type, no prefix)* | FastHSM kernel + BTree kernel | Per-entity scratchpad for HSM `AiActivity` working state and BTree node working state. Single-typed projection slot. | **No.** |
| **`BlueprintBlackboard1024`** / `BlueprintBlackboard4096` / `BlueprintBlackboard16384` *(Blueprint-owned)* | Blueprint subsystem (Runtime DD §4) | Instance dispatch storage; tiered allocator across three component types. | **Yes** — full partition allocator already in Slice 1. |

These are different ECS components with different ComponentIds and coexist on the same entity. A character can simultaneously carry:
- The engine's `Blackboard1024` (holding e.g. HSM state for its current `AiActivity`).
- A `BlueprintBlackboard1024` (holding three Instance Blueprints via the partition allocator).

#### What's not constrained in Slice 1

Three things people might intuit as "blocked" — they're not:

- **Stacking Instance Blueprints on one entity.** Multiple Instance Blueprints attached to a single entity, each with its own state, calling each other via `CallPeerBlueprint`. This works in Slice 1 already, using `BlueprintBlackboard*`'s partition allocator. No constraint to lift.
- **Calling Blueprints from BTree or HSM.** AiPrimitive Blueprints are designed to be hostable as `BTreeAction` / `BTreeCondition` / `HsmAction` / `HsmGuard`. The host kernel invokes the thunk; the thunk runs. Slice 1 capability.
- **Mixing Instance and AiPrimitive Blueprints on one entity.** Different storage components, no conflict.

#### What IS constrained in Slice 1

Specifically: **only one AiPrimitive working-state Blueprint can be simultaneously active per entity.**

The reason: an AiPrimitive Blueprint hosted as `HsmAction` or `BTreeAction` doesn't use `BlueprintBlackboard*`. Its generated thunk projects working state **inline over the engine's `Blackboard1024`**, which is where HSM and BTree already keep their per-entity activity state. From Runtime DD §13.5:

> AiPrimitive working state lives in `Blackboard1024`, not in `BlueprintBlackboard*`. The reconciliation logic is different — it's *inline* in the generated thunk.

This was a deliberate Slice 1 economy: piggyback on the slot HSM/BTree already provide, avoid touching the engine's component layout. The cost is that `Blackboard1024` is single-typed — if two AiPrimitives wrote to it simultaneously, they'd trash each other's bytes. The 8-byte `StructureHash` header at offset 0 detects mismatched layout and re-initializes, which makes single-active-AiPrimitive safe but two-concurrent unsafe.

In practice Slice 1 lives with this because:
- HSM has only one active `AiActivity` at a time per state (the kernel guarantees this).
- BTree conditions are typically stateless evaluators (no working state to collide).
- Most AI behaviors don't need two stateful AiPrimitive Blueprints competing on one entity.

#### What Slice 2 should do (corrected)

The right Slice 2 move is **not** to retrofit a partition allocator onto the engine's `Blackboard1024`. That component is the engine's own, used by HSM and BTree internals; changing its layout would ripple into the FastHSM kernel and BTree kernel. Wrong layer.

Instead: **move AiPrimitive working state into a Blueprint-owned component**, parallel to `BlueprintBlackboard*` but used by the BTree/HSM-hosted thunk path. Either:

- **Option α** — new component `BlueprintAiWorking1024` (size mirroring engine's `Blackboard1024`), with the partition allocator already designed in Slice 1. *(rejected)*
- **Option β** — merge into existing `BlueprintBlackboard*` tiers, making the AiPrimitive thunks lookup their slot the same way Instance dispatch does. *(chosen)*

> ✅ **RESOLVED (architect + user, 2026-06-15): Option β, approved & designed.** Per-node working-slot key = `FNV-1a(BehaviorAssetId, NodeVisualId)`, baked into the per-node adapter thunk. Three mandated fixes: tier-upgrade race → synchronous `Input`-phase provisioning; hot-reload ghost slot → re-publish `AssignBehaviorEvent` (not inline `ResetSlot`); concurrent stateful Subtree → cross-region validator hard-error. Full design: **`BTree_AiActionParameterBinding_Detailed_Design.md` §4**.

Either way, the engine's `Blackboard1024` is **not modified**. The change is entirely Blueprint-side:

1. Add a Blueprint-owned working-state component (or extend `BlueprintBlackboard*` usage).
2. Modify the AiPrimitive emit template (Compiler DD §10.4) to project over the new component using a partition-allocator slot lookup, rather than projecting over `Blackboard1024.Memory` at offset 8.
3. The thunk has the entity reference already (`HsmKernelBridge.Self` or `BTreeContext.Self`); it does a partition-allocator lookup to find its slot before projecting.

Architectural cost: one extra component-lookup per AiPrimitive tick (negligible; same dictionary path used by Instance). The HSM kernel and BTree kernel are unchanged — they still hand a `Blackboard1024*` to the thunk; the thunk just chooses to ignore it for working-state purposes and use the Blueprint-owned component instead.

#### Forward-compatibility already in Slice 1

The Slice 1 helper API was intentionally shaped to extend:

```csharp
// Slice 1 signature, designed to be forward-compatible:
public bool TryGetAiPrimitiveWorkingState<T>(Entity self, int blueprintId, out Span<byte> bytes);
// Slice 2 implementation adds multi-slot support via the partition allocator;
// signature unchanged. The blueprintId disambiguates which slot to return.
```

Slice 1 implements this by reading directly from `Blackboard1024.Memory` after checking the structure hash. Slice 2's implementation does a partition-table lookup keyed by `blueprintId`, returning the right slot's bytes.

The generated thunk's projection code changes shape, but the runtime API the test harness and editor consume stays the same.

### C2. Partition allocator defragmentation **[MED | S]**

`BlueprintBlackboardPartitions` uses first-fit with coalescing in Slice 1. If telemetry shows fragmentation pain (entities running out of slot capacity despite having free bytes), Slice 2 adds an on-demand defragmentation pass that:
- Walks the slot table.
- Slides allocated slots toward the start of the payload.
- Updates slot table offsets.
- Coalesces all free space at the end.

The header's `PayloadHighWater` field was added in Slice 1 specifically to support this — Runtime DD §4.3.

### C3. Blackboard tier downgrade **[LOW | S]**

Slice 1 only upgrades tiers (1024 → 4096 → 16384), never downgrades. If an entity sheds Blueprints and stays oversized, no reclamation happens. Slice 2 could add downgrade if telemetry shows long-lived entities with persistently underutilized tiers. Runtime DD §7.6 noted.

### C4. Per-Blueprint runtime profiling **[MED | S]**

Slice 1 reports system-level `BlueprintTickSystem` timing only. Slice 2 may add lightweight stopwatch instrumentation per Blueprint per tick, gated behind a developer-mode toggle, surfaced in the editor's debug panels. Runtime DD §6.9, §12.5.

### C5. Zero-allocation `GetAllWorldSingletons` **[LOW | XS]**

Slice 1 accepts one small allocation per frame on this path (per Runtime DD §10.2, ~16 bytes per frame for a list of ≤3 entries). Slice 2 replaces with a struct enumerator pattern to hit strict 0-bytes/frame steady state. Runtime DD §10.3.

### C6. Background-thread compile for Quick Reload **[MED | S]**

Slice 1 runs the full Quick Reload pipeline on the main thread (~100ms latency, bounded but visible as a frame-rate dip). Slice 2 moves Stages 1-7 to a background task; main thread only does Stage 8 (Roslyn) + ALC load + `ApplyQuickReload`. Reduces main-thread cost to ~60ms. Editor DD §10.4.

### C7. Programmatic MSBuild API **[LOW | XS]**

Slice 1 uses `Process.Start("dotnet build")` for Full Rebuild (~3 seconds). Slice 2 may switch to `Microsoft.Build.Locator` API for sub-second rebuild. Editor DD §16.2. Implementation churn risk on the MSBuild API is the main cost.

### C8. Source-generator-cached signature catalog **[LOW | XS]**

`IAssetCatalog.EnumerateAll()` walks the file system on every call. Editor DD Inline Patches Patch 1 notes that Slice 2 could cache parsed signatures with file-mtime checks. Only relevant for projects with thousands of `.bp.json` files.

### C9. Per-asset PDB toggle **[LOW | XS]**

Slice 1: PDB loading is all-or-nothing on the coordinator. Slice 2: editor controls PDB emission per-asset, so only assets the author is actively debugging carry the debug overhead. Hot Reload DD §8.6.

### C10. HSM dispatcher snapshot for true rollback **[LOW | XS]**

Slice 1 hot-reload failure policy: log + accept temporary HSM dysfunction until next successful reload. Slice 2 could snapshot `HsmActionDispatcher` state before `ClearAll`, restore on failure. Hot Reload DD §6.5, §11.3. Implementation is straightforward (~100s of bytes of state); cost-vs-complexity favors deferral until needed.

---

## 5. Theme D — Debugging depth (Universal Breakpoints)

> **Note:** this theme was substantially reshaped after a Slice 1 design conversation with the architect. The original entries listed conditional breakpoints (D1), pin-value evaluation (D6), and live state editing (D7) as three separate items sharing an expression-evaluator dependency. The architect's response collapsed these into one unified architecture — **Universal Breakpoints** — built on engine primitives that already exist (the replay-search predicate compiler, event scanner compiler, and `EntityRepository.SyncFrom` snapshot machinery). The new design also resolves the soft-pause one-tick-drift problem from Slice 1 via a forward-snapshot pattern with deferred mutation. This section is rewritten to reflect the unified design.

### D1. Universal Breakpoints **[HIGH | M-L]**

A single feature that subsumes D1, D6, D7, and parts of D10 from the original sketch. Built by wiring together five existing engine primitives plus one new orchestration system.

#### What it gives users

The Slice 2 debugger surface becomes:

- **Predicate breakpoints over any component data.** Pause when `CurrentHealth < 10` on any entity. Pause when `Locomotion.ActiveAction == MoveTo`. Predicates compile via `IPredicateCompiler` from `SearchPredicateDto` (the same JSON shape the Replay Browser already accepts).
- **Event breakpoints over the transient event bus.** Pause when `HitEvent` fires with `Damage > 50`. Pause when `DamageAssessedEvent` is published for entity 42. Compiled via `EventScannerCompiler` into `EventScannerDelegate`.
- **Blueprint node breakpoints (Slice 1's narrow case).** Pause when `BlueprintLatentCursor.NodeIdAtEntry == 'specific-node-guid'`. This is now just one specific predicate shape on top of the universal substrate — the Slice 1 Debug Protocol DD's surface (structure-hash safety check, breakpoint reconciliation, etc.) continues to apply but as a refinement, not a separate system.
- **Genuine pre-execution pause via forward-snapshot rewind.** Click a node-level breakpoint, see the state from *before* the breakpoint tick — not Slice 1's "one tick later" drift. Click Step and time advances naturally with no resimulation.
- **Live state editing with deferred mutation.** Edit a value while paused; the edit applies on the next tick via the standard `EntityCommandBuffer` write path. The breakpoint tick itself is never re-run.

#### The five engine primitives this reuses

| Primitive | Slice 2 use |
|---|---|
| `IPredicateCompiler` | Compiles `SearchPredicateDto` → `Func<EntityRepository, Entity, bool>` for component data breakpoints. Already built for Replay Browser; zero new compiler work. |
| `EventScannerCompiler` | Compiles `TransientEventPredicateDto` → `EventScannerDelegate` for event breakpoints. Reads `FdpEventBus` double-buffers directly. |
| `EntityRepository.QueryDelta` | Chunk-versioning-aware iteration that skips untouched chunks. Lets the breakpoint system check only entities whose components changed this tick, keeping evaluation within frame budget. |
| `EntityRepository.SyncFrom` | Unmanaged memory copy for snapshot capture. ~2ms per full-world snapshot — invisible since engine halts on breakpoint anyway. |
| `EntityCommandBuffer` (`SetComponentRaw` / `SetManagedComponentRaw`) | Carries deferred mutations from the inspector into the next tick. Standard ECB write path, no new mutation channel needed. |

#### The triple-buffer pause architecture

When a breakpoint predicate evaluates `true`:

```
Tick N (during PostSimulation phase):
  ├─ DataBreakpointSystem.Execute via QueryDelta
  │    ├─ Predicate fires on entity 42
  │    │    ├─ _postTickSnapshot.SyncFrom(_liveRepo)   ← capture exact tick-N-end state
  │    │    ├─ _liveRepo.SyncFrom(_preTickSnapshot)    ← rewind to tick-N-start state
  │    │    ├─ timeController.RequestPause()
  │    │    └─ Return — no thread block
  └─ Frame N completes; engine halts at frame boundary

Editor UI:
  └─ Now inspecting _liveRepo (which is the pre-tick state)
  └─ User sees genuine pre-execution state
```

Three repository states held during pause:

| Repo | Contents | Role |
|---|---|---|
| `_preTickSnapshot` | World state at start of Tick N | Continuous snapshot maintained every tick by `DebugSnapshotProvider`. |
| `_postTickSnapshot` | World state at end of Tick N (captured at breakpoint moment) | The "save point" for clean restoration. |
| `_liveRepo` | Currently set to `_preTickSnapshot`'s contents | What the user inspects in the editor. |

#### The clean step (observation-only path — 99% of debugging)

User clicks Step or Continue after only observing (`IsDirty == false`):

1. `_liveRepo.SyncFrom(_postTickSnapshot)` — byte-for-byte restoration of the exact tick-N-end state.
2. `timeController.RequestResume()` (or `RequestStepOneTick`) — engine advances normally.
3. Done. Zero resimulation. Zero replay logic. Zero risk of determinism drift.

**This is the key insight.** Resimulating tick N would have been risky because components flagged `DataPolicy.NoRecord` or `DataPolicy.NoSnapshot` can't be perfectly restored from snapshot — the replay could diverge from the original frame that triggered the breakpoint. By forward-snapshotting the post-tick state, we don't need to know how to replay; we just need to remember what the outcome was.

#### The dirty step (mutation path — handled via deferred mutation, no resimulation)

User edits a value while paused (`IsDirty == true` from `StructEdit`):

1. The edit is captured as a pending mutation in a `_pendingDebugMutations` queue. It does **not** mutate `_liveRepo` (which is read-only-conceptually while inspecting the rewound state).
2. User clicks Step or Continue.
3. `_liveRepo.SyncFrom(_postTickSnapshot)` — same clean restoration as the observation path.
4. The pending mutations are drained into the `EntityCommandBuffer` (or written directly to `_liveRepo` between ticks).
5. `timeController.RequestStepOneTick()` — engine ticks forward.
6. The mutations take effect at the boundary of tick N+1.

**Trade-off:** the user's edit doesn't apply *during* tick N retroactively; it applies at the start of tick N+1. For 99% of debugging cases ("give this entity 1000 health to see if it survives the next attack", "flip this flag to force a state transition") this 1-tick latency is unnoticeable and matches the intuitive mental model of "I'm setting this up for the next moment of simulation."

**What we gain:** complete elimination of the resimulation path. No `EventAccumulator` event injection. No `DataPolicy` divergence risk. No physics rewind. The debugger becomes a pure observer plus a deferred-write surface.

#### Why this is safe for the Flight Recorder

The `AsyncRecorder` and `RecorderTickSystem` need **no awareness** of the debugger. Why:

- Tick N is naturally simulated and recorded once, before the breakpoint hits.
- The pause halts time advancement but doesn't re-run any tick.
- The clean step restores `_postTickSnapshot` (which equals what was already recorded) and continues. The recorder sees a linear chronology: tick N, then time stops, then tick N+1. From the recorder's perspective the developer's pause is invisible.
- Deferred mutations appear as standard ECB writes at the boundary of tick N+1 — exactly as if any other system had requested them. The recorder captures them as a normal delta between tick N and tick N+1.

No duplicate frames. No rollback of the write head. No suspension of the recorder. The `.fdp` files remain perfectly valid with zero extra logic. This is the architectural payoff of the forward-snapshot approach beyond debugging safety.

#### The orchestration: DataBreakpointSystem + DebugSnapshotProvider

Two new engine-level services:

```csharp
public sealed class DataBreakpointSystem : IEcsModuleSystem
{
    // Runs in SystemPhase.PostSimulation, AFTER all simulation systems write
    public void Execute(EntityRepository repo, /* ... */)
    {
        if (_breakpoints.Count == 0) return;   // gate: zero cost when nothing's set

        foreach (var bp in _breakpoints)
        {
            switch (bp)
            {
                case ComponentDataBreakpoint cdb:
                    EvaluatePredicateBreakpoint(repo, cdb);  // uses QueryDelta
                    break;
                case EventBreakpoint eb:
                    EvaluateEventBreakpoint(repo, eb);       // scans FdpEventBus
                    break;
            }
        }
    }
}

public sealed class DebugSnapshotProvider : IEcsModuleSystem
{
    // Runs at the very start of each tick (SystemPhase.PreSimulation, first)
    public void Execute(EntityRepository repo, /* ... */)
    {
        if (!_anyBreakpointActive) return;   // gate: zero cost without breakpoints
        _preTickSnapshot.SyncFrom(repo);
    }
}
```

The cost-gating matters: when no breakpoints are set, `DebugSnapshotProvider` skips the snapshot and `DataBreakpointSystem` skips evaluation. Production frame cost stays at zero. When any breakpoint is set across any subsystem, every tick pays ~2ms for the snapshot — but only sessions actively debugging incur this cost.

#### The IBlueprintTimeController interface generalizes

Slice 1's `IBlueprintTimeController` should be renamed to `IEngineDebugTimeController` (or similar) and gains rewind-aware methods:

```csharp
public interface IEngineDebugTimeController
{
    void RequestPause();
    void RequestResume();
    void RequestStepOneTick();

    // New for Slice 2:
    void BeginObservationalRewind();   // signals "we're now showing rewound state"
    void EndObservationalRewind();     // signals "we've restored and are advancing"
    bool IsInRewoundState { get; }
}
```

The Blueprint subsystem becomes one of multiple subscribers — same as physics breakpoints, scenario-system breakpoints, or any other ECS subsystem.

#### Compatibility with Slice 1

The Slice 1 Debug Protocol DD's structure-hash safety check, breakpoint reconciliation across hot reload, debug map indexing, etc. — all of it stays. Blueprint node breakpoints become a specific predicate shape (`BlueprintLatentCursor.NodeIdAtEntry == specific-guid`) on top of the universal substrate, but the surface the editor presents is unchanged. The user's existing breakpoint list, callstack window, watch panel — all keep working. Universal Breakpoints adds capability rather than replacing.

#### What's still in scope for Slice 2 within this theme

These items from the original Theme D survive as smaller follow-ups to the universal breakpoint architecture:

- **D2. Watch persistence [MED | XS]** — persist watches to `watches.json`. Independent of universal breakpoints; trivial.
- **D4. Multi-debugger multiplexing [LOW | XS]** — `MultiplexingProbeSink` for multiple subscribers to `DebugProbe.Sink`. Still useful alongside the new system.
- **D5. Stack-frame inspection during pause [MED | S]** — click a callstack frame to see state from that frame's perspective. Compatible with universal breakpoints (the predicate-driven pause carries a callstack snapshot just like Blueprint-node pause).
- **D8. CLR-debugger sync [LOW | S]** — sync Blueprint breakpoints to Visual Studio source-line breakpoints. Independent of universal breakpoints.
- **D9. Pause on Blueprint exception [LOW | XS]** — separate concern; predicate-style breakpoints don't cover exceptions directly. Still listed.
- **D11. "Step abandoned due to reload" notification [LOW | XS]** — UX polish.
- **D12. Auto-rebind breakpoint on structure-compatible reload [LOW | XS]** — UX polish.

#### What's dropped from the original Theme D

These items are subsumed by Universal Breakpoints (D1) and no longer need separate work:

- ~~D6. Pin-value evaluation at pause without committing~~ — the predicate compiler can evaluate arbitrary expressions over current state without committing. Free with D1.
- ~~D7. Live state editing~~ — deferred mutation via ECB is part of D1's design.
- ~~D10. Conditional breakpoints on pin-value changes~~ — covered by component data predicates.
- ~~D3. Step-into across peer calls (full mid-tick precision)~~ — the rewind-to-pre-tick pause gives genuine pre-execution inspection, removing the original motivation for the more dangerous Option B re-entrant render pump. Step-into semantics still apply at the tick-boundary granularity established in Slice 1, but inspection precision is now what users actually wanted.

The collapse from "many separate items each needing an expression evaluator" to "one architecture reusing the predicate compiler" is the architectural advance.

---

## 6. Theme E — Architecture extensions

### E1. Cross-instance event-dispatcher binding **[HIGH | M]**

See A1. The Slice 2 cross-entity dispatch is the most-cited single deferred capability across all docs.

### E2. Save/load typed access for instance state **[MED | S]**

Slice 1 already serializes `BlueprintBlackboard*` bytes via the engine's scenario serializer (the bytes are part of the entity's component state, save-game-ready automatically). What's missing: typed read of those bytes from save files, e.g., "give me the CurrentHealth value of the entity I saved 30 minutes ago." Architectural cost: the scenario serializer needs to know which Blueprint owns which slot, and the StructureHash should match the running version.

### E3. Multiple world-singleton Blueprints per tier **[MED | XS]**

Slice 1: one world-singleton Blueprint per tier (3 total: one each for 1024/4096/16384). Slice 2 lifts via the partition allocator's normal slot-table mechanics on the singleton component. Trivial extension; mostly a constraint relaxation in `BlueprintRegistry.RegisterWorldSingleton`. Architecture v1.2 §6.8.

### E4. Worker-thread Blueprint dispatch **[LOW | L]**

Slice 1 is single-threaded simulation. If Slice 2 introduces worker-thread parallel chunk iteration, Blueprints need:
- `Volatile.Read` / `MemoryBarrier` around the registry snapshot swap.
- Thread-safe `DebugProbe.Sink` access.
- Per-chunk parallel-safety for `BlueprintTickSystem`.

Architecturally invasive. Architecture v1.2 §14 explicitly deferred. Probably Slice 3+ rather than Slice 2.

### E5. Network-replicable `BlueprintBlackboard*` **[LOW | M]**

Slice 1 says "brain-role only" — networking is out of scope. Slice 2 may want network replication of Blueprint state for multiplayer-AI scenarios. Runtime DD §4.8 notes the design is replication-friendly (fixed-byte buffer, no managed refs). Surface still needs per-Blueprint replication policy + delta-encoding strategy.

### E6. Construction script integration with scenario load **[LOW | S]**

Per A9 — if construction scripts arrive, they need a hook into the engine's scenario-load pipeline. Compiler DD §3.

### E7. Engine-level pre-unload hook **[LOW | XS]**

`AiHotReloadCoordinator.OnBeforeUnload` event. Slice 1 doesn't need it (state lives in stable components). Slice 2 may need it if Blueprint metadata caches (e.g., parsed graph DOMs for debugger UI) need to drop cleanly. Hot Reload DD references.

---

## 7. Theme F — Catalog evolution to attribute-driven

### F1. `[BlueprintExposedEvent]` attribute **[HIGH | S]**

Slice 1's `EngineEventCatalog` is hand-curated. Slice 2's source generator scans loaded assemblies for `[BlueprintExposedEvent]` on engine event types and builds the catalog automatically. No compiler changes needed — the catalog interface stays the same; only its construction path changes.

The attribute itself is already defined in Slice 1 (per Runtime DD §1.3 module layout). Slice 1 declares it; Slice 2 wires it.

### F2. `[BlueprintExposedChannelCommand]` attribute **[HIGH | S]**

Same pattern for the channel command catalog. Engine-side teams add the attribute to channel types; the catalog builds itself. Slice 1 hand-curates; Slice 2 generates.

### F3. `[BlueprintExposedWaitPrimitive]` attribute **[MED | XS]**

Same pattern for the wait primitive catalog. Smaller surface — Slice 1 has few wait primitives. Slice 2 brings parity with F1/F2.

### F4. Auto-rebuild catalog on engine assembly reload **[LOW | S]**

If the engine team adds a new channel command and rebuilds, the catalog should refresh automatically without requiring an editor restart. Editor-side wiring to the hot-reload coordinator's `OnReloadCompleted` event. Easy to do once F1/F2 are in.

---

## 8. Theme G — Editor UX polish

### G1. Dock layout serialization **[MED | S]**

Slice 1: editor opens with default window arrangement every time. Slice 2: persist dock layout in `BlueprintEditorPreferences` so window arrangement survives editor restart. Requires engine's docking-system integration. Editor DD §14.4.

### G2. Custom keybindings **[LOW | S]**

Slice 1 uses engine defaults. Slice 2 lets users customize Blueprint-specific keybindings (Quick Reload, Set Breakpoint, etc.). Requires engine's input-mapping system integration. Editor DD §14.4.

### G3. Graph editor — multi-select + box-select **[MED | S]**

Slice 1: one node selected at a time. Slice 2: multi-select for batch operations (delete N nodes, copy/paste, group). Editor DD §5.1.

### G4. Graph editor — group/comment nodes **[MED | S]**

Visual organization. A group is a labeled rectangle around several nodes; a comment is a text annotation. Doesn't affect execution. Editor DD §5.1.

### G5. Graph editor — link waypoints **[LOW | S]**

Slice 1 draws links as direct bezier curves. Slice 2 may add waypoint nodes to route links manually for cleaner diagrams. Editor DD §5.1.

### G6. Graph editor — minimap **[LOW | S]**

Overview of the whole graph in a corner. Useful for large graphs. Editor DD §5.1.

### G7. Auto-save on Quick Reload **[LOW | XS]**

Slice 1: Quick Reload doesn't write to disk; user must Save & Rebuild separately. Slice 2 may offer an opt-in "auto-save on every Quick Reload" mode. Editor DD §14.1.

### G8. Save-dialog auto-save-on-blur **[LOW | XS]**

Slice 1 prompts manually on asset-switch with unsaved changes. Slice 2 may add auto-save-on-blur. Editor DD §16.6.

### G9. Inspector — pin selection mode **[MED | S]**

Slice 1 inspector shows asset-level or node-level. Slice 2 adds pin-level: click a pin, see its metadata + default literal + type override controls. Editor DD §6.2.

### G10. Asset Browser — search/filter UX **[MED | S]**

Slice 1: tree of folders + assets, no filter. Slice 2 adds a search box at the top filtering by name, kind, or callable peer.

### G11. Find references navigation **[MED | S]**

"Where is this asset used?" — list all assets that reference it via `CallablePeers`. Editor DD §1.6.

---

## 9. Theme H — Operational concerns

### H1. Replay compatibility verification **[HIGH | XS]**

Slice 1 architecture is replay-safe by construction (Blueprint state lives in unmanaged components, ECB ordering is deterministic). Slice 2 should add an automated regression test: record N frames of a scenario, replay, assert bit-identical final state. This is more "validation we did the right thing in Slice 1" than new design. Architecture v1.2 §1.2.

### H2. Save/load round-trip test **[MED | S]**

Test that scenario save → load preserves Blueprint state byte-for-byte. Confirms E2's automatic-via-scenario-serializer claim.

### H3. Asset deletion + reference repair **[MED | S]**

If a user deletes asset A while asset B references it, B's compile fails. Slice 1: user manually fixes B. Slice 2: editor surfaces "you're deleting an asset that's referenced; here are the references" and offers to repair or block deletion. Editor DD §4.8.

### H4. Asset Guid migration **[LOW | S]**

If a user accidentally duplicates a Guid (manual JSON edit, copy-paste), the registry detects at registration time and throws. Slice 1 surfaces the error and the user fixes manually. Slice 2 could add tools to detect and offer to re-Guid one of the colliders. Compiler DD §18.6.

### H5. Live multi-author collaboration **[LOW | L]**

Multiple developers editing the same `.bp.json` simultaneously. Out of scope for any near-term Slice. Listed in Editor DD §1.6.

### H6. Telemetry collection **[MED | S]**

Slice 2 should add lightweight telemetry collection from editor sessions: how often Quick Reload is used vs Full Rebuild, average Quick Reload turnaround, frequency of compile failures by diagnostic code, peak entity counts with Blueprints attached. Informs Slice 3 priorities. No invasive instrumentation; just counters surfaced in a dev-mode dashboard.

---

## 10. Cross-theme dependencies

A few items are prerequisites for others:

- **F1, F2** (attribute-driven catalogs) make **A1** (cross-entity events) easier because new events automatically appear in authoring.
- **C1** (AiPrimitive concurrent working-state) unlocks multi-AiPrimitive-working-state Blueprints per entity, which interacts with A6 (multi-Blueprint Quick Reload) — both want to test "many Blueprints on one entity" stress paths.
- **D1** (conditional breakpoints) depends on having an expression evaluator that's also useful for **D6** (pin-value evaluation at pause without committing) and **D7** (live state editing). One expression-evaluator implementation serves all three.
- **B1** (Map/Set containers) and **D7** (live state editing) both push on the type registry's marshaling surface.
- **E2** (typed save/load) and **H2** (save/load round-trip test) are paired.

If a Slice 2 phasing emerges, an early sub-phase that tackles the expression-evaluator unlocks several D-theme items at once.

### What "stacking Blueprints" actually means (clarification)

A common informal request from users is "I want to stack Blueprints — call one from another, also call them from BTree and HSM." It's worth being precise about which parts of this are Slice 1 capabilities and which are Slice 2:

| Capability | Slice 1? | Notes |
|---|---|---|
| Multiple Instance Blueprints on one entity | ✅ Yes | Each gets its own slot via `BlueprintBlackboard*` partition allocator. |
| One Blueprint calling another via `CallPeerBlueprint` | ✅ Yes | Direct function-graph invocation; caller and callee don't share state. |
| Calling an AiPrimitive Blueprint from BTree | ✅ Yes | AiPrimitive Blueprints can be authored as `BTreeAction` or `BTreeCondition` hostings. |
| Calling an AiPrimitive Blueprint from HSM | ✅ Yes | Same; `HsmAction` and `HsmGuard` hostings. |
| Mixing Instance and AiPrimitive Blueprints on one entity | ✅ Yes | Different storage components (`BlueprintBlackboard*` vs engine's `Blackboard1024`); no conflict. |
| **Multiple AiPrimitive Blueprints with working state, concurrently active on one entity** | ❌ Slice 2 (C1) | Currently share the engine's `Blackboard1024` projection; can only have one active at a time per entity. |

So "stacking" is mostly already there. The narrow piece Slice 2 adds is concurrent-active *AiPrimitive working-state* Blueprints — and the fix is Blueprint-side only, not an engine modification.

---

## 11. What Slice 1 explicitly preserves for Slice 2

Just as important as the candidates list: knowing what Slice 1 was *careful* to keep extensible.

| Slice 1 design choice | What it preserves |
|---|---|
| `BlueprintLatentCursor.WaitEventMask` field | Forward slot for WaitForEvent latent primitive (B5) |
| `BlueprintLatentCursor.WaitUntilTick` field | Forward slot for tick-based waits |
| `BlueprintLatentCursor.ResumeAt = 0` reserved | "No active cursor" sentinel preserved |
| `BlueprintBlackboardHeader` 32-byte size with `Reserved` ulong | Forward room for new metadata without re-versioning |
| Header version byte | Migration path if header layout changes |
| Catalog interfaces (`IEngineEventCatalog`, etc.) | Same interfaces, different implementations for attribute-driven (F1, F2, F3) |
| `DebugProbe.Sink` static field | Replaceable with `MultiplexingProbeSink` (D4) without compiler changes |
| `IBlueprintTimeController` interface | Substitutable for richer time-control implementations |
| `Breakpoint.HitCount` field | Already populated; Slice 2 conditional breakpoints (D1) can read it |
| `Breakpoint.FilterEntity` field | Already there; per-entity filtering is Slice 1 capability |
| `BlueprintRegistry` snapshot-based read | Forward-compatible with worker-thread parallel reads (E4) via `Volatile.Read` |
| `BlueprintBlackboardPartitions.PayloadHighWater` | Defragmentation-ready (C2) |
| `BlueprintAsset.EditorMetadata` extensibility point | Slice 2 adds dock-layout-per-asset, etc. (G1) without touching schema |
| `[BlueprintExposedEvent]` attribute defined but unused | Attribute exists in Slice 1 module; Slice 2 wires it (F1) |
| Slot-table linear scan over `header.SlotCount` (not `MaxSlots`) | Densely-packed dense-on-detach pattern; ready for multi-slot world-singletons (E3) |

These were the architecturally costly decisions that explicitly bought future flexibility. The cost of Slice 1 is partly the cost of *not closing doors* — Slice 2 inherits an architecture designed to extend rather than rebuild.

---

## Closing note on prioritization

The 50+ items above are far more than Slice 2 will ship. The Slice 2 architecture pass after Slice 1 ships will need to:

1. **Look at Slice 1 implementation telemetry** — which "I bet this will be painful" predictions came true, which didn't.
2. **Pick a coherent theme.** Slice 2 probably shouldn't be "do everything"; it should be 1-2 themes that compound. My pre-implementation guess (low confidence, updated after the Universal Breakpoints design conversation): three natural Slice 2 themes stand out:
   - **(D) Universal Breakpoints** — the architectural advance is mostly already mapped (predicate compiler + event scanner + forward-snapshot pattern, all reusing engine primitives that exist). High impact, surprisingly tractable given the architect's insight that no resimulation is needed.
   - **(A) Cross-entity events + multi-Blueprint scale** — the most-requested Slice 1 deferral.
   - **(F) Attribute-driven catalogs** — quality-of-life for engine teams contributing new events/channels.
3. **Defer aggressively.** Half the items above will turn out to be Slice 3+ once telemetry shows where users actually push.

Slice 2 is not a list to ship. It's an option pool to draw from.

---

*End of Slice 2 candidates document. This is the closing artifact of the Slice 1 design phase.*
