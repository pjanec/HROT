# BATCH-S2-AB — Allow ~3x deeper zoom-in on the editor 2D map

## Problem
The editor's 2D map can't zoom in far enough for precise placement. The `MapCamera` is created
with default limits (`MinZoom=0.1`, `MaxZoom=10`); `MaxZoom` is the most-zoomed-in limit. The user
wants ~3x more zoom-in detail.

## Fix — ONE FILE
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — at the `MapCamera` construction (~line 1517,
`_camera = new MapCamera();`), raise `MaxZoom` to give ~3x more zoom-in headroom. Use object-initializer
or set the property right after:
```csharp
_camera = new MapCamera
{
    MaxZoom = 30f, // BATCH-S2-AB: ~3x deeper zoom-in than the default 10 for precise placement
};
```
(If other MapCamera properties were being relied on by defaults, keep them — only add MaxZoom. If the
line is `_camera = new MapCamera();` exactly, replace with the object-initializer form above.)

## Verify (report, do not fix here)
- Check whether the editor's per-entity 2D gizmo primitives set `MaxZoomLod` (a non-zero `MaxZoomLod`
  culls a primitive when `zoom > MaxZoomLod * 0.25f` — see DebugPrimitiveRenderer2D.cs ~line 93). Grep
  the editor entity gizmo emitters (e.g. EntityPresentationGizmoShared / ScenarioEditor rendering) for
  `MaxZoomLod`. If they set a low value, gizmos would VANISH when zoomed past it at the new MaxZoom=30.
  REPORT what you find (don't change gizmo LOD in this batch) so the lead can decide.

## Constraints
- One file. Do not change MapCamera.cs itself (MinZoom/MaxZoom are settable properties — set at the
  construction site only). Do not touch IG's IgCameraConstants (that's the separate IG app).

## Acceptance
- Builds clean.
- (User) The editor 2D map zooms in ~3x further than before for precise work; zoom-out unchanged.
- Report any MaxZoomLod culling risk found.
