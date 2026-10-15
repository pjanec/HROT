<!--STATUS
state: LIVE
updated: 2026-10-15
build-state: DESIGN — REQUIREMENTS ONLY. Founded 2026-10-15 by the behaviors lane (CE-2181); the owning lane is open (R-2019 says "backend's call"). No UML yet: the design starts with the inventory in §2 and the questions in §4.
current-answer: §3 the requirements, §4 the open questions — §2 is the measured ground.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7b W1 OWNS the point target's kind on a Strike (Building · Fortification · Bridge · Runway) and the weapon choice that reads it; this file owns what the hit DOES to the structure
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — OWNS blast and fragments through a building's barrier (D-M4: where destructible buildings plug in); this file changes the barrier when the building breaks
  - docs/DESIGN_Building_Interiors.md — OWNS doors (Breach), rooms and §14 runtime navmesh change; E3 wall breaching waits on its P2 — a collapse or a breach here is that geometry change
  - docs/designs/navig-2/Navigation_Design_v2_0.md — OWNS R-218: geometry changes (breach, crater, placed building) are copy-on-write navmesh rebuilds, never in place under a query
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — OWNS mortars and artillery, the other source of heavy rounds on a building or a bunker
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — the "building damage (collapse / breach from a heavy charge)" engine row and AIR-1 (precision strike on a building)
-->

# Structure damage — buildings, bunkers, bridges, runways

🔒 **User, `2026-10-15`:** *"structure damage will come as another programme, if not yet having any requirement documents, we can found
one."* — and, choosing a weapon for a strike: *"Point target usually cary a reason why to strike it - building or bridge or bunker or runway
or something"* (Air Operations §7b, W1, R-2020).

## 1. Where the requirement comes from

| source | what it asks |
|---|---|
| Air Operations §7b **W1** (R-2020) | a Strike names its target's kind: `Building` · `Fortification` (bunker) · `Bridge` · `Runway`; the weapon is chosen for it — ⛔ but nothing is damaged until this programme |
| Catalogue, engine row *"building damage"* | collapse / breach from a heavy charge — air strike, bombs, breaching (🟨: doors breach, walls deferred) |
| Catalogue **AIR-1** | a precision air strike on a building |
| **R-2019** | *"Destructible buildings later, backend's call"* |
| Armour Hit Model **D-M4** | destructible buildings are not that batch — the plug-in point is the building BARRIER that blast passes through |
| Building Interiors **§14** / **R-218** | the navmesh changes at runtime by copy-on-write; wall breaching (E3) waits on §14's P2 |

## 2. What exists — measured `2026-10-15`

| fact | code | design |
|---|---|---|
| buildings are terrain, not entities: footprints, wall panels, prisms, surfaces — static | `TerrainWorld.cs:104-113` (`Buildings`, `Panels`, `Prisms`) | Building Interiors |
| doors carry state and can be breached | door state + `Breach` action | Building Interiors, Catalogue |
| a building's walls already reduce blast for people (closed barrier `exp(−R / 100 mm)`, open-obstacle shadow) | `AreaEffect.cs:20-70` | Armour Hit Model §7 |
| nothing damages a structure — no structure health, state or event | search `BuildingDamage\|StructureDamage\|StructuralDamage\|Destructible` over `*.cs`: 0 (graph + grep agree) | — |
| no bridge, bunker or runway feature kind in the terrain | the recipe worlds' kinds: building, door, fence, fire, ramp, sight, slab, surface, wall, window | — |

## 3. Requirements (gathered — not yet decided)

| # | requirement |
|---|---|
| **S1** | a structure has a damage state (intact · damaged · destroyed), and a part of it (a wall panel, a span) may break alone |
| **S2** | a structure has a hardness: a bunker needs a heavy or penetrating round, a house does not — the weapon choice (Air Ops W2) reads it |
| **S3** | a broken building changes the world: the blast barrier (Armour Hit Model D-M4), cover and line of sight, the navmesh (R-218 copy-on-write), and hurts the people inside |
| **S4** | a broken bridge span is impassable (routes and navmesh); a cratered runway is unusable (⚠ no consumer yet — planes do not land in v1, Air Ops B9) |
| **S5** | the damage state is replicated, saved with the scenario and drawn on the map and in the image generator |

## 4. Open questions — for the founding design

| # | question | lean | what would change it |
|---|---|---|---|
| **Q1** | who owns it | backend (R-2019); behaviors adds the weapon choice and the AI's reactions | the user assigning another lane |
| **Q2** | a structure as a TERRAIN feature with state, or as an ENTITY with a TKB type | an entity with a TKB type (a DIS cultural-feature kind) for anything that can be a target, so the hit, damage, replication and save paths an entity already has apply, and a Strike can name it | measuring that thousands of buildings as entities cost too much — then terrain state, with entities only for named targets |
| **Q3** | how fine | per structure first (S1's state), per wall panel second (breach, E3) | — |
| **Q4** | the navmesh | reuse R-218 and Building Interiors §14 (P2 tiles); no second rebuild path | — |
