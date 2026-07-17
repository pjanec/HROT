# Architect question #8 — the wave core (`DispatchWaveWithTargets` + `IsWaveCompleted`)

**Context.** The last major migration targets are the two hardest oracle nodes:
`Action_DispatchWaveWithTargets` (`HillAttackCommanderNodes.cs:287-437`) and
`Condition_IsWaveCompleted` (`:447-503`). Q#3 (`Squad_State_Fit_And_Lean_Slot_Design.md`) already
blessed a **lean path**: generic `MemberSlotList` + `SlotRotation` **Blueprint-node vocabulary** hiding
the SoA/compaction behind verbs. **But Q#3 predates actually building slices 2-7**, where you steered us
(Q#6, Q#7) firmly toward **curated `FunctionCall` helpers + visual orchestration, demand-driven — do NOT
build speculative node vocabulary**. The wave core is the migration's **only** `MemberSlotList` consumer.
So Q#8's spine is: **does the wave core follow the Q#3 "build the node vocabulary" plan, or the Q#6/Q#7
"curated kernels + visual orchestration" pattern that has shipped five slices?** Plus four coupled
node-level decisions. Leans given; we proceed on the leans unless you redirect.

**What the oracle actually does (the difficulty).**
`DispatchWaveWithTargets`: outer loop over the roster (cap 8 active), wave-parity filter
(`sub.Index % 2 == CurrentWave`), an **inner scan** building an `avail[]` free-slot list from
`BurnedSlotsMask|WaveUsedSlotsMask` then a **`Random.Shared.Next` pick**, firing-slot + baseline-slot
interpolation, `PickClosestBaselineSlot` (distance²), round-robin target from `GetTargetFromPool`,
`NetworkIdentity` read, a write into a **compacting SoA tracker**
(`ActiveEntityPacked[8]`/`ActiveSlotIndex[8]`/`ReturnBaselineSlotIndex[8]`/`HasStartedRun[8]`), bitmask
set, `HullDownAttackParams` JSON + managed `PublishEvent`, then wave flip.
`IsWaveCompleted`: **reverse** loop over the SoA tracker; per entry — dead → burn firing slot + release
baseline + **`SwapRemove`**; not-started → latch `HasStartedRun` off `BehaviorState.ActiveBehaviorHash`;
started-and-finished → release baseline + `SwapRemove`. Returns `Running`/`Success`.

---

## Q-A — the spine: `MemberSlotList` as Blueprint **nodes** (Q#3), or curated **`MemberSlotListOps`** helpers (Q#6/Q#7 pattern)?

Q#3 blessed generic `MemberSlotList`/`SlotRotation` **node kinds** (Add/SwapRemoveAt/ForEach/… + Acquire/
Release/Burn) — a large compiler build (new nodes + IR + Stage5 cases + emit + registry + coverage) that
is **reusable** for future squad behaviors. Slices 2-7 instead proved a **curated-helper** pattern: keep
the unsafe/complex kernel in reviewable C# (`UnitRosterOps`, `SegmentMath`, `MaskOps`, `AreaQueryBatchOps`,
`MoveIntentJson`), call it from a **visual `FunctionCall`**, and draw only the orchestration.
- **Our lean:** apply the **Q#6/Q#7 curated pattern** to the wave core too — a `MemberSlotList` **curated
  struct** held as one WorkingState var, with `MemberSlotListOps.{Add, SwapRemoveAt, Count, GetEntity,
  GetSlotIndex, GetBaselineSlot, GetStarted, SetStarted}` curated helpers (over `ref MemberSlotList`),
  plus the outer roster iteration / parity / publish / wave-flip drawn **visually** (`FlowForEach`,
  `Branch`, `Compare`, `PublishEvent`, `SetVariable`). This ships the wave core on the **same, proven,
  reviewable** machinery as the other five slices and builds **no speculative node vocabulary** for a
  single consumer. Defer the generic `MemberSlotList`/`SlotRotation` **node** vocabulary (Q#3) until a
  *second* squad behavior actually needs it (demand-driven). **This revises Q#3 — confirm the revision**,
  or tell us you want the generic node vocabulary built now as the reusable foundation.
- **Reuse vs build:** lean = ~1 curated struct + ~8 tiny helpers + existing nodes (M, mirrors shipped
  slices). Q#3-as-written = a new generic node family + IR/emit/registry/coverage (L, reusable).

## Q-B — the inner free-slot scan + random pick: curated kernel or nested `FlowForEach`?

`DispatchWaveWithTargets` has an **inner loop** (build `avail[]` from the blocked mask, then random-pick)
nested inside the outer roster loop. `FlowForEach` today is single-level, latent-free, branch-only-in-body
— nesting is unproven and a big lift.
- **Our lean:** a curated `SlotOps.PickRandomFreeSlot(ushort blockedMask, int totalSlots, <rng>) → int`
  (returns the chosen free slot, or `-1` if none) — the whole inner scan+pick is a self-contained kernel
  with no visual-node form, so it stays curated; the outer roster loop stays a visual `FlowForEach`. Do
  **not** build nested `FlowForEach` for one consumer.
- **Reuse vs build:** lean = 1 curated helper (S). Nested-`FlowForEach` = a substantial scheduler lift (L).

## Q-C — randomness + determinism (`Random.Shared`)

The slot pick uses `Random.Shared.Next` — **nondeterministic**, which breaks replay and makes proof tests
unassertable. Our headless-proof discipline needs determinism.
- **Our lean:** the curated `PickRandomFreeSlot` helper draws from a **deterministic, sim-derived seed**
  (e.g. hash of `self.Index ^ CurrentWave ^ (int)SimulationTime`) rather than `Random.Shared`, so the
  choice is replayable and the proof can assert an exact slot. Confirm the engine wants deterministic RNG
  for blueprint-authored AI (we believe replay/rollback requires it) — or tell us `Random.Shared` in a
  curated helper is acceptable and proofs should assert only *a valid free slot* (set-membership), not an
  exact one.
- **Reuse vs build:** either way it is one curated helper; this is purely the determinism policy.

## Q-D — the SoA tracker as a curated-struct WorkingState var (by-ref to helpers)

The tracker is a compacting SoA (`fixed long/byte[8]`). Slices so far proved **scalar** WorkingState vars
only; a **struct** WorkingState var containing `fixed` arrays, passed **by ref** into curated helpers
(`MemberSlotListOps.Add(ref list, …)`), is unproven end-to-end (declaration, `ref` emit, roundtrip).
- **Our lean:** declare `MemberSlotList` (the curated struct, capacity **8** to match the oracle's
  `ActiveAttackerCount < 8` and `fixed [8]` — *not* the doc's 16; confirm 8) as a single WorkingState var,
  and confirm the compiler can (1) hold a `fixed`-array struct as a WorkingState field and (2) emit a
  by-ref pass into a curated helper. If by-ref WorkingState→helper is not currently expressible, that is
  the one genuinely-new *compiler* capability this slice needs — flag it and we design it hands-on.
- **Reuse vs build:** lean reuses WorkingState + curated helpers if by-ref works; otherwise a small,
  well-scoped compiler add (ref-arg emit for a WorkingState struct var).

## Q-E — the remaining kernels (confirm curated, mirroring shipped slices)

`PickClosestBaselineSlot` (distance²), the round-robin `GetTargetFromPool` + `NetworkIdentity` target
resolve, and the `HullDownAttackParams` JSON build are all self-contained kernels with no visual-node form.
- **Our lean:** all curated `FunctionCall` helpers — `SlotOps.PickClosestBaselineSlot(...)`, a
  `TargetPoolOps.ResolveNetId(handle, roundRobinIndex, view)` (bundling `GetTargetFromPool` + alive +
  `NetworkIdentity` read → `long` netId, `0` if none), and a `HullDownIntentJson.Build(...)` (mirrors
  `MoveIntentJson.Build`, Q#6-C) — feeding a managed `PublishEvent(AssignTacticalIntentEvent, IntentId=
  "HullDownAttack")`. Confirm (routine, but it's a lot of curated surface).

---

**Our lean defaults if you're happy with them:** A — curated `MemberSlotListOps` + visual orchestration
(revise Q#3; defer the generic node vocabulary to a second consumer). B — curated `PickRandomFreeSlot`
inner kernel, no nested `FlowForEach`. C — deterministic sim-derived seed (replayable/testable). D —
`MemberSlotList` curated struct as a by-ref WorkingState var, capacity 8; flag by-ref emit as the one
possible new compiler bit. E — curated helpers for closest-baseline / target-resolve / Hull-down JSON.
We proceed on these unless you redirect.

---

## ARCHITECT ANSWERS (2026-07-17) — all five leans APPROVED

- **A — curated `MemberSlotListOps` + visual orchestration (APPROVED; revises Q#3).** Cleared to revise
  Q#3. Grow node vocabulary demand-driven, not speculatively — the wave core is the ONLY `MemberSlotList`
  consumer, so a generic node family (nodes + IR + Stage5 emit) is unjustified. Curated `MemberSlotListOps`
  over a struct WorkingState var + visual outer loop matches the shipped Slice 2-7 pattern. Defer the
  generic node vocabulary until a *second* squad behavior needs it.
- **B — curated `SlotOps.PickRandomFreeSlot` (APPROVED).** Keep the inner scan+pick curated; do NOT build
  nested `FlowForEach` for one consumer.
- **C — deterministic sim-seeded RNG (APPROVED & MANDATED).** MUST use a deterministic sim-derived seed
  (e.g. `hash(self.Index ^ CurrentWave ^ (int)SimulationTime)`). `Random.Shared` is **rejected** — the
  engine requires deterministic RNG for replay/rollback/headless-proof discipline. **Proofs assert exact
  slots**, not set-membership.
- **D — SoA tracker as a by-ref curated-struct WorkingState var, capacity 8 (APPROVED).** Right data shape.
  If the compiler cannot yet emit a `ref` pass of a struct WorkingState var into a curated helper, **build
  that specific compiler capability** — a much smaller, more valuable add than a bespoke node family.
- **E — curated closest-baseline / target-resolve / Hull-down JSON helpers (APPROVED).** Mirrors EQS + the
  earlier slices.

**Cleared to proceed on all five leans.**
