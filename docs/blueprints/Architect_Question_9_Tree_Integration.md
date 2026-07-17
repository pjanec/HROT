# Architect question #9 — assembling the per-node blueprints into the running commander tree

**Context.** The per-node migration is **complete**: every `HillAttackCommanderNodes`/`HillAttackTankNodes`
behavior node now has a visually-authored `.bp.json` twin, each proven in **isolation** against the
(untouched) C# oracle through the real generator (`HillAssault2_*` 52/52). The natural next phase is
**integration** — assembling these twins into the actual running commander behavior (the oracle's
`BuildPlatoonHillAttackTree`: a `Sequence` of setup actions + a `Repeater` over the EQS→dispatch→wait
loop). Two questions block it; both are architectural, so we're putting them to you before building.

**What we already know (from the migration):**
- Each AiPrimitive blueprint emits its own `[BTreeAction]`-style thunk (`AiPrimitiveEmitter`), so wiring
  them into a `Sequence`/`Repeater` tree is mechanically feasible (that's just a BTree definition).
- **The blocker is shared state.** The oracle's six commander nodes all mutate **one**
  behavior-scoped `HillAttackMutableState` ("State" slot). Our per-node blueprints each declared their
  **own** `WorkingState`, duplicating the shared fields (`TotalSlots`, the slot bitmasks, `CachedEqsRequestId`,
  the runner tracker, `CurrentWave`, …). That was correct for isolated per-node proofs, but a running tree
  needs the nodes to see **each other's** writes (e.g. `CalculateSegments` sets `TotalSlots`, `DispatchWave`
  reads it; `DispatchWave` fills the tracker, `IsWaveCompleted` drains it).
- Mechanisms that exist in the blueprint model (unused so far by this migration): per-blueprint
  `WorkingState` (partition-slot-backed), **`GetShared`/`SetShared`** (shared ECS component read/write),
  `ValueChangedSource.PeerBlueprintVariable`, and `CallPeerBlueprintNode`.

---

## Q-A — how do the assembled blueprints share the commander's behavior-scoped mutable state?

1. **Shared ECS component.** Promote the shared fields to a real component (≈ `HillAttackMutableState` as
   an ECS component on the commander) and have every blueprint read/write it via **`GetShared`/`SetShared`**
   instead of `WorkingState`. Re-authors the state access in each blueprint, but uses only shipped nodes and
   gives honest shared semantics. *(our lean)*
2. **Shared behavior-scoped `WorkingState` slot.** Extend the runtime so N blueprints in one tree bind the
   **same** partition slot (keyed by the tree/behavior, not the blueprint) — mirrors the oracle's "State"
   slot most closely, but needs a new slot-sharing mechanism + a way to declare "this WorkingState is shared
   across these blueprints" (new compiler/host capability).
3. **Peer-blueprint variables.** Use `PeerBlueprintVariable`/`CallPeerBlueprint` so one blueprint owns the
   state and peers reference it. Existing enum support suggests partial machinery, but the ownership/lifetime
   story across a Sequence+Repeater is unclear.
- **Our lean:** **(1) shared ECS component.** It is the smallest honest step (no new host capability), it
  matches how blueprints are already meant to share cross-node state (`GetShared`/`SetShared` exist for
  exactly this), and it keeps each blueprint independently compilable/testable. We'd migrate the shared
  commander fields to a component and re-point the per-node blueprints' state access at it. Confirm this is
  the sanctioned shared-state model for a multi-blueprint behavior — or tell us you want the behavior-scoped
  shared-slot (2) as the real model, in which case that host capability is the thing to design next.
- **Reuse vs build:** (1) = re-author state access in ~6 blueprints, zero new host/compiler work. (2) = a new
  shared-slot capability (larger, but the closest structural match to the oracle).

## Q-B — where does the tree structure live, and is it in scope now?

The oracle's `BuildPlatoonHillAttackTree` is a code-first `BTreeBuilder` (Sequence + Repeater). For the
blueprint world the tree could be authored as (a) a `.btree.json` referencing the generated blueprint
BTreeAction thunks, or (b) a higher-level "composite" blueprint. Also: is full-tree integration a goal you
want pursued now, or is the per-node twin set (with the oracle staying the assembled behavior) the intended
end state for this migration, with integration a separate later track?
- **Our lean:** treat integration as its **own** track gated on Q-A. Once the shared-state model is fixed,
  assemble via a `.btree.json` over the blueprint thunks and add ONE end-to-end proof (drive the whole
  Sequence+Repeater a few ticks vs. the oracle's tree). But if you'd rather the migration close at the
  proven per-node twins, we'll stop here and log integration as future work.

---

**Our lean defaults:** A — shared ECS component via `GetShared`/`SetShared` (no new host capability),
unless you want the behavior-scoped shared-slot model. B — integration is a separate track gated on A;
proceed only if you want the full assembled behavior rebuilt (vs. closing at the per-node twins).

---

## ARCHITECT ANSWERS (pending)

_(to be filled in once relayed)_
