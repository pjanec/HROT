<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: ⭐ §18 per-entity parallelism (the rule, what the census must count, determinism for free) · §16 J-3 (the chain) · §11.1 the Windows numbers
  - STATUS_Threaded_Analysis_Instruments.md (on exp/threaded-analysis) — A-1 the access tracer (per TYPE) this extends to per ENTITY; A-2 the reach tool (state outside the world)
  - HANDOFF_Coordinator_To_Backend_Threaded_POC_4.md — PAUSED by the user; this session takes this instead
-->

# HANDOFF — coordinator → backend: MEASURE whether every simulation system can run per entity in parallel — the cross-entity access census

**For:** the session that was running threaded POC 4 (paused by the user). **From:** coordinator. **Dispatched at `cf8dbebab`**, scope frozen.
🔒 User, `2026-11-12`: *"todays computers have 16+ physical cores so parallelization is the way to go"* · *"basically every system can be
parallelized including the brains"* · *"the lane doing HANDOFF_Coordinator_To_Backend_Threaded_POC_4.md is now paused so we can use it to do
whatever measurements we need in this direction"*.
⛔⛔ **Autonomous** (R-4156). ⛔ **Instruments and measurements only** — build no parallel system. Decide, record with a claim table, continue.

## Where to work

⭐ A NEW branch **`exp/entity-census`** from **`exp/perf`**, taking the access tracer from `exp/threaded-analysis`
(`FDP/Engine/Fdp.Core/AccessTrace.cs` and its hooks, `scripts/access-trace-*.py`; and `Hrot/Tools/Hrot.Tools.ReachAnalysis`). Status file
`docs/blueprints/batches/STATUS_Entity_Access_Census.md` ON THAT BRANCH. ⛔ Never push to `exp/perf` (being validated) or any lane; leave
`exp/threaded-poc-4` as it is (paused). Every instrument opt-in, default OFF, 0 B when off.

## The rule being tested (§18)

A system processing entity E writes E's components directly; any effect on ANOTHER entity is a command in a per-batch buffer played in a fixed
order after the phase; any read of another entity's component that some system WRITES in the same phase sees last tick's value. ⇒ parallel
per entity, and deterministic (independent of thread count).

## The items

| # | item | done when |
|---|---|---|
| **N-1** | ⭐ **per-entity attribution**: the tracer records the CURRENT entity per thread — set when a system's entity iteration yields an entity (the `EntityQuery` / `ForEach` / `ParallelEntities` paths, and BrainTick's per-unit loop); every component read / write / has, add / remove, create / destroy, bus publish is classified **own** (the current entity) / **other** / **no current entity** (a global system), by system, component type and call site, with sampled stacks | a rail: a test system reading and writing its own and a neighbour's components is classified exactly; 0 B when off |
| **N-2** | ⭐ **the census** on the 1 000-man CGF node (and the SimHost), 300+ frames playing: for EVERY system of the Simulation phase (and PostSimulation) | per system: own writes · ⭐ cross-entity writes (type, call site, per frame) · cross-entity READS of a type some system writes in the same phase (= the last-tick set) · structural changes (own / other) · bus publishes · entities created this frame and read later in it · "no current entity" accesses (= a global system or a missed hook) |
| **N-3** | **state outside components** per system: A-2's reach tool from each system's `Execute` — the shared dictionaries, caches and statics it mutates (`DecisionCadence`, dedup sets, `BlueprintSharedState`, `ObjectiveCensus.s_men` …) | a list per system: object · field · mutated how |
| **N-4** | ⭐ **classify every system**: **READY** (own writes only; cross reads only of types nobody writes in the phase) · **CONVERTIBLE** (the list of cross writes → commands, cross reads → last-tick, structural → deferred, shared state → per-thread / per-batch) · **GLOBAL** (not per-entity — needs its own parallel algorithm) — each with the conversion's size | the table, ordered by the system's Windows ms (§11.1) |
| **N-5** | **the last-tick view's cost**: the component types in the last-tick set — copy just those per tick vs the replica R (already synced for off-frame egress) as the read view | ms and MB per tick at 1 000 men for each option |
| **N-6** | **the brain's split**: a trace of BrainTick's remaining time (Windows ~42 ms) — scoring vs tree / HSM / blueprint execution vs actions, per behaviour kind | the share table |
| **N-7** | ⭐ **the prize model**: from the Windows per-system ms and N-4, the frame at 4 / 8 / 16 / 32 cores if every READY + CONVERTIBLE system ran per entity (Amdahl, the GLOBAL ones and the merge / playback / last-tick copy as the serial part) | the curve, and what bounds it |
| **N-8** | ⭐ **the verdict** — is the rule viable for the whole phase; the conversion list in order of payoff; the user calls it needs (e.g. a leader's orders reaching soldiers a tick later, like R-4208) | top of the status file; folded into Parallel Simulation §18 |

## Rules

- Measure before claiming; a "no cross-entity write" needs the census AND the reach tool. State what the hooks cannot see (raw pointers / spans
  taken once, `GetComponentTable`).
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** N-8 is written — however far N-1…N-7 got.

---
_coordinator lane, 2026-11-12_
