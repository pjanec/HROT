<!--STATUS
state: LIVE — measured (CE-4454 Part 2)
updated: 2026-11-12
current-answer: VERDICT (top), then R-1 … R-3
stale-below: nothing
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §18.2 the last-tick copy declared by construction and the per-read check this measures (§18.2 as measured, on this branch)
  - HANDOFF_Coordinator_To_Backend_Perf_Verdict_And_Read_Check.md — CE-4454, this measurement (Part 2)
  - STATUS_Entity_Access_Census.md — the census whose per-thread current entity is the hook; its blind spots are R-3's starting list
-->

# STATUS — the per-read cross-entity check: what it costs (CE-4454 Part 2)

**Branch:** `exp/read-check` from `exp/entity-census@1769bfc0a`. ⛔ Experimental, merged nowhere.

## VERDICT

⭐ **AFFORDABLE AS DESIGNED — keep the check in the repository's own read path; no separate view type is needed for cost.**

| question | answer |
|---|---|
| serial code with the check compiled in (flag off) | ⭐ **no measurable cost** — micro ±0 … +1 ns (inside the run-to-run spread); the 1 000-man rig's CGF frame inside its own noise (R-2) |
| during a parallel phase, own entity | +1.5 … +2.7 ns per access (~10–15 % of a 16–27 ns access) — the `[ThreadStatic]` read and compare |
| during a parallel phase, another entity | **cheaper than today** (11 vs 16 ns): served from a flat last-tick array |

Three things the build must do, found here:
1. ⛔ **The last-tick copy must be native (or pinned) memory** — callers turn a typed ref into a pointer kept for the tick
   (`BlueprintTierSpec.cs:130-161`, R-3); a managed array (this prototype) could move under a GC.
2. ⛔ **Route the one by-type-id cross-entity read through the typed path** — `WeatherField.TryOutline` (`:293`, `GetManagedComponentByTypeId`) is
   called from a brain's `EngagementArea` (`:136, 239`) on the AREA entity; the check cannot see it.
3. ⭐ **One per-thread current entity** — this prototype keeps its own `[ThreadStatic]`; the census keeps a per-thread stack (`EntityCensus.cs:47`).
   The build uses ONE (the per-entity job sets it; the census reads it) — no second owner of the same fact.

⚠ Not measured: the cross-entity WRITE redirect (to the per-batch command buffer) — the prototype throws instead; and a parallel run with the flag on
(it needs the per-entity jobs of §18, not built).

## The prototype

`FDP/Engine/Fdp.Core/ParallelReadCheck.cs` + one line in each of the repository's internal read / write paths (`EntityRepository.cs`:
`GetUnmanagedComponentRO` / `RW`, `GetManagedComponent`, `GetManagedComponentRefRO`, `GetManagedComponentRO`, `GetManagedComponentRW`) —
every typed read reaches one of them: the public `GetComponentRO/RW<T>`, `TryGetComponent<T>` (the pure path) and `ISimulationView` alike.

```csharp
if (ParallelReadCheck.Compiled && ParallelReadCheck.Active && ParallelReadCheck.IsOther(entity.Index))
    return ref ParallelReadCheck.OtherUnmanagedRO<T>(entity);      // the last-tick row, or a throw when T is undeclared
```

| switch | what it is |
|---|---|
| `Compiled` | `HROT_READ_CHECK=1`, a `static readonly` read once ⇒ unset, the JIT removes the hook |
| `Active` | a plain static: the parallel phase is running |
| `IsOther` | a `[ThreadStatic]` current entity (the per-entity job sets it per entity) ≠ the accessed one |

The rail: `HROT_READ_CHECK=1 dotnet …/Fdp.Core.ReadCheckBench.dll check` — 7 assertions (a cross-entity read is last tick's value, unmanaged /
`TryGet` / managed; own reads live; an undeclared type throws; a cross-entity write throws; phase off = live). Red-proved: the unmanaged hook
removed ⇒ 3 FAIL.

## R-1 — the micro-benchmark (ns per access)

