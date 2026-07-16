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

### `MemberSlotList` — API (ARCHITECT-BLESSED, question #3)
No existing runner-list construct exists (closest are the bespoke `HillAttackMutableState` SoA and the
`UnitRoster` component) — so `MemberSlotList` is genuinely new. Blessed shape:
```csharp
[BlackboardDtoStruct]                          // blittable, engine public API, opaque to designer
public struct MemberSlotList
{
    // FIXED NAMED columns (SoA), NOT generic scalar columns — the compiler's struct-reflection
    // needs concrete typed fields. Capacity 16 (matches UnitRoster.Capacity — commander ceiling).
    // EntityId[16] (long), FiringSlot[16] (byte), BaselineSlot[16] (byte), HasStarted[16] (byte), Count.
}

// verbs (Blueprint nodes over a MemberSlotList WorkingState var):
Add(list, entity, firingSlot, baselineSlot)   // append a runner (HasStarted=0)
SwapRemoveAt(list, index)                       // O(1) compaction (matches Hill-attack SwapRemove)
Count(list) → int
Get(list, index) → (entity, firingSlot, baselineSlot, hasStarted)   // or a ForEach source
SetHasStarted(list, index, value)               // latch the run-start flag
```
**⚠ Implementation hazard (architect):** if the columns use C# 12 `[InlineArray]`, direct index
assignment inside an unmanaged component triggers the **defensive-copy `ldobj` bug — writes are
silently lost.** Must expose a `GetSpanRW()` (or equivalent) and mutate through the span. This is the
kind of bug that would waste a day; bake it into the primitive from the start + a test that writes,
reloads, and asserts the value stuck.

Same encapsulation principle as `SlotRotation`: hides the SoA + compaction behind verbs.

## Q5 reuse (architect) — more "we already have X"
- **EQS target pool:** use `AreaQueryBatchHelper.GetTargetFromPool(repo, targetGroupHandle, index)` —
  safely unpacks an entity from the `EqsTargetPool` native-array singleton. No new node needed beyond
  wrapping it.
- **Round-robin targets:** no helper — it's just `activeTankIndexInWave % targetCount`. Wrap in a small
  Blueprint helper function (pure `FunctionCall`).
- **Baseline reservation:** NOT a new construct — just a **second `SlotRotation`** instance (firing +
  baseline are two `SlotRotationState` vars). Confirmed.

## Lean coverage of the hard Hill-attack nodes

| Node | Lean constructs used |
|---|---|
| `CalculateSegments` | math (P6) → `TotalSlots`; init two `SlotRotationState` vars + wave `int` |
| `DispatchAllToBaseline` | `ForEach`(roster) + P2(read pos) + math + `AcquireSlot`(baseline) + `PublishEvent` |
| `DispatchWaveWithTargets` | `ForEach`(roster) + parity check + `AcquireSlot`(firing) + baseline acquire + `MemberSlotList.Add` + round-robin (index math) + `PublishEvent`; toggle wave `int` |
| `IsWaveCompleted` | `ForEach`(MemberSlotList) + P2(foreign `BehaviorState`/alive) + `BurnSlot`/`ReleaseSlot` + `SwapRemoveAt` |

Everything maps to generic, reusable pieces — no Hill-attack-specific C#, no forced doctrine model.

## Status: design complete, architect-blessed (question #3)
Column model (fixed named SoA), capacity (16), the `SlotRotation`-×2 baseline decision, and the Q5
reuse are all settled above. The one build-time risk to carry forward is the `[InlineArray]`
`GetSpanRW()` hazard. Ready to build when green-lit — no open design points remain for the lean path.
