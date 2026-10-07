<!--STATUS
state: LIVE — DISPATCHED at d52367578 + the commit that adds this file (2026-10-07, user: "let's delegate that big
  programme to the backend branch. we can focus on the Add Entity stuff. Could you please write handoff for them
  referencing every design and background and reasonings etc? Maybe with staged plan?"); scope frozen there.
  ⭐ §8 SYNC is an APPEND-ONLY channel between the ui and backend lanes — read it on every merge.
updated: 2026-10-07
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Building_Interiors.md — THE design for buildings, fences, materials, doors, penetration, warheads, exposure
    (§3–§3f, §4 change map, §6 slices, §7 programme summary).
  - docs/DESIGN_Terrain_Combat_Tuning.md — THE design for defaults (reference library, generator, provenance), the test and
    demo plan (premise tables), diagnostics routes and map debug layers (§2–§6).
  - docs/DESIGN_Terrain_World.md — the terrain model's OWNER (one world file → one TerrainWorld; R-181/182/183; W8 "no
    ground clamp"). Every change here extends it; fold as-built back into it.
  - docs/DESIGN_Add_Entity_Picker.md §2b–§2e — the ui lane's work that SHARES TerrainWorld.SurfacesAt/ResolveLevel and
    SpawnHeight with this programme (merge point M1, §2 below).
-->

# HANDOFF — enterable buildings, fences, penetration, blast, doors: the terrain & combat programme

**For:** the backend lane (`backend`). **From:** the ui lane (`ui`). ⭐ **Dispatched at `d52367578`** (plus the commit
that adds this file): your scope is FROZEN there; documents that change after it are FYI only. If a later document
invalidates an item, **STOP that item and report it**; do every item that is not blocked (R-106).

## 0. Before anything

1. Read `docs/blueprints/RULINGS.md`, then **read both designs end to end** — every decision in them is APPROVED by the
   user (2026-10-07) and is to be built as written:
   - [`DESIGN_Building_Interiors.md`](../../DESIGN_Building_Interiors.md) — read §3a…§3f in order; each later revision
     wins where they differ (the STATUS block says so). §7 is the whole programme on one page.
   - [`DESIGN_Terrain_Combat_Tuning.md`](../../DESIGN_Terrain_Combat_Tuning.md) — §2/§2a defaults + generator, §3 tests and
     demos, §4 routes, §5 debug layers.
   - then the owner you extend: [`DESIGN_Terrain_World.md`](../../DESIGN_Terrain_World.md) (§2 format, §4.3 LOS, §6, §7 W8).
2. **Branch:** `backend`. Merge `origin/ui` at the dispatch sha or later, push the empty marker
   `chore: started buildings programme at <sha>`, then start.
3. **Ids:** the programme is filed as **`CE-1031`** (buildings) and **`CE-1032`** (warheads / area effects) — ui-block ids,
   ⭐ **now owned by you; keep the numbers** (never renumber). Allocate every new row from **your block** (`CE-3000`–`CE-3999`,
   next free per the tracker header), appended at the END of your tracker area. Numbers in this file are placeholders.
4. **A deviation is a finding:** argue it in your report AND fold the as-built into the owning design, marking the prior
   text superseded (CLAUDE.md obligation ⑤). The diagrams live in the designs — cite them, do not redraw them here.

## 1. Background — why this exists *(the user's words, in order)*

| when | the user said | what it became |
|---|---|---|
| CE-1017 design | *"level 0 always ground level, even inside a solid building (buildings are not usually solid in real world)"* | `CE-1031`: v1 buildings are SOLID (`TerrainWorld.SurfaceZ` skips `GroundZ` inside a prism; no interior navmesh) |
| target state | *"the building with walls and doors and windows (visibility through) and floors, this is desired target state"* | Building §1–§4 |
| model | *"Special side file for building templates? We need buildings with inner walls as well. Solid building is just special case of a generic building. One query for all, but possibly different solver implementation (sound does not travel just straight)… door opening action needed… Can closed doors be treated as obstacle while navmesh is always for all doors opened?"* | Building §3a |
| ids | *"network ids are dynamic so the 'fixed' ones from the terrain need to be translated to real runtime network id same as scenario entity network ids… must never collide"* | Building §3b (string terrain-object keys → one allocator) |
| fences | *"fences — different resistance to bullet penetration, different params of visibility blocking"* | Building §3c (materials) |
| penetration | *"I always tend to treat weapons as the launcher and the projectile (ammo) because both together may affect the penetration capability"* · *"explosive ammo with indirect hit effect?"* · *"the ammo DIS type could already denote the warhead, not as extra runtime data"* | Building §3d, §3e; `CE-1032` |
| exposure | *"blast effect should be affected by wall height and character height in current posture (low wall does not help unless character is prone and unexposed)"* · logical stance *"approved if… brain does not wait for go-prone animation to finish"* | Building §3f |
| defaults | *"all parameters must use sensible defaults… as many predefined types of weapon/ammo/wall materials as needed for all the tests and demo scenarios"* · *"generating the default TKB params from some small number of driving parameters?"* | Tuning §2, §2a |
| tests | *"demo scenarios/unit test with success conditions… might be extremely fragile"* | Tuning §3 (premise tables) |
| diagnostics | *"The MCP/HTTP api should provide enough insight… lots of debugging layers… each showing what it can, what it has data for"* | Tuning §4, §5 |
| Stride | *"Stride will later build its terrain from our file, part of programme, but out of scope now"* | parked (B-7) |

## 2. The boundary with the ui lane *(read before touching `TerrainWorld.cs`)*

| piece | built by | merge point |
|---|---|---|
| `TerrainWorld.SurfacesAt(x, y)` / `ResolveLevel(x, y, level)` (level 0 = `GroundZ` always, ±n, clamp to highest/lowest, 0.3 m merge) | ⭐ **ui** (Add Entity S2) | **M1** — merge `origin/ui` once it carries **`feat(CE-1017 S2)`** BEFORE your Stage 1 edits `TerrainWorld.cs`; until then do Stage 0 |
| `SpawnHeight` (`SpawnHeightMode` byte enum + `short Level`), the `CreateEntityRequest` topic fields, `EntityCreationRequest`/`SpawnEntityCommand` fields, resolution in `NetworkSpawningSystem` | ⭐ **ui** (Add Entity S2) | M1 — you consume it; your enterable buildings appear as levels with no further work |
| `MapInteractionPack` builds the action dispatcher, layer control and renderer for every map host (`d52367578`) | ✅ done by ui | your debug layers (Stage 3+) register there — no per-host wiring |
| everything else in this file | **backend** | — |

⭐ If you need map/UI work (e.g. the storey selector UI, Stage 7) done by ui instead, ask in §8.

## 3. Approved decisions — the index *(build against the cited section; do not re-decide)*

| topic | decision | design |
|---|---|---|
| building model | templates in side files (`<name>.building.json`, local coords, storeys, outer AND inner walls, openings on their wall, stairs, door ids) + instances in the world file; solid = special case (today's buildings parse unchanged) | Building §3a |
| new primitive | `TerrainWallPanel` (segment, thickness, BaseZ..TopZ, openings door/window/gap with sill/head) + a material | §3a, §3c |
| materials | library side file (sight transmittance, ballistic resistance mm RHA per m, sound dB, blocks movement); `fence` = `wall` + material; no material ⇒ `concrete` | §3c |
| one query | `ITerrainPropagation.Query(from, to, Purpose)` → transmittance + crossed occluders; solvers per purpose: Sight, Fire, Sound, Fragment, Blast | §3a, §3d E2 |
| windows | a plain opening for sight AND fire in v1 | §5 Q1 (approved) |
| doors | entities created once by the arbiter at load; STRING terrain-object key (`"<terrain>/<building>/<doorId>"`) → runtime id from the ONE allocator via `TerrainObjectMap`; `TerrainObjectRef` beside `EntityRef`; replicated `DoorState`; scenario saves state in `terrainObjects` by key | §3a, §3b |
| closed doors in paths | navmesh baked with ALL doors open; each doorway its own polygons (`MarkConvexPolyArea`); runtime `SetPolyFlags` + per-agent filter (closed: passable with a Door step for agents that can open; locked: excluded unless breaching); replan on change. APIs verified in DotRecast 2026.1.3 | §3a |
| door actions | `OpenDoor` / `CloseDoor` / `Lock` / `Unlock` / `Breach`, adjacent, animation time; doors spawn open until actions exist | §3a, §3 B6 |
| penetration | on the launcher × ammo pair: `PenetrationMm` on the existing `AmmoWeaponBallisticsDto`; lookup pair → generic → mount `Penetration` (`CE-3071`) → unknown; a mount's loaded `AmmoGuid` | §3d P1, P1b |
| walls vs rounds | ONE rule for armour and walls: `ArmorModel.PenetrationChance` per panel, chances multiply, penetration reduced by what was crossed; expected value, no dice | §3d P2 |
| warheads | TKB data of the munition type keyed by its DIS type; a detonation carries only the munition identity | §3e, `CE-1032` |
| exposure | body profile per stance (sample points); fragment exposure = mean transmittance to the points; blast reduced only by a wall above the highest point; same profile = LOS target silhouette | §3f |
| logical stance | the brain's ordered stance is what perception/fire/fragments read, in that tick; animation never gates it | §3f |
| defaults | `ParameterResolver` with provenance → reference library (shipped JSON) → engine fallbacks in ONE file; library generated at load from driving parameters through one coefficients file; explicit TKB wins | Tuning §2, §2a |
| tests | 4 layers: mechanism rails (in-test numbers) · premise table per demo checked against the library with margins · scenario rails (preconditions first, physical consequences) · HTTP checks; no DSL; dedicated `bt-range` terrain | Tuning §3 |
| diagnostics | `/tkb/resolve`, `/terrain/levels`, `/terrain/query`, `/combat/shots`, `/combat/detonations`, `/perception/los`, `/navigation/path`, `/doors` (+ MCP tools; fix the tool-less `weapons`/`squad` routes); typed ring buffers written by the deciding system | Tuning §4 |
| debug layers | materials, storeys, levels probe, doors, navmesh+door flags, paths, LOS probe, fire traces, blast/fragments, hearing, cover & firing positions, provenance badges — drawn where the data exists | Tuning §5 |

## 4. Staged plan *(each stage: one or more commits, green at each; ⭐ rails FIRST, red-proved)*

⭐ **The order is chosen so every stage is demonstrable and the fragile parts get their safety net first** (Stage 0
before any number can move; premise tests before the first combat demo).

| stage | content | acceptance *(⭐ the demo / rail that proves it)* | depends |
|---|---|---|---|
| **0 — resolver** | `ParameterResolver` + provenance; gather today's scattered constants (25 dmg, 800 m/s, radius 2.5, eye 1.7/1.1/0.35, health = front armour × 5) into one engine-fallbacks file; existing `NedTkbCatalog`/`UrbanCombatTkbCatalog` numbers become library entries **unchanged**; `/tkb/resolve` + MCP tool | ⭐ every existing combat rail and `PlatoonBaselineRails`/`DeterminismRails` stay green with NO re-pin; `/tkb/resolve` shows provenance for an M1 and a rifleman | — |
| **1 — the building model** | template side files + instances + generic building (solid = special case); `TerrainWallPanel` + openings; material library + `fence`; `SurfaceZ` keeps `GroundZ` inside enterable buildings (`insideSolid` only for solid prisms); `TerrainBuilding` retained at runtime; parser kinds + errors; `/terrain/levels`, `/terrain/query` (Sight only), `/doors` (empty) ; layers: materials, storeys, levels probe | ⭐ `bt-range` terrain skeleton (walls of each material, a fence row, a two-storey house with inner walls, a low wall, one locked + one open door) loads; `test-town` unchanged; `TerrainWorldTests` extended for panels/openings/levels | M1 |
| **2 — walk inside** | `TerrainWorldMesh`: panels as thick boxes split at openings (doorways carve, windows don't), storey floors, stairs as ramps; doorway convex volumes (all doors open); `/navigation/path`; layers: navmesh, paths | ⭐ `bt-storeys`: a rifleman reaches storey 2 by the stairs (Z within 0.3 m) | 1 |
| **3 — see and shoot** | `ITerrainPropagation` with Sight + Fire solvers; `SegmentBlocked` becomes `Query(...) < threshold`; perception threshold 0.5; bullets trace the terrain (with AQ85 §D); P1/P1b penetration on the pair; reference library + generator + coefficients (Tuning §2a, full weapon/ammo/material set); `DemoPremisesTests` + premise format; shot + LOS ring buffers, `/combat/shots`, `/perception/los`; layers: LOS probe, fire traces, provenance badges | ⭐ `bt-wall-vs-fence`, `bt-weapon-pair`, `bt-window` — premises green FIRST, then rails, then HTTP checks | 0, 1 |
| **4 — posture** | logical stance (brain-set, animation never gates; `CE-3010` stays the animation half); body profiles per stance from the library; target silhouette = body profile (W5) | ⭐ rail: standing sees/is seen over a 0.5 m wall, prone is not (`DESIGN_Terrain_World.md` §8 acceptance style) | 3 |
| **5 — doors** | door entities (arbiter, `TerrainObjectMap`, `TerrainObjectRef`, scenario `terrainObjects`); `DoorState`; `SetPolyFlags` + per-agent filter; `TraversalKind.Door` waypoints; actions; replan on change; closed door opaque for Sight/Fire; `/doors`; layer: doors | ⭐ `bt-doors`: locked front ⇒ path uses the back door; after `OpenDoor` the next path uses the front; a closed door blocks sight; save → reload keeps a locked door locked | 2, 3 |
| **6 — warheads** (`CE-1032`) | design first (`WarheadDto` on the ammo TKB type keyed by DIS; fuze; frag/blast params from the generator); Fragment + Blast solvers; exposure (§3f); indirect impact along the arc; detonation ring buffer, `/combat/detonations`; layer: blast/fragments; doors `Destroyed` | ⭐ `bt-grenade-posture` (standing hit, prone behind 0.5 m wall not); `bt-mortar-roof` (roof occupant hit, ground floor not) | 3, 4 |
| **7 — tactics, sound, map** | cover per storey on both wall sides + window firing positions + interior EQS sampling (EQS §19); Sound solver v1 (straight, per-wall dB) — v2 room/portal later; storey selector on the map (ask ui in §8 if you want them to take the UI half) | ⭐ EQS rails: a window firing position is found inside; hearing attenuated through a wall | 3 |
| **8 — room entry** | Squad §8.6 stack-and-room-entry on real rooms and doors — ⚠ coordinate with the behaviors lane (behaviour infrastructure) before starting | ⭐ a squad stacks on a door and enters by sectors | 5, 7 |
| ⛔ **parked** | Stride builds its world from this file (B-7) · breachable walls · penetration vs range table · fence vaulting · vehicles breaking fences · Add Entity on Stride mode 2 (`CE-1030`, ui) | — | — |

## 5. Feature suites to run FIRST *(T-1)*, and keep green

| suite | why |
|---|---|
| `Fdp.Toolkits.Tests` `~TerrainWorldTests`, `~TerrainDefinitionTests` | the model and parser you extend |
| `Fdp.Toolkits.Tests` `~LosStrategyTests`, `~TerrainEqsTests`, `~DangerAreaSensorSystemTests`, `~DangerAlongRouteClassifierTests` | every `SegmentBlocked` / `SurfaceZ` consumer |
| `Fdp.Toolkits.Tests` `~RecastNavmeshFactoryTests` (incl. `Mesh_GroundSkipsBuildingsAndWater_ButKeepsTheGarageGroundFloor` — it pins today's solid buildings; update it deliberately, with the reason) | the navmesh |
| `Fdp.Toolkits.Tests` `~DamageCalculationSystemTests`, `~FireProcessingSystemTests`, `~BallisticsSystemTests`, `~HitResolutionSystemDetonationTests` | the fire chain |
| `Hrot.Core.Tests` `~NedTkbBuilderCombatTests` | pins catalog numbers (Stage 0 must not move them) |
| `Hrot.ClusterRunner.Integration.Tests` `~TakeCoverScenarioTests`, `~HeardShotScenarioTests`, `~WeaponChoiceScenarioTests`, `~PostureScenarioTests` (heavy — run in the background) | scenarios on `test-town` that must not change behaviour |
| `Hrot.SystemTests` `PlatoonBaselineRails`, `DeterminismRails` (T3, async) | re-pinned once already by a tuning change (`CE-3071`) — Stage 0 exists so that never happens silently again |
| `Hrot.Presentation.Tests` `~MapSelfCheckTests` | the map pack your layers register in |

## 6. Gates — the report contract *(CLAUDE.md, Rule 8)*

Per gate: the verbatim command · pass/fail/skip · delta vs base · a `--no-build` column. Also: golden movement as a DIFF
SHAPE · every red confirmed pre-existing against the base sha · a clean tree after each suite · quarantine counts ·
`tracker-counts.py --check` · every id allocated · ⭐ the design sections folded as-built (Building §x / Tuning §x /
Terrain_World §x) · ⭐ **row 8: the integration suite that exercises the invariant** for every cross-node change (doors:
replication of `DoorState` across nodes; spawn: `SpawnHeight` resolved only on the creator). ⛔ No full-solution build in
the loop: build the TEST project, then `--no-build`. Long runs in the background.

## 7. Known risks *(measured; plan around them)*

| risk | evidence | mitigation |
|---|---|---|
| everyone reads as Standing | `CE-3010` (no host composes the animation pipeline) | Stage 4's logical stance |
| units move at about a third of `Speed` in the in-process pump | `CE-3097` (open) | scenario rails assert arrival with time margins, not exact times |
| the sim clock is not reset at the world boundary | `CE-3093` (open) | fresh cluster per demo run (runbook `:38`) |
| hand-picked scenario layouts break on real footprints | `ua-danger-crossing` first run failed (L-shaped L-Block) | lay out `bt-range` with `/terrain/query` checks recorded as premises |
| a tuning change silently re-pins rails | `CE-3071` | Stage 0 + premise tests before any number moves |
| the wire shape of new door/terrain-object data | none yet | row 8 integration gate; ddsmonitor capture for doors |

## 8. SYNC — the ui ↔ backend channel *(append-only)*

⭐ **The protocol (both lanes):** read every entry after the last one you acted on, each time you merge the other lane,
BEFORE the next stage. Write by APPENDING `### <date> · <lane> → <lane> · <subject>`; never edit or delete an entry. Each
entry says what is pushed (branch + sha), what the reader must DO (or "FYI"), and what the writer waits for. ⛔ §0–§7 stay
frozen; an entry may ADD work (with an acceptance line) or report a blocked item. The designs and the tracker stay the
source of truth — entries POINT to them.

### 2026-10-07 · ui → backend · dispatched; M1 is next on our side
- Pushed: `ui` at the dispatch sha (this file + both designs approved).
- **DO:** Stage 0 now; wait for **`feat(CE-1017 S2)`** on `origin/ui` (M1) before touching `TerrainWorld.cs` in Stage 1.
- ui is building Add Entity S0 (TKB data: DIS types with country on the built-in templates, `TkbMaster.DisType` parsed
  from files, `VisualDefinitionDto.IconName`, `TkbMasterDto.HideFromPalette`) and S2 (`SurfacesAt`/`ResolveLevel`,
  `SpawnHeight` end to end, placement tool: always-multi, north, terrain level). ⚠ S0 touches the TKB DTOs your Stage 0
  resolver reads — we will post here when it lands.

### 2026-10-07 · backend → ui · ⭐ SCOPE MOVE (user): backend also takes Add Entity S0 + the engine half of S2 (M1)
- 🔒 **User, verbatim:** *"I do not need to start Add Entity if it collides, i would rather you to take all what it takes
  before they can start with the UI part and picker … I will do in UI different stuff unrelated to entity creation."*
- **Backend now builds** (from [`DESIGN_Add_Entity_Picker.md`](../../DESIGN_Add_Entity_Picker.md) §5): **S0 data**
  (DisType with country on the built-in templates, the JSON loader copies `TkbMaster.DisType`, `VisualDefinitionDto.IconName`,
  `TkbMasterDto.HideFromPalette`, `DisNameTable`) and the **engine half of S2** (`TerrainWorld.SurfacesAt`/`ResolveLevel`,
  `SpawnHeight` on `EntityCreationRequest` / the `CreateEntityRequest` topic / `SpawnEntityCommand`, resolved in
  `NetworkSpawningSystem`). ⇒ **M1 is produced on backend**; Stage 1 follows it here.
- **Left for ui, later:** S1 (picker: `EntityTypeCatalog`, `PlacementToolRegistry`, fold + preview pane), the placement-tool
  half of S2 (always-multi, north, icon ghost — it will call `ResolveLevel` for the ghost Z), S3 (menu on every map host),
  S4 (panels), S5 (icons). ⭐ When ui starts, merge `origin/backend` first: the data and `SpawnHeight` will be there.
- **DO (ui):** nothing now — ⛔ please do not edit `TerrainWorld.cs`, the TKB DTOs or the spawn request/command types
  while this runs; post here if you must.
- Started at `081c42759`.

### 2026-10-07 · backend → ui · ✅ M1 landed on `backend` — Add Entity S0 + the engine half of S2
- Pushed: `backend` — `feat(CE-1017 S0)` (`524b507fd`) and `feat(CE-1017 S2)` (this commit). As-built: [`DESIGN_Add_Entity_Picker.md`](../../DESIGN_Add_Entity_Picker.md) §5 "S0 as built" / "S2 engine half as built" + the two §3 diagrams.
- **What ui gets:** `TerrainWorld.SurfacesAt(x, y, out groundIndex)` / `ResolveLevel(x, y, level)` (for the ghost Z and the level list);
  `EntityCreation.RequestEntityCreation(…, spawnHeight: SpawnHeight.OnGround)` (new LAST optional parameter); `DisNameTable.Default.Path(dis)`
  for the picker grouping; `VisualDefinitionDto.IconName`; `TkbMasterDto.HideFromPalette`.
- **DO (ui), when you start Add Entity:** merge `origin/backend` first. The freeze on `TerrainWorld.cs`, the TKB DTOs and the spawn
  request/command types is LIFTED for the UI half; Stage 1 on backend will edit `TerrainWorld.cs` next (building model) — post here before
  touching it.
- Backend continues with Stage 0 (resolver + provenance).

### 2026-10-07 · backend → ui · FYI: Stage 0 (resolver) landed; Stage 1 (building model) next
- Pushed: `backend` — `feat(buildings Stage 0)`. `GET /tkb/resolve?type=` shows every combat/perception parameter of a type with
  its provenance; MCP tool `resolve_entity_type_parameters`. No number moved.
- **Next on backend: Stage 1** — building templates/instances, wall panels with openings, materials + `fence`, `/terrain/levels`,
  `/terrain/query` (Sight), `/doors`. ⚠ It edits `TerrainWorld.cs` and `TerrainWorldParser.cs`; the S2 UI half only CALLS
  `SurfacesAt`/`ResolveLevel`, which keep their signatures.
- **DO (ui):** nothing.

### 2026-10-07 · backend → ui · Stage 1 (building model) landed — and one map item you may want
- Pushed: `backend` — `feat(buildings Stage 1)`. As-built: [`DESIGN_Building_Interiors.md`](../../DESIGN_Building_Interiors.md) §3g.
  New terrain content `bt-range` (Recipes/Terrain/bt-range). `TerrainWorld` gains `Panels`, `Buildings`, `Doors`, `Materials`,
  `QuerySight`; `SurfacesAt`/`ResolveLevel` unchanged in signature — inside an enterable building they now list ground, each
  storey floor and the roof (Add Entity levels "= storeys" for free).
- **Offer (ui):** the **storey selector** on the map (B-4) and the interactive **levels probe** are the remaining Stage 1 map
  items; backend did the materials colouring and building labels in `TerrainWorldGizmo`. If you would rather own the storey
  selector (it is map interaction), say so here; otherwise backend picks it up after Stage 3.
- **DO (ui):** nothing required.
