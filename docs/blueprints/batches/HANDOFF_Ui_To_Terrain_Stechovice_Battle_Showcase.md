# Handoff: ui → terrain — finish the Stechovice battle showcase

| | |
|---|---|
| **From** | `ui`, 2026-11-23 |
| **For** | the `terrain` lane |
| **User, verbatim** | *"i would like you move the scenario work to terrain lane too - pls describe what is left to do in another handoff to terrain lane."* — the original ask: *"create Scenario on Stechovice terrain, where blue and red team are fighting each other, and there are some military vehicles, and there is platoon commanding the squads. Basically something like the city battle, but located in Stechowice. Amazing would be if there are some helicopters flying or aircraft flying, and there is also some air defense fighting the aircraft. So it is kind of a show. … Some riverbank battle and some village battle."* |
| **Depends on** | [HANDOFF_Ui_To_Terrain_Large_Terrain_Router_And_Cover.md](HANDOFF_Ui_To_Terrain_Large_Terrain_Router_And_Cover.md) (yours, in progress) — the scenario cannot be judged until those two fixes land |
| **Ids** | none allocated here — yours to allocate |

## 1. What exists (on `ui`)

| piece | where | state |
|---|---|---|
| the terrain | `assets/terrain-input/Stechovice/` (the import INPUT, ~3 MB, heights at 30 m) — imported on first use by `TerrainCatalog` (CE-11037, R-11017, [DESIGN_Terrain_Import_UTM.md](../../DESIGN_Terrain_Import_UTM.md) §13) | ✅ built; ~290–390 s once per machine on 4 cores, navmesh prefilled |
| the generator | `scripts/stechovice_battle_scenario.py` — reuses the city-battle platoon kits (`macros/city-battle/rifle-platoon`, `militia-platoon`, squads 1–3); prints a placement table checking water, building footprints and slope against the input set | ✅ runs |
| the scenario | `scenarios/showcases/stechovice-battle/scenario.json` (~100 entities) | ⚠ never run to the end |
| the checks | `scenarios/showcases/stechovice-battle/checks.draft.json` — 15 expect rows, **never judged**; named `.draft` on purpose so `scripts/scenario-regression.sh` does not pick it up | ⏳ rename to `checks.json` when it passes |

**The frame (measured on the running editor):** scenario (x, y) = input-set local + (2021, 2128) — the terrain's origin is its south-west
corner (`terrain.json` `frame.corner`). The generator keeps input-local metres and converts at the edge (`W()`).

**The layout** (scenario coordinates): Blue HQ (SupportCommand) over both Blue platoons, the Apache and the A-10.
- **Riverbank** (~y 1568): Blue platoon (leader + 3 squads, PlatoonDefend, facing west) + IFV on the east terrace (~973); Red platoon + a
  technical on the low west terrace (~765); river ~135 m wide. Chosen by a DEM + woods line-of-sight search — elsewhere the west bank is a
  wooded cliff and the banks never see each other.
- **Village** (~y 2268): Red platoon (3 squads between houses, PlatoonDefend), 2 AT gunners, a technical on the E–W road; Blue platoon
  (PlatoonAttack) from ~x 2251 with a tank and an IFV on the road.
- **Air**: Apache (AirPlatformAttack, 60 m AGL, from the east), A-10 (AirPlatformStrike on the ZU-23, 450 m AGL, from the west).
- **Air defence**: a radar SAM and a ZU-23 on the village's north hill (the west plateau is wooded cliff).

## 2. What was blocking, and its state

