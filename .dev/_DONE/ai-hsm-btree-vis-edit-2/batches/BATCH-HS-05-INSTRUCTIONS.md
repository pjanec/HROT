# BATCH-HS-05 — Initial-state arrows  **[VISUAL GATE]**

**Task:** TASK-HS-05. **One objective only.** Finish the explicit TODO in `HsmInitialArrowRenderer.Render`: draw the `⦿→` initial-child marker (filled circle + arrow) for each composite state and each region of a parallel state. The existing LCA-highlight path must keep working.

Design ref: TASK-DETAIL.md §TASK-HS-05; HSM host doc §8.1. The pixel appearance is confirmed by the lead later (visual gate) — your job is correct **logic + geometry**, made headless-testable.

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch only the files below. Do NOT change the command sink, model, or other renderers.
2. **No cheating to pass.** If blocked, STOP + write the blocker.
3. **Finish without asking** — build + test until `Failed: 0`, then report.
4. **Headless only** — you make the LOGIC + GEOMETRY headless-testable (pure static helpers asserted on values); you are NOT responsible for pixel confirmation.
5. **Tests assert behavior** (which markers, what coordinates), not strings. 6. **Litter-free.** 7. **Report = truth.**

## Files
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmInitialArrowRenderer.cs` — implement the TODO; add two `internal static` pure helpers.
- Read for model: `StateNode` (`IsInitial`, `Children`, `IsParallel`, `RegionNodes`, `Position`, `SizeOverride`), `RegionNode` (`InitialChild`, `RegionIndex`).
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Renderers/HsmInitialArrowGeometryTests.cs` (new). (The test project already sees internals of `Hrot.Hsm.Editor`.)

## Design — split logic / geometry / draw

### 1. Marker collection (pure logic — headless-testable)
Add an `internal readonly record struct InitialMarker(StateNode Container, StateNode InitialChild, int RegionIndex);` (RegionIndex = -1 for a normal composite).

```csharp
internal static IReadOnlyList<InitialMarker> CollectInitialMarkers(HsmAsset asset)
```
Rules (iterate `asset.AllStates`; skip `asset.RootState` — the synthetic root has no body):
- **Parallel state** (`s.IsParallel`): for each `RegionNode r` in `s.RegionNodes` where `r.InitialChild is not null` → add `(s, r.InitialChild, r.RegionIndex)`.
- **Normal composite** (`!s.IsParallel && s.Children.Count > 0`): find the child with `IsInitial == true` (first such); if found → add `(s, child, -1)`.
- Simple/leaf/pseudo states and composites without an initial child contribute nothing.

### 2. Marker geometry (pure math — headless-testable)
The marker is the UML initial pseudostate: a small filled circle floating just **above** the initial child, with an arrow pointing **down** into the child's top-center. No container-bounds math needed.
```csharp
internal const float MarkerGap = 24f;  // graph-space gap above the child's top edge

// All in GRAPH space.
internal static (Vector2 circleCenter, Vector2 arrowStart, Vector2 arrowEnd)
    ComputeMarkerGeometry(Vector2 childPos, Vector2 childSize)
{
    float cx = childPos.X + childSize.X * 0.5f;     // child top-center X
    var arrowEnd    = new Vector2(cx, childPos.Y);  // child top edge
    var circleCenter = new Vector2(cx, childPos.Y - MarkerGap);
    return (circleCenter, circleCenter, arrowEnd);
}
```

### 3. Render (ImGui — visual gate)
At the TOP of `Render` (before/after the existing LCA loop — keep that loop intact), iterate `CollectInitialMarkers(_asset)`; for each:
- `childSize = marker.InitialChild.SizeOverride ?? DefaultNodeSize;`
- `(circleCenter, arrowStart, arrowEnd) = ComputeMarkerGeometry(marker.InitialChild.Position, childSize);`
- Convert all three with `ctx.Viewport.GraphToScreen(...)`.
- Draw: `ctx.DrawList.AddCircleFilled(screenCircle, radius * ctx.Zoom, color)` (pick a small radius ~5f and a readable color, e.g. the same gold or a neutral white/gray); draw the arrow line `AddLine(screenStart, screenEnd, color, thickness*ctx.Zoom)`; add a simple arrowhead at `screenEnd` (two short lines, or reuse any existing arrow helper if one exists — check `ImDrawListExtensions`; if none, two short lines forming a "v" are fine).
Scale sizes by `ctx.Zoom` consistently with `DrawLcaOutline`.

> Keep it defensive: never throw if positions are zero; no allocation-heavy work per frame beyond the small marker list.

## Tests (`Hrot.Hsm.Editor.Tests`, new file)
Build assets directly (root + states) like the other Host tests; set `IsInitial`/region `InitialChild` as needed. Assert VALUES:
1. **Composite with an initial child** → `CollectInitialMarkers` returns exactly one marker `(composite, initialChild, -1)`.
2. **Composite without any initial child** → zero markers.
3. **Parallel with 2 regions, each with an InitialChild** → two markers with the right `RegionIndex` and `InitialChild`.
4. **Parallel region with null InitialChild** → that region contributes no marker.
5. **Synthetic root skipped** → even if root has children, root is never a marker container.
6. **ComputeMarkerGeometry** with `childPos=(100,200)`, `childSize=(120,40)` → `arrowEnd==(160,200)`, `circleCenter==(160,176)`, `arrowStart==circleCenter`. (Exact values.)

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 417 passed. List pre-existing failures; confirm 0 new.

## Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-05-REPORT.md`
The collection rules; the geometry helper; how the arrowhead is drawn; test names + assertions; before/after counts; note that pixel appearance is the lead's visual gate. Do not commit.
