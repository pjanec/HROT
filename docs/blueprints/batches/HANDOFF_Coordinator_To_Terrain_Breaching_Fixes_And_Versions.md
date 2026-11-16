<!--STATUS
state: LIVE — dispatched as a FOLLOW-UP: start when your minefield batch is reported — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../DESIGN_Vehicle_Breaching.md — B-D4 (a building wall bumped open for infantry), B-D11 (the bulldozer), §7.6 the dozer's cut, V-9 / V-12 the multi-panel cut
  - ../Architect_Question_112_Terrain_Versions_On_Reimport.md — §0 the ruling (R-4280), §2 A–D
  - ../../DESIGN_Ground_Patches_And_Road_Blockage.md — D-4 (a re-import is a new version)
-->

# HANDOFF — coordinator → `terrain`: your decisions ruled, two breaching defects, terrain versions (R-4280; CE-4629, CE-4630, CE-5004)

**For:** the `terrain` lane, as a FOLLOW-UP — start when your minefield batch is reported. **From:** coordinator. **Dispatched at `b324236a6`**;
⛔ scope FROZEN there; a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-16`: *"approved · R-5000 lean accepted, vehicles should be able to cross the 10cm curb · the narrow hole was made by FPS possessed
bulldozer. Sized like a door (width, height) and still the character can [not] pass through. charcater goes a bit in but is pushed back"*

## ⭐⭐⭐ UNATTENDED
Never ask, never wait; decide by the claim table and record an `R-5xxx` lane decision with what would change it; a failed premise stops ONE item
(R-106); commit, push and update `STATUS_Terrain_Breaching_And_Versions.md` after each item.

## 0. Your decisions — ruled (R-4280)
R-5001…R-5031 APPROVED as decisions; R-5004, R-5012, R-5029, R-5031 and the dozer half of R-5008 are accepted as NOT built. **R-5000 CHANGED**
(item 1). Mark them in `STATUS_Terrain.md`.

## 1. The items, in order
| # | item | done when |
|---|---|---|
| **1** | **R-5000 changed — a vehicle crosses a kerb**: a wall panel lower than ~0.25 m (data: a minimum panel height) does not stop a hull | a car and a tank drive over a 10 cm edging; a 1 m garden wall still holds a car |
| **2** | **CE-4630 — a possessed (FPS) soldier cannot enter a bumped hole in a building wall** (door-sized, 1.2 × 2.2 m): he goes a little in and is pushed back, in any posture. ⭐ ROOT-CAUSE it first (the tracker row lists the candidates: the walker is clamped to the navmesh, `ManualMotion.KeepOnNavmesh`; is the interior joined to the outside only through authored doorways? did the re-bake run on his node? does the passage's fine area / the floor height leave no link?) and fold the cause into the owning design | an ORDERED soldier walks through the hole (the AI uses the same navmesh) and a possession rail walks a possessed one through it |
| **3** | **CE-4629 — a bulldozer's hole is blade-wide**: ① a bulldozer gets a row of its own in `contact-breach.json`; bumping a BUILDING wall it opens a passage of the blade's width (hull width + margin) and the wall's full height — the hull still never enters a building (B-D4, R-3025); ② the AI dozer's ordered cut (`DozerCutSystem.CutWall`) stops clamping to ONE panel and uses the multi-panel cut the player's break already has (`ContactBreach.Cut`, R-5008) — one implementation | a bumped building wall, a cut straight wall and a cut stepped wall each leave a gap ≥ the blade's width |
| **4** | **the FEATURE SCENARIO** (R-4277) proving 1–3: `tt-dozer-breach` — a What / Judge `description`, `checks.json` expect rows (gap widths, the ordered soldier through each gap, a car over the kerb), golden | `scripts/scenario-regression.sh tt-dozer-breach` passes in the editor tier |
| **5** | **AQ112 / CE-5004 — terrain versions**, in the order B → A → C: ① `TerrainCatalog.ResolveDefinition(name, expectedHash)` (current when its hash matches, else `versions/<hash>/`, else the C-5 error naming the missing version), passed by the overlay load and the replay; ② the importer moves the old folder to `versions/<hash>/` before it writes the new one, `versions/` excluded from the content hash AND **git-ignored even in a committed demo terrain** (`.gitignore`); ③ *Re-compile scenarios on this terrain* as the import wizard's last page, reporting the ones that fail. D (prune) later | a scenario saved on the old version still loads after a re-import, and a recording made on it replays; the re-compile moves a scenario to the new version |

## 2. Gates and report
T-1 first (breach, hull guard, contact breach, navmesh bake, terrain catalog / residency suites); each new rail red-proved by a compiling mutation;
0 B per steady frame (R-2084); fold the as-built into `DESIGN_Vehicle_Breaching.md` and AQ112 (obligation ⑤); the gate-report contract (rule 8);
ids from your block.
