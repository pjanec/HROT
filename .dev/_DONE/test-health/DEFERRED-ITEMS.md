# Test-Health — Deferred Items (return-later tracker)

Two items from the TH-4 architect-decision pass are **intentionally left failing** and
parked here. They were investigated far enough to know the shape of the fix, but each
turned out to be a proper multi-step batch rather than a quick correction, and were
deferred to focus on the §4.4 MVP. **Do not silently re-green these** — they should stay
red until properly fixed. Companion: `DECISIONS.md` (D-8, D-13), `TEST-HEALTH.md` (ledger).

Status date: 2026-07-12. Main baseline: `380b4f59`.

---

## D-13 · DistributedTank (7) + ComponentDamage (5) — Scenarios suite

**Failing tests:** `Fdp.Examples.Scenarios.Tests`
- `DistributedTankScenarioPhaseATests.*` — 7 (Phase2 MuscleNodeMovesOnCommand, PhaseB
  BrainHullReachesActive_AtTick5, Phase2 LocoMsgConsumedViaDds, Phase3
  BrainTurretTracksHull_AtTick40, PhaseA RunToTick10_ExitsZero, PhaseB
  MuscleHasGhostForBrainHull, Phase4 SplitAuthorityBothChannelsActive).
- `ComponentDamageScenarioTests.*` — 5 (Phase2 HealthDecreases, Phase3 MoveFlagStripped,
  Phase4 LocomotionCleared_ByHSM, Phase5 WeaponStillFires, RunToCompletion_ExitsZero).

**Root cause (DistributedTank — verified, instrumented tick-by-tick):**
It is a **scenario-fixture translator-wiring gap, NOT an engine regression.** The
architect ruling D-13 (real regression, suspect commit `7c35badb`) is **wrong** —
`7c35badb`'s only touch to the promotion path was a mechanical `BitMask256→512` accessor
swap; the brain-hull ELM zero-participant auto-promote works (Active from tick 1).
`DistributedTankScenario.Configure` wires the Muscle-side `EntityLifecycleModule` /
`ReplicationLogicModule` with **no `ITkbEntityTranslator`s**, so the promoted CommandTank
ghost never gets the ECS physics components the template implies.

**Partial fix found (reverted — main stays at baseline):**
Wire two translators into the muscle ELM **and** `ReplicationLogicModule`:
```csharp
// DistributedTankScenario.cs ~line 331, Configure()
// usings: CarKinem.Tkb; Fdp.Toolkit.Spatial; Fdp.Interfaces (ITkbEntityTranslator)
var muscleTranslators = new ITkbEntityTranslator[]
{
    new SpatialCoreTkbTranslator(),      // adds SimTransform
    new VehicleKinematicsTkbTranslator(),// adds NavState (+ VehicleParams/State, PhysicsCollider)
};
var muscleReplicationElm = new EntityLifecycleModule(
    muscleTkb, Array.Empty<int>(), translators: muscleTranslators);
_muscleReplicationModule = new ReplicationLogicModule(
    _muscleEntityMap, muscleTkb, muscleReplicationElm, muscleTranslators);
```
Muscle is physics-only (split-authority) → do NOT wire brain/combat translators.

**Why still red:** with both translators wired, the `SetAuthority<SimTransform>` throw at
`DistributedTankScenario.cs:507` clears (2 lifecycle tests recover) but the same 7 still
exit code 1 for a **third gap** only visible in the scenario's own `FdpLog` output — which
`dotnet test` stdout does not capture. Needs a tracing run that surfaces `FdpLog`.

**Next batch:** (1) apply the 2-translator fix, (2) run with `FdpLog` captured to a file to
find the third checkpoint that fails, (3) close it, (4) separately investigate the 5
ComponentDamage tests — **not yet investigated, likely a different cause**.

Memory: `project-distributedtank-fixture-gap.md`.

---

## D-8 · Presentation `ctx.Resources` NRE — Fdp.Presentation.Tests

**Failing tests (13 of 162):** `Fdp.Toolkit.Vis2D.Tests` —
`DebugPrimitiveRenderer2DEntityLocalTests`, `DebugPrimitiveRenderer2DEntityLocalAllShapesTests`
(SC_GZ012_*, SC_GZ027_*), `DebugGizmoLayerActivationTests` (SC_GZ025_1/2/5),
`DebugGizmoLayerHitTests` (SC_GZ026_1/3/4). Plus **one test crashes the test host**
(blame-crash did not cleanly name it).

**State:** the architect decision D-8 was to cleanly extract the `eebd7d9e` fix
(null-guard `ctx.Resources` in `DebugPrimitiveRenderer2D` + `DebugGizmoLayer`, ~84-line
`DebugGizmoLayer` change) **and set `Resources` in the test `MakeCtx()`**.

**What was tried (reverted):** an agent applied only the production null-guards
(`ctx.Resources?.Get<MapCamera>()` + skip `_inner.Render` when null). That fixed most NREs
(**149/162 pass**) but the 13 above still fail — they exercise entity-local coordinate
translation / hit-testing that needs a **real `MapCamera` in `ctx.Resources`**, which the
test helpers do not provide. The agent skipped the test-side setup.

**Next batch:** keep the production guards, then update the shared test context helper
(`RenderTestHelpers.MakeCtx` in `Vis2D/Gizmos/DebugPrimitiveRenderer2DTests.cs`, and the
per-class `MakeCtx` in the failing files) to populate `RenderContext.Resources` with a
`MapCamera` so the 13 tests exercise the real path; **isolate the host-crashing test**
separately (run `--blame-crash` and inspect the Sequence xml / native dump).

---

## How to re-run (environment note)
`dotnet test <proj>.csproj -c Debug --nologo` — if `NU1301 "local source './nugets'
doesn't exist"`, create the empty folder first: `mkdir -p ./nugets` (the nuget.config
references a local feed that is not present in this checkout). Do **not** run multiple
`dotnet test` concurrently — they collide on shared dependency DLLs (`CS2012` file lock).
