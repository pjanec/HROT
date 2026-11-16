<!--STATUS
state: LIVE
updated: 2026-11-16
build-state: DESIGN — COMPLETE (R-4270, R-4271); ⏳ REVIEWED (§8: 8 blockers) — awaiting rulings
current-answer: §3 decisions (approved) · §3a the fixed ids · §3b UML · §4 slices
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../DESIGN_Ground_Patches_And_Road_Blockage.md — OWNS `GroundDamage` (a replicated record of what blasts did to the ground) and the navmesh areas (crater area) — the minefield's record and area follow both
  - ../../DESIGN_Vehicle_Breaching.md — OWNS the bulldozer's clear order (B-4, CE-4601) and the breach-record pattern the mine-clearing vehicle reuses
  - ../vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — M1 plans a BOTTOM facing "for mines later" — an AT mine strikes it
  - ../../DESIGN_Terrain_Authoring.md — OWNS the scatter's deterministic placement inside a polygon (the pattern MF-2 follows)
  - ../../DESIGN_Civilians_And_Crowd_Control.md — OWNS charges and `TriggerIed` (an IED is a single, command-fired mine)
-->

# DESIGN — minefields: laid, struck, avoided, breached

🔒 User, `2026-11-16`: *"mines and mine fields and behaviors regarding to them. Mine field is a zone, and the mines have some density inside and must
be deterministically and equidistantly placed. And when someone or something steps on the mine and explodes, this is an indication that that area is
actually dangerous and that the units needs to avoid it. And there needs to be some kind of mine removal vehicle … and an action to clear a passage
through a minefield, which will basically cut the zone into parts."*

## 1. INVENTORY *(measured `2026-11-16`; graph: `search_graph name_pattern=".*(Mine|Minefield|DangerArea|Hazard|Avoid|Clearance|Lane|Breach).*" label=Class` ⇒ the danger-area family, `BreachDoor*`, `HullClearance` — no mine class; `docs/` + `.dev/` searched (minefield / landmine / mine clearing): only `CAPABILITIES.md:442` "Minefields … ⏳" and Urban Armour M1's "Bottom for mines later"; index coverage could not be checked through the CLI)*

| exists | where | used as |
|---|---|---|
| ⭐ **"a blast here ⇒ this area is dangerous"**: `ShelledAreaSystem` notes every burst, merges them, marks a SHELLED danger area units avoid, forgets it later | `ShelledAreas.cs:23, :132` (CE-4583), `DangerAreaKind.Shelled` | ⭐ MF-6's exact precedent — a mine strike is a danger area of kind Minefield |
| the danger-area family: a sensor, kinds (crossings, open ground, gas, shelled), replicated results, the crossing manoeuvre | `DangerAreaSettings.cs:28-40`, `DangerAreaSensorSystem`, `DangerAreaCrossingManeuver` | units sense and avoid / cross danger areas |
| navmesh AREAS priced per mover (door 2, woods 3, crater 4), a per-mover filter | `NavDoorways.cs:19-68`, `NavLayerSelection.cs:20` | MF-7: a minefield area; ⚠ a filter PER SIDE is not measured to exist |
| a replicated, recorded damage record per node (`GroundDamage` craters, `BuildingDamage` breaches) | Ground Patches P-4, Breaching B-2 | MF-3: the record of struck and cleared mines |
| deterministic placement inside a polygon from a seed (the scatter) | Terrain Authoring §18g, `FeatureKinds.cs:216` | MF-2's pattern |
| charges, a burst's area effect, the vehicle damage parts (mobility kill) | `Charges.cs`, `AreaEffectSystem`, Body Parts | the detonation |
| ⛔ an armour BOTTOM facing (planned in Urban Armour M1) | `ArmorModel` | MF-4 needs it for an AT mine |
| the bulldozer's clear order (cut a wall / fell a tree at a point) | B-4, CE-4601 | MF-9's pattern for clearing a lane |
| ⛔ a mine, a minefield, a mine-clearing vehicle, a per-side known-area filter | — | built here |

## 2. The proven pattern *(engineer doctrine and constructive simulations — each piece adopted for a reason, §3)*

