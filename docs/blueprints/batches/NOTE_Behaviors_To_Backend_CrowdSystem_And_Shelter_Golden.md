<!--STATUS
state: LIVE — behaviors → backend: answers to two notes.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5f.1 the CrowdSystem grid (as built), §7.4a the perf gate's numbers
-->

# NOTE — behaviors → backend: `CrowdSystem` scaling fixed · `FindEnterableShelter` golden added

**① [NOTE_Backend_To_Behaviors_CrowdSystem_Scaling](NOTE_Backend_To_Behaviors_CrowdSystem_Scaling.md)** — fixed. Cause: every-pair work
inside one town-sized crowd (a component read per pair). A 20 m cell grid, same results by construction
([§5f.1](../../DESIGN_Civilians_And_Crowd_Control.md)). `python3 scripts/perf-civilians.py 300 500` at 500: **`CrowdSystem` 2.79 ms**
(max 5.2; was 103.6), **CGF all systems 88.8** (was 338.0), 494 frames in 60 s (was 322). Folded into §7.4a (your section — one
paragraph under your table, said here). Next on CGF at 500: `TogglableSimulationGroup` 19.9, `CycloneEgressSystem` 13.7,
`RecorderTickSystem` 13.4 (`CE-3243`), `OwnershipRecomputeSystem` 10.4.

**② [NOTE_Backend_To_Behaviors_EnterableShelter_Golden](NOTE_Backend_To_Behaviors_EnterableShelter_Golden.md)** — added:
`EqsFlatTerrainGoldenTests` scenario for `FindEnterableShelter` (two bt-range `house-2f` on flat ground, the south one's front locked;
`CaptureOnFlatGround` gained an optional `terrain`), golden `FindEnterableShelter.flat.golden.json` (3 candidates).
`EqsFlatTerrainGoldenTests` 13/13, `RegisteredTemplates_AllHaveAGoldenScenario` green.

**Ids allocated:** none.
