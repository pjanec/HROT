# 3D Cognitive Spatial Awareness Promotion — Design v1.1

> **Changelog v1.0 → v1.1** (open items resolved by architect review):
> - **O-1 → delete `GroundClampingState` entirely** (§3 Tier 1, §9). Rendering uses standard transform
>   interpolation for any sub-frame smoothing, not a terrain offset.
> - **O-2 → disambiguation rides Recast `walkableHeight`** (§6 Axis-2, §9). The multi-level fixture's
>   deck clearance **must strictly exceed the agent's `walkableHeight`** for the snap test to be valid.
> - **O-3 → the `, 0f)` grep is a mandatory pre-merge step** (§7, §9), not optional insurance.

> **Status:** Detailed design. This is the **committed pre-step** to the Squad Coordination design;
> the maneuver work depends on it. Supersedes the EQS 3D Promotion scoping note — the scope grew
> (correctly) from "EQS results" to "authoritative simulation altitude," because the architect review
> revealed the 2D assumption lives below EQS, in the transform/perception layer.
> **Audience:** Implementation lead and reviewer.
> **Drives:** One atomic change that makes `SimTransform.Position.Z` the authoritative physical
> altitude, carried across the network and throughout the simulation, and propagates real Z through
> EQS, perception/`TargetMemory`, pathing cost, and the position-carrying translators.
> **Depends on:** nothing (it is the root pre-step). **Gates:** the Squad Coordination design, and
> corrects multi-level behavior for all existing AI.

---

## 1. The unifying principle

> **`SimTransform.Position.Z` is the single authoritative physical altitude — carried across the
> network and throughout the simulation — not a visual rendering offset.**

Every change in this document is a consequence of that one statement. The engine is currently 2D by a
*deliberate* flat-earth simplification (not legacy cruft): `SimTransform.Position.Z` is forced to
`0f`, EQS results store only X/Y, `TargetMemory` stores 2D contacts, `PathCost` uses XZ-distance, and
a family of translators hardcode the geodetic Z to `0f`. This was fit-for-purpose for flat terrain.
Multi-level urban content — bridges, overpasses, stacked walkable surfaces — outgrows it: a position
under a bridge and one on the deck above share X/Y and differ only in Z.

The good news established during review: **the transport and navmesh are already 3D.** DDS `GeoPoint`
carries an 8-byte altitude double; `WGS84Transform` round-trips altitude through ECEF/ENU math that is
projection-independent (a deck position resolves to the same physical floor on every node); and
`INavmeshProvider.ProjectToNavmesh` / Recast already do true 3D nearest-polygon search, which
disambiguates overlapping surfaces natively once given a real Z. So this is **not** a protocol or
navmesh redesign. It is making the simulation's authoritative state carry the altitude that everything
underneath already supports, and connecting the value to the consumers that currently discard it.

---

## 2. Why this grew from "EQS 3D" to "cognitive 3D"

The scoping note assumed an EQS-only change. The architect's enumeration showed the 2D assumption is
broader and rooted lower:

- **`TargetMemory` is 2D** (`ThreatEvaluationSystem` passes only X/Y into `AddOrUpdateTarget`). The
  entire squad layer — threat ranking, fire allocation, LOS reasoning — reads `TargetMemory`; flat
  contacts make a bridge threat and a street threat indistinguishable regardless of how good EQS is.
- **`SimTransform.Position.Z` is force-zeroed** at the root. Promoting EQS while the transform stays
  flat would just propagate a flat Z faithfully — accurate plumbing carrying wrong data.
- **The DEM1 elevation path actively walls altitude off** from authoritative state (it lives as a
  visual offset in `GroundClampingState`). This is the thing that has to *invert*, not be patched.

So the real title is **3D Cognitive Spatial Awareness** — authoritative altitude at the transform,
read truthfully by EQS, perception, pathing, and the squad layer. EQS is one consumer among several,
not the subject.

---

## 3. Three tiers, sequenced by dependency, shipped atomically

All three tiers land in **one PR** (§7 explains why atomicity is forced). They are ordered here by
data dependency — each tier reads what the previous makes authoritative.

### Tier 1 — Authoritative altitude (the root)

Make `SimTransform.Position.Z` carry true physical altitude. This is mostly **deletion of a deliberate
diversion**, not new machinery.

