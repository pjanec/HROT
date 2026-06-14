# BATCH-S2-R — Two-way 2D↔3D selection sync

## Problem (proven by [SelDiag] logs)
Selecting an entity in the 2D editor map leaves the 3D view `HasSelection=False` — the 2D editor
selection (`EditorSubsystem._selectionState.PrimarySelected`) is NOT bridged to the 3D
`EditorStrideSubsystem.SelectionState`. They're independent. Need both-way sync so:
- Select in 2D map → 3D selection box appears.
- Click-select in 3D (BATCH-S2-Q) → 2D editor reflects it.

## Verified APIs
- 2D: `EditorSubsystem._selectionState` (private `DefaultSelectionState?`) with `Entity? PrimarySelected { get; set; }`
  and `int Version` (bumps on select/clear).
- 3D: `EditorStrideSubsystem.SelectionState` (`EditorSelectionState`) with `Select(Entity)`, `Clear()`,
  `Entity SelectedEntity`, `bool HasSelection`, `int Version`.

## Scope — TWO FILES

### File 1: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — expose the 2D selection (narrow primitives)
Add two public members (near the other public accessors, e.g. by `IsHeadless` ~line 586). Expose
PRIMITIVES (Entity? + int), NOT the `DefaultSelectionState` type, so the Stride assembly needs no
extra reference:
```csharp
/// <summary>
/// The primary selected entity in the 2D editor map (BATCH-S2-R, 2D↔3D selection sync).
/// Null when nothing is selected or in headless mode. Setting it updates the 2D selection.
/// </summary>
public Fdp.Core.Entity? Selected2DEntity
{
    get => _selectionState?.PrimarySelected;
    set { if (_selectionState != null) _selectionState.PrimarySelected = value; }
}

/// <summary>Monotonic version of the 2D selection (changes on each select/clear). 0 in headless. (BATCH-S2-R)</summary>
public int Selection2DVersion => _selectionState?.Version ?? 0;
```
(Confirm `_selectionState.PrimarySelected` is `Fdp.Core.Entity?` — if the namespace alias differs,
match it. Confirm `DefaultSelectionState.Version` exists; the investigation confirmed it does.)

### File 2: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — version-guarded two-way mirror
1. Add fields near the other tick/selection fields:
```csharp
// BATCH-S2-R: 2D↔3D selection sync version trackers.
private int _last2dSelVersion = -1;
private int _last3dSelVersion = -1;
```

2. Add a private mirror method:
```csharp
/// <summary>
/// Keeps the 2D editor selection (<see cref="EditorSubsystem.Selected2DEntity"/>) and the 3D
/// <see cref="SelectionState"/> in sync, one direction per frame (whichever changed), using
/// version counters to prevent feedback bounce. (BATCH-S2-R)
/// </summary>
private void SyncSelection2D3D()
{
    if (_editor == null) return;
    int v2d = _editor.Selection2DVersion;
    if (v2d != _last2dSelVersion)
    {
        // 2D changed this frame → push to 3D.
        _last2dSelVersion = v2d;
        var e = _editor.Selected2DEntity;
        if (e.HasValue && e.Value != Fdp.Core.Entity.Null && World != null && World.IsAlive(e.Value))
            SelectionState.Select(e.Value);
        else
            SelectionState.Clear();
        _last3dSelVersion = SelectionState.Version; // sync tracker so we don't bounce back
    }
    else if (SelectionState.Version != _last3dSelVersion)
    {
        // 3D changed this frame (e.g. click-to-select) → push to 2D.
        _last3dSelVersion = SelectionState.Version;
        _editor.Selected2DEntity = SelectionState.HasSelection
            ? SelectionState.SelectedEntity
            : (Fdp.Core.Entity?)null;
        _last2dSelVersion = _editor.Selection2DVersion; // sync tracker
    }
}
```

3. Call `SyncSelection2D3D()` in `TickHosted` AFTER `_editor!.Update(dt)` (so the 2D selection for
   this frame is settled) and BEFORE the `EmitSelectionHighlight()` call (so the box reflects the
   synced selection the same frame). The selection-highlight block is ~line 1267-1270; place the
   sync call just before it (or right after `_editor.Update(dt)` ~line 1238). Hosted path only — the
   OFF/mock path has no real 2D editor selection.

## Constraints
- Two files only. No change to the raycast/move/box-emit logic.
- One direction per frame (the `else if`); update BOTH version trackers when syncing so it can't
  oscillate.
- All null/liveness-guarded.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Select a unit in the 2D editor map → `[SelDiag] HasSelection=True entity=#N` and the 3D
  selection box appears. Click-select a different unit in 3D → the 2D editor's selection follows.
  (If `[SelDiag]` shows True but no box renders, that's the separate gizmo-render issue to isolate
  next — the sync itself is then proven by the log.)