| piece | practice | why it fits |
|---|---|---|
| a minefield laid in **ROWS at fixed spacing, staggered** (a standard pattern), density = mines per metre of front | doctrine (row mining, the "standard pattern") | deterministic and equidistant by construction — the user's requirement |
| two families: **anti-personnel** (pressure, a few kg) and **anti-tank** (a vehicle's weight, attacks the BELLY / tracks) | doctrine | different triggers, different victims |
| a field is DISCOVERED by a strike, by seeing marking, or by reconnaissance; until then it is invisible | doctrine | knowledge is PER SIDE |
| on a strike: **halt, do not move, back out on your own tracks**, call engineers | battle drill | the victim's reaction |
| **breaching**: a mine plough or flail vehicle clears a LANE (~4–5 m wide) at walking pace; a line charge (MICLIC) clears ~100 m × 8 m at once; infantry prodding is slow | doctrine | the user's clearing action; the lane is then MARKED |
| one minefield = ONE object in constructive simulations; mines are its derived points | JCATS / OneSAF practice | thousands of mines must not be thousands of entities |

## 3. Decisions *(✅ MF-1…MF-12 APPROVED `2026-11-16`, R-4270 — 🔒 user: *"minefields approved, one field with lanes cut out"*; ✅ MF-13…MF-16 APPROVED, R-4271 — 🔒 user: *"Minefields approved. Please record design and run the review."*)*

| # | question | ⭐ lean | why · blast radius |
|---|---|---|---|
| **MF-1** | what is a minefield? | ⭐ ONE entity: a closed polyline, a mine TYPE (AP / AT / mixed), a SPACING (or density), a ROW ORIENTATION, a seed, and per side whether it is KNOWN at start (own fields are known and marked; enemy fields usually not) | thousands of mines cost one entity; saved, replicated, recorded once |
| **MF-2** | how are mines placed? | ⭐ DERIVED, never stored one by one: rows at the spacing along the orientation, each row offset by half a spacing (staggered), clipped to the polygon — the SAME points on every node from the field's parameters (a seed only for an optional small jitter, 0 by default) | 🔒 user: *"deterministically and equidistantly placed"* |
| **MF-3** | struck and cleared mines | ⭐ a replicated, recorded per-field RECORD of the mine indices that went off or were cleared (a bitset, the `GroundDamage` / breach-record pattern) — a struck mine never goes off twice | every node agrees; replay restores it |
| **MF-4** | the trigger | ⭐ one serial system: for each mover INSIDE a field's bounds, the derived mines under its footprint (a per-field point grid built once); AP ⇒ any person or vehicle; AT ⇒ a vehicle over a weight only (a man walks over it). The burst: AP = a small area effect at the foot; AT = under the hull — the BOTTOM facing (Urban Armour M1, built here), a track / wheel ⇒ mobility kill | the user's *"steps on the mine and explodes"*; cheap (only movers inside a field) |
| **MF-5** | the victim's reaction | ⭐ a reaction **MineStrike**: halt, do not move, the casualty cared for in place, back out along the unit's OWN recent track; report a SPOT of the field to the chain (the existing report path) | the battle drill; the conduct gate (R-4263) keeps a "Dash" from overriding it? — no: a struck unit HALTS (a mine is not fire) |
| **MF-6** | ⭐ "this area is dangerous" | ⭐ a danger area of a new kind **Minefield**, made by a strike exactly as `ShelledAreaSystem` makes a shelled one: a circle round the blast that GROWS with further strikes (merged); known to the VICTIM'S SIDE (its own units and the reported chain); never forgotten (unlike shelling) | reuses the precedent; knowledge is per side |
| **MF-7** | avoiding it | ⭐ units route AROUND known minefield area: a navmesh area **Minefield** stamped for every field, priced per mover by its side's KNOWLEDGE (unknown ⇒ walks in; known ⇒ closed, or a high cost when the order says so) — ⚠ needs a per-SIDE filter: measure `NavLayerSelection` first | the user's *"units need to avoid it"* |
| **MF-8** | a known field and marking | the known extent is what the side has learned (the grown circles, a reconnaissance report, or the whole polygon when marked); a MARKED field (own, or an enemy field found and marked) is known in full | the doctrine's marking |
| **MF-9** | ⭐ the mine-clearing vehicle | ⭐ a new type — an engineering tank with a **mine plough / flail** (and, as a second option, a **line charge**) and an order **ClearLane { start, end, width ~4.5 m }** (the bulldozer's clear-order pattern, B-4): it drives the lane at ~1–2 m/s; the mines in its swept width DETONATE harmlessly (flail) or are turned aside (plough) and are recorded CLEARED (MF-3); the line charge clears ~100 × 8 m at once | 🔒 user: *"some kind of mine removal vehicle … an action to clear a passage"* |
| **MF-10** | ⭐ "cutting the zone into parts" | ⭐ a cleared LANE is a corridor polyline + width SUBTRACTED from the field: the field stays ONE entity with its list of lanes; its mines inside a lane are cleared, the navmesh area is re-stamped without the lane (a passage), the lane is MARKED and known to the clearing side | the user's intent ("parts") without splitting the entity — its identity, record and replay stay one; ⚠ if the user wants literally separate fields, a split is possible but loses the one record |
| **MF-11** | the breach behaviour | the company's breach play (clear the lane under cover — suppression on the far side, smoke, then the lane) and units crossing in single file through the marked lane | the assault design's breach, applied |
| **MF-13** | ⭐ **how does the victim side LEARN the field?** (🔒 user: *"if the single explosion will reveal the whole minefield somehow magically as a simplification, or if the danger area is just around the single exploded mine And the victim side needs to send scouts/engineers … in some simplified way"*) | ⭐ **neither magic nor a dead end — a per-side KNOWLEDGE GRID over each field, filled in three ways**: ① a STRIKE marks the cells within a CONSERVATIVE radius (~50 m) around the blast as **SUSPECTED** — the side avoids them at once (doctrine: a unit that hits a mine assumes it is IN a minefield); ② an **ENGINEER or SCOUT** team on a `ReconMinefield { suspected area }` task walks at detector pace (~0.5 m/s) and, within its detection radius (~5 m), turns cells into **KNOWN-MINED** (the field covers them) or **KNOWN-CLEAR** (it does not) — following the boundary it finds (a deterministic edge-trace on the grid), so the field's extent appears as it walks; ③ when the trace closes (or the team is ordered to **MARK** it), the field becomes **MARKED** — known in full to that side and drawn as the marked minefield. Further strikes add suspected circles; a recon DRONE may reveal a SURFACE-laid field from the air later (an option, not now) | realistic doctrine (minefield reconnaissance and marking) made SIMPLE: one grid per field per side (a few hundred bytes, 10 m cells), no per-mine detection; navigation avoids suspected + known-mined cells; the knowledge is replicated to the side's nodes |
| **MF-14** | ⭐ **where the knowledge LIVES, how it is recorded and saved** (🔒 user: *"Where would the mine knowledge grid be stored? Is that part of some world singleton … How would it be stored, recorded to the scenario? Because it has to be"*) | ⭐ **NOT a world singleton** — 📐 the shelled area IS node-level (`ShelledAreas.cs:9-16`: "no component id: a restore forgets"), which is right for shelling and WRONG here (knowledge must survive a restore, a replay and a save). ⭐ It follows the **runtime damage-record pattern** the engine already uses for craters, felled trees and breaches (`CellRecordSystem.cs:17-23`, `TreeDamage.cs:21`, `BuildingDamage`): ONE small **record entity per (field, side)** carrying an unmanaged **`MinefieldKnowledge`** component — the field's link, the side, the cell size, the grid at 2 bits per cell (unknown · suspected · known-mined · known-clear) and a **Marked** flag — owned by the node that decides (where the strike or the recon happens), **recorded** (keyframes, seek, replay), **replicated** to every node, and **SAVED** into the scenario through a section keyed by the field's stable id + side, the way `TerrainObjectsSection` saves the breaches (`TerrainObjectsSection.cs:8-20`; the record entity itself transient, rebuilt at load). ⭐ Size: ≤ 1024 B per component (the engine's cap) ⇒ up to 4096 cells — a field up to ~640 × 640 m at 10 m cells; a larger field coarsens its cells (≤ 64 × 64) — fields are rarely larger | one proven pattern, nothing new; runtime knowledge is recorded and saved like runtime damage |
| **MF-15** | ⭐ **OWN minefields** (🔒 user: *"Own minefields should be also avoided by everyone who knows"*) | the field's per-side **known-at-start** flags (MF-1) create that side's record already **Marked** (known in full) — no grid needed until something changes; every unit of a side that knows a field avoids it exactly as an enemy field it has learned: **knowledge is per SIDE, not per field owner** (a field laid by Blue is known to Blue, and to Red only by what Red learns). ⭐ Knowledge is SHARED by the whole side (its common picture — reports travel the chain at once, a simplification) | one rule for own and enemy fields |
| **MF-16** | ⭐ **MANY minefields** (🔒 user: *"what if there are multiple minefield entities? How the knowledge grid will be structured to cover that?"*) | ⭐ there is NO global grid: each field has its OWN records, one per side that knows anything about it (absent ⇒ that side knows nothing). Fields are independent — a strike near two fields marks suspected cells in each field's own grid; a recon team traces whichever field it is on. Avoidance asks every field: a route for a unit of side S is priced through a side-aware navmesh **query filter** (📐 the engine already has a custom filter — `DoorAwareQueryFilter`, `NavDoorways.cs:194` — that is the seam) over the minefield navmesh area: a polygon of field F is closed / costly for S where S's record of F says suspected or known-mined, open otherwise (an unknown field is walked into); the march's danger-area sensor shows known fields ahead per side | per-field state scales with the number of fields; the route filter is the one place sides differ |
| **MF-12** | authoring and drawing | a Minefield tool (a closed polyline, type, spacing, orientation, seed, known-to sides); drawn as the tactical graphic (a hatched area, mine symbols) ONLY where the viewer's side knows it; the instructor sees all; lanes drawn as marked corridors; individual mines invisible except to the instructor | the user's map; per-side display |

## 3a. The fixed ids — for EVERY lane *(measured free on `coordinator`, `backend`, `ui`, `behaviors`, `terrain`, `2026-11-16`)*

| what | the value | note |
|---|---|---|
| the minefield entity | TKB **8830** `Minefield`, DIS **5 · 0 · 0 · 91** (cultural feature, HROT — beside the scenario macro's `5.0.*.90`), class **`"Minefield"`** = `5.0.*.91` | the authored field (polygon, type, spacing, orientation, seed, known-at-start per side, its lanes) |
| the knowledge record | TKB **7206** `MinefieldKnowledgeRecord` (the record block 7201–7205: ground / tree damage …) | one per (field, side), transient, saved through the section (MF-14) |
| the mines (rounds) | munitions **6051** `TkbMineAntiPersonnel` · **6052** `TkbMineAntiTank` · **6053** `TkbLineCharge` | 6047–6049 smoke, 6050 the radar SAM |
| the mine-clearing vehicle | TKB **116** `Engineer_MineClearer`, DIS **1 · 1 · \* · 9 · 2** (large tracked utility, beside the bulldozer `1.1.*.9.1`), class **`"MineClearer"`** = `1.1.*.9.2` | plough / flail + a line charge mount |
| the navmesh area | **`MinefieldArea = 5`** (door 2, woods 3, crater 4) | per-mover, priced per side by the query filter (MF-16) |
| the danger kind | **`DangerAreaKind.Minefield = 8`** | ⚠ the kinds' bitmask is a SIGNED byte with every bit used (`Shelled` already sits on bit 0) — the review decides how to widen it |
| the components | **`Minefield`** (on 8830; the parameters, a lanes list), **`MinefieldStrikes`** (the struck / cleared mine bitset, MF-3), **`MinefieldKnowledge`** (on 7206, MF-14) — unmanaged, recorded, replicated; ids allocated by backend at build | ⚠ the strike bitset of a big field may pass 1024 B — chunked like the knowledge |

## 3b. UML

```mermaid
classDiagram
  class Minefield {
    <<new, unmanaged, on TKB 8830>>
    Polygon
    MineType
    SpacingM
    OrientationDeg
    Seed
    KnownAtStart
    Lanes
  }
  class MinePattern {
    <<new, derives the staggered rows, the same on every node>>
    MinesIn()
  }
  class MinefieldStrikes {
    <<new record, struck and cleared mines>>
  }
  class MinefieldKnowledge {
    <<new record per field and side, TKB 7206>>
    Grid2bit
    Marked
  }
  class MineTriggerSystem {
    <<new, serial, movers inside a field>>
  }
  class ArmorModel {
    <<existing, grows a BOTTOM facing>>
  }
  class AreaEffectSystem {
    <<existing, the AP burst>>
  }
  class ShelledAreaSystem {
    <<existing, the precedent for suspected circles>>
  }
  class MinefieldKnowledgeSystem {
    <<new, strikes and recon fill the grid>>
  }
  class SideAwareNavFilter {
    <<new, beside DoorAwareQueryFilter>>
  }
  class MineStrikeReaction {
    <<new behaviour: halt, care, back out, report>>
  }
  class ReconMinefield {
    <<new behaviour: trace the edge, mark>>
  }
  class ClearLane {
    <<new order on TKB 116: plough, flail, line charge>>
  }
  class TerrainObjectsSection {
    <<existing save section, grows the knowledge>>
  }
  Minefield ..> MinePattern
  MineTriggerSystem ..> MinePattern
  MineTriggerSystem --> MinefieldStrikes : a mine goes off once
  MineTriggerSystem ..> ArmorModel : AT under the hull
  MineTriggerSystem ..> AreaEffectSystem : AP burst
  MinefieldKnowledgeSystem ..> MineTriggerSystem : a strike
  MinefieldKnowledgeSystem ..> ReconMinefield : cells the team sees
  MinefieldKnowledgeSystem --> MinefieldKnowledge : writes
  ShelledAreaSystem .. MinefieldKnowledgeSystem : the same shape, but recorded
  SideAwareNavFilter ..> MinefieldKnowledge : per side
  ClearLane --> Minefield : adds a lane
  ClearLane --> MinefieldStrikes : clears the mines in it
  TerrainObjectsSection ..> MinefieldKnowledge : saved per field and side
  MineStrikeReaction ..> MinefieldKnowledgeSystem : reports
```
*What it shows that prose hid: the field's mines are never stored — they are DERIVED; the only runtime state is two records (what went off, what
each side knows), both following the damage-record pattern, and the ONE place sides differ is the route filter.*

```mermaid
sequenceDiagram
  participant P as Blue patrol
  participant T as MineTriggerSystem
  participant K as MinefieldKnowledgeSystem
  participant E as Blue engineers
  participant N as SideAwareNavFilter
  participant C as Blue mine clearer
  P->>T: a man steps on an AP mine
  T->>P: the burst, a casualty
  T->>K: a strike in field F
  K->>K: Blue's grid of F, suspected cells within 50 m
  P->>P: MineStrike, halt, back out, report
  N->>N: Blue routes avoid the suspected cells
  E->>K: ReconMinefield, cells within 5 m become mined or clear
  K->>K: the edge traced, F marked for Blue
  C->>T: ClearLane through F
  T->>T: the lane's mines cleared
  N->>N: Blue routes through the marked lane
```
*What it shows: knowledge grows in three steps — a strike, a recon, a mark — and every step is a write to ONE record that is recorded, replicated
and saved; the lane then opens a passage for that side only.*

```mermaid
graph TD
  SIM[SimHost - the ground integrator and the fire path] --> TR[MineTriggerSystem, serial, after the movers]
  TR --> AE[AreaEffectSystem and the hull damage]
  TR --> KS[MinefieldKnowledgeSystem]
  CGF[CgfLogicPack - brains] --> BEH[MineStrikeReaction, ReconMinefield, ClearLane, the breach play]
  BEH --> KS
  NAV[the navmesh provider - the side-aware query filter] --> ROUTES[routes per side]
  KS --> REC[the knowledge records - recorded, replicated, saved]
  REC --> NAV
  SAVE[ScenarioSerializer - the terrain objects section] --> REC
  SCH[kernel scheduler every frame] --> SIM
  SCH --> CGF
```
*What it shows: the trigger runs where the movers move, the decisions where the brains run, and the knowledge travels between them as a record —
no node writes another node's entity.*

## 4. Slices *(lean — after the rulings: ids, UML, the review)*

| # | builds | lanes |
|---|---|---|
| MF-S1 | the field entity, the derived pattern, the record, the trigger, the bursts (+ the armour BOTTOM facing) | backend |
| MF-S2 | knowledge per side: the danger kind Minefield from strikes, marking, the per-side navmesh filter | backend + behaviors |
| MF-S3 | the MineStrike reaction and avoidance in routing | behaviors |
| MF-S4 | the mine-clearing vehicle, ClearLane, the line charge, lanes cutting the field | backend + behaviors |
| MF-S5 | the authoring tool and the per-side drawing | ui |
| MF-S6 | the breach play and crossing the lane | behaviors |

## 8. ⛔ The review — `2026-11-16` *(three independent reviewers: doctrine + behaviour, engine fit, authoring / display / the clearer; ✔ = re-measured by the coordinator)* — ⏳ AWAITING RULINGS

⭐ 32 raw findings merged into 20. NOT buildable as drawn until MR-1…MR-8 are ruled.

| # | sev | the defect | evidence | ⭐ lean |
|---|---|---|---|---|
| MR-1 | ⛔ BLOCKER | the knowledge record breaks the 1024 B cap (4096 cells × 2 bits = 1024 B before its header); a struck-mine BITSET of a large AP field is ~12 KB | `EntityCommandBuffer.cs:39, :87, :122` | the grid capped at 62 × 64 cells (or tiles); NO strike bitset — mines inside a LANE are cleared by geometry, STRUCK mines (sparse) are a capacity-N list of lattice (row, col) indices, more entities past capacity (the cell-record shape, keyed by field) — a lattice index survives a polygon edit |
| MR-2 | ⛔ BLOCKER | the record pattern is "one entity per key per DECIDING node": strikes decided on the SimHost, recon on the CGF ⇒ two entities per (field, side) and a save that throws on a key in two slices | `CellRecordSystem.cs:26-31, :65-96`, `ScenarioMergeCore.cs:183-207` | the union as a LATTICE MAX (unknown < suspected < known-clear < known-mined; Marked OR-ed), order-independent; a sibling of `CellRecordSystem` keyed by (field, side); save / load / merge arms; the saved key the field's stable scenario id |
| MR-3 | ⛔ BLOCKER | ONE navmesh area per span, "a later mark wins": a minefield area stamped over woods lets wheels into the wood, over craters lets vehicles through them — and area 5 cannot tell WHICH field | ✔ `RecastNavmeshFactory.cs:186-197`, `NavDoorways.cs:282-283` | COMPOSITE area ids (Minefield, Minefield + Woods, Minefield + Crater — 64 ids exist); a polygon → field map after the bake (the `DoorPolys` precedent); a lane as a later "open" volume; fields and lanes in the static-obstacle bake's signature (a 0.5–2 s rebake of touched tiles, never a live mesh write) |
| MR-4 | ⛔ BLOCKER | no path request knows the mover's SIDE; road legs bypass the navmesh filter | ✔ `PathfindingEvents.cs:30` (`_pad2`), `RoutePlanner.cs:113` | a side byte in the request (in the pad; a wire row); the solver builds a per-side minefield table once per tick and re-points the filter (the `DoorStates` precedent); road edges priced by the same table |
| MR-5 | ⛔ BLOCKER | the MineStrike drill has no track to back out on (only a march lead's 80 m breadcrumbs), the medic carries the casualty back ACROSS the field, and the side's own filter traps the struck unit inside closed cells | `SquadManeuverRunner.cs:197-266`, ✔ `CasualtyCareBehavior.cs:39, :98` | a per-mover BREADCRUMB record kept only inside a field's bounds; backing out and the medic follow it (safe by construction: what was under it has gone off); care IN PLACE; a unit's own breadcrumbs are open to its filter |
| MR-6 | ⛔ BLOCKER | MF-5's "a mine is not fire, a Dash halts" cannot be built — the conduct gate refuses ANY forbidden conduct, the only escape is "cannot comply" | ✔ `BehaviorConduct.cs:89-97` | MineStrike declares Halts \| LeavesRoute honestly ⇒ a dismounted squad on a Dash presses on (realistic), a mobility-killed vehicle yields (CD-5); MF-5 rewritten |
| MR-7 | ⛔ BLOCKER | the recon trace is not specified (start, rule, ties, end), and the team would set off the mines it traces; 5 m detection on 10 m cells marks half-swept cells clear | — | the team walks ONLY on its known-clear cells or breadcrumbs; start at the cell nearest the strike, left-hand rule on the 4-neighbourhood, stop at the start or after N cells; a cell known only when FULLY swept; detector pace a tuning value (~0.1–0.3 m/s) |
| MR-8 | ⛔ BLOCKER | per-side DISPLAY is defeated: the area tool gives every area a style and the generic overlay draws it for every viewer; the outline is stored twice (unmanaged `Polygon` + the editable polyline) and an edit re-indexes struck mines | ✔ `MapOverlayGizmo.cs:11-16`, `AreaAuthoringArm.cs:193-199`, `EditablePolyline.cs:18-40` | the outline is the ONE `EditablePolyline`; `Minefield` holds only parameters; the field is born WITHOUT a visible overlay style and ONE `MinefieldGizmo` draws it, filtered by the viewer's side ✔ `StationMode.SideOf` (`StationMode.cs:59`; the instructor = every side); editing a struck field's shape is refused |
| MR-9 | SHOULD | the danger kind: the byte mask is full AND the danger solve has no side; MF-6's circles would be a SECOND store of the knowledge | `DangerAreaSettings.cs:28-58`, `ShelledAreas.cs:12-20` | the kinds widened to `ushort` (bit-0 hack dropped); Minefield footprints DERIVED from the requesting side's record (no circle store); a minefield is NO-ENTER (like gas), never "crossable" |
| MR-10 | SHOULD | the armour BOTTOM silently reads Top (`float[4]` clamped); a downward normal maps to Side | `ArmorModel.cs:13, :74-89`, `BodyZones.cs:24, :33`, `BodyPartHit.cs:98-104` | the array to 5, bottom armour fields defaulting to rear, Bottom in `FaceOf`, an explicit mine path, a rail per consumer |
| MR-11 | SHOULD | `ClearLane` writes the field (another entity, another node's) | `ClearObstacleBehavior.cs:30-39` | lanes in a RECORD owned by the deciding node (like breaches); the serial trigger clears the blade's swath for any mover whose plough / flail is active; the brain only switches its equipment (the bulldozer's outlet pattern) |
| MR-12 | SHOULD | the recon brain and the SimHost strike write the record from the parallel phase / another node | R-4212 | observations as events through the batch buffer, applied by a serial `MinefieldKnowledgeSystem` on the owning node; the DDS descriptor ordinals and translators fixed in §3a now |
| MR-13 | SHOULD | knowledge with no witness; marking is per side though physical | — | a strike teaches the side only if a living member is within ~300 m, or when the report is sent; a MARKED field is learned by ANY side (civilians too) whose unit sees its edge |
| MR-14 | SHOULD | a point test lets a fast mover step over a mine at high time scale | — | test the SEGMENT from the last footprint to this one; candidate mines by lattice arithmetic (O(1), 0 B) |
| MR-15 | SHOULD | the breach is 100 % reliable; generic speeds; no follow-up | — | a seeded per-mine survival chance for plough / flail; a proofing pass; tool speeds (flail ~0.3–0.8 m/s); an `AntiHandling` flag per field |
| MR-16 | SHOULD | civilians, animals, SUMO, a vehicle drill | `TrafficBodies.cs:83`, `BridgeClosureSystem` | the trigger over every BODY (birds excluded; livestock sets off AP — AT ⚠ the user's call, lean: rarely); SUMO cars triggered through their bodies and a known / marked road field CLOSED to them; a vehicle drill (halt, crew stays, followers reverse in their tracks) |
| MR-17 | SHOULD | detonation unstated; the flail's own blasts | `Charges.cs:57-70` | each mine publishes the munition's detonation (effects, sound, hearing, crater free); a line charge = detonations every ~8 m; the flail immune to its own; a flail loop sound |
| MR-18 | SHOULD | the lane: no side, no authoring, straight vs polyline | — | lanes are straight segments with `ClearedBy` and a known-to mask, drawn from the lane list; *Add lane* / *Delete lane* through the area tool in line mode |
| MR-19 | SHOULD | slices leave decided pieces homeless; no acceptance; no mockup | R-4143 | a test per slice; a demo (a patrol strikes, engineers trace and mark, the clearer cuts a lane, a squad crosses); the mockup; ui owns TKB 116's kit; the breach play written explicitly |
| MR-20 | NIT | polygon-level filter vs cells; the shared lane; the instructor's dots | `NavDoorways.cs:279` | PassFilter closes a polygon with a known-mined cell, `GetCost` prices suspected cells along a segment; a lane opens per side through the filter; the mine dots from a per-field cache; 7206 renamed in one test |

⭐ **Found sound:** derived mines, one field per entity, own fields known at start and the AP / AT split match doctrine and the constructive
practice; all ids are free; vehicle mass is available; the record pattern stops in replay and records by bytes; the tile-hash rebake covers new
volumes; MF-14's reason against a singleton holds; the ClearLane order needs no new UI (the order form picks two points).