**Current (2.5D DEM1) flow — the wall:**
```
SimTransform.Position.Z forced to 0f
TerrainQueryResolutionSystem: TargetZOffset = HitZ − referenceSimZ   → GroundClampingState
TransformSyncSystem: applies TargetZOffset as a VISUAL Z correction only,
                     explicitly preventing it from feeding dead-reckoning / authoritative state
```
The terrain query *already computes the true altitude* (`HitZ`). The pipeline goes out of its way to
keep it out of the authoritative state and reroute it to a render-only offset.

**Promoted flow — the inversion:**
```
TerrainQueryResolutionSystem: HitZ  →  SimTransform.Position.Z   (authoritative)
TransformSyncSystem: no separate visual Z correction — render reads the authoritative Z
GroundClampingState: no longer holds elevation
```

Changes:
- `TransformSyncSystem`: stop forcing `Z=0`; stop applying the visual-only Z correction.
- `TerrainQueryResolutionSystem`: write `HitZ` into `SimTransform.Position.Z` instead of computing a
  `TargetZOffset` for `GroundClampingState`.
- `GroundClampingState`: **deleted entirely** (O-1) — it stops carrying elevation, and with elevation
  now authoritative on `SimTransform.Position.Z` there is no residual role; any render sub-frame
  smoothing uses standard transform interpolation, not a terrain offset.
- Dead-reckoning / replication now operate on a live Z. **This is the risk concentrator** (§8): the
  wall currently protects prediction from a moving Z, so removing it must be validated on sloped and
  stepped terrain, not just flat.

### Tier 2 — Cognitive spatial awareness reads the real Z

With authoritative Z available at the transform, the cognitive carriers stop discarding it.

**EQS result path:**
- `EqsResult`: add `float PositionZ`. 24 B → 28 B raw → **32 B after 8-byte alignment** (the `long
  EntityId` governs alignment). This is *better* than today: 32 B packs exactly two per 64-byte cache
  line (the old 24 B straddled lines); `EqsCognitiveBuffer`'s `[InlineArray(16)]` goes 384 B → 512 B =
  exactly 8 cache lines. **Unconditional widening — no 2D/3D toggle** (a toggle would fracture the
  solver, generators, tests, and DDS topics; the architect was explicit). The `[InlineArray]` span-cast
  write discipline applies to the layout change.
- `EqsResultEntry` (DDS wire): carry altitude (the 3D primitive exists).
- `EqsCognitiveBuffer`: hold 3D results; `GetSpanRW/RO` via `MemoryMarshal` (the defensive-copy trap).

**Generators — "stop discarding the Z you already sampled"** (no new vertical-band parameter needed for
the v1.3 set: `Self`, `Donut`, `Grid`, `Cone`, `EntitiesInArea`, `EntitiesInRadius`, `NavmeshSamples`,
`CoverPoints`, `OffsetFromContext`):
- *Geometric* (`Donut`, `Grid`, `Cone`, `OffsetFromContext`): apply planar X/Y offsets but **retain the
  context origin's Z**, pass the full `Vector3` to `ProjectToNavmesh`; Recast snaps to the correct
  vertical level.
- *Entity-based* (`Self`, `EntitiesInRadius`, `EntitiesInArea`): stop flattening `SimTransform`'s 3D
  position when writing the candidate.
- *Database* (`CoverPoints`, `NavmeshSamples`): become 3D once their sources are (below).
- `NavmeshSamplesGenerator`: stop flattening the sampled point (it maps North→`PositionY` and drops Z).
- `EntitiesInRadiusGenerator`: stop flattening spatial-hash results.

**Sources:**
- `CoverPoint` / `ICoverProvider`: widen the cover-node struct to 3D (it mirrors the old `EqsResult`
  2D shape and feeds `CoverPointsGenerator`).

**Scoring / filter tests:**
- `DistanceScoreTest`: 2D `Vector2` planar falloff → true `Vector3.Distance`.
- `NavmeshReachableTest`, `PathCostScoreTest`: stop reconstructing `new Vector3(X, 0f, Y)`; use the
  candidate's real Z.

**Perception boundary (the symmetry that makes the squad layer 3D):**
- `TargetMemory`: widen contacts to carry altitude.
- `ThreatEvaluationSystem`: pass real Z into `AddOrUpdateTarget` instead of X/Y only.

### Tier 3 — Cost and translator symmetry

Depends on Tier 1's authoritative Z and Tier 2's widened carriers flowing through.

