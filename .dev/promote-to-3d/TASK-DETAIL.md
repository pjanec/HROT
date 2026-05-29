# TASK-DETAIL — 3D Cognitive Spatial Awareness Promotion

Detailed per-task specifications for promoting `SimTransform.Position.Z` to the authoritative physical
altitude and carrying it through EQS, perception/`TargetMemory`, pathing cost, and the position-carrying
translators, as described in
[3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md](./3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md).

**DESIGN reference shorthand used throughout this file:**
- "Design §N" = section N of
  [3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md](./3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md).
- "Change-table row X" = the corresponding row of Design §4 (the architect's consolidated enumeration).

**Companion documents:**
- Progress: [TASK-TRACKER.md](./TASK-TRACKER.md)
- Carried-over technical debt: [DEBT-TRACKER.md](./DEBT-TRACKER.md)
- Newcomer onboarding: [ONBOARDING.md](./ONBOARDING.md)
- Developer rules of engagement: [../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md)

---

## 0. Read this first — facts the design assumes, verified against the codebase

This task set was written after verifying every claim of the design against the current code. Three
facts are load-bearing for *correctness* and a developer who misses them will silently reintroduce the
flat-earth bug. They are stated here once and referenced by task.

### 0.1 Two coordinate conventions coexist — altitude is NOT always the third component

| Space | Used by | X | Y | Z |
|---|---|---|---|---|
| **Sim / EQS (Z-up)** | `SimTransform.Position`, `EqsResult`, `CoverPoint`, `TargetMemory`, `NavState`, trajectory pool, `DistanceScoreTest` | East | North | **Altitude** |
| **Navmesh / Recast (Y-up)** | `INavmeshProvider` (`ProjectToNavmesh`/`PathCost`/`SampleNavmeshPoints`), `NavWaypoint.Position`, `RoutePlan` waypoints | East | **Altitude** | North |

Consequences that MUST be honored:

- The `new Vector3(x, 0f, y)` reconstructions in `NavmeshReachableTest` / `PathCostScoreTest` /
  `NavmeshSamplesGenerator` are in **Recast (Y-up)** space. The `0f` is the **altitude (Y) slot**.
  The 3D fix is `new Vector3(PositionX, PositionZ, PositionY)` — the EQS altitude (`PositionZ`) goes
  into the **middle** component, NOT appended as the third. Writing `new Vector3(X, Y, Z)` is WRONG.
- `NavmeshSamplesGenerator` reads navmesh points (Y-up) and maps `PositionX = pt.X`, `PositionY = pt.Z`.
  The dropped axis today is the navmesh **Y (altitude)**. The 3D fix adds `PositionZ = pt.Y`.
- `RouteTrajectorySyncSystem` builds `new Vector2(wp.Position.X, wp.Position.Z)` from a Recast (Y-up)
  `RoutePlan` waypoint. Carrying altitude means `new Vector3(wp.Position.X, wp.Position.Z, wp.Position.Y)`
  into the (Z-up) trajectory pool.
- `DistanceScoreTest` is already in Sim (Z-up) space (`new Vector2(Position.X, Position.Y)`), so its 3D
  fix is the simple one: `new Vector3(Position.X, Position.Y, Position.Z)`.

### 0.2 The navigation/trajectory layer is pervasively 2D — scope was decided explicitly

- `MoveToParams.Destination`, `NavigationIntent.FinalDestination`, `EcsNavigationIntent.FinalDestination`,
  `NavState.FinalDestination` are all `Vector2`.
- `TrajectoryWaypoint.Position` / `CustomTrajectory` and ALL Hermite/Catmull-Rom spline + arc-length +
  `SampleTrajectory` math are `Vector2`. The pool feeds the core vehicle integrator
  (`CarKinematicsSystem`), `FormationTargetSystem`, `RouteTrajectorySyncSystem`, and trajectory rendering.
- `NavWaypoint.Position` (the navmesh planner's output) is **already `Vector3`** — altitude exists and
  is *discarded* at the trajectory-pool / intent / `NavState` boundaries.

**Decision (review):** this PR widens the destination/intent **data carriers** and the trajectory-pool
**storage** to carry Z, but the **spline curvature math and `CarKinematicsSystem` steering stay
2D-projected** (Z carried + linearly interpolated for position output, NOT fed into vehicle dynamics).
`SimTransform.Position.Z` remains owned by `TerrainQueryResolutionSystem` (Tier 1). See Phase 3.

### 0.3 Generators named in the design that do NOT exist

Design §3 lists a "v1.3 set": `Self`, `Donut`, `Grid`, `Cone`, `EntitiesInArea`, `EntitiesInRadius`,
`NavmeshSamples`, `CoverPoints`, `OffsetFromContext`. **Only three exist** as production
`IEqsGenerator`s: `EntitiesInRadiusGenerator`, `NavmeshSamplesGenerator`, `CoverPointsGenerator`.
The rest are not implemented (EQS uses an `[EqsTemplate]` source-generated registry, not a class per
shape). **Generator work is scoped to the three that exist** (P3D-203); the others are N/A — see the
coverage matrix.

### 0.4 Amendments to the design adopted by this task set

| Design item | As written | Adopted in tasks |
|---|---|---|
| O-1 `GroundClampingState` | "delete entirely" | **Slim, don't delete.** Drop the visual-offset fields (`TargetZOffset`, `CurrentZOffset`); keep `LastValidIgAltitude` + `IgAltitudeBaselineEstablished` so `TerrainQueryResolutionSystem` jump-rejection survives. Rename to reflect the reduced role. (P3D-101/102) |
| Tier 3 nav translators | "drop the `0f`" | Requires widening the 2D destination chain + trajectory-pool storage (see §0.2). Expanded into P3D-302/303/304. |
| Tier 2 generators | 9 generators | 3 real generators only (see §0.3). |

---

## 0.5 Design coverage matrix (every design change mapped to a task)

| Design §4 row / item | Task |
|---|---|
| Tier 1 — `TransformSyncSystem` stop Z=0 / drop visual offset | P3D-103 |
| Tier 1 — `TerrainQueryResolutionSystem` write `HitZ` → `SimTransform.Position.Z` | P3D-102 |
| Tier 1 — `GroundClampingState` removed (amended: slimmed) | P3D-101 |
| Tier 1 — dead-reckoning on live Z (risk concentrator §8) | P3D-104 |
| Tier 2 — `EqsResult` add `PositionZ` (→32 B) | P3D-201 |
| Tier 2 — `EqsResultEntry` (DDS) carry altitude | P3D-202 |
| Tier 2 — `EqsCognitiveBuffer` 3D + `MemoryMarshal` access | P3D-201 |
| Tier 2 — `NavmeshSamplesGenerator` retain sampled Z | P3D-203 |
| Tier 2 — `EntitiesInRadiusGenerator` stop flattening | P3D-203 |
| Tier 2 — geometric generators (`Donut`/`Grid`/`Cone`/`OffsetFromContext`) | **N/A — not implemented** (§0.3) |
| Tier 2 — entity generators (`Self`/`EntitiesInArea`) | **N/A — not implemented** (§0.3) |
| Tier 2 — `CoverPoint`/`ICoverProvider` widen to 3D | P3D-204 |
| Tier 2 — `CoverPointsGenerator` stream 3D | P3D-203 |
| Tier 2 — `DistanceScoreTest` → `Vector3.Distance` | P3D-205 |
| Tier 2 — `NavmeshReachableTest`/`PathCostScoreTest` use real Z | P3D-205 |
| Tier 2 — `TargetMemory` widen to 3D | P3D-206 |
| Tier 2 — `ThreatEvaluationSystem` pass real Z | P3D-206 |
| Tier 3 — `PathCost` uniformly 3D | P3D-301 |
| Tier 3 — `StubNavmeshProvider` 3D distance | P3D-301 |
| Tier 3 — `NavigationIntentEgressTranslator` drop geodetic `Z=0` | P3D-304 |
| Tier 3 — `PathResponseSolverEgressTranslator` drop `Up=0f` | P3D-304 |
| Tier 3 — `NavigationIntentBridgeSystem` drop destination `…,0f` | P3D-302 |
| Tier 3 — ingress translators populate Z from geodetic altitude | P3D-304 |
| Tier 3 — destination chain + trajectory pool 3D (implied by above) | P3D-302, P3D-303 |
| Tier 3 — `EqsCognitiveBufferRenderer` render Z | P3D-401 |
| Tier 3 — `EqsSensorGizmo` verify Z used | P3D-401 |
| Ripple §5 — Utility AI `TargetMemory` readers | **OUT — separate PR** (`../group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md`) |
| §6 Axis-1 — flat-terrain golden parity | P3D-001 (capture), P3D-403 (assert) |
| §6 Axis-2 — multi-level proof fixture | P3D-402 |
| §6 Axis-3 / §8 — dead-reckoning on slopes/steps | P3D-104 |
| §7 — Flight Recorder break coordination | P3D-405 |
| O-2 — fixture deck clearance > `walkableHeight` | P3D-402 |
| O-3 — mandatory `, 0f)` pre-merge grep sweep | P3D-404 |

> **Atomicity (Design §7):** all P3D-1xx/2xx/3xx tasks land in **one PR** with the §6 regression gate
> (P3D-403 + P3D-402 + P3D-104) green. The phases below are a build/review order, not separate merges.
> Building/testing intermediate tasks is expected; **merging** a partial state is not (it would inject
> the exact `0f`-Z bugs being removed and crash DDS deserialization across the Brain/Muscle boundary).

---

## Phase 0 — Baseline capture (must run BEFORE any change)

### P3D-001 — Capture flat-terrain golden `EqsCognitiveBuffer` baseline

**Design reference:** Design §6 Axis-1.

**Scope (IN):**
- A harness/test that, on a **flat** test map (constant terrain Z), runs every registered
  `[EqsTemplate]` starter template against a fixed scenario and serializes the resulting
  `EqsCognitiveBuffer` Top-K rows: `EntityId`, `PositionX`, `PositionY`, `Score` (and `Flags`/
  `FlagsMeaningful`) to a committed golden artifact.
- Determinism harness: fixed seed/tick so the capture is reproducible.

**Scope (OUT):** No production code changes. No `PositionZ` yet (that arrives in P3D-201; this captures
the **pre-change** 2D baseline).

**Constraints:**
- Capture must run on `main`/pre-change tree so it records true legacy behavior.
- Golden artifact lives under this workstream's test fixtures and is referenced by P3D-403.

**Success conditions:**
1. Golden files exist for all registered starter templates (enumerate via the `[EqsTemplate]` registry;
   do not hardcode a count).
2. Re-running the capture twice on the unchanged tree produces byte-identical artifacts (determinism).
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

## Phase 1 — Tier 1: Authoritative altitude (the root)

> Design §3 Tier 1, §8, §9 O-1. This phase is the **risk concentrator**: it removes the wall that today
> keeps a moving Z out of dead-reckoning. Read Design §8 before starting.

### P3D-101 — Slim `GroundClampingState` to a terrain-clamp baseline component

**Design reference:** Design §3 Tier 1, §9 O-1 (amended — see §0.4).

**Scope (IN):**
- Remove the visual-offset fields `TargetZOffset` and `CurrentZOffset` from `GroundClampingState`
  (`FDP/Toolkits/Fdp.Toolkits/Geographic/Components/GroundClampingState.cs`).
- Retain `LastValidIgAltitude` (float) and `IgAltitudeBaselineEstablished` (byte) — the jump-rejection
  baseline state.
- Rename the struct to reflect its reduced role (suggested: `TerrainClampBaseline`); update its
  `[ComponentId]` entry in `GeographicComponentIds` and all references.

**Scope (OUT):** No behavior change to jump-rejection logic itself (that is P3D-102). No new fields.

**Constraints:**
- Keep it a blittable unmanaged struct.
- The XML docs must no longer describe a "visual Z correction"; they describe jump-rejection baseline only.

**Success conditions:**
1. The struct contains exactly `LastValidIgAltitude` + `IgAltitudeBaselineEstablished` (+ explicit
   padding); a unit test asserts it is an unmanaged value type.
2. No symbol named `TargetZOffset` or `CurrentZOffset` remains anywhere in the solution (grep clean).
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-102 — `TerrainQueryResolutionSystem` writes `HitZ` into authoritative `SimTransform.Position.Z`

**Design reference:** Design §3 Tier 1 (the inversion), §8.

**Scope (IN):**
- In `FDP/Toolkits/Fdp.Toolkits/Geographic/Systems/TerrainQueryResolutionSystem.cs`, on an accepted hit
  write `res.HitZ` into `SimTransform.Position.Z` (authoritative) instead of computing
  `TargetZOffset = HitZ − ReferenceSimZ` into the (now-removed) visual-offset field.
- Keep jump-rejection: compare `res.HitZ` against the entity's prior accepted altitude using the slimmed
  `TerrainClampBaseline.LastValidIgAltitude` + `IgAltitudeBaselineEstablished` (bootstrap = first hit
  always accepted), with the existing `JumpRejectionThresholdMeters = 5f` guard. Update
  `LastValidIgAltitude` to `res.HitZ` on accept.

**Scope (OUT):** No visual offset. No change to terrain raycast batching (`TerrainQueryBatchData`).

**Constraints:**
- Must use the command buffer to set both `SimTransform` (Z component only — preserve X/Y/rotation) and
  the baseline component, consistent with the existing ECS write discipline.
- Bootstrap path (`IgAltitudeBaselineEstablished == 0`) must still accept the first hit (sea-level worlds
  use `LastValidIgAltitude = 0` as a valid altitude — do not infer "unset" from the value alone).

**Success conditions:**
1. Unit test: an accepted hit with `HitZ = h` results in `SimTransform.Position.Z == h` (X/Y unchanged).
2. Unit test: a second hit within `±5 m` of the baseline is accepted and updates Z; a hit beyond `±5 m`
   (non-bootstrap) is rejected and leaves `SimTransform.Position.Z` unchanged.
3. Unit test: the first hit for an entity is always accepted regardless of magnitude (bootstrap).
4. Existing `TerrainQueryResolutionSystemTests` are updated to the new contract and pass.
5. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-103 — `TransformSyncSystem` stops applying the visual-only Z correction

**Design reference:** Design §3 Tier 1.

**Scope (IN):**
- In `FDP/Examples/Fdp.Examples.Common/Systems/TransformSyncSystem.cs`, remove the
  `GroundClampingState` block in `SyncRemoteEntities` that rewrites `smoothed.Z` to
  `netTf.LastPosition.Z + newCurrentOffset`. Remote smoothing must lerp the full authoritative
  `Vector3` (including Z) toward `NetworkTransform.LastPosition`.
- Remove the `groundClampZSmoothingRate` constructor parameter and field (no longer used).
- Any render sub-frame smoothing now uses standard transform interpolation on the authoritative Z; no
  terrain offset path remains.

**Scope (OUT):** No change to owned-entity copy (`SyncOwnedEntities`) other than that it already copies
the full `Vector3` Position. No change to the `driveFromNetwork` replay path semantics beyond Z now being
real.

**Constraints:**
- `SimTransform.Position.Z` is never forced to `0f` and never overwritten by a derived offset.
- `TransformSyncSystemGroundClampingTests` must be rewritten/retired to reflect that no visual offset is
  applied.

**Success conditions:**
1. Unit test: a remote entity whose `NetworkTransform.LastPosition.Z = h` smooths its
   `SimTransform.Position.Z` toward `h` (no offset added).
2. No reference to `GroundClampingState`/`TerrainClampBaseline` remains in `TransformSyncSystem`.
3. `TransformSyncSystemRegistrationTests` still pass (interpolation of all entities; owned-not-overwritten).
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-104 — Dead-reckoning regression fixture on slopes and steps (Axis-3 risk probe)

**Design reference:** Design §6 Axis-3, §8.

**Scope (IN):**
- An integration fixture: an entity traverses a **sloped** ramp onto a **stepped** deck with a live,
  changing authoritative Z. Assert predicted vs. authoritative position stays within tolerance and that
  replication across nodes does not jitter or diverge in Z.

**Scope (OUT):** Not a unit test of a single system; this is the cross-system safety probe for Tier 1.

**Constraints:**
- Must exercise the `driveFromNetwork`/remote smoothing path (P3D-103) with non-zero, varying Z.
- Tolerance bands documented in the test; chosen to fail if a flat-Z assumption silently re-enters
  prediction.

**Success conditions:**
1. On the ramp, predicted Z tracks authoritative Z within the documented tolerance every tick.
2. On the step transition, no overshoot/oscillation beyond tolerance; replication converges.
3. The fixture is part of the §6 regression gate (referenced by the atomic-PR merge checklist).
4. `dotnet build IOS-IG-SimHost.sln` succeeds and the fixture passes.

---

## Phase 2 — Tier 2: Cognitive spatial awareness reads the real Z

> Design §3 Tier 2, §7 (atomic struct/DDS coupling), §8.1 (the `[InlineArray]` defensive-copy trap).

### P3D-201 — Widen `EqsResult` to carry `PositionZ` (24 B → 32 B) and update the buffer

**Design reference:** Design §3 Tier 2 (EQS result path), §7, §8.1.

**Scope (IN):**
- Add `public float PositionZ;` to `EqsResult`
  (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`). World-space altitude (Sim Z-up).
- Verify `EqsResultArray` (`[InlineArray(16)]`) and `EqsCognitiveBuffer` footprints follow:
  24→32 B per entry; `EqsResultArray` 384→512 B; buffer accessors unchanged (`GetSpanRW/RO` via
  `MemoryMarshal.CreateSpan`/`Unsafe.As`).
- Update all size-assertion tests (e.g. any `Marshal.SizeOf<EqsResult>() == 24`) to `32`.

**Scope (OUT):** No DDS change here (P3D-202). No generator/scoring use of `PositionZ` yet (P3D-203/205).
**No 2D/3D toggle** — the widening is unconditional (Design §3 Tier 2).

**Constraints:**
- `EqsResult` stays `[StructLayout(LayoutKind.Sequential)]`; `long EntityId` governs 8-byte alignment so
  28 raw rounds to 32. Field order: keep `EntityId, PositionX, PositionY, PositionZ, Score, Flags,
  FlagsMeaningful` (place `PositionZ` adjacent to X/Y).
- `[InlineArray]` writes MUST continue to go through `GetSpanRW()` — direct indexer assignment is
  forbidden (Design §8.1).

**Success conditions:**
1. `Marshal.SizeOf<EqsResult>()` returns `32`.
2. A test writes `span[i].PositionZ`, re-reads via `GetSpanRO()`, asserts retention (defensive-copy path
   still bypassed).
3. A test asserts `Marshal.SizeOf<EqsResultArray>() == 512`.
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-202 — Widen the `EqsResultEntry` DDS wire + EQS result translators to carry altitude

**Design reference:** Design §3 Tier 2 (`EqsResultEntry` carries altitude), §7 (atomic with the struct).

**Scope (IN):**
- Add `public float PositionZ;` to `EqsResultEntry`
  (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs`, `[DdsStruct][DdsIdlFile("hrot-eqs-msgs")]`)
  and regenerate the DDS/IDL artifacts for `hrot-eqs-msgs`.
- `EqsResultEventEgressTranslator` (`Hrot.Network.NED/SimHost/`): write `PositionZ` from the
  `EqsResult`/result event.
- `EqsResultIngressTranslator` (`Hrot.Network.NED/CGF/`): populate `EqsResult.PositionZ` from the wire.

**Scope (OUT):** No change to `EqsResultEntry.Flags`/`FlagsMeaningful` (`ushort` — unchanged).

**Constraints:**
- Egress and ingress MUST change together with the struct (P3D-201); a partial state crashes
  deserialization across the Brain/Muscle boundary (Design §7).
- Regenerated IDL must be committed.

**Success conditions:**
1. Round-trip test: an `EqsResult` with `PositionZ = z` published via egress and read via ingress yields
   `PositionZ == z` (tolerance-exact for float).
2. Existing EQS DDS round-trip tests pass with the new field populated.
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-203 — Generators retain real Z (the three existing production generators)

**Design reference:** Design §3 Tier 2 (Generators), §0.1 (coordinate conventions), §0.3 (scope).

**Scope (IN):**
- `EntitiesInRadiusGenerator`: the spatial grid returns 2D neighbor positions, so source altitude from
  each neighbor's `SimTransform.Position.Z` and write it into `EqsResult.PositionZ` (observer-self
  excluded as today). Keep `PositionX/Y` from the grid.
- `NavmeshSamplesGenerator`: build the navmesh query center as `new Vector3(tf.Position.X,
  tf.Position.Z, tf.Position.Y)` (Sim→Recast) and write `PositionZ = rawPoints3D[i].Y` (Recast altitude
  → EQS altitude) alongside the existing `PositionX = pt.X`, `PositionY = pt.Z`.
- `CoverPointsGenerator`: stream `PositionZ` from the now-3D `CoverPoint` (depends on P3D-204).

**Scope (OUT):** `Self`, `Donut`, `Grid`, `Cone`, `EntitiesInArea`, `OffsetFromContext` — **do not exist**
(§0.3); no work. No new vertical-band parameter (Design §3 Tier 2).

**Constraints:**
- Honor §0.1: the navmesh center/sample mapping puts altitude in the Recast **Y** slot, never the third
  component of an EQS-space vector.
- `EntitiesInRadiusGenerator`'s per-neighbor `SimTransform` lookup must skip neighbors lacking
  `SimTransform` (defensive), matching existing null/observer handling.

**Success conditions:**
1. `EntitiesInRadiusGenerator`: a neighbor at `SimTransform.Position.Z = z` produces a candidate with
   `PositionZ == z`; `PositionX/Y` unchanged from the 2D behavior.
2. `NavmeshSamplesGenerator`: with a stub navmesh returning a point at Recast `Y = a`, the candidate has
   `PositionZ == a`, `PositionX/PositionY` matching the prior 2D mapping.
3. `CoverPointsGenerator`: a 3D cover point at altitude `z` produces a candidate with `PositionZ == z`.
4. Existing generator tests updated and passing; `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-204 — Widen `CoverPoint` + `ICoverProvider` family to 3D

**Design reference:** Design §3 Tier 2 (Sources).

**Scope (IN):**
- Add `public float PositionZ;` to `CoverPoint` (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CoverPoint.cs`),
  adjusting explicit padding (24 B → 28 B; floats/byte only, 4-byte alignment).
