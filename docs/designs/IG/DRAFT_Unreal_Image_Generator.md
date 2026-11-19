<!--STATUS
state: LIVE — a DRAFT (recorded direction, not a buildable design)
updated: 2026-11-17
current-answer: the whole file; the measured results in "Measured on Windows `2026-11-17`"
stale-below: none
known-rot: none
known-conflict: none
related-designs:
  - ../../reference/NED_DDS_Protocol.md — OWNS the wire an outside node reads and writes (its first outside reader is the DIS/HLA gateway)
  - DESIGN-IG.md — the image-generator node role (IG) inside HROT, today raylib / the 3-D map; the Unreal client is another IG
  - ../thermal/DESIGN_Thermal_Drivers.md — the external INFRARED image generator connects the same way (DDS, the drivers on the entities)
  - ../weapons/DRAFT_Weapon_Malfunctions_And_Physical_Devices.md — physical devices in the loop: another outside node on the same bus
-->

# DRAFT — Unreal Engine as an image generator next to HROT (option B) — CE-4651

🔒 User, `2026-11-16`: *"Is there any real possibility how to embed the HROT engine into an Unreal based application?"* — then, on the two options:
*"Yes, the option B looks very attractive."* · `2026-11-17`: *"we talked about Unreal Engine integration using option B, which is actually HROT engine running
in parallel to Unreal Engine plugin, and they talk over DDS. Is that recorded somewhere?"* — recorded here.

## The direction (option B)

**Unreal runs next to a background HROT and joins its cluster as an IMAGE-GENERATOR node** — just another node on the bus:

| part | how |
|---|---|
| process | the Unreal plugin starts `Hrot.ClusterRunner` (headless) as a background process, or connects to a cluster already running elsewhere |
| data in | the plugin subscribes to HROT's DDS topics (positions and rotations, damage, effects, weather, …) through CycloneDDS's native C / C++ library — the protocol is already written up for outsiders (`NED_DDS_Protocol.md`) |
| same machine | ✅ **local sockets** (🔒 user, `2026-11-17`: *"no shared memory support, we will use local sockets"*) |
| control | the existing HTTP debug API (load, play, pause, possess) or the command topics |
| what HROT changes | nothing — the IG role exists; Unreal only subscribes and draws |

**Why B, not A** (A = the .NET runtime hosted INSIDE the Unreal process via `nethost` / `hostfxr`, the way the Stride host composes a node in-process):
B keeps two processes (a crash in one does not kill the other; each debugs normally), changes nothing in HROT, fits the cluster design, and the same client
can later point at a remote cluster or join a multiplayer session. ⇒ A only for a specific need: one executable to ship, the lowest possible latency for a
first-person player, or game code that must call the simulation synchronously (and even then HROT steps on its own threads, Unreal reads snapshots).

## The user's direction, `2026-11-17`

🔒 *"unreal should play the same role as the 3d map, including the gizmos and FPS player inputs"* · *"how to import terrains is the right question,
likely the one to be solved first as everything depends on that. I need to use the nice unreal renderer and its capability to render nice terrain. Out
terrains are minimalistic but unreal can render them in nice way. We could import our terrain to unreal to make it nicer, or vice versa."* ·
*"maybe we can author terrain in unreal is a way compatible with our terrain?"*

⇒ the Unreal client is a full **3-D map station**: it draws the world, the gizmos (selection, routes, areas, labels, the NATO symbols) and takes the
FPS player's input — the role the 3-D map has in HROT today. ⇒ **the terrain comes first.**

## What a HROT terrain IS *(measured `2026-11-17`)*

| layer | today | file |
|---|---|---|
| ground | a HEIGHT GRID — small terrains an ASCII grid (10–100 m cells, e.g. `basic-desert` 61 × 61 at 10 m); the large imported ones a TILED store (256 × 256 posts, 1 m, 16-bit) from a GeoTIFF DEM | `*.height.asc`, `DESIGN_Terrain_Import_UTM.md` §2 |
| the semantic world | ONE GeoJSON: `building` (footprint + height; interiors designed), `wall` (a line + a material: hedge, wood fence, steel plate, …), `surface` (an area of class `open` / `forest` / `water`), `prism`, `ramp`, `slab`, lone trees | `*.world.geojson` (`city-battle`: 472 buildings, 194 walls, 146 surfaces) |
| roads | the SUMO network (lanes with their geometry) + `roads.json` | `roads.json`, the `.net.xml` |
| geo | an origin (lat / lon) or a UTM frame | `terrain.json` |
| changes at run time | breaches, craters, felled trees, collapsed buildings, bulldozer cuts — replicated (Dynamic Terrain, §2b of CAPABILITIES) | the terrain feature topics |

