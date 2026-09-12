# BATCH-HS-05 REPORT — Initial-state arrows

**Date:** 2026-06-13  
**Branch:** `blueprint-integ-1`  
**Task:** TASK-HS-05 — implement the initial-child marker TODO in `HsmInitialArrowRenderer.Render`.

## Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmInitialArrowRenderer.cs` | Added `CollectInitialMarkers`, `ComputeMarkerGeometry`, `InitialMarker` record struct, and render loop |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Renderers/HsmInitialArrowGeometryTests.cs` | **New** — 6 headless tests |

No other files touched (command sink, model, other renderers all untouched per Working Agreement).

## Implementation

### 1. Marker collection (`CollectInitialMarkers`)

`internal static IReadOnlyList<InitialMarker> CollectInitialMarkers(HsmAsset asset)`

Iterates `asset.AllStates`, skipping `asset.RootState` (the synthetic root, which has no visual body). Rules:

- **Parallel state** (`s.IsParallel`): for each `RegionNode r` in `s.RegionNodes` where `r.InitialChild is not null` → emits `(s, r.InitialChild, r.RegionIndex)`.
- **Normal composite** (`!s.IsParallel && s.Children.Count > 0`): finds the first child with `IsInitial == true`; if found → emits `(s, child, -1)`.
- Simple/leaf/pseudo states and composites without an initial child contribute nothing.

The marker is a `readonly record struct InitialMarker(StateNode Container, StateNode InitialChild, int RegionIndex)` — cheap stack allocation, no per-frame GC pressure.

### 2. Geometry helper (`ComputeMarkerGeometry`)

`internal static (Vector2 circleCenter, Vector2 arrowStart, Vector2 arrowEnd) ComputeMarkerGeometry(Vector2 childPos, Vector2 childSize)`

Pure math — all in graph space:
- `cx = childPos.X + childSize.X * 0.5` (child top-center X)
- `arrowEnd = (cx, childPos.Y)` (child top edge)
- `circleCenter = (cx, childPos.Y - MarkerGap)` (24f above child top)
- `arrowStart = circleCenter` (line runs from circle center down to arrow end)

No container-bounds math needed.

### 3. Render loop

At the top of `Render()` (before the existing LCA loop, which is left intact):
1. Calls `CollectInitialMarkers(_asset)`.
2. For each marker:
   - Resolves `childSize = marker.InitialChild.SizeOverride ?? DefaultNodeSize`.
   - Computes geometry via `ComputeMarkerGeometry`.
   - Converts all three graph-space points to screen via `ctx.Viewport.GraphToScreen`.
   - Draws filled circle (`AddCircleFilled`, radius 5f × zoom).
   - Draws arrow line (`AddLine`, thickness 2f × zoom).
   - Draws arrowhead: two short lines forming a "v" pointing down. Tip at `screenEnd`, left wing to `(X - 5f*zoom, Y - 5f*zoom)`, right wing to `(X + 5f*zoom, Y - 5f*zoom)`. Uses same color and thickness as the arrow line.

Constants: `MarkerGap = 24f`, `MarkerRadius = 5f`, `ArrowThickness = 2f`, `ArrowheadArmLength = 5f`. Color: neutral gray `(0.75, 0.75, 0.75, 1.0)`.

No existing arrow helpers in `ImDrawListExtensions` were suitable (only `AddBezierWithArrow` for cubic Bezier flow wires). Two-line "v" arrowhead is self-contained.

### 4. Arrowhead approach

Two `AddLine` calls drawing a downward-pointing "v":
```
tip = screenEnd (bottom point)
leftWing  = tip + (-headSize, -headSize)   // up-left
rightWing = tip + (headSize, -headSize)    // up-right
```
Both wings drawn at the same thickness as the arrow line.

## Tests

All 6 new tests in `HsmInitialArrowGeometryTests.cs`. Assets built manually via `BuildManualAsset` (direct `HsmAsset` construction) to avoid compiler-root side-effects that would produce unpredictable marker counts.

| # | Test | Assertions |
|---|------|-----------|
| 1 | `CollectInitialMarkers_CompositeWithInitialChild_ReturnsOneMarker` | 1 marker: `Container="A"`, `InitialChild="B"`, `RegionIndex=-1` |
| 2 | `CollectInitialMarkers_CompositeWithoutInitialChild_ReturnsZeroMarkers` | `markers.Should().BeEmpty()` |
| 3 | `CollectInitialMarkers_ParallelWithTwoRegionsEachWithInitialChild_ReturnsTwoMarkers` | 2 markers: `RegionIndex=0→A`, `RegionIndex=1→B`, both Container="P" |
| 4 | `CollectInitialMarkers_ParallelRegionWithNullInitialChild_Skipped` | 1 marker (null-InitialChild region skipped): `InitialChild="A"`, `RegionIndex=0` |
| 5 | `CollectInitialMarkers_SyntheticRootSkipped` | 1 marker (A), root not in markers; `NotContain(m => m.Container == asset.RootState)` |
| 6 | `ComputeMarkerGeometry_ReturnsExpectedValues` | Exact values: `arrowEnd=(160,200)`, `circleCenter=(160,176)`, `arrowStart==circleCenter` |

## Before / after counts

| | Before | After |
|---|--------|-------|
| Build (Hsm.Editor) | 0 errors | 0 errors |
| Build (Hsm.Editor.Tests) | 0 errors | 0 errors (1 pre-existing BTREE0002 warning) |
| Tests passed | 417 | **423** (+6 new) |
| Tests failed | 0 | **0** |
| New failures | — | **0** |

## Visual gate note

**Pixel appearance is the lead's visual gate.** This batch provides correct logic + geometry, headless-verified by value assertions. The visual confirmation at zoom levels, with different child sizes/positions and on parallel states, is deferred to the lead review.

## Compliance

- [x] ONE task — touched only `HsmInitialArrowRenderer.cs` + new test file
- [x] No changes to command sink, model, or other renderers
- [x] Headless only — pure static helpers asserted on values
- [x] Existing LCA-highlight loop preserved
- [x] No `BLUEPRINT_REGENERATE_SNAPSHOTS` env var used
- [x] `Failed: 0`, 0 build errors
- [x] No commit