- Update `ICoverProvider` / `ManualCoverProvider` (and any test cover provider, e.g. `MockCoverProvider`)
  so cover nodes carry/return altitude.
- Update the `CoverPoint_IsExactly24Bytes` test to assert **28** bytes (rename accordingly).

**Scope (OUT):** No LOS/quality semantics change; only the position widening.

**Constraints:**
- `CoverPoint` stays `[StructLayout(LayoutKind.Sequential)]`; recompute padding so the struct is exactly
  28 bytes with 4-byte alignment (there is no 8-byte member here, unlike `EqsResult`).

**Success conditions:**
1. `Marshal.SizeOf<CoverPoint>()` returns `28`.
2. A cover provider returns a node with `PositionZ = z` and a consumer reads it back unchanged.
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-205 — Scoring/filter tests use real Z (with correct axis mapping)

**Design reference:** Design §3 Tier 2 (Scoring/filter tests), §0.1.

**Scope (IN):**
- `DistanceScoreTest` (Sim Z-up): replace the `Vector2(X,Y)` planar falloff with
  `Vector3.Distance(new Vector3(obs.X,obs.Y,obs.Z), new Vector3(c.PositionX,c.PositionY,c.PositionZ))`.
- `NavmeshReachableTest` and `PathCostScoreTest` (Recast Y-up): replace `new Vector3(X, 0f, Y)` for both
  observer and candidate with `new Vector3(PositionX, PositionZ, PositionY)` — altitude in the middle
  slot (§0.1).

