<!--STATUS
state: LIVE — dispatched to the WINDOWS session, diagnosis only
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_From_OSM.md — §5.4 roads (netconvert call, CE-4409 optional roads, CE-1494 heights, CE-6021 service type override)
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — why Týn is imported (the city-battle town)
-->

# HANDOFF — ui → WINDOWS session: why the OSM import of Týn shows no roads (CE-6022)

**Dispatched at:** the `ui` commit that adds this file (on top of `a7f8f1fde`). ⛔ **Diagnosis only — change no code.** Report the answers to §3
in chat (or as `docs/blueprints/batches/REPORT_Windows_OSM_Roads.md` on `ui`).

🔒 User, `2026-11-13`: *"OSM import produced no usable roads; if it did they are not drawn."* Earlier: *"city buildings ok but no road among them"*.

## 0. What is known (measured on the Linux cloud VM, same OSM data)

| | |
|---|---|
| the import pipeline, Týn 4 × 3.5 km, real netconvert 1.20.0 | ✅ `roads/roads.net.xml` written, 1 560 vehicle edges, 139 km; 99.2 % of the drivable OSM streets carried (CE-6021, after the fix) |
| the fix `5e6f64fe1` (on `ui`) | `NetconvertRoadBuilder` now passes `--type-files <SUMO_HOME>/data/typemap/osmNetconvert.typ.xml,<override>` — ⚠ **if that SUMO file is NOT found it THROWS**, and a road failure imports the terrain WITHOUT roads (CE-4409, an amber warning in the wizard's finished box) |
| the 2-D road layer | draws only DRIVABLE lanes, at most 2 048 segments per view (fixed in the same commit: town streets first) |
| ⛔ not measured | anything on Windows: the SUMO install layout, the wizard's warning, the editor's log, the 3-D road mesh |

## 1. The suspects, most likely first

1. **The build predates the fix** — then service streets have no car lane and are not drawn (the CE-6021 defect).
2. **The fix itself fails on Windows** — `Netconvert.SumoHomeOf` (Hrot/Engine/Hrot.Terrain.InputSet/Netconvert.cs:98) returns a folder with no `data\typemap\osmNetconvert.typ.xml` ⇒ throw ⇒ NO roads at all, with the amber warning.
3. **netconvert fails or is not found** (CE-4409) ⇒ no `roads/` ⇒ the terrain has no `sumoNet`.
4. **The net exists but is not loaded/drawn** — the terrain's `terrain.json` has no `sumoNet`, or the loader places it off the terrain, or the road layer is off.

## 2. Steps (run in order, stop at the first that answers)

1. `git log --oneline -3` in the checkout the editor was built from — does it contain `5e6f64fe1`? Rebuild the editor if not (`dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`).
2. Re-run the import of Týn in the editor's OSM wizard (File ▸ New ▸ Terrain from OpenStreetMap…, the same area). **Copy the finished box text verbatim** — any amber line is the answer to suspects 2/3.
3. Find what netconvert the import used: `where netconvert`, `echo %HROT_SUMO_HOME%`, `echo %SUMO_HOME%`, and whether `<repo>\.local\sumo\1.20.0\` exists. In that install, does `…\data\typemap\osmNetconvert.typ.xml` exist? (pip layout: `<venv>\Lib\site-packages\sumo\data\typemap\…`.)
4. Open the imported terrain folder (`assets\terrain\<name>\` or the wizard's output folder): list it. Does `terrain.json` contain `"sumoNet"` and `"roadNetworks"`? Is there a `*.net.xml`, and how big is it?
5. If a net exists, count it (PowerShell):
   ```powershell
   $x=[xml](Get-Content <net.xml>); ($x.net.edge | ? {$_.function -ne 'internal'}).Count
   ($x.net.edge | ? {$_.type -eq 'highway.service'} | % {$_.lane} | ? {$_.disallow -match 'passenger' -or ($_.allow -and $_.allow -notmatch 'passenger')}).Count   # service lanes WITHOUT cars — expect 0 after the fix
   $x.net.location.convBoundary
   ```
6. Load the terrain in the editor and grep its log (`<ClusterRunner bin>\logs\`) for `[Traffic]` — two lines are expected: `SUMO net '…' parsed in … ms — N edges …, placed …` and `road scene built … — N triangles`. Copy both. None ⇒ the net was not declared/loaded (suspect 4).
7. With the terrain loaded: does the 3-D view show road surfaces (zoom onto the old town square)? Does the 2-D map show road lines at a close zoom (~1 m/px) and at the whole-terrain view? A screenshot of each in chat (⛔ not in git).

## 3. Report (≤ 15 lines)

The commit built · the finished box text · netconvert path + whether the type map file exists · the terrain folder listing + `terrain.json` road keys · the step-5 counts + `convBoundary` · the two `[Traffic]` log lines · 3-D / 2-D yes/no + screenshots. ⛔ No fix — the ui/showcase-world lane fixes from your numbers.
