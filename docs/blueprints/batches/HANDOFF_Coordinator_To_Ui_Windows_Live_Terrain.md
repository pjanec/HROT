<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
-->

# HANDOFF — coordinator → ui (on the user's WINDOWS machine): the OSM wizard LIVE end to end, and the first real frame rates

**For:** ui, **run on the user's Windows machine** (many cores, a real GPU, open internet — what the cloud VM cannot give: Overpass is blocked
there and its GL is software). **From:** coordinator. **Dispatched at `<STAMP>`**, scope frozen there. Merge the other lanes first; push
`chore: started HANDOFF_Coordinator_To_Ui_Windows_Live_Terrain at <sha>`; status file `STATUS_Ui_Windows_Live_Terrain.md`. Same
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md). ⛔⛔ **Never stop for a question** (R-4156); a premise that fails stops THAT item only (R-106).

**Basis:** [RESUME_Coordinator_Terrain_Import.md](../RESUME_Coordinator_Terrain_Import.md) §0 / §2 (how to run things; prerequisites) ·
[REPORT_Terrain_Import_Real_Runs.md](../REPORT_Terrain_Import_Real_Runs.md) (what the CLI runs already proved on this machine) ·
[DESIGN_Terrain_From_OSM.md](../../DESIGN_Terrain_From_OSM.md) §6 / §6b (the wizard), §7 (failures), §9 (usage policies) ·
[DESIGN_Map_3D_Mode.md](../../DESIGN_Map_3D_Mode.md) §6ah / §6ai (your chunked terrain, trees, roofs) · budgets R-4192 (**3-D ≥ 30 fps**).
⛔ No real terrain data, OSM-derived data or screenshots in git (`real-1` stays excluded locally; `terrain-input/` is ignored).

**Setup:** `bash scripts/install-sumo.sh` / `powershell -File scripts/install-sumo.ps1` (SUMO 1.20.0) · the editor in **Release** · the terrains:
`real-1` (already on this machine from the real runs), `osm-kolin-test` (same), `scale-1` (`python scripts/terrain-scale.py --trees`).

| # | id | what | done when |
|---|---|---|---|
| **L-1** | CE-4383 | ⭐ **the wizard LIVE, end to end, by hand and by rail**: File ▸ New ▸ Terrain from OpenStreetMap… → search a place (Nominatim) → draw a box on the real tiles (a village ≈ 2 × 2 km AND a town ≈ 1.5 × 1.5 km) → Options → **Preview from the real Overpass** (the budget verdict, the layers, the hover, a note click) → Import (real DEM, netconvert, the importer) → **Open in New Scenario** → run the scenario briefly. Time every stage; fix what breaks (the wizard is yours; the library / importer are the coordinator's — a fix there is an allowed, STATED cross-lane edit). Then one opt-in live rail (env var) that drives the wizard's model through a real small box headless | the run's numbers and the side-by-side stills (scratchpad + chat, ⛔ never git) in the status file; every failure fixed or filed with its message |
| **L-2** | CE-4384 | **Overpass robustness** (OSM §7, open since §14.8): ① an Overpass answer that is too large / a 504 or "runtime error: … out of memory" → split the box into 4, fetch, merge by OSM id (deduplicate); ② a **200 that carries `<remark> runtime error …`** is a FAILURE naming the service, not an empty area; ③ the error text the wizard shows names the service and the retry time. Live-proved on this machine against a box that triggers ① (a dense city) | rails on recorded / synthetic responses for ①–③ (⛔ no real OSM data in git — hand-written XML); the live proof in the status file |
| **L-3** | CE-4385 | ⭐ **the first REAL frame rates** — the editor on this GPU, Release: `real-1`, `osm-kolin-test`, `scale-1` in **3-D** (near 40 m, mid 400 m, far 3 km, and a flight along a street at 15 m/s) and **2-D** (whole terrain, close in): fps (min / avg over 10 s), frame ms split CPU / GPU if measurable, triangles and draw calls (your D-1 counters), memory, garbage per frame. ⭐ Under 30 fps anywhere ⇒ find the biggest cost and fix it (your chunk / LOD / instancing code), re-measure | the table in Map 3D §6ah (a new "measured on Windows" block) and the status file; every view ≥ 30 fps or a row naming the cost and the fix |
| **L-4** | CE-1449 | *(your own row, needs Windows)* Stride's run controls built by hand with no run choice and no gate | its row's done-when |

**Order:** L-1 → L-3 → L-2 → L-4. Gates: the touched test classes + each new rail red-proved once; frame rails as usual; `tracker-counts.py --check`.

---
_coordinator lane, 2026-11-10_
