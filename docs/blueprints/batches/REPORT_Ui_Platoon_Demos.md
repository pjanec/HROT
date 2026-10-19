<!--STATUS
state: LIVE — ui → behaviors: the report of HANDOFF_Behaviors_To_Ui_Platoon_Demos (UI-1 … UI-7).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Building_Interiors.md — §3g.1 the shared templates folder, the roof storey, house-3f, the CQB house (UI-1…UI-3)
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — §12.1 cqb-village as built (UI-4)
  - docs/DESIGN_Command_Display.md — (ui) the sectors, the command picture, the Reports window (UI-5, UI-6)
-->

# REPORT — ui → behaviors: the platoon demos' buildings, terrain and displays *(UI-1 … UI-7)*

Started at `d49dbb844` (merged `behaviors@d49dbb844`); `behaviors` + `backend` merged again `2026-10-19` before UI-4. Your rows taken
over: `CE-2285` (→ `CE-1182`), `CE-2290` (→ `CE-1189`), each with a dated line.

| item | state | what — and the deviation, if any |
|---|---|---|
| **UI-1** `CE-1181` | ✅ | `Recipes/Buildings/` (copied to the output); `house-2f` moved there; `bt-range` unchanged. ⚠ a third lookup step, `TerrainAssets.RecipesSibling`, so the editor and the suites (which read the source tree) find the shared folder too |
| **UI-2** `CE-1182` | ✅ | `roof` accepts rings; ⚠ a wall may also be LOW (`height`, the parapet). `house-3f`: a roof storey with a 3 × 4.5 m stair house (door `roof`) and a 1 m parapet — crouched cover by the thresholds, the head seen from the street only above it |
| **UI-3** `CE-1188` | ✅ | `cqb-house` exactly the picture (hallway, A–D, closet E only through D, side doors N / S), front door WEST as drawn; in `bt-range` 60 m west of House A (your (160, 100) was House B's); 4 premises rows. Doors 1.0 m |
| **UI-4** `CE-1189` | ✅ | `Recipes/Terrain/cqb-village/` = §12.1's map (OBJ `house-3f` between H1 / H3, H6 / H4 / H5 / SNP across Main Street, CMP with its gate and posts, hedge, low wall, berms, wreck); premises: the support line sees OBJ's south windows on both storeys · SNP sees OBJ and the support line · the hedge route hidden from OBJ's south face · the CMP gate seen from the NE post. ⚠ the streets are open ground (no road network — the demos are on foot) |
| **UI-5** `CE-1192` | ✅ (⚠ D4) | sectors outlined in the unit's colour + name and NoFire areas hatched red — every node (`EngagementArea` is replicated); the selected commander's `CommandPicture` as age-faded symbols, its subordinates' progress, requests; a selected man's arc. ⚠ **the picture is brain-local**: it draws on SimHost's map, not yet on the editor / CGF / ExCon — **your call**, [NOTE_Ui_To_Behaviors_Command_Display_Data](NOTE_Ui_To_Behaviors_Command_Display_Data.md) (lean: log before the inbox's picture guard; replicate the picture read-only) |
| **UI-6** `CE-1191` | ✅ (⚠ D4) | the Reports window (editor, ExCon, SimHost) over the node's `SquadReportLog`: time · kind / subtype · from → to · SPOT fields · age · SYN · verdict, unit and kind filters; the wording shared with `GET /squad/reports` (`CommandReportText`). ⚠ the log fills only where the staff runs — the same D4 |
| **UI-7** | ⏭ not taken yet | the offer stands; behaviors' `sl0-platoon-march` (`CE-2262`) is the hierarchy to mirror when it is |

## Gates *(verbatim, `--no-build` after building the test project)*

| gate | result |
|---|---|
| `Fdp.Toolkits.Tests --filter "~.Terrain.\|~.Navigation.\|~.Eqs."` | 688 / 0 |
| `Hrot.Core.Tests` (whole) | 319 / 0 (`DemoPremises` incl. bt-range, cqb-house, cqb-village, demo-city) |
| `Hrot.Presentation.Tests` (whole, `DISPLAY=:99`) | 769 / 0, 2 skipped (the known audio pair) |
| `Hrot.ExCon.Tests` · `Hrot.Editor.Tests` | 402 / 0 · 516 / 1 — ⚠ the one red is `NoNewInlineNetworkIdLookupAppears` on coordinator's `TrafficReplicas.cs` ([your note](NOTE_Behaviors_To_Coordinator_NetworkId_Rail_Red.md)), untouched here |
| `Hrot.ClusterRunner.Tests --filter ~ShippedTerrainFilesAreDeployedTests` | 3 / 0 (every shipped world loads from the build output, templates included) |
| `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` | OK · 312/312 · OK |

## A finding for every lane that walks into buildings

⚠ A red-proof found it: a **partial** `PlanPath` ends at the goal's x / y **and height** when the goal lies straight above a reachable floor
(DotRecast clamps the end onto the last polygon and keeps the goal's height). A path "to the roof" with its stairs removed still "ended
on the roof". ⇒ judge completeness by `PathExists` / `PathCost`, never by the last waypoint. The ui rails and backend's `Stage2_*` rail now
do; recorded in [Navigation_Design_v2_0 §14](../../designs/navig-2/Navigation_Design_v2_0.md).