`Fdp.Core.ReadCheckBench`, Release, a Stopwatch loop over 4 096 entities × 4 000 passes, median of 7 rounds, each case in its OWN process (so the
`static readonly` is folded as in production), two passes interleaved; the 4-core cloud VM. 0 B per access (the 40 B per round is the
harness's Stopwatch).

| access | ① today | ② compiled in, flag OFF | ③ flag ON, own entity | ④ flag ON, other entity → last-tick copy |
|---|---|---|---|---|
| `GetComponentRO<T>` (unmanaged) | 16.2 · 16.6 | 16.1 · 16.1 (**±0**) | 18.4 · 18.1 (**+2.0**) | 10.7 · 11.2 (**−5.4** — a flat array) |
| `GetComponentRW<T>` | 16.2 · 16.7 | 17.0 · 16.5 (+0.3) | 17.9 · 17.9 (+1.5) | n/a — a cross-entity write is a command (§18) |
| `TryGetComponent<T>` | 26.8 · 26.0 | 29.1 · 26.8 (+0.9 … +2.3, noisy) | 27.9 · 29.1 (+2.1) | 27.1 · 26.8 (±0) |
| managed `GetComponentRO<T>` | 19.4 · 20.1 | 20.3 · 21.5 (+1.1) | 23.3 · 21.6 (+2.7) | 20.3 · 20.3 (+0.5) |

⇒ ⭐ **Flag off, the check costs nothing measurable** (within the run-to-run spread of ~1 ns). Flag on, ~1.5–2.7 ns per own-entity read
(the `[ThreadStatic]` read + compare), ~10–15 % of today's 16–27 ns access. ⚠ The access itself is the expensive part: a typed read goes through a
cached open delegate (`UnsafeShim`) and a table lookup — 16 ns — while the last-tick copy, a plain array, serves it in 11.

## R-2 — the 1 000-man rig, serial, the check compiled in and the flag off

`scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --seconds 30`, `HROT_QUERY_TIMINGS=0`, the **Release** runner built from this
branch (the same binary both arms: `HROT_READ_CHECK` unset vs `=1`), two runs per arm, interleaved; the 4-core cloud VM.

| run | CGF work ms/frame | `BrainTickSystem` ms | SimHost work ms/frame | CGF entities |
|---|---|---|---|---|
| today 1 | 67.2 | 8.6 | 40.0 | 13 889 |
| check 1 | 58.8 | 7.1 | 39.3 | 14 158 |
| today 2 | 57.0 | 6.6 | 38.7 | 13 854 |
| check 2 | 60.5 | 7.2 | 36.9 | 13 836 |

⇒ today 57.0–67.2 ms, with the check 58.8–60.5 ms: **the difference is inside the run-to-run spread (10 ms between the two "today" runs)** — no
measurable cost. Consistent with R-1 (±0 … +1 ns per access flag off).

## R-3 — what bypasses the check (`file:line` on this branch)

The check sees every TYPED read. What reaches component data otherwise, in code that runs inside the Simulation phase:

| path | sites | does it matter? |
|---|---|---|
| ⛔ **by type id, managed** — `GetManagedComponentByTypeId` | `Weather/WeatherField.cs:293` (`TryOutline`, `AreaEntityContains`), called from **`Behavior/Components/EngagementArea.cs:136, 239`** (a brain reads its engagement AREA — another entity) and `Combat/ItemSources.cs:29, 32` | ⭐ **yes — a genuine cross-entity read the check cannot see.** ⇒ route `TryOutline` through the typed managed read (`GetComponentRO<EditablePolyline>`), or give the by-id read the same hook |
| ⚠ pointers taken from a TYPED access and kept for the tick | `Blueprints/Partitioning/BlueprintTierSpec.cs:130-161` (the occurrence store's tier blocks), `Behavior/HostedSubtree.cs:427, 447`, `Behavior/HsmOccurrence.cs:268`, the executors' `fixed (byte* p = channel.State)` (`CarryExecutors`, `EmbarkExecutor`, `EjectExecutor` …) | ✅ checked at ACQUISITION (the typed call is the hook) — and all own-entity. ⭐ **But it constrains the copy:** a cross-entity read hands out a pointer INTO the last-tick copy ⇒ **the copy must be native or pinned memory** (the prototype's managed arrays could move under a GC) |
| component-mask / presence reads | `OccurrenceStoreAccess.cs:193` (`PresentTiers`), `WeatherField.cs:292`, `HasComponent*` | ✅ no — presence is structure, and structural changes are deferred in the phase (§18.1) ⇒ stable for the whole phase |
| `GetComponentTable<T>` · `ComponentTable.GetSpan` | **0** production callers outside `Fdp.Core` | ✅ none today — ⚠ a future caller would bypass the check; an analyzer / the read guard should forbid it in per-entity systems |
| `GetComponentPointer` / `GetManagedComponentByTypeId` / `GetRawObject` elsewhere | ReplayBrowser (`BinaryMerger`, `PredicateCompiler`, `ResultProjection`, `TimelineLaneCompiler`), Replication (`OwnershipRecomputeSystem`, `OwnershipApplier`, `ComponentEdits`), `EgressVisitGate`, `NedReplicationModule`, `DeferredTakeoverSystem`, the recorder, `StagingEntityExtractor`, the debug API, diagnostics | ✅ not the Simulation phase (ingress / egress / replay / tools) — outside the rule |
| singletons (`GetSingleton*`) | many | not per entity — shared state, the census's N-3 list |

⚠ Coverage: `git grep` on this branch for every raw accessor (`GetComponentPointer`, `GetManagedComponentByTypeId`, `HasComponentByTypeId`,
`GetComponentMask`, `GetComponentTable`, `GetSpan`, `GetRawObject`, `TryGetTable`, `Unsafe.AsPointer`, `fixed (`) is the list above; the graph's
`search_code` (indexed from the `exp/perf` tree at session start) agrees on the one Simulation-phase by-id site (`WeatherField.cs`) and finds a subset
of the rest. `check_index_coverage` is not available through the CLI ⇒ "0 callers" is "0 found".