| # | blocker | state |
|---|---|---|
| 1 | the load failed after 30 s: the navmesh stage (34 s here) reported no progress (`DirectorRunner.LoadStallSeconds`) | ✅ `ui` `743de9e49` (CE-11038): the navmesh stage reports progress; a real Stechovice load passes it |
| 2 | every scenario-regression run re-imported the terrain into its private staging root | ✅ `ui` `743de9e49` (CE-11039): the import goes to the dev checkout's `assets/terrain` (else the NAS), once per machine |
| 3 | stepping ~0.3 sim s per 30 wall s — `RoadGraphRouter.Route` and `VehicleFootprint.Solid` scan the whole world | ✅ yours, `terrain` `e9a48cd67` / `074f8d763` (CE-5043 / CE-5044) |
| 4 | **the first-use import itself reports no progress** (~6 min on 4 cores, on the frame thread): a load that TRIGGERS the import fails the 30 s stall check though the import completes; the next run is fine | ⏳ **CE-11040, yours if you take it**: forward the importer's `IProgress<ImportProgress>` into `LoadProgress.ReportDetail`, as CE-11038 does for the navmesh. Until then pre-import once (`Hrot.Tools.TerrainImport`, or one run that is allowed to fail) |
| 3a | every soldier's move also priced a road route | ✅ `ui` `09504a447`: R-11018 — soldiers go `NavmeshFirst` by default |

## 3. What is left — yours

0. **Merge `ui`** (at or after `743de9e49`) — it carries CE-11038/11039, R-11018 and this scenario.
1. **Run it**: Preview → `POST /sim/run-to`, or `scripts/scenario-regression.sh` once `checks.json` exists. Measure the wall
   time of a full run; keep the sim length to a few minutes.
2. **Behaviour findings from the partial realtime runs** (~14 sim min, before the fixes) — check each again, fix or adjust the scenario:
   - the riverbank platoons were never seen to fire at each other (they see each other in the LOS model) — confirm they engage;
   - the A-10 flew on for hundreds of km after its strike — nothing in `AirPlatformStrike` brings it back (a return / orbit, or a second
     task);
   - HQ `SupportCommand` re-tasked the Apache to the riverbank technical instead of its assigned village one;
   - the village Blue `PlatoonAttack` started moving only at ~269 s sim — find why it waits that long;
   - the village tank's `MoveToLocation` ended `FailedBlocked` off-road; the worker blamed `VehicleParams.OffRoad*` = 0 on the M1 —
     ⚠ **unverified** (`OffRoad.Authored` may treat 0 as "not modelled"); it now drives along the E–W road;
   - a "outside the perception grid [0..1000]" warning appeared once at spawn — confirm it is transient on a 4 × 4.3 km terrain.
3. **Tune and judge the checks** (`checks.draft.json`): several rows are guesses (the Apache / A-10 reaching the named places, the SAM firing
   at the Apache). The description must say What happens and how to Judge it (CLAUDE.md "a feature works only when its scenarios pass").
   Rename to `checks.json`, pass `scripts/scenario-regression.sh stechovice-battle`, capture the golden (`--verify-capture`), re-run → GOLDEN same.
4. **Preview and docs**: `preview.jpg` if the thumbnail script makes one; the scenario's row in `docs/CAPABILITIES.md` / `docs/USAGES.md`
   the way `city-battle` is listed.
5. **Optional, the user's wish list**: more aircraft / helicopters over both battles, the air defence visibly engaging — "kind of a show".

## 4. Added `2026-11-23` — the performance items (tracked, yours if you take them)

- **CE-11043** — a door is an entity only while its state differs from the house's default (Stechovice loads 4,431 door entities today;
  every reader already falls back to the authored state). Suggestions ①–⑥ and the one design decision (who creates the entity on the first
  change) are in the tracker row.
- **CE-11044** — the ~20 systems that walk all ~4,500 entities every tick narrow their queries to what they read (the measured 114 ms per tick).
- **CE-11049** — measured by `ui` on headless Stechovice with the battle running: each ground change re-bakes the obstacles and rebuilds the
  WHOLE terrain collision index (`TerrainResidency.Indexed → BepuTerrainIndex..ctor`, ≈ 15 % of all samples, on worker threads). Lean:
  rebuild it incrementally, as the navmesh already reuses unchanged tiles. Details in the tracker row.