⇒ our terrain is **minimal geometry + rich MEANING** (what each area and line IS). That is exactly what a renderer needs to DRESS it procedurally.

## How Unreal makes it look far better — dressing the MEANING, not replacing the ground

| what | Unreal feature | driven by our data |
|---|---|---|
| the ground surface | a **Landscape** from our height samples (World Partition for the large terrains); an **auto-material** blending grass / dirt / rock / sand by slope, height and noise, with **Runtime Virtual Texturing** so roads and objects blend into the ground; fine **displacement** (Nanite tessellation) for close-up relief — cosmetic only, within a few cm | the height grid + the `surface` classes as landscape layer weights |
| forests, grass, rocks, hedges | the **PCG framework** (procedural content generation) scatters trees in `forest` polygons, grass and small stones on `open`, hedges along `hedge` walls, reeds at water edges; **Nanite foliage**; free photogrammetry assets (**Fab / Megascans**) | the `surface` and `wall` features; lone trees placed EXACTLY |
| buildings | **procedural facades** on our footprints and heights (PCG shape grammar / modular kits: walls, windows, roofs, doors where our doors are); damaged variants for breaches | `building` footprints, heights, doors, interiors |
| roads | road meshes and markings along the SUMO lanes (landscape splines or PCG along a spline), kerbs, sidewalks | the SUMO network's lane geometry |
| water | the **Water** plugin (lakes, rivers, shores with foam) | `surface: water` + OSM waterways |
| sky, light, weather | **Lumen** global illumination and reflections, **Sky Atmosphere**, **Volumetric Clouds**, height fog, rain / snow particles, the sun from the date and lat / lon | our replicated weather and time of day (`WeatherConditions`, the solar position) |
| effects | **Niagara** explosions, smoke, fire, dust, muzzle flashes; **decals** and mesh patches for craters and scorch; **Chaos** fracture for a breaching wall | our effect and terrain-change messages |
| beyond the map edge | **Cesium for Unreal**: real-world terrain and imagery around the play area, georeferenced by our origin — a horizon instead of the world's end (aircraft already fly beyond the map, RS-2a) | the terrain's geo origin |
| people and vehicles (later) | MetaHumans, motion matching driven by our pose and gait | the replicated pose, stance, animation state |

## ⭐ The one rule — what may differ between HROT and Unreal

