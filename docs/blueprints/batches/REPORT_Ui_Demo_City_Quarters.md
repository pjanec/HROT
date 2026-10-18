<!--STATUS
state: LIVE — ui → behaviors: the report of HANDOFF_Behaviors_To_Ui_Demo_City_Quarters (UI-8…UI-12) and of UI-1 / UI-2 of HANDOFF_Behaviors_To_Ui_Platoon_Demos (their prerequisites).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — §4b the quarter plan, placements by quarter, as built
  - docs/DESIGN_Building_Interiors.md — §3g.1 the shared templates folder, the roof storey, the quarter templates
-->

# REPORT — ui → behaviors: demo-city's quarters *(UI-8 … UI-12)*, with UI-1 / UI-2

Started at `d49dbb844` (merged `behaviors@d49dbb844`). **FYI coordinator:** this adds to your demo-city terrain (`CE-4028`) — six of its
houses are replaced where a quarter stands; the road net and the traffic are untouched.

| item | state | what — and the deviation, if any |
|---|---|---|
| **UI-1** `CE-1181` | ✅ built | `Recipes/Buildings/` copied to the output; `house-2f` moved there. ⚠ a third lookup step, `TerrainAssets.RecipesSibling`, so source-tree readers (editor, unit suites) find the shared folder too |
| **UI-2** `CE-1182` (your `CE-2285`) | ✅ built | `roof` accepts rings; ⚠ a wall may also be LOW (`height`) — the parapet; `house-3f`'s stair house is 3 × 4.5 m (a 3 m rise needs a ~3 m flight inside it) |
| **UI-8** `CE-1183` | ✅ built | the plan picture + placements by quarter in Urban Battle §4b. ⚠ **the battle street is Avenue E**: Avenue M is one-way with a sidewalk on its east side only (measured in the net), so a west row's doors could face no sidewalk |
| **UI-9** `CE-1184` | ✅ built | `courtyard-house`, `office-4f` (roof storey reachable by a switchback stair), `shop-2f`, `market-hall` (offered, built). ⚠ doors 1.0 m: a 0.9 m door bakes or not by voxel alignment at an arbitrary placement (measured) |
| **UI-10** `CE-1185` | ✅ built | 136 buildings (80 residential, 27 commercial, 29 business) by `scripts/demo-city-quarters.py`; every quarter front door entered from its street (135); walled compounds on the battle street (10); two `house-3f` roofs see along it. ⚠ the market square's shops face the square (a pedestrian plaza), not a sidewalk — the shelter promise is checked and holds |
| **UI-11** `CE-1186` | ✅ built | the landing square 565..615 × 295..345 (50 × 50 m) behind the battle street's west row; a helicopter from the open south edge settles onto it without a collision |
| **UI-12** `CE-1187` | ✅ built | the feature's rails first (shelter, Traffic, deployment), then `demo-city/premises.json` — table below |

## Gates *(verbatim commands, `--no-build` after building the test project)*

| gate | result |
|---|---|
| `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "~.Terrain.\|~.Navigation.\|~.Eqs."` | 678 / 0 (incl. the shelter rail `CE4028_…`, `UI9_` ×4, `UI10_`, `UI11_`) |
| `dotnet test Hrot/Engine/Hrot.Core.Tests --filter ~DemoPremises` | 32 / 0 (26 bt-range + 6 demo-city) |
| `dotnet test Hrot/Engine/Hrot.Traffic.Tests` · `Hrot.ClusterRunner.Tests --filter ~ShippedTerrainFilesAreDeployedTests` | 56 / 0 · 3 / 0 |
| `python3 scripts/demo-city-quarters.py --check` | fences hold; files are current |
| `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` | OK · 312/312 · OK |
| red-proofs | the sibling lookup off · a wall's height ignored · roof rings read as flat · the upper flights dropped · every door locked (UI-10) · the office's storey-2 flight removed (UI-9) · a house moved into the landing square (UI-11) · the compound's yard wall removed (UI-12) — each fails its rail; all restored |

## The demo-city premises, as the test printed them

```
[urban-battle-street] 'the roof of res-be-22 (house-3f) sees south along the battle street' (sight, expect seesThrough) <661.4, 375, 7.7> → <653, 260, 1.5>
total transmittance 1
[urban-battle-street] 'the landing square is seen from the roof of office biz-ms-07' (sight, expect seesThrough) <584, 411.4, 14.5> → <590, 320, 1>
total transmittance 1
[urban-battle-street] 'the roof of res-bw-01 (house-3f) sees north along the battle street' (sight, expect seesThrough) <638.6, 9, 7.7> → <647, 128, 1.5>
total transmittance 1
[urban-battle-street] 'the yard of compound res-bw-03 is hidden from the street' (sight, expect blocks) <655, 35, 1.7> → <636.2, 35, 1>
crossed panel 'res-bw-03' concrete: transmittance 0
total transmittance 0
[urban-battle-street] 'the battle street is seen from an upper window on its west side (res-bw-02)' (sight, expect seesThrough) <639.3, 24.6, 4.6> → <647, 125, 1.5>
total transmittance 1
[urban-battle-street] 'the battle street is seen from an upper window on its east side (res-be-03)' (sight, expect seesThrough) <660.7, 40.4, 4.6> → <653, 130, 1.5>
total transmittance 1
```

## Not done here

- **UI-3 … UI-7** of the platoon hand-off (the CQB test house, `cqb-village`, the command picture, the Reports window, the scenarios) — next.
- ⚠ Not measured: a live cluster load of demo-city with the quarters (the vehicle layer's bake, the tile cache on a node). The
  infantry bake of the whole terrain took 14.4 s uncached in the rail.