- `PathCost`: **uniformly 3D** (the `INavmeshProvider.PathCost` signature already takes `Vector3`; true
  3D cost — stairs/ramps to a deck cost more — is more correct for all agents, not just squads; no
  bifurcation).
- `StubNavmeshProvider`: patch its XZ-only `PathCost` to true 3D distance (match `FakeNavmeshProvider`).
- **The `0f`-Z hardcodes — fix every one** (fixing one and leaving siblings reintroduces the bug):
  - `NavigationIntentEgressTranslator` (geodetic `Z=0`).
  - `PathResponseSolverEgressTranslator` (`Up = 0f` per waypoint — flattens the returned path).
  - `NavigationIntentBridgeSystem` (`new Vector3(p.Destination.X, p.Destination.Y, 0f)`).
  - The **ingress** sides, symmetrically: populate Z from geodetic altitude on the way back.

**Presentation (adapts, verify):**
- `EqsCognitiveBufferRenderer`: format Z alongside X/Y.
- `EqsSensorGizmo`: confirm Top-K lines use the new Z (extruded/3D, also better for multi-level debug).

**Unaffected:**
- Area-query path (`AreaQueryRequestEvent`/`ResultEvent`, `EqsTargetPool`): operates on entity handles,
  not coordinates — no change.

---

## 4. Complete change-set (the architect's enumeration, consolidated)

| Tier | File / type | Change |
|---|---|---|
| 1 | `TransformSyncSystem` | stop forcing `Z=0`; drop visual-only Z correction |
| 1 | `TerrainQueryResolutionSystem` | write `HitZ` → `SimTransform.Position.Z` |
| 1 | `GroundClampingState` | **deleted** — elevation now lives on `SimTransform.Position.Z` |
| 2 | `EqsResult` | add `float PositionZ` (→32 B aligned) |
| 2 | `EqsResultEntry` (DDS) | carry altitude |
| 2 | `EqsCognitiveBuffer` | 3D results; `MemoryMarshal` span access |
| 2 | `NavmeshSamplesGenerator` | retain sampled Z |
| 2 | `EntitiesInRadiusGenerator` | stop flattening hash results |
| 2 | geometric generators (`Donut`/`Grid`/`Cone`/`OffsetFromContext`) | retain context Z, project 3D |
| 2 | entity generators (`Self`/`EntitiesInRadius`/`EntitiesInArea`) | stop flattening `SimTransform` |
| 2 | `CoverPoint` / `ICoverProvider` | widen to 3D |
| 2 | `CoverPointsGenerator`, `NavmeshSamplesGenerator` | stream 3D once sources are 3D |
| 2 | `DistanceScoreTest` | `Vector2` → `Vector3.Distance` |
| 2 | `NavmeshReachableTest`, `PathCostScoreTest` | use real Z, drop `0f` reconstruction |
| 2 | `TargetMemory` | widen contacts to 3D |
| 2 | `ThreatEvaluationSystem` | pass real Z into `AddOrUpdateTarget` |
| 3 | `PathCost` | uniformly 3D |
| 3 | `StubNavmeshProvider` | 3D distance |
| 3 | `NavigationIntentEgressTranslator` | drop geodetic `Z=0` |
| 3 | `PathResponseSolverEgressTranslator` | drop `Up=0f` per waypoint |
| 3 | `NavigationIntentBridgeSystem` | drop destination `…, 0f` flatten |
| 3 | ingress translators (siblings) | populate Z from geodetic altitude |
| 3 | `EqsCognitiveBufferRenderer` | render Z |
| 3 | `EqsSensorGizmo` | verify Z used |

---

## 5. Ripple into already-designed work

- **Utility AI readers (additive).** `ContactThreatLevel`, `DistanceToContext`, the LOS-flavored
  readers read `TargetMemory`, which now carries Z. They keep working; `DistanceToContext` becomes a
  3D distance, which subtly changes utility scores on multi-level terrain. Not a break — but the build
  order should note **utility decisions may want re-tuning once contacts are 3D** (cheap to flag now,
  annoying to discover after). On flat terrain, scores are unchanged (constant Z).
- **Squad Coordination (the dependent).** This promotion is precisely what lets the squad layer reason
  about and path to multi-level positions. The squad design consumes the now-3D EQS cover query for
  overwatch/flanking positions and the now-3D `TargetMemory` for threat reasoning.

---

## 6. Regression strategy — two axes