| must be IDENTICAL (it changes the simulation) | may be Unreal-ONLY (cosmetic) |
|---|---|
| ground height (a man and a tank stand on the same ground; line of sight is HROT's) · every building's footprint, height, doors and windows · walls and their materials · forest outlines and lone trees · water · roads | ground texture and micro-relief (≤ a few cm) · grass, flowers, small stones, leaves · facade detail, signs, clutter that blocks nothing · lighting, sky, post-processing |

## The direction — ⭐ the terrain PACKAGE is the contract, written and read by both sides *(lean, not decided)*

| option | what | lean |
|---|---|---|
| **A** HROT → Unreal | an exporter turns the terrain package into an Unreal level: the landscape from the heights, the semantic features as data the plugin's PCG graphs dress | ⭐ FIRST — every existing and OSM-imported terrain becomes beautiful with no hand work; one-way, simple |
| **B** Unreal → HROT (the user's idea) | the terrain is AUTHORED in Unreal — sculpt the landscape, place TAGGED actors (a `HrotBuilding` with its footprint and height, `HrotWall` splines with a material, `HrotSurface` volumes: forest / water / open, road splines) — and an exporter WRITES the same package (heights → our tiles; tagged actors → the GeoJSON; roads → a SUMO network) | ⭐ SECOND — feasible because the package's vocabulary is small; the Unreal-only dressing stays in the level, untagged, never exported |
| round trip | A and B are one MAPPING TABLE used both ways; the package is versioned by content hash (terrain versions, AQ112), so a terrain edited in Unreal is a new version every node loads | — |
| ⛔ rejected | Unreal-native data as the truth (HROT reading `.umap` / landscape assets) — HROT would depend on an Unreal installation, and the OSM pipeline and the editor's terrain tools would lose their output format | — |

## Open — to measure before a design (updated)

| # | question |
|---|---|
| U-2 | the minimal entity snapshot (pose, type, damage, person pose, effects) — which topics and rates |
| U-3 | type → Unreal asset (the DIS type / TKB class chain — the same key the 3-D map's families use) |
| U-4 | the FPS player's input from Unreal back to HROT (`ManualDrive`, station commands) and its latency |
| U-5 | the terrain export (option A): landscape sizes (Unreal needs particular vertex counts per component — pad or resample our posts), 1 m posts for the large terrains, the world-partition grid, the UTM / lat-lon frame |
| U-6 | ⭐ **the gizmos in Unreal**: HROT draws them with its own projectors (ImGui / raylib); lean — stream the projectors' PRIMITIVE draw lists (lines, rings, labels, icons in world space) to the client so ONE gizmo implementation serves both maps, rather than re-implementing each gizmo in Unreal |
| U-7 | dynamic terrain in Unreal: a breach, a crater, a felled tree, a collapsed building — which become mesh swaps, decals or a landscape patch |
| U-8 | option B's tagged-actor vocabulary and the exporter (an Unreal editor plugin) |

## Where the terrain starts — OSM straight into Unreal, or OSM into HROT then amended in Unreal? *(`2026-11-17`; lean, not decided)*

🔒 User: *"or we import from open street maps directly to unreal using some existing importer that does it nice? or we import to our terrain and ammend
it in unreal?"*

**Existing OSM → Unreal importers** (web search `2026-11-17`; vendor claims, not verified): **City Generator** and **Road Generator (OSM to Unreal)** on Fab
— roads on landscape splines, buildings and driveways from OSM through PCG graphs (a 4 × 4 km Miami recreation is shown); **Landscape Combinator** — real
DEM landscapes, OSM ways as splines, buildings from OSM; **TopoFetch** — elevation, satellite texture, buildings and roads, UE 5.8+; the old
**StreetMap** plugin (UE4, flat — no heights); **Cesium for Unreal** — streamed OSM buildings or Google photorealistic tiles, georeferenced, with only
approximate collision. ⚠ DEM support and what each exposes as data is unconfirmed.

| option | what | ⭐ verdict |
|---|---|---|
| **1** OSM → Unreal directly (one of the importers) | the nicest look fastest | ⛔ as the SOURCE: it is a SECOND interpretation of the same OSM — its building heights, road geometry and ground resampling will not match HROT's, and HROT needs its own data anyway (SUMO's network for traffic, the navmesh, line of sight, breaching). Two importers of one source drift ("one implementation per concept") — and the simulation and the picture would disagree on where a wall is |
| **2** OSM → **HROT** (our pipeline, built: DEM heights, buildings, walls, surfaces, the SUMO network by `netconvert`) → export to Unreal → **amend in Unreal** → the sim-relevant amendments exported back (tagged actors, option B above) | one interpretation of OSM; the simulation and the picture agree by construction; Unreal adds the beauty and the hand edits | ⭐ **the lean** |
| **2 + 1's generators** | in step "export to Unreal", feed the importers' PCG building / road generators OUR footprints, heights and lanes instead of raw OSM — their nice facades and road meshes on our geometry | ⭐ worth measuring (U-9): do their PCG graphs accept generic polygons / splines as input? |
| Cesium | the real world BEYOND the play area (a horizon), never inside it | ✅ as a backdrop |

| # | open |
|---|---|
| ~~U-9~~ | ✅ answered below: Landscape Combinator yes (GeoTIFF + GDAL vectors), City / Road Generator only through splines, TopoFetch no |
| U-10 | what an Unreal amendment may change and still export (a moved building, a new wall, a sculpted ditch) — the tag vocabulary of option B |

### U-9 answered from the documentation *(`2026-11-17`; three web readers — vendor docs, not tried in Unreal)*

| plugin | price · licence | takes OUR data? | fit |
|---|---|---|---|
| **Landscape Combinator** (open source, GitHub; UE 5.8, Windows + Linux) | free for personal use from GitHub; ⚠ commercial = its paid Fab listing (the LICENSE file not read) | ✅ **heightmap**: a local **GeoTIFF**, georeferenced (CRS default EPSG:4326), or a folder of `_x0_y0` tiles; Z scale 1 = real size · ✅ **vectors**: "any vector file supported by GDAL (e.g., Shapefile, GeoJSON, KML)" · ✅ **buildings** from closed footprint SPLINES, height from a `height` tag / floors from `building_levels` (⚠ whether a local file's attributes reach those tags is not stated) · ✅ **roads** from spline components | ⭐ **the best fit** — our height tiles as GeoTIFF, our `*.world.geojson` (⚠ local metres today: needs our geo frame written into it, or "Skip Coordinate Conversion"), our lanes as splines |
| **City Generator** ($99.99) / **Road Generator** ($39.99), Fab | licence text blank on both listings | ⛔ not documented: OSM import or splines drawn by hand; building height one parameter stepped by floor; roads are landscape splines; no GeoJSON / CSV / heightmap input | ⚠ usable only through a script of ours that creates their splines from our data — their docs do not promise it |
| **TopoFetch** ($49.99, commercial licence) | — | ⛔ downloads everything itself (OpenTopography, OSM, imagery); no own-file input | ⛔ out |
| Epic's PCG / Georeferencing / landscape import | free with Unreal | ⚠ not verified — the docs pages did not render for the reader | to check in an installed Unreal (`Engine/Plugins`) |

⇒ **lean for option A's first step**: export our terrain as a **GeoTIFF heightmap + GeoJSON vectors with our geo frame**, and let **Landscape Combinator** build the landscape, buildings and road splines in Unreal; dress the rest with Epic's PCG. ⚠ The first concrete test (on the Windows machine with Unreal): one of our terrains through Landscape Combinator — do the heights line up to the centimetre, and do our building heights reach its tags?

Sources: [StreetMap](https://github.com/MikeFricker/StreetMap) · [OSM importer forum](https://forums.unrealengine.com/t/plugin-openstreetmap-importer/81775) ·
[Landscape Combinator](https://forums.unrealengine.com/t/plugin-landscape-combinator-a-plugin-to-create-real-world-landscapes-free-for-personal-use/772305) ·
[Road Generator (OSM to Unreal), Fab](https://www.fab.com/listings/f205fd21-1428-4a4b-b067-a46d1a2fe86f) · [City Generator thread](https://forums.unrealengine.com/t/yazan-hanna-city-generator/2719107) ·
[Miami City Environment](https://forums.unrealengine.com/t/miami-city-environment/2856143) · [Cesium photorealistic tiles](https://cesium.com/learn/unreal/unreal-photorealistic-3d-tiles/) ·
[Cesium collision](https://community.cesium.com/t/enabling-collision-sun-shadows/27862) · [Landscape Combinator docs](https://landscapecombinator.github.io/LandscapeCombinator/) · [its GitHub](https://github.com/LandscapeCombinator/LandscapeCombinator) · [City Generator, Fab](https://www.fab.com/listings/657d0a1d-dd2f-4016-a3df-821560216c78) · [TopoFetch docs](https://topofetch.com/docs)

## ⭐ Measured on Windows `2026-11-17` — our terrain through Landscape Combinator (CE-4651, branch `unreal-ig`)

Unreal 5.8.4, Landscape Combinator 0.9.0 (GitHub `main`, compiled into a C++ project outside the repo, `D:\Work\HrotUnreal`), driven headless-ish
by the editor's own Python (`scripts/unreal/lc-measure.py` → `hrot_lc_import.py`); the export is `scripts/terrain-to-unreal.py`. Machine,
timings and every run: [`STATUS_Windows_Unreal_Terrain.md`](../../blueprints/batches/STATUS_Windows_Unreal_Terrain.md).

| measured | `basic-desert` (61 × 61 posts, 10 m) | `city-battle` (1 501 × 1 501, 1 m, UTM 32N) |
|---|---|---|
| ⭐ ground height at the posts, Unreal line trace − HROT `TerrainHeightGrid.Sample` | mean 1.1 mm, max 1.8 mm (2 000 posts) | mean 0.4 mm, max 2.3 mm (2 000 posts) |
| between the posts | max 0.47 m — ⚠ Unreal splits a cell along the OTHER diagonal (UL-6) | max 2.6 mm (2 000 cell centres) |
| placement | exactly 0…600 m (after UL-4) | exactly 0…1 500 m |
| landscape build | 1–24 s, 16 components | 18–26 s, ⚠ 10 000 components (UL-7) |
| buildings | — | ✅ all 472 built in 26 s, each on the ground (base − ground 0.0 m); ⭐ **footprints exact** (median edge 0.00 m per template); ⚠ **heights = 3 m × floors + ~1.2 m** (UL-8): +0.9…+1.7 m above ours, −0.3 m on the barn |

| # | decision / finding | why (measured) |
|---|---|---|
| UL-1 | the plugin from GitHub compiles against 5.8.4 with no change (431 actions, 333 s), its own GDAL bundled | — |
| UL-2 | drive the editor with its Python: `-ExecutePythonScript` + `EditorPythonScripting.set_keep_python_script_alive(True)`, a Slate tick callback, and the plugin's "Generation for … finished / failed" log line as the completion signal | without keep-alive the editor closes one tick after the script returns; the plugin's status is not readable from Python |
| UL-3 | the CRS handed to the plugin is its SHORT form (`EPSG:n`, or a PROJ string) — never WKT | it names a working folder after the CRS; WKT is not a valid path |
| UL-4 | the GeoTIFF's pixel north-west CORNER sits on the HROT post | the plugin puts a landscape vertex there; pixel-is-area put the landscape half a cell (5 m) off |
| UL-5 | the vector files it reads are WGS84 lon / lat | its spline importer reads every vector file as EPSG:4326, whatever the file declares |
| UL-6 | ⚠ OPEN: Unreal's landscape triangulates each cell along the other diagonal than HROT (south-west → north-east) | harmless at 1 m posts (below the 16-bit step); up to ~0.5 m between 10 m posts on a slope. Either HROT adopts Unreal's diagonal (a sim change: `TerrainHeightGrid.Sample`) or coarse terrains are exported resampled to 1–2 m |
| UL-7 | ⚠ OPEN: pad the export to an efficient component size (e.g. 127 quads) by repeating the edge posts | 1 500 quads divide only by the 15-quad component ⇒ 10 000 components (Unreal advises ~1 000); the frame-time cost not yet measured |
| UL-8 | ⚠ OPEN: building HEIGHTS do not follow ours | the plugin's height is floors × its style's `LevelHeight` (+ a wall top): no per-building metres. Options: one style per height class · scale each generated building to our height afterwards · our own generator on our footprints (the footprint half already matches exactly) |

⇒ **U-5 answered for the ground** (the frame, the padding question as UL-7, sizes); **U-9 verified in Unreal**: GeoTIFF + vectors ✅, building footprints ✅,
building heights ⚠ (UL-8). Not yet measured: road splines, walls, forests, the frame time in the viewport, the large tiled terrains (no Stechovice
package on this machine).

## ⭐ The look in Unreal — built `2026-11-17/18` (UL-9 … UL-14), and what the user asked for next

🔒 User, `2026-11-18`: *"so far it looks amazing. note we would like to have some windows transparent so that we can place character behind them -
maybe stechovice as OSM imported terrain is missing opened windows but that would be nice (later) adding to HROT as imported building post-process
to include internal floors to selected houses so that we can really place characters inside and they see outside."*

| built | how (all from OUR terrain package) |
|---|---|
| UL-9 the ground | `tools/unreal/HrotTerrain` `ImportHeights` — exact (≤ one 16-bit step) in seconds; Landscape Combinator dropped |
| UL-10 the look | `scripts/unreal/look.py` + `hrot_look.py`: sky / sun / clouds / fog / Lumen; a ground material placing Megascans surfaces by OUR cover mask (forest / water / open) and slope; soil patches |
| UL-11 trees | European beech (Nanite, Fab) at points inside OUR forests, on HROT's ground (`ScatterInstances`) |
| UL-12 roads | the ground's roads mask by OSM class (asphalt / dirt) + SUMO's junction polygons |
| UL-13 houses | each house from ITS template (`scripts/terrain_look.py`): walls by storey, the template's windows (frame + glass) and doors, a pitched roof from the imported roof volume, wall / roof materials varied; grass and ferns near the cameras |
| UL-14 road meshes | `hrot-roadscene` → HROT's own `TrafficRoadScene` (SumoSharp `Sim.Scene`: lanes, junction fills, sidewalks, markings, crosswalks) |

| # | open — the user's list |
|---|---|
| U-11 | ⭐ **transparent windows** a character can stand behind — the glass is opaque today (UL-13 draws a frame and a pane on a solid wall) ⇒ cut the wall at the template's openings and use a translucent glass |
| U-12a | 🔒 User, `2026-11-18`: *"HROT is working on converting non-enterable solid houses into houses with inner floors and windows. And to mark windows as having glass (by default) or shattered or damaged (cracks with bullet holes). We not yet done. Just record we should return to it later."* ⇒ ⏸ RETURN LATER: when HROT's building conversion lands, the Unreal houses follow the template — interior floors, and each window drawn by its STATE (glass · shattered · cracked with bullet holes), the state replicated at run time (U-7) |
| U-12 | ⭐ **enterable houses**: an import post-process in HROT that gives SELECTED imported houses their internal floors (and the opened windows OSM does not carry), so characters stand inside and see out — the HROT side first (the building template's storeys / slabs / openings), then Unreal draws what the template says |
| U-16 | ⭐ **people**: 🔒 user, `2026-11-18`: stances (stand with idle sway, crouch, prone), gaits (walk, run, crouch-run with aimed weapon, crawl), weapons (rifle, pistol, shoulder-fired tube) lowered / aimed / holstered with upper-body aim twist, fire with kick, grenade / stone throw, phone carried and used; dressed — uniforms and civilian clothes. 🔒 *"i need to show 1000+ characters in scene so the assets need to support LOD"* ⇒ every character asset on ONE skeleton with a LOD chain; tiers by distance (full skeletal + layers near, cheap skeletal mid, instanced vertex-animation / impostors far). Sources being fetched: Game Animation Sample, Lyra, Mixamo, City Sample Crowds, MetaHumans |
| U-16a | ✅ built `2026-11-19` (contact sheets, scratchpad): **the state-driven person** — `UHrotPersonState` (an actor component: HROT's per-person state lands here) + `UHrotPersonAnimInstance` (native, no graph): base = stance + gait in place, upper body = weapon + state layered from `spine_01`, an additive action (fire / reload / grenade toss), aim yaw / pitch over the spine; every change CROSSFADES (`BlendTime` 0.25 s, the stride phase kept) and the aim turns at `AimTurnRate` (240 °/s). Sources on ONE skeleton (UE5 mannequin): Lyra (rifle / pistol / unarmed, aim, fire, reload, toss, hits), Mixamo retargeted by script (`scripts/unreal/hrot_mixamo.py`: prone, crawl, kneel, phone, idles). ⚠ an unattended editor does not tick animation: the sheets pose explicitly (`PoseNow`) — the game / IG ticks normally, not yet checked there. Missing: stone throw, shoulder-fired launcher, holster, texting |
| U-16b | ✅ built `2026-11-19`: **dressed people + PLAY MODE**. The same state-driven person on the City Sample crowd bodies (`/Game/Hrot/People/BP_Person_NN`): the clip set retargeted Manny → crowd skeleton `SK_Base` by script (`scripts/unreal/retarget.py` → `/Game/Hrot/People/Anims`, same names, IK rig auto-generated, 40 clips); the weapon at `hand_r` where `weapon_r` sits on the mannequin (the crowd skeleton has none). Checked in **Simulate** (the engine ticks the people, no explicit posing): states switched live, actors moved by the host at the gait's speed (anim lab `live` row). 📐 Measured traps: ① the crowd Blueprint's LEADER is a hidden root `SkeletalMeshComponent0` (the `*_base` mesh) — Body / Top / Bottom / Shoes copy its pose, so the anim instance goes on the leader, never on `Body`; ② the retargeted clips carry the pelvis HEIGHT in `root` (the retargeter's root-motion op) — so "in place" drops only the root's horizontal travel, and the copies get `enable_root_motion` + `force_root_lock` OFF (either pins the root to the reference pose: the people sank ~80 cm); ③ the crowd body faces its actor's +Y, like the mannequin; ④ an ADDITIVE clip in the upper slot (Lyra's `MM_Rifle_Fire`) played as a pose collapsed the upper body — the anim instance now adds it on top (fading with the crossfade), so HROT's clip table may put either kind there; ⑤ first-time crowd texture builds (4K, ~750 MB each) exhausted memory ⇒ `Editor.AsyncTextureCompilationMaxConcurrency=2` in the project. Next: a uniformed soldier body (a uniform outfit on the same skeleton) and the LOD tiers for 200 people |
| U-16c | ✅ built `2026-11-19`: **the uniformed soldier + SHOTS**. Fab "US_Soldier" (user's pick: modular low-poly, its own skeleton with the mannequin's proportions — pelvis / head / hand within 0.1 cm; ⚠ **no LODs**, 15 parts, 12 materials on the body ⇒ LODs + a merge before 200 of them) gets the same 40 clips (`retarget.py --target …SK_US_Soldier_Base`). **The weapon is PART OF THE PERSON STATE** (`UHrotPersonState.Weapon` / `WeaponSocket` / `WeaponOffset`, a static or skinned mesh, null = unarmed): a component of the person, re-applied in play. **`UHrotWeaponFx::FireShot(weapon, fx, hits, normals)`** = one shot as HROT will report it: Lyra's Niagara set (`/Game/Effects`, copied as is: muzzle flash `NS_WeaponFire`, tracer, shell eject, concrete impact), driven by their `User.*` parameters the way Lyra's `B_WeaponFire` / `B_WeaponImpacts` do; a miss still draws its tracer (300 m). 📐 Measured traps: ① a Character Blueprint (US_Soldier) starts its own idle on BeginPlay ⇒ the state component re-applies our anim on its first tick (once) and on re-register; ② Character movement (gravity, AI possession) ⇒ off: HROT moves people; ③ a separate weapon ACTOR attached to a Blueprint body lost its attachment in play ⇒ a component of the person; ④ `set_editor_property` on a Blueprint person REBUILDS its components (anim reset, weapon doubled, crowd face loose) ⇒ state written without the edit notification; ⑤ hold Lyra's **SK_** weapons (Muzzle / ShellEject sockets; SM_Rifle is SK_Rifle turned 180°) with Lyra's attach transform (yaw −90 at `weapon_r`) — a body without `weapon_r` holds it where the mannequin does, transferred **through a shared animated pose** (the bone-relative offset tilted it 25°, the reference poses reversed it); ⑥ a skinned weapon's bones must refresh (its muzzle socket rides on a bone); ⑦ the body's own gun hidden (`BODY_HIDE`); ⑧ **retarget poses NOT auto-aligned** (user caught twisted feet): `auto_align_all_bones` re-aimed the feet — Lyra's clips came out toes-up ~80° on the crowd and the soldier, Mixamo's with pointed toes on the mannequin; now Manny → crowd / soldier keep the shared reference pose (identical foot axes), Mixamo → Manny aligns the ARM bones only — measured heel→ball within 0.5 cm of the mannequin on every body; ⑨ Lyra's `weapon_r` transform only on the mannequin skeleton — the crowd skeleton has its own `weapon_r` (the civilians' rifles came out reversed); measured muzzles now agree on all bodies. ⚠ the contact sheet poses explicitly ⇒ the crowd FACE (own head anim) is posed after the body, or it stays at standing height. Not yet: the impact on a surface checked in a picture, a pooled effect per weapon (one Niagara system per shot today) |
| U-16d | ✅ measured `2026-11-19`: **200 soldiers**. ① LODs generated for the soldier pack (`scripts/unreal/hrot_lods.py`, Unreal's skeletal reduction, 4 levels): 25 703 → 6 380 → 3 181 → 1 583 vertices a soldier, sections 37 → 32 → 31 → 31. ② `UHrotPersonState::MergeBodyParts` merges the parts that follow the leader (same skeleton) into ONE mesh, cached per combination and shared (Epic's SkeletalMerging plugin); sections kept. ③ the person setup is ONE module, `scripts/unreal/hrot_person.py` (`PersonKit.make`), used by the anim lab and the crowd test. 📐 Stechovice, editor, AC power, uncapped — 200 armed soldiers: street view **+5.7 ms separate → +2.4 ms merged** (empty 12.2 ms), village aerial +3.9 / +4.1 ms (far: LODs carry it, the merge does not). ⚠ single runs inside the editor — a direction, not a budget. 📐 traps: the editor throttles to 60 Hz **on battery** (`r.DontLimitOnBattery 1`) and never above the engine's SmoothedFrameRateRange (62 fps; lifted in the project's DefaultEngine.ini) — an empty scene at exactly 16.67 ms is a cap, not a cost; a person is NAMED and foldered at spawn (a later rename is an edit: the Blueprint rebuilds and leaves the weapon behind). Next: draw calls (~31 a soldier: a texture atlas / fewer materials), the cheap far tier, measured in play rather than the editor |
| U-16e | ✅ built + measured `2026-11-19`: **MANY shots, cheaply** (user: *"performance of the fire effects - there will be many"*, *"more realistic muzzle flash than the laser beam"*). `UHrotShotRenderer` (a world subsystem, ticks in the editor and in play): every MUZZLE FLASH of the world = 3 cards (one facing along the barrel, two along it) in ONE instanced mesh with Lyra's flipbook art (`T_MuzzleFlash_Directional_01_MSK`: 4 × 2 frames along U × V; B = the flash from the front, R / G = the tongues from the side — read off a channel render), 60 ms; every TRACER = a 1.5 m streak flying at 850 m/s, 1 round in 5 per weapon, in a second instanced mesh — replaces Lyra's beam drawn muzzle → impact. Materials by script (`scripts/unreal/hrot_shotfx_assets.py`; ⚠ a Power node's inputs are `Base` / `Exp`). The NEAR extras (Lyra's smoke + sparks, shell, impact) stay Niagara, pooled, each with a RATE budget per second (smoke 60, shell 60, impact 30) within range of the view (40 m; impacts 150 m). `FireShot`'s signature is unchanged. 📐 200 merged soldiers, Stechovice street view, ~970 rounds/s (10 rounds/s in 1 s bursts): **old way 125 ms (8 fps, then out of memory) → per-frame budget 22.8 ms → rate budgets ≈ 0 ms** (under the 120 fps cap); flash + tracer + 4 lights alone: 0 ms measurable; each extra alone at 8 a frame: shell +0.1, smoke +0.7, impact +2.0 ms; pooling changed nothing (the cost is running the effects, not creating them). `StartTestFire` (native) drives the measurement; `crowd.py --fire 10 [--all-niagara] [--only smoke|shell|impact]`. ⚠ the editor's frame cap moved to 120 fps (8.33 ms) in these runs: costs under it are unmeasured |
| U-16f | ✅ measured `2026-11-19`, **in PLAY mode** (Simulate, `crowd.py --play`): **200 soldiers 31.8 ms → 9.0 ms** (empty map 8.33 ms, the 120 fps cap), checked in a picture (posed, armed). Not draw calls: shown vs hidden differed ~2 ms. ① **people animate only when rendered** (`UHrotPersonState::ApplyAnimation` sets it, + URO; the anim lab alone asks `always_tick` for its off-screen rows) — always-tick cost +23 ms, when-rendered +8–10 ms; ② **`UHrotPersonState::QuietOwner`**: the body actor's tick and every hidden / meshless skinned component off (the 14 parts merged into the body and the Character's empty mesh ticked every frame in play — ~8.7 ms with NOTHING drawn or animated). Measured on the way: our layered state animation costs ~0.5 ms over the engine's plain single-clip animation for 200; animation fully frozen saved ~1 ms; forcing URO to every 8th frame ~1.5 ms. ⇒ the vertex-colour / single-material soldier (the soldier pack is textureless flat colours — `M_*` with no textures) is **parked**: rendering is ~2 ms of the old 23. ⚠ a measurement that changes a person's property with `set_editor_property` REBUILDS it in play too (its parts tick again: +8 ms) — the crowd test writes by attribute |
| U-16g | ✅ built `2026-11-19`: **combat states** (user: grenade, launcher, camos to tell the sides apart at a glance, dying / dead). ① `UHrotPersonState.bBaseLoops` = false: the base clip plays ONCE from its start and holds its last frame — dying → dead is one state (the clip HROT picks: front / back / left / right). ② the UNIFORM is person state: `MaterialOverrides` (slot → material, `ApplyMaterials`, again in play and after a merge); sides by script (`hrot_person.SIDES`): A = the pack's camo tinted woodland green, B = FLAT desert tan (⚠ measured: a tint alone left both camo-brown — the camo texture carries its own colours). ③ grenade: Lyra's full-body toss once, or its additive on any gait. 📐 Lyra's 6 death clips END MID-COLLAPSE (kneeling / bent — Lyra dissolves its dead into cubes): full falls come from Mixamo (`Dying` lies flat, held) — more requested from the user. ⏳ shoulder-fired launcher: no mesh or pose on disk (Fab / Mixamo); ragdoll: decided AGAINST as the default (user asked) — a death clip is identical on every IG channel and in replay, a ragdoll is not; an opt-in for blasts later, computed once and shared |
| U-17 | ⏳ **vehicles** (user, `2026-11-19`: *"vehicles with controllable turrets and damaged/destroyed states"*): first thinking — per-vehicle STATE from HROT (hull pose, speed, turret yaw, gun pitch, secondary weapon, hatches, lights, damage intact / damaged / destroyed + kill kind; events gun fired, hit); Unreal: hull + turret + gun as child parts rotated from the state (no animation system); gunfire through `UHrotShotRenderer` (a muzzle socket on the gun); damaged = budgeted smoke + scorch; destroyed = burnt material, fire → smoke, turret displaced. Waiting: which vehicle types, and assets with SEPARATE turret / gun parts (Fab) |
| U-13 | asphalt reads brown (darkened gravel) — a grey asphalt surface; the street view a little dark |
| U-14 | conifers: the Megaplants trees are SKELETAL (not instanceable) — a static Megascans spruce / pine set |
| U-15 | lane markings on rural roads: Sim.Scene draws dividers only between same-direction lanes — a centre line would be a SumoSharp change (read-only in HROT, R-4046): the user decides |

## ⛔ HISTORY — the first open list

| # | question |
|---|---|
| ~~U-1~~ | ✅ answered: no shared memory — local sockets |
| U-2 | the minimal entity snapshot an Unreal client needs (pose, type → its Unreal asset, damage state, pose of a person, effects) — which topics, which rates |
| U-3 | the type → Unreal asset mapping: the DIS type / the TKB class chain (the same key the 3-D map's families use) |
| U-4 | the possessed player: input from Unreal back to HROT (the existing `ManualDrive` / station commands) and the latency it adds |
| U-5 | the terrain: the same height field and buildings as HROT (the terrain package export) so a body stands on the same ground on both sides |

⛔ Not approved as a build; a design comes when the user asks for one.
