# Blueprint Generic Primitives — Design (DRAFT, nothing built)

> **Status:** design/discussion only. Supersedes the "just write a bespoke C# helper per node"
> reading of the architect's Q0/GAP answers where that would force a programmer into the loop.
> **Goal (non-negotiable):** a **non-programmer** must be able to author complex behaviors — up to and
> including the full Platoon Hill-attack (our deliberate worst-case benchmark) — in blueprints. That
> is the primary reason blueprints exist. So **we DO port all of Hill-attack**; "leave it in C#" is
> not an acceptable outcome for any part a designer would need to change.
> **Method:** for every construct that's currently hard to express in a blueprint, design **one
> generic, reusable primitive** — a hardcoded-C# blueprint node + its static helpers + its public-API
> structs — that a designer composes without writing C#. Structured `ForEach` is the template.

## 1. The reconciliation: "curated-generic", not bespoke, not fully-general

The architect is right that blueprints must not become a fully-general visual language, and right that
complexity belongs in C#. The correction: that C# must be **generic engine public-API**, authored
**once**, reused across all behaviors — **not** a new bespoke helper per behavior (which would put a
programmer back in the authoring loop and defeat the point).

This is exactly the philosophy the architect already applies elsewhere — we just extend it to the
remaining hard patterns:

| Existing curated-generic precedent | We extend the same idea to… |
|---|---|
| EQS: `SpawnEqsSensor`/`ReadEqsResult` (generic lifecycle, C# does the query) | component reads, iteration, events, slot pools |
| Engine Event Catalog (curated list, not arbitrary types) | `PublishEvent` picks from the catalog |
| Restricted blueprint type system (scalars/vectors/entities) | primitives expose only those pin types |
| Structured `ForEach` (bounded, latent-free) | the model for every other primitive |

**Curated-generic = generic across behaviors, constrained in surface** (attribute-gated, read-only
where appropriate, no arbitrary ECS mutation, no unbounded/latent loops). Safe for the compiler and
for a non-programmer; still expressive enough to build Hill-attack.

## 2. The primitive family

Each primitive is hardcoded C# (node + lowering + helpers + public structs), authored once.

| # | Primitive | What a designer drops | Closes | Notes |
|---|-----------|-----------------------|--------|-------|
| **P1** | **`ForEach`** (structured, bounded, latent-free body → synchronous C# `for`) | iterate a collection pin, wire a body | GAP-1 | body is a sub-DAG; validator forbids latent nodes inside |
| **P2** | **`GetComponent<T>`** (read-only, on any Entity pin), restricted to `[BlueprintReadable]` components | "read TargetMemory / NavigationStatus of \<entity>" | GAP-7 (self) + GAP-2 (foreign) | curated set; exposes the component's scalar/vector/entity fields + array fields as `ForEach` sources |
| **P3** | **`GetSingleton<T>`** (read-only), restricted to `[BlueprintReadable]` singletons | "read NetworkEntityMap" | GAP-7 | curated set |
| **P4** | **`PublishEvent`** (catalog-gated), same-entity direct / cross-entity deferred | pick an event, fill scalar/entity payload pins | GAP-3 | `[SharedAiAction]`-backed; cross-entity → `BlueprintDeferredEvent`, 1-frame latency |
| **P5** | **Squad slot/role/phase** (`PartitionElements`/`AssignRoles`/`AcquireSlot`/`AdvancePhase`) | assign firing/baseline slots, waves, roles | GAP-4 + GAP-5 | implement the already-designed-but-unlowered nodes; the bitmask/SoA bookkeeping hides *inside* the primitive's public struct |
| **P6** | **Math/Geometry Library** (pure `FunctionCall` to curated static fns) | lerp, distance, angle, vector ops | — | mostly works today via `FunctionCall`; ensure a curated blueprint-exposed math lib |
| **P7** | **Context-aware `FunctionCall`** (trailing `self, ISimulationView view` auto-bound) | last-resort read that fits no generic node | GAP-7 | the escape hatch — used *rarely*, not per-node |

## 3. Hill-attack coverage map (does the family fully cover the worst case?)

| Hill-attack node | Primitives it would use | Fully coverable? |
|---|---|---|
| `Action_AbortEngagement` | Return | ✅ (slice 0 done) |
| `Condition_HasTarget` | P3 (NetworkEntityMap) + P2 (TargetMemory) + P1 (scan) + Branch | ✅ |
| `Action_ReverseToBaseline` | `ChannelCommand` + P4 (ClearBehavior on terminal) | ✅ |
| `Action_AimAndFireSpecific` | P2 (resolve target) + `ChannelCommand`(Weapon) + WorkingState round count | ✅ |
| `Condition_AreAllAtBaseline` | P2 (UnitRoster) + P1 (ForEach subordinate) + P2 (NavigationStatus foreign) | ✅ (needs foreign read) |
| `Action_CreepToAndBeyondSlot` | P2 (SimTransform) + P6 (geometry) + `ChannelCommand` + WorkingState phase | ✅ |
| `Action_CalculateSegments` | P6 (geometry) + WorkingState | ✅ |
| `Action_DispatchAllToBaseline` | P2 (roster) + P1 (ForEach) + P6 (baseline pos) + P4 (order per member) | ✅ |
| EQS loop | `SpawnEqsSensor`/`ReadEqsResult` (exist) | ✅ |
| `Action_DispatchWaveWithTargets` | P5 (slot/wave assign) + P1 + P4 | ✅ (P5 is the crux) |
| `Condition_IsWaveCompleted` | P5 (slot state) + P1 + P2 (foreign BehaviorState poll) | ✅ (P5 + foreign read) |

**Result:** the family covers 100% of Hill-attack. The load-bearing new pieces are **P2 (curated
foreign+self component read)** and **P5 (squad slot/role/phase)** — these are where the real design
depth is, and where we most extend past the architect's strict answers.

## 4. Points that need architect sign-off (we extend past the strict answers)

1. **P2 curated `GetComponent<T>`** — the architect rejected a *general* `GetComponent`. We propose an
   **attribute-gated (`[BlueprintReadable]`), read-only, typed** component read — curated, not general.
   This is the crux negotiation: without it, non-programmers can't read ECS state, and the whole goal
   fails. Frame it as the EQS/event-catalog philosophy applied to reads.
2. **P1 structured `ForEach`** — architect deferred loops citing topological-sort/latent breakage;
   that reasoning only applies to *unstructured back-edge* loops. A structured, bounded, latent-free
   `ForEach` (body sub-DAG → synchronous `for`) is compiler-safe. Ask to approve it as the sanctioned
   iteration primitive.
3. **P5 squad primitives** — approve implementing the already-designed nodes as the generic slot/wave
   mechanism (so the bitmask/SoA bookkeeping lives inside an engine primitive, not designer-authored).
4. **Foreign-entity read** (P2 on another entity) — read-only cross-entity component access; confirm
   this is safe within a tick (reads only) vs must-be-deferred.

## 5. Open questions / to design next

- P2: how to expose a component's `fixed`/array fields (e.g. `TargetMemory.EntityIds[]`) as a
  `ForEach` source without leaking unmanaged layout — a typed read-only span/enumerable pin?
- P5: the public API shape of a "slot pool / squad plan" struct a blueprint declares as WorkingState,
  and the exact `AcquireSlot`/`AssignRoles` node semantics.
- Authoring ergonomics: how these primitives appear in the palette / inspector for a non-programmer
  (ties into UX-1 intent-first authoring).

## 6. Sequencing (when we do build — not yet)

P7 + P4 (smallest, unblock conditions/events) → P1 `ForEach` → P2 `GetComponent` (self, then foreign)
→ P3 → P5 (hardest, last). Validate each on the specific Hill-attack node(s) it unlocks.
