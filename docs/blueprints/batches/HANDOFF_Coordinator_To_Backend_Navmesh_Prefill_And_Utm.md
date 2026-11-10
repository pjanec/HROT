<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Import_UTM.md — ⭐ OWNS both: §2 (UtmTransform in the class diagram, "navmesh baked offline … stored with the terrain"), §6.1 stages 1 and 6, §11.4 I-b / I-l (what the importer does today)
  - ../../designs/navig-2/Navigation_Design_v2_0.md — §14 P2 the tiled bake and NavTileCache (keys = each tile's input hash)
  - ../../DESIGN_Geo_Origin.md — R-229, the ONE origin and IGeographicTransform; UtmTransform is its second implementation
  - ../../DESIGN_Artifact_Staging.md — how a terrain folder reaches every node (the prefilled tiles ride it)
  - ../../DESIGN_Scale_Rig.md — the rig the load times are measured with
  - ../REPORT_Terrain_Import_Real_Runs.md — the measured cold / warm loads this fixes
-->

# HANDOFF — coordinator → backend: the navmesh baked at import (CE-4393) and `UtmTransform` (CE-4394)

**For:** backend. **From:** coordinator. **Dispatched at `<stamp>`**. ⭐ Scope FROZEN there. **Queue it after your current egress work (CE-3427
N-1).** Merge `coordinator` first, push `chore: started HANDOFF_Coordinator_To_Backend_Navmesh_Prefill_And_Utm at <sha>`, and keep a status file.
You own the bake and the geo seam, so ⭐ **you design the details** (diagram first, in UTM §2 / §6.1 / a new §12). This handoff is the frame:
goal, fences, my leans, acceptance.

🔒 User, `2026-11-10`: *"pls write handoff to backend, points 1 and 2"*. Point 1 is the navmesh prefill, point 2 is `UtmTransform` (the
coordinator's status reply named them as what remains of the terrain import).

## N — the navmesh baked at import (CE-4393)

**Why (measured):** the first open of an imported terrain bakes its whole navmesh.

| terrain | first open | warm (tiles cached) | where measured |
|---|---|---|---|
| `real-1` (the other simulator's set) | ⛔ **366 s** cold, 106 722 tiles | 25.3 s | scale rig, the user's 24 cores ([REPORT](../REPORT_Terrain_Import_Real_Runs.md) §4) |
| `real-1` | ⛔ 154 s | — | the editor on Windows (Map 3D §6ah "Measured on Windows") |
| `osm-kolin-test` | ⛔ 57 s | — | the editor on Windows |

R-4192's budget is "load ≤ 30 s warm (navmesh cached)". A prefill makes the FIRST open a warm one. UTM §11.4 **I-l** deferred it ("a later item
once the bake time per terrain is measured"); it is now measured.

| # | lean (you may change it — say why) | rejected — one line |
|---|---|---|
| N-1 | ⭐ the importer bakes every tile and writes them INTO THE TERRAIN FOLDER (`navtiles/`); `NavTileCache` gains it as a read-only layer, consulted after memory and before the per-machine folder. The folder already reaches every node through staging, so each node's first open is warm | prefill only the importing machine's own cache: every other node (and every other machine) still bakes cold |
| N-2 | the import bakes through THE SAME baker, settings and inputs as a load (`RecastNavmeshFactory` → `RecastNavmeshBaker`, both layers, the doorway volumes, the woods), so the keys are identical by construction. ⛔ no second bake path | an importer-side bake: two bakers drift, and every key misses |
| N-3 | both import fronts get it, the CLI (`hrot-terrain-import`, `hrot-terrain-osm --import`) and the editor's OSM wizard, with progress and cancel; `--no-navmesh` skips it | — |
| N-4 | measure and report the `navtiles/` size for `real-1` and `osm-kolin-test` (it is staged with the terrain) | — |

⚠ **Fences:**
- CE-4391 S-3 (coordinator, `b2186adf3`) just added per-tile versions to `SwitchableNavmeshProvider` and the damage overlay in the P-7a obstacle bake
  (UTM §8.5). A damaged tile's key differs, so it re-bakes at runtime as before; keep that working.
- A stale `navtiles/` (an older bake setting) must be a cache miss, never a wrong tile. The key's hash already covers it; prove it with a rail.

**Done when:**
- a rail shows a prefilled terrain's first load bakes 0 tiles;
- `real-1`'s first open in the editor on Windows is inside R-4192's 30 s, measured and written into UTM §6.1 stage 6 as built;
- the size is reported (N-4).

## U — `UtmTransform`, the exact UTM frame (CE-4394)

**Why:**
- Today an imported terrain is placed through the tangent plane: the importer writes the lat / lon of the terrain's (0, 0) (UTM §11.4 **I-b**).
- Local positions are exact. ⚠ Lat / lon read-outs drift from the true grid by ≲ 2 m at 5 km, and headings ignore the grid convergence.
- The importer ALREADY writes the exact frame (`TerrainFolderWriter.cs:105`: `frame.utm` = zone, hemisphere, e0, n0); nothing reads it yet.
- For the other simulator's terrain, **e0/n0 = that engine's own origin (32N, 372 000 / 6 043 000)**, so both engines' local positions are numerically
  EQUAL (UTM §2 "frame"). That only holds once HROT's lat / lon side uses the same grid.

| # | lean | rejected — one line |
|---|---|---|
| U-1 | ⭐ `UtmTransform : IGeographicTransform` is the SECOND implementation of the existing seam (Geo Origin R-229; class sketch in UTM §2): local = (E − e0, N − n0, h), over the shared `Utm` class (`FDP/Toolkits/Fdp.Toolkits/Geographic/Transforms/Utm.cs`). A terrain that declares `frame.utm` selects it; every other terrain keeps the tangent plane (`WGS84Transform`). ⛔ The 53 files using `IGeographicTransform` do not change: the interface stays | a UTM mode inside `WGS84Transform`: two meanings behind one class |
| U-2 | selected through the ONE place the origin is set today (`TerrainResidency.ApplyGeoOrigin`, R-229). It needs a way to swap the IMPLEMENTATION, not only `SetOrigin`; design that seam, and keep the "the ONE instance every translator holds" property | each host choosing its own transform: two hosts, two answers |
| U-3 | headings: the grid convergence is applied wherever a heading crosses between local and geodetic (DIS orientation, the external engine). INVENTORY those sites first (`search_graph` + grep) | ignoring convergence: up to ~2–3° of heading error away from the zone's central meridian |
| U-4 | any zone, both hemispheres, a box across a zone edge (the OSM design §4b requires this of the shared transform) | zone 32N only |

**Done when:**
- rails:
  - `real-1`'s corners convert to the source's lat / lon to < 0.1 m;
  - local → geodetic → local round-trips;
  - a southern-hemisphere and a zone-edge box;
  - a heading with convergence;
  - an existing (tangent-plane) terrain unchanged byte-for-byte in its read-outs;
- UTM §11.4 I-b's "waiting for `UtmTransform`" is closed as built.

## Both

- Design first, in the OWNING doc (UTM; a new §12 "the importer's navmesh and the UTM frame, as designed / as built" is fine), with UML, the
  `INVENTORY`, and reciprocal `related-designs` links (Geo Origin ↔ UTM).
- Report with the gate contract (per gate: command, counts, delta, `--no-build`; reds proved pre-existing against the base sha).
- ⛔ Anything that needs the user is skipped and listed.
