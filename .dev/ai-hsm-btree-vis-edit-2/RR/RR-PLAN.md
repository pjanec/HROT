# RR — Wire reroute points across all graph editors

> **Origin:** user request (2026-06-13) — draggable reroute/waypoint points on wires (to route around nodes, e.g. for back-and-forth transitions between adjacent states). "Missing in all the graphs (btree, hsm, blueprints)."
> **Companions:** [../DEBT-TRACKER.md](../DEBT-TRACKER.md) (VE-DEBT-008).
> **Execution:** lead specs + hard-verifies; coding via sonnet agents.

## Finding (confirmed in code)

The NodeEditor **UI side is complete**: the wire context-menu "Insert Reroute Node Here" (`CanvasRenderer.cs:634-642`), Ctrl/double-click on a wire (`CanvasInput.cs:280-301`), and reroute drag (`WireRenderer.cs` `RerouteDragOverridePositions`) all emit `GraphCommand.InsertReroute(LinkId, Vector2)` / `MoveReroute(LinkId, int, Vector2)` / `RemoveReroute(LinkId, int)`. Reroute dots + bent beziers already render. **The only gap is host-side:** no host command sink handles these commands (all have a `default` arm that drops/ignores them), and BTree/Blueprint links have no waypoint backing store.

## Tasks (per editor; do HSM first to validate the UI integration end-to-end)

| ID | Editor | Size | Work | Status |
|---|---|---|---|---|
| RR-01 | HSM | **trivial** | Add InsertReroute/MoveReroute/RemoveReroute to `HsmCommandSink`, mutating `TransitionNode.Waypoints` (+`MarkDirty`). Model list + `.hsm.json` persistence already exist. | ✅ DONE — 3 handlers (append on insert; range-guarded move/remove; unknown-link no-op); covered by the existing end-of-Apply `MarkDirty`. HSM tests 497/0 (11 new). |
| RR-02 | Blueprint | medium | Add waypoints to `Link` (`GraphTypes.cs`); expose via `BlueprintLinkModel`; handle the 3 commands in `BlueprintCommandSink`. | ✅ DONE — added `LinkWaypoint{X,Y}` (property-based, round-trip-safe; nullable→omitted when empty for back-compat); `Link.Waypoints`; `BlueprintGraphModel.FindAssetLink(LinkId)` resolver (reuses `MakeLinkId`); 3 sink handlers (append/guarded-move/guarded-remove, no-op on unknown/oob) + `_markDirty`+`RebuildAndNotify`. 15 new tests incl. JSON round-trip; **0 new failures** (7 pre-existing Blueprints failures = known DEBT-006/perf set, not ours). |
| RR-03 | BTree | larger | per-child waypoint store + sink + DTO/mapper/emitter + layout-contract/projector. | ✅ DONE — waypoints stored per-child on `BTreeEditorNode` (LinkId is keyed on child VisualId; one edge per child); `BTreeParentChildLink.ChildVisualIdFromLinkId` resolves via the self-inverse XOR; 3 sink handlers; persisted through both paths (DTO `NodeEditorMetadataDto.Waypoints` via `BTreeWaypointDto{X,Y}` + mapper, AND the `[BTreeLayout]` method via `BTreeEditorLayout.LinkWaypoints` + builder + projector). Emitted only when present → **byte-identity gate stays green** (SampleScout unchanged). BTree.Editor 524/0, Persistence 129/0 (17/17 byte-identical), Generators 52/54 (only the 2 known pretty-print failures). |

**RR workstream COMPLETE (2026-06-13):** wire reroute points now work in HSM, Blueprint, and BTree. VE-DEBT-008 resolved.

### Shared command shape (all editors)
- `InsertReroute(LinkId, Vector2 pos)` → resolve link → `waypoints.Insert(<end or nearest-segment index>, pos)`. (Demo appends; for correctness insert at the segment nearest `pos` so multiple reroutes order along the wire. v1 may append + rely on drag; document choice.)
- `MoveReroute(LinkId, int idx, Vector2 pos)` → `waypoints[idx] = pos` (guard idx range).
- `RemoveReroute(LinkId, int idx)` → `waypoints.RemoveAt(idx)` (guard).
- After each: `MarkDirty()` (or the editor's equivalent dirty/save trigger).

### Verification per batch
- Build the editor + run its test project (no regressions; HSM baseline 486/0).
- Add command-sink unit tests: InsertReroute adds a waypoint to the right link; MoveReroute updates index; RemoveReroute removes; round-trip persists (where applicable: HSM .hsm.json, BTree .btree.json, Blueprint asset JSON).
- Visual gate: user inserts + drags a reroute on a wire and confirms the bezier bends and survives save/reopen.

## Notes
- `LinkId` for HSM = transition `VisualId`; resolve via `_asset.FindTransitionByVisualId`. BTree/Blueprint resolve per their link-id scheme.
- Insertion index ordering matters when a wire has multiple reroutes — prefer nearest-segment insertion over append so dragging doesn't tangle. Confirm what the canvas drag expects (it passes a `WaypointIndex` from the rendered dot order).