**Scope (OUT):** No change to falloff curves, thresholds, or `Flags`/`FlagsMeaningful` bit semantics.

**Constraints:**
- On flat terrain (constant Z) the 3D distance must equal the prior 2D distance to within float
  tolerance — this is what P3D-403 verifies.

**Success conditions:**
1. `DistanceScoreTest`: two candidates identical in X/Y but differing in `PositionZ` receive different
   scores; with equal Z, scores equal the legacy 2D result.
2. `PathCostScoreTest`/`NavmeshReachableTest`: the observer/candidate vectors passed to the navmesh place
   altitude in the Recast Y component (verified via a recording stub provider).
3. Existing scoring tests updated; flat-terrain cases unchanged. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-206 — Widen `TargetMemory` to 3D contacts; `ThreatEvaluationSystem` passes real Z

**Design reference:** Design §3 Tier 2 (Perception boundary), §5 (ripple), §7.

**Scope (IN):**
- Add a `PositionsZ` parallel array to `TargetMemory`
  (`FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs`), mirroring `PositionsX/Y`
  in the add/update, eviction, and insertion-sort code paths.
- Add a `posZ` parameter to `TargetMemory.AddOrUpdateTarget(...)` and store it. Update **all 21 callers**
  to pass real altitude (from `SimTransform.Position.Z` / the contact's 3D position).
- `ThreatEvaluationSystem`: pass the contact's real Z into `AddOrUpdateTarget` instead of X/Y only.
- Verify `TargetMemoryTranslator`: if it carries contact **positions** over the wire, carry Z; if it only
  emits `InitialTargetsIntent`/scalar threat (no live positions), note "no position on wire — no change"
  in the task report.

**Scope (OUT):** Utility AI `TargetMemory` readers — **explicitly deferred** to
`../group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md` (Design §5). Do not modify Utility
readers here.

**Constraints:**
- The widening is **additive** (Z appended; X/Y semantics unchanged) so callers that legitimately have no
  altitude on flat maps pass the contact's `SimTransform.Position.Z` (which is `≈0` on flat terrain).
- `TargetMemory` must remain a blittable unmanaged value type.

**Success conditions:**
1. `AddOrUpdateTarget` stores and round-trips `posZ`; eviction and the descending insertion-sort move the
   Z slot in lockstep with X/Y (unit test with ≥`MaxTrackedTargets+1` entries).
2. `ThreatEvaluationSystem` writes a contact whose `PositionsZ[slot]` equals the source
   `SimTransform.Position.Z`.
3. All 21 `AddOrUpdateTarget` call sites compile and pass `posZ`; `TargetMemory_IsUnmanagedValueType`
   passes.
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

## Phase 3 — Tier 3: Cost, destination, and trajectory symmetry

> Design §3 Tier 3, §0.1, §0.2. Carriers + storage carry Z; spline/steering stay 2D-projected.

### P3D-301 — `PathCost` uniformly 3D (`StubNavmeshProvider`)

**Design reference:** Design §3 Tier 3 (`PathCost`, `StubNavmeshProvider`).

**Scope (IN):**
- `StubNavmeshProvider.PathCost` (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs`): add the
  vertical term so cost is true 3D Euclidean distance (`sqrt(dx²+dy²+dz²)`), matching
  `FakeNavmeshProvider.PathCost`. (`INavmeshProvider.PathCost` already takes `Vector3`.)

**Scope (OUT):** No change to the production navmesh provider's `PathCost` (already 3D via Recast); no
signature change.

**Constraints:** Recast Y-up — the added term is the Y (altitude) delta; do not drop X or Z.

**Success conditions:**
1. `StubNavmeshProvider.PathCost(from, to)` for `from=(0,0,0)`, `to=(3,4,0)` returns `5`; for
   `to=(0,12,0)` (altitude only) returns `12` (previously `0`).
2. A `PathCostScoreTest` over the stub now penalizes a candidate that differs only in altitude.
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-302 — Widen the navigation destination/intent chain to `Vector3`

**Design reference:** Design §3 Tier 3 (`NavigationIntentBridgeSystem`), §0.2.

**Scope (IN):**
- Widen to `Vector3` (Sim Z-up): `MoveToParams.Destination`, `NavigationIntent.FinalDestination`,
  `EcsNavigationIntent.FinalDestination`, `NavState.FinalDestination`.
- Update the producers/consumers: `MoveToExecutor`, `PlanRouteExecutor`, and
  `NavigationIntentBridgeSystem` (the three `new Vector3(p.Destination.X, p.Destination.Y, 0f)` sites:
  `PathfindingRequestEvent.End` for `MoveTo` and `PlanRoute`, and the crowd `SetAgentTarget` destination)
  — carry the real destination Z instead of `0f`.
- `PathfindingRequestEvent.Start` already uses `SimTransform.Position` (now 3D) — verify it flows.

**Scope (OUT):** Spline math and `CarKinematicsSystem` steering (§0.2 — stays 2D-projected; P3D-303
covers storage). Blueprint authoring UI for the destination remains 2D-authored unless trivially
adaptable; if a blueprint param schema hash changes, record it (codegen drift) in the task report.

**Constraints:**
- Struct sizes/layouts: re-verify `MoveToParams` (currently 32 B with `Vector2 Destination`) and
  `NavState` after the `Vector2`→`Vector3` change; update any size-assertion tests.
- The `NavigationIntentBridgeSystem` crowd path (`_dtCrowd.SetAgentTarget`) must pass the 3D destination.

**Success conditions:**
1. `MoveToExecutor` writes a `NavigationIntent.FinalDestination` whose Z equals the action's destination Z.
2. `NavigationIntentBridgeSystem` publishes a `PathfindingRequestEvent.End` with the real destination Z
   (no `0f`) for both `MoveTo` and `PlanRoute`; `NavigationIntentBridgeSystemTests` updated and passing.
3. No `new Vector3(..Destination.X, ..Destination.Y, 0f)` remains in `NavigationIntentBridgeSystem`.
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-303 — Trajectory pool stores + interpolates Z (steering stays 2D)

**Design reference:** Design §3 Tier 3, §0.2.

**Scope (IN):**
- Widen `TrajectoryWaypoint.Position` and `CustomTrajectory` waypoint storage to `Vector3` (Sim Z-up).
- `TrajectoryPoolManager`: `RegisterTrajectory` / `RegisterTrajectoryWithKey` accept `Vector3[]`
  positions; store Z. `SampleTrajectory` returns the position with **Z linearly interpolated** between
  bracketing waypoints; the **tangent/heading and arc-length/Hermite/Catmull-Rom curvature continue to be
  computed on the X/Y projection only**.
- `RouteTrajectorySyncSystem`: build positions as `new Vector3(wp.Position.X, wp.Position.Z,
  wp.Position.Y)` (Recast→Sim, §0.1) from the 3D `RoutePlan` waypoints.
- `CarKinematicsSystem.SampleCustomTrajectory`: propagate the 3D sample; **do not** override
  `SimTransform.Position.Z` from the trajectory — `TerrainQueryResolutionSystem` (P3D-102) remains the
  authoritative Z writer. The trajectory Z is carried for fidelity/inspection/replication, not steering.

**Scope (OUT):** No change to vehicle dynamics, speed control, or steering geometry (all stay XY).
`FormationTargetSystem` adapts to the new sample signature but keeps 2D following behavior.

**Constraints:**
- Flat-terrain behavior must be byte-identical for X/Y motion (P3D-403). The only new information is the
  carried Z.
- Hermite/Catmull-Rom helper signatures may change to accept `Vector3` for storage, but their curvature
  output must equal the prior `Vector2` result on the X/Y projection.

**Success conditions:**
1. `TrajectoryWaypoint.Position` is `Vector3`; a registered trajectory with endpoints at `Z=0` and `Z=10`
   sampled at the midpoint returns `pos.Z ≈ 5` (linear), while `pos.X/pos.Y` match the legacy 2D sample.
2. `RouteTrajectorySyncSystem` carries `RoutePlan` altitude into the pool (Recast Y → Sim Z mapping
   verified).
3. `CarKinematicsSystem` produces identical X/Y motion to the 2D baseline on flat terrain (regression);
   `SimTransform.Position.Z` is governed by the terrain query, not the trajectory.
4. Existing `TrajectoryPoolTests` / `HermiteTrajectoryTests` / `TrajectoryInterpolationTests` updated to
   `Vector3` and passing. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-304 — Navigation egress/ingress translators carry real altitude

**Design reference:** Design §3 Tier 3 (egress translators + ingress siblings), §0.1.

**Scope (IN):**
- `NavigationIntentEgressTranslator`: replace `ToGeodetic(new Vector3(FinalDestination.X,
  FinalDestination.Y, 0f))` with the real 3D `FinalDestination` (now `Vector3` from P3D-302); the wire
  `GeoPoint.Altitude` carries it.
- `NavigationIntentIngressTranslator`: replace `new Vector2(cartesian.X, cartesian.Y)` with the full 3D
  Cartesian (`ToCartesian` already returns altitude) into the now-`Vector3`
  `EcsNavigationIntent.FinalDestination`.
- `PathResponseSolverEgressTranslator`: stop flattening — the anchor `new Vector3(firstPos.X, firstPos.Y,
  0f)` and each waypoint `Up = 0f` must use the real trajectory-waypoint Z (now `Vector3` from P3D-303).
- `PathResponseBrainIngressTranslator`: reconstruct 3D waypoints — `positions[w] = anchor3D +
  new Vector3(rel.East, rel.North, rel.Up)` — and register them into the now-`Vector3` pool (P3D-303).
  `RelativeVector3` already carries `Up`.

**Scope (OUT):** No DDS schema change to `GeoPoint`/`RelativeVector3` (both already carry altitude/`Up`).

**Constraints:**
- These are genuine "stop discarding altitude that's already on the wire" fixes — the wire types already
  carry it (Design §1).
- Egress/ingress must change together (Design §7).

**Success conditions:**
1. `NavigationIntent` round-trip: a destination at altitude `a` survives egress→ingress with
   `FinalDestination.Z ≈ a` (within geodetic round-trip tolerance).
2. `PathResponse` round-trip: a planned waypoint at altitude `a` survives egress→ingress with the
   reconstructed pool waypoint `Position.Z ≈ a` (no `Up=0f`).
3. `NavigationIntentEgressTranslatorTests` and path-response translator tests updated and passing.
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

## Phase 4 — Presentation, proof, and the merge gate

### P3D-401 — Presentation renders/uses Z

**Design reference:** Design §3 Tier 3 (Presentation).

**Scope (IN):**
- `EqsCognitiveBufferRenderer` (`Hrot.IG/Gizmos/`): format/display `PositionZ` alongside X/Y.
- `EqsSensorGizmo` (`Hrot.IG/Gizmos/`): confirm Top-K lines read the new Z (extruded/3D draw; better for
  multi-level debug).

**Scope (OUT):** No new gizmo features beyond surfacing Z.

**Constraints:** Read-only consumers of the buffer; must not mutate results.

**Success conditions:**
1. The renderer output includes the Z value for each Top-K row (unit/snapshot test or documented manual
   verification with a screenshot in the task report).
2. `EqsSensorGizmo` draws using `PositionZ` (verified via the gizmo's vertex/line data in a test or
   documented manual check).
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

### P3D-402 — Multi-level proof fixture (Axis-2)

**Design reference:** Design §6 Axis-2, §9 O-2.

**Scope (IN):**
- A bridge-over-street fixture: a cover/EQS query under the bridge returns `Z≈0` candidates; on the deck
  returns `Z≈(deck height)` candidates; Recast's 3D snap disambiguates two surfaces overlapping in X/Y.
- **O-2 hard requirement:** the fixture's deck clearance MUST strictly exceed the agent's configured
  Recast `walkableHeight`, or the 3D snap is not valid and the test is meaningless. Bake this into the
  fixture spec and assert it.

**Scope (OUT):** Not a flat-map test (that is P3D-403).

**Constraints:** Uses the real (or DotRecast-backed) navmesh provider so `ProjectToNavmesh`/3D snap is
exercised, not the stub.

**Success conditions:**
1. Two candidates sharing X/Y but on different levels are produced with distinct `PositionZ` and are not
   merged/confused.
2. The fixture asserts `deckClearance > walkableHeight` as a precondition (fails loudly otherwise).
3. The same query on flat ground returns a single level (no spurious second surface).
4. `dotnet build IOS-IG-SimHost.sln` succeeds and the fixture passes.

### P3D-403 — Flat-terrain parity gate (Axis-1)

**Design reference:** Design §6 Axis-1.

**Scope (IN):**
- Re-run the P3D-001 capture on the post-change tree and assert `EntityId`, `PositionX`, `PositionY`, and
  final `Score` (and `Flags`/`FlagsMeaningful`) are bit-or-tolerance-identical to the golden baseline for
  every starter template. Only `PositionZ` is new (constant ≈0 on flat ground).

**Scope (OUT):** No multi-level assertions (P3D-402).

**Constraints:** This is the **merge gate** for the atomic PR. Any X/Y or score drift means a change did
more than add a constant Z — fix it before merge.

**Success conditions:**
1. All starter-template golden comparisons pass within documented float tolerance.
2. `PositionZ` is `≈0` for all flat-map candidates.
3. The gate is wired into CI / the PR checklist.

### P3D-404 — Mandatory `, 0f)` / `Position.Z` pre-merge grep sweep (O-3)

**Design reference:** Design §7, §9 O-3.

**Scope (IN):**
- A documented, repeatable sweep for residual flat-earth hacks across the transform, DEM/Geographic, EQS,
  perception, navigation, and translator paths: `Vector3` `, 0f)` constructions and suspicious
  `Position.Z` reads/writes that re-zero altitude. Record findings and resolutions in the task report;
  anything intentionally left becomes a `DEBT-TRACKER.md` row.

**Scope (OUT):** Not a code change in itself — it is a verification checklist that may spawn fixes folded
into the relevant P3D task.

**Constraints:** Tier 3 covers the known cognitive/egress sites comprehensively; this sweep is the cheap
insurance for anything the enumeration missed (Design O-3). It is **mandatory**, not optional.

**Success conditions:**
1. The sweep is run and its output recorded; every hit is classified (fixed / legitimate-2D /
   tracked-as-debt).
2. No unexplained `…, 0f)` altitude construction remains on the promoted paths.

### P3D-405 — Flight Recorder break coordination + schema version bump

**Design reference:** Design §7 (Flight Recorder break — "tell-everyone-first").

**Scope (IN):**
- Bump the relevant recording/schema version so old Flight Recorder files are rejected cleanly rather
  than mis-deserialized (the break is engine-wide: `EqsResult`, `TargetMemory`, and `SimTransform`
  semantics all shift).
- Announce the break (changelog entry + team notice); document that recorded sessions do not survive this
  PR.

**Scope (OUT):** No attempt to migrate old recordings (acceptable per Design §7).

**Constraints:** Must land in the same atomic PR as the struct/schema changes.

**Success conditions:**
1. Loading a pre-change recording fails fast with a clear version-mismatch message (not a silent
   misread).
2. A changelog/notice entry exists describing the engine-wide recorder break.
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

*End of TASK-DETAIL. All Phase 1–3 tasks merge atomically (Design §7) behind the Phase 4 regression gate
(P3D-403 flat parity + P3D-402 multi-level proof + P3D-104 dead-reckoning). Coordinate conventions (§0.1)
and the nav-scope decision (§0.2) are the two things most likely to be gotten wrong — re-read them before
touching navmesh, generator, or trajectory code.*
