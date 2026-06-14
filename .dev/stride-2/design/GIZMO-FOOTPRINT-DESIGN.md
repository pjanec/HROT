# 2D Map Gizmo Footprint — Design (signed off)

## Problem
2D map gizmos are wrong size/shape for many entities. Root cause: D1-spawned (and other
translator-less) entities lack `VehicleParams`/`DisType`/shape data, so `DebugPrimitiveRenderer2D`
falls back to a hardcoded **5 m magenta box** (≈10× a 0.6 m soldier; no humanoid shape). The shape
library already has correct humanoid/vehicle/aircraft profiles keyed by DIS type — they're just not fed.

## Agreed behavior (user sign-off: 2026-06-14)
- **Shape:** keep the current `PerspectiveShapeRenderer` and its existing profiles (it already applies a
  pitch/roll perspective distortion in the 2D top view). NO MIL-STD-2525 symbology.
- **Size rule = continuous floor (option 1A):** `drawnSize = max(realFootprintMeters × zoom, minPixelFloor)`.
  - Zoomed in → true-to-scale footprint (no overlap, precise placement).
  - Zoomed out → shrinks with the map until it hits a readable pixel floor (~12 px), then holds.
  - No hard zoom threshold, no pop, no hysteresis. Type stays legible via shape even when floored.
- **Click target:** independent `minClickRadius` so tiny/dense units stay selectable regardless of
  visual size.
- **Footprint source:** TKB shape dims (`CollisionShapeKind` + `ShapeDims`) — these are the SAME dims the
  physics body is built from, so the footprint is body-accurate by construction. Optional later: the
  Stride muscle may overwrite with resolved body bounds; renderer unaffected.

## Architecture — NO 3D leak into the renderer
The renderer is already metric (draws N meters from `VehicleParams.Length/Width`). The sizing/3D
knowledge PUSHES neutral 2D data into an ECS component; the renderer PULLS plain floats + an enum.

```
TKB ShapeDims ──(translator at TKB instantiation)──► [ Map2DFootprint {LengthM, WidthM, Shape} ] ──read──► 2D gizmo renderer
   (sizing knowledge)                                     neutral ECS component (floats+enum)              (stays 3D-agnostic)
```

- **`Map2DFootprint`** — new generic presentation component (no 3D/physics refs): `float LengthM`,
  `float WidthM`, `GizmoShapeCategory Shape` (Humanoid / GroundVehicle / FixedWing / RotaryWing / Unknown).
- **Writer:** the TKB instantiation path / translators (the same ones that attach VehicleParams / DisType /
  perception models). Maps `CollisionShapeKind`→Shape; box extents→length/width; capsule radius→small
  humanoid footprint.
- **Reader:** the 2D gizmo emitter (`EntityPresentationGizmoShared.DrawSemanticShape`) reads
  `Map2DFootprint` (fallback to a category default when absent); the renderer applies the 1A size rule
  using `MapCamera.Zoom`.

## CORRECTION (verified 2026-06-14): spawns are ALREADY TKB-grounded
`EditorStrideSubsystem` (lines 584–596) wires `NetworkSpawningSystem` + `CreateEntityRequestSystem` +
`ScenarioSource` with the full `BuildTranslators()` chain. D1/D2 (`StrideTestHarnessCases.EnqueueSpawn`)
goes through `ScenarioSource → CreateEntityRequestSystem → SpawnEntityCommand → NetworkSpawningSystem`,
which runs ALL translators including **Perception**. So perception is NOT broken by spawn, and there is NO
need to rewire the spawn path. The real gaps are narrow:
- **DisType = 0 for D1 spawns:** `EnqueueSpawn` doesn't set `EntityCreationRequest.DisType`, so
  `ResolveProfileId` returns 0 → shape library returns `_fallback` → magenta box. (Scenario entities set it
  from the template.) Fix = look up the TKB template's DisType in `EnqueueSpawn`.
- **No footprint for non-vehicle shapes:** `VehicleKinematicsTkbTranslator` only writes `VehicleParams.Length/Width`
  for `OrientedBox` entities, so capsule (infantry) get length/width = 0 → renderer's 5 m fallback. The gizmo
  reads vehicle dims, not the physics shape dims.

## Phased plan (batches — LOW RISK, no shared spawn surgery)
1. **G1 — DisType on D1/D2 spawns:** `EnqueueSpawn` sets `EntityCreationRequest.DisType` from the TKB
   template (`TkbDb` lookup). Fixes the SHAPE (humanoid vs vehicle) for editor debug spawns. Tiny/contained.
2. **G2 — `Map2DFootprint` component + populate from TKB shape dims** for ALL shapes (incl. capsule), in
   `VehicleKinematicsTkbTranslator.Inject` (it already reads `VehicleParametersDto` + `StrideRenderModelDefDto`)
   or a small dedicated translator. Mirrors `StrideVisualBindingSystem.ResolveShapeDims` "0→default" logic.
3. **G3 — renderer:** gizmo emitter reads `Map2DFootprint` (fallback to category default, drop the 5 m magenta);
   `DebugPrimitiveRenderer2D.DispatchShape` SemanticShape branch applies the 1A rule
   `visualLen = max(meters×zoom, minPixelFloor)/zoom` (zoom is in scope there) + a min click radius.

## Open / confirmed
- (1) size rule: **1A continuous floor** — CONFIRMED.
- (2) symbology: **keep current shape renderer, no MIL-STD** — CONFIRMED.
- (3) footprint source: **TKB dims (== body bounds)**, optional muscle override later — CONFIRMED.
- 3D-leak concern: resolved via neutral `Map2DFootprint` component — CONFIRMED.
