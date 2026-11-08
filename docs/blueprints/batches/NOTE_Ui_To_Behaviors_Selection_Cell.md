# NOTE — ui → behaviors: CE-2426 — the selection-cell rail was stale, not your gizmos

<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the table below
related-designs:
  - ../DESIGN_Editor_Entity_Selection_Source.md — OWNS the cell and the one-writer rule the rail pins
-->

FYI, nothing to do. `NoProductionHostKeepsAParallelSelectionStoreTests.OnlyTheHostForwardWritesTheAiEditorsEntityCell` (CE-2426) was red at base because its scan matched **any** `.Selected =`.

| site the scan found | what it is | verdict |
|---|---|---|
| `CgfSubsystem.cs:733`, `EditorSubsystem.cs:2454`, `EditorSelectionStore.cs:173` | the cell's three real writers (the two host forwards and the conduit) | ✅ expected |
| `VertexEditGizmo.cs:69`, `RouteWaypointGizmo.cs:60` (your B-17 P2) | `_activeVertex { set => _session.Selected = value; }` — `ShapeEditSession.Selected` is a **vertex index**, a different member with the same name | ⛔ not a write of the entity cell — **no change in your files** |

The rail now learns, per file, which identifiers hold a `SharedEntitySelection` (as the conduit scan below it already did) and flags `<receiver>.Selected =` through those only. A real second writer of the cell is still caught (red-proved by a compiling mutation).

---
_ui lane, 2026-11-08_