**Axis 1 — flat-terrain parity (the safety net).** Capture golden `EqsCognitiveBuffer` outputs (Top-K
entity ids, X/Y, final scores) for the eight Starter Pack templates (e.g. `FindNearestEnemy`,
`FindCoverFromTarget`) on a flat test map *before* the change; apply the atomic promotion; rerun and
assert X/Y and scores are bit-or-tolerance-identical. On flat ground the promotion only adds a constant
Z, so candidate generation, scoring weights, and Top-K reduction must not move. Passing this **provably
preserves existing behavior.**

**Axis 2 — multi-level proof (the purpose).** A bridge-over-street fixture asserting the
previously-impossible: a cover query under the bridge returns Z≈0 candidates, on the deck returns
Z≈(deck height) candidates, and Recast's 3D snap disambiguates two surfaces overlapping in X/Y. This is
what the whole promotion is *for*; flat parity alone would pass even if 3D did nothing.

**Axis 3 — dead-reckoning on slopes/steps (the risk probe).** Because Tier 1 removes a wall that today
protects prediction from a live Z, a fixture on sloped and stepped terrain must assert
prediction/replication stays stable with authoritative Z moving — see §8.

---

## 7. Why one atomic PR

`EqsResult` is a blittable unmanaged struct; changing 24→32 B alters `EqsCognitiveBuffer`'s footprint
and the DDS `EqsResultEntry` simultaneously. Ingress and egress translators must update together or
deserialization crashes across the Brain/Muscle boundary. Staging the transport apart from the struct
apart from the cost functions would inject exactly the `0f`-Z bugs we are removing. So structs, DDS
topics, translators, `PathCost`, generators, `TargetMemory`, and the Tier-1 transform inversion land in
one unified PR with the §6 regression gate.

**Flight Recorder break (hard coordination note).** The schema shift invalidates existing Flight
Recorder files — and not just EQS: `TargetMemory` and `SimTransform` semantics change too, so the break
is engine-wide for this PR. Acceptable for a deliberate promotion, but it is a **tell-everyone-first**
item: recorded sessions do not survive this change.

---

## 8. The risk concentrator: dead-reckoning with a live Z

Tier 1 is mostly deletion, but it is not low-risk, and the risk is localized to one place: today the
DEM offset is **deliberately** kept out of dead-reckoning. Removing that wall means the authoritative Z
now moves as an entity traverses sloped or stepped terrain, and prediction/replication must handle a
varying Z they previously never saw. The Axis-3 fixture (§6) exists specifically to probe this: an
entity moving up a ramp onto a deck, asserting predicted vs. authoritative position stays within
tolerance and replication does not jitter or diverge across nodes. If any instability appears, it
appears here — not in the mechanical Tier-2 widenings. This section is the one a reviewer should read
hardest.

---

## 9. Resolved items

- **O-1. `GroundClampingState` — DELETE entirely.** Once `TerrainQueryResolutionSystem` writes `HitZ`
  into `SimTransform.Position.Z`, a separate elevation-offset component is obsolete. Any sub-frame
  smoothing the render pipeline wants uses standard transform interpolation, not a terrain offset. No
  residual role.
- **O-2. Vertical disambiguation rides Recast `walkableHeight`.** Recast resolves vertical snapping via
  its configured `walkableHeight` and extrusion bounds. **Hard fixture requirement:** the multi-level
  test fixture's deck clearance must *strictly exceed* the agent's configured `walkableHeight`, or the
  3D Euclidean search will not reliably snap to the correct overlapping polygon and Axis-2 (§6) is not
  a valid test. Bake this into the fixture spec.
- **O-3. The `, 0f)` grep is a MANDATORY pre-merge step.** A sweep for `Vector3` `, 0f)` constructions
  (and `Position.Z` reads) across the transform and DEM1 paths must run before merge. Tier 3 covers the
  known cognitive bridges and egress translators comprehensively; this sweep is the cheap insurance
  against any remaining flat-earth hack the enumeration missed.

---

*End of 3D Cognitive Spatial Awareness Promotion design v1.1. Pre-step to the Squad Coordination
design. One atomic PR; flat-parity + multi-level-proof + dead-reckoning regression; engine-wide Flight
Recorder break; `GroundClampingState` deleted; mandatory pre-merge `, 0f)` sweep. Once landed, the
squad layer's threat reasoning (`TargetMemory`) and overwatch positioning (EQS cover query) are 3D and
multi-level-correct.*
