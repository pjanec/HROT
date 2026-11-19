<!--STATUS
state: WITHDRAWN `2026-11-19` — the coordinator's HANDOFF_Coordinator_To_Terrain_Glass_And_Building_Edits.md assigned GW-3 to terrain (cross-lane); terrain BUILT it (CE-5034, `GlassLayer3D`). ⇒ ui: nothing to build — an FYI that a new layer now lives in your `Hrot.Presentation/Map3D` and is added in `MapInteractionPack` · was: LIVE — terrain → ui · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Glass_Windows.md — OWNS the glass (D-1…D-10), §5 the look, §6 GW-3
-->

# HANDOFF — terrain → ui: GW-3, the look of glass (CE-5034)

Dispatched at `36a1ad8f3` (terrain). Your scope is FROZEN at that sha; later documents are FYI only.

| what is there for you (built, terrain) | where |
|---|---|
| every glazed window's PANE: a thin leaf per window, `(panel, opening)` per pane | `TerrainWorld.PaneLeaves` / `Panes` (CE-5032) |
| the damage of every window, as every node's view sees it — hits, cracks (u, v 0..255 on the pane), broken | `DoorStates.Windows` → `WindowTable` (`TryGet`, `PaneBroken`), replicated as `dtWindowDamage` (CE-5033) |
| where a window broke (the shard burst's trigger on a map is the state change — a late joiner plays none) | a pane whose entry turns `Broken` |

| build (design §5, mockup `docs/UX/glass-windows.svg`) | |
|---|---|
| intact: a pale-blue translucent pane in the opening, a faint sheen | the chunk build, from `PaneLeaves` |
| cracked: a crack star at each kept (u, v), both faces | from `WindowTable` |
| broken: the open hole (as today), a few shards in the frame; a LOCAL shard burst outward and down at the moment it breaks | from the state change |
| acceptance | frame rails against the mockup (R-4146); the scenarios `gw-shoot-the-window` / `gw-fire-out` (scenarios/tests) show all three states |
