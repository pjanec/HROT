# Squad-state fit check + lean slot design (DESIGN, nothing built)

> **Question:** does the Hill-attack wave/slot/burn logic fit the engine's generic `SquadCognitiveState`
> + squad primitives (so we reuse them and prove them), and — regardless — what's the leaner,
> more-generic slot construct that reproduces Hill-attack without the heavy doctrine machinery?

## Verdict 1 — `SquadCognitiveState` does NOT fit Hill-attack. Do not force it.

`SquadCognitiveState` (1024 B) models **infantry fire-and-movement doctrine**: bounding-overwatch
*elements* (covering/bounding/overwatch), 12 doctrine *slots* (by slot-id), 16 *roles*, a
threat-matrix *assignment* region, a *contact* pool, and a *phase* machine (`PhaseId` +
`PhaseSequencer`). Hill-attack is a different shape — wave-based armor attack runs with per-runner
tracking and two distinct slot spaces. The correspondence is mostly forced:

| Hill-attack concept | Nearest in SquadCognitiveState | Fit |
|---|---|---|
| Firing-slot occupancy | in-state `Slots` array (occupancy bit **undefined, write-dead outside tests**) | partial |
| Baseline-slot reservation (2nd 16-bit mask) | — | **no home** |
| Active-runner tracking: compacting SoA of ≤8 runners keyed by packed Entity + `HasStartedRun` latch + SwapRemove | — (state is indexed by slot-id/roster, no per-live-runner list) | **no home** |
| Wave grouping by immutable `Entity.Index` parity | `ElementPartition` (score/hysteresis by roster index) | partial (wrong driver) |
| Current-wave 0↔1 toggle | `PhaseId` + event/table `PhaseSequencer` | partial (overkill) |
| Burn firing-slot on death | (semantically = `SlotRotation.BurnSlot`, but that's a *separate* struct) | partial |
| Round-robin targets | `Assignment` + `GreedyMatrixAssigner` (greedy threat matrix, not positional) | partial |

Two no-homes (baseline reservation, the ≤8-runner SoA) and four square-peg partials. Forcing it would
mean bending Hill-attack to a doctrine it isn't. **Recommendation:** reserve `SquadCognitiveState`'s
first real use for a behavior that's actually element/role/phase-shaped (a bounding-overwatch
maneuver) — proving it there is meaningful; proving it on Hill-attack would prove a mismatch.

> Note: the whole squad layer (`SquadCognitiveState` + maneuvers + systems + the four squad Blueprint
> node kinds) is **library-complete but unwired** — exercised only by tests, not on any running
> behavior, and the squad nodes have **no IR lowering** (confirms the safety-net finding). So there is
> no "reuse-in-place" shortcut here regardless.

## Verdict 2 — the lean path, and its core primitive already exists

You asked for a "simpler, more generic squad-member-slot-array" construct. The recon found the engine
already ships the slot half of it:

**`SlotRotation` / `SlotRotationState`** (`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/SlotRotation.cs`)
— a standalone **4-byte** struct (`UsedMask` + `BurnedMask`) with `AcquireSlot(totalSlots)→int`
(first-fit not-used/not-burned), `ReleaseSlot(i)`, `BurnSlot(i)`. Its own doc says it *"generalizes
`HillAttackMutableState.BurnedSlotsMask`/`WaveUsedSlotsMask`."* This is exactly the lean slot pool —
generic, tiny, no doctrine baggage.

The lean Hill-attack rebuild = **two small generic primitives + generic nodes**:

1. **`SlotRotationState`** (exists) → expose as Blueprint nodes `AcquireSlot` / `ReleaseSlot` /
   `BurnSlot` over a `SlotRotationState` WorkingState var. Use **two instances**: one for firing
   slots (burn on death), one for baseline slots (release on return/death). Covers occupancy +
   baseline reservation + burn-on-death cleanly.
2. **`MemberSlotList` (new, to design)** — a generic **fixed-capacity list of Entity records with a
   few scalar columns**, encapsulating the SoA + swap-remove so the designer never touches raw
   arrays (and we avoid the unlowered `ArrayMake/ArrayGet` path, GAP-5). This is the "member-slot
   array helper" — it holds the active runners.
3. **wave counter** = a plain WorkingState `int`, advanced with generic arithmetic + `SetVariable`
   (no `PhaseSequencer` needed).
4. compose with **`ForEach`** (P1), **component reads** (P2), **`PublishEvent`** (P4).

### `MemberSlotList` — strawman API (the one new construct)
```csharp
[BlackboardDtoStruct]                          // blittable, engine public API, opaque to designer
public struct MemberSlotList { /* fixed cap (16); Entity + up to K scalar columns per row; Count */ }

// generic verbs (Blueprint nodes over a MemberSlotList WorkingState var):
Add(list, entity, col0, col1, …)   // append a runner + its firingSlot/baselineSlot/started
SwapRemoveAt(list, index)          // O(1) compaction (matches Hill-attack's SwapRemove)
Count(list) → int
Get(list, index) → (entity, col0, col1, …)     // or exposed as a ForEach source
Set(list, index, colN, value)                   // e.g. latch HasStartedRun=1
```
Generic (any entity-keyed record list), reusable, and it hides the compaction/SoA — the same
encapsulation principle as `SlotRotation`.

## Lean coverage of the hard Hill-attack nodes

| Node | Lean constructs used |
|---|---|
| `CalculateSegments` | math (P6) → `TotalSlots`; init two `SlotRotationState` vars + wave `int` |
| `DispatchAllToBaseline` | `ForEach`(roster) + P2(read pos) + math + `AcquireSlot`(baseline) + `PublishEvent` |
| `DispatchWaveWithTargets` | `ForEach`(roster) + parity check + `AcquireSlot`(firing) + baseline acquire + `MemberSlotList.Add` + round-robin (index math) + `PublishEvent`; toggle wave `int` |
| `IsWaveCompleted` | `ForEach`(MemberSlotList) + P2(foreign `BehaviorState`/alive) + `BurnSlot`/`ReleaseSlot` + `SwapRemoveAt` |

Everything maps to generic, reusable pieces — no Hill-attack-specific C#, no forced doctrine model.

## Open point for review
`MemberSlotList` capacity + column count/typing (Hill-attack needs Entity + firingSlot + baselineSlot
+ startedFlag = 1 entity + 3 bytes; a generic cap of 16 rows × N scalar columns). Worth confirming the
column model (fixed named columns vs N generic scalar slots) before building.
