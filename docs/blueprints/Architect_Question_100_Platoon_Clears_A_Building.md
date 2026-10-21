<!--STATUS
state: LIVE — ✅ APPROVED 2026-10-21 (R-2067) — user: "Approved" (A1 · B2 · C1 · D1 · E1 · F1, the leans as written)
updated: 2026-10-21 (approved)
build-state: DESIGN — PL-1 (CE-2264); approved, the build is ON HOLD at the user's word ("Pls hold on just record approval")
current-answer: §0 the decision · §2 the sub-questions with leans
stale-below: nothing
known-rot: none yet
known-conflict: DESIGN_Building_Clearing §8.1 ① (four watch points) vs DESIGN_Platoon_Attack_Defend_Clear §6 (two corner teams) — A reconciles · §8.1 relief ③ (rooms C, D, E) vs the as-built room order (§7.1b: hall → A → C → D → B → E) — E reconciles · CB-18's "backend's B-15 (open)" names a DONE row about damage attribution, not a fire hold — F reconciles
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS the clearing: CB-9 (BuildingClearance), CB-17/CB-18, the room drill (§7.1b as built), the demos (§8.1)
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS the one platoon attack and its presets (§10, R-2035), AT-1…AT-13 as built (§8f), sectors (§13, DA-L)
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — OWNS the platoon HSM form (PC-1), the preset's phase table (§4), the squad plays and roles (§6), PC-5 (the staff writes NoFire per cleared room)
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — OWNS the staff inbox and the picture the BuildingClearance fold joins
-->

# Architect Question 100 — the platoon clears a building (PL-1)

## 0. The decision, in under a page

🔒 **User, `2026-10-21`:** *"Approved"* — every lean below as written (R-2067). The build waits for the user's go.

The platoon attack's **ClearBuilding** preset (R-2035) exists as code: `PlatoonAttack` with `Preset: ClearBuilding` sends the assault squad
`ClearBuilding` and relieves a Black assault with a reserve (AT-9). 📐 Measured what is missing for PL-1:

- the **support squad gets no order at all** in this preset (`PlatoonAttackBehavior.cs:428` — `Support && p.Preset == 0`);
- **no `BuildingClearance`** anywhere — the room states live only inside the squad's play (`ClearBuildingParams.Rooms`, CB-63);
- a **relieving squad starts the building from scratch** — the order is `{Building, Speed}` (`ClearRoomParamsJsonDto.cs:33`);
- **no NoFire writer** exists (PC-5 not built), and the one mechanism has two limits: **4 areas per unit**, fixed on the wire
  (`EntityEngagementAreaTranslators.cs:69-71`), and **2-D** outlines (`EngagementArea.cs:226`, `PolygonMath.Contains`) — the CQB house has
  6 rooms, and a 2-D room outline also covers the storey above it.

**Decision (recommended): A1 · B2 · C1 · D1 · E1 · F1** — the support isolates from two diagonal corners; the platoon keeps a
`BuildingClearance` folded from the RoomClear reports, which also carries each cleared room's 3-D box, and the ONE NoFire reader refuses a
shot ending in a room any commander up the chain holds Cleared; a relief continues that record; the relief demo's ③ follows the real room
order; CB-18's stale interim rule goes.

**Rejected:**
- *PC-5 as written (a NoFire area entity per cleared room)* — the 4-area cap is on the wire and the outline is 2-D: it cannot hold the CQB
  house's 6 rooms, and it would forbid fire into a storey above a cleared room. It also needs brains to spawn replicated polyline entities,
  which nothing does today.
- *lift the support's fire into the whole building at the foothold* — cheap, but it stops covering the rooms still held: the leakers CB-18
  exists for.
- *four separate watch points (§8.1 ①)* — the role table (PL §6) fields two teams; two diagonal corners already see the four faces and
  all four exits of the CQB house.

**Design docs checked:** Building Clearing CB-9 / CB-18 / §8.1 — apply: they OWN the record, the fire rule and the acceptance ·
Defend and Attack §10 — applies: the clearing IS the preset · Platoon §4 / §6 / PC-5 — apply: phase table, roles, the staff writes NoFire ·
Defend and Attack §13c (DA-30) — applies: it sets the 4-area cap · Civilians §7.16 B-15 — does NOT apply: damage attribution, DONE ·
AQ99 — does not apply: the squad demos' spacing and losses.

## 1. INVENTORY *(codebase-memory CLI `search_graph` `.*Clearance.*` (25, none a building record), `.*Isolat.*` (108, none a play),
`.*ClearBuilding.*` (11: `ClearBuildingBehavior`, `ClearBuildingPlay`, `ClearBuildingParams`, `ClearBuildingParamsJsonDto`, the name),
`.*RoomClear.*` (3, the report kind), `.*Ineffective.*` (3: `CombatEffective.MinEffective`), `.*Relie.*` (`PlatoonAttackBehavior.Relieve`);
grep `NoFire` over `Hrot/` + `FDP/`: readers only — the debug sector route and the wire translator, no brain writer.
`check_index_coverage` not run — CLI only.)*

| claim the leans rest on | code — how it IS | design — how it was MEANT |
|---|---|---|
| the support idles in the ClearBuilding preset | ✅ `PlatoonAttackBehavior.cs:428`, `:441` | ⛔ PL §4: support → `IsolateBuilding` |
| the room states live only in the squad's play | ✅ `ClearBuildingBehavior.cs:34`, `:229` | ✅ CB-63: the platoon's record comes with PL-1 |
| RoomClear carries the room's centre | ✅ `RoomDrill.Report` (CB-62) | ✅ CB-62 |
| a relief re-clears every room | ✅ the order is `{Building, Speed}` · `RoomOrder.Next` reads only its own states | ⛔ CB-9: "a relieving squad continues it" |
| NoFire is 4 areas, on the wire | ✅ `EngagementArea.MaxNoFire` · `EntityEngagementAreaTranslators.cs:69-71` | ✅ DA-30 |
| NoFire is 2-D | ✅ `EngagementArea.cs:220-227` | ⛔ searched DA §13: no storey rule |
| the gate is in the executors, on every node the men run | ✅ `AimAndFireExecutor.cs:465`, `:623`; `FireAtPointExecutor.cs:277`; EA replicated brain → every node (CE-3261) | ✅ DA §13c |
| a friend in the line already holds the shot | ✅ CE-321 `LineOfFire.cs:10` (CE-2338 refined it: a friend lying down no longer holds it) | ⛔ CB-18 calls it "backend's B-15 (open)" — B-15 is damage attribution, DONE (Civilians §7.16) |
| men can be downed on cue for the relief demo | ✅ `stage_entity_variable` `Health` over HTTP (`DebugApiRouteDocs.cs:1279`); the twin sets it directly | ⛔ BC §8.1: "unmeasured" |
| the CQB house: one storey, 6 rooms, 4 exits | ✅ `Recipes/Buildings/cqb-house.building.json` | ✅ BC §8 |

## 2. The sub-questions

### A — how the support isolates

- **A1 (lean):** `IsolateBuilding` — two fire teams at **diagonal corners**, each watching two faces (PL §6 roles: the leader at the
  corner with the wider view, the AR on the longest face). Built as a squad play on the existing posts machinery: two authored posts
  (`SquadDefendPlay`'s D-2f path) at the corners with their arcs on the faces. PROGRESS Set when both corners are manned.
  §8.1 ① becomes "the four exits are covered by the two corner teams before the assault reaches the door".
- A2: four watch points, one per exit (§8.1 ① as written). It needs four separate posts for a 6-man squad, which splits the fire teams.
- *Blast radius:* behaviors only. *What would change it:* a house whose exits a diagonal corner cannot see (an L-shaped house) — then the
  play falls back to a post per face.

### B — how "fire only into rooms not yet cleared" (CB-18) is enforced

- B1: PC-5 as written — the staff spawns a polyline area entity per cleared room and lists it in the platoon's NoFire. ⛔ The 4-area cap
  and 2-D outlines (§0).
- **B2 (lean):** the platoon's **`BuildingClearance`** (C) also holds each cleared room's box (its 2-D outline from the survey + its floor and
  ceiling height); it is replicated like `EngagementArea` (CE-3261's shape); the ONE reader, `EngagementAreas.IsNoFire`, also refuses a
  point inside a Cleared room of any commander up the shooter's chain. One reader, no new firing rule (DA-11), 3-D, up to 32 rooms.
- B3: no per-room rule — the support lifts to ReturnFire at the foothold. ⛔ Stops covering the rooms still held.
- *Blast radius (B2):* one new replicated component — its translator lives in `Hrot.Network.NED`, which is **backend's** area (a cross-lane
  edit I would make and name, as CE-3261's shape). *What would change it:* if you want brains able to spawn real area entities anyway
  (other uses: a cordon, a no-go zone), B1 becomes worth its cost.

### C — where the room record lives

- **C1 (lean):** `BuildingClearance` on the platoon commander, folded by the staff inbox from RoomClear / RoomBlocked (CB-9, CB-62/63):
  per room Unknown / Suspected / Cleared / Blocked; a later Contact or UnderFire located in a Cleared room sets it back to Suspected
  (CB-31). The room boxes come from ONE building survey the platoon asks when it plans the objective. Shown on
  `GET /entities/{platoon}/squad` → `clearance`.
- C2: keep the states in the squad play only. ⛔ CB-9 / CB-63 say the platoon owns them; a relief could not continue.

### D — what a relief does

- **D1 (lean):** the trigger stays AT-9 (the assault reports Black, i.e. below 50 % — the CombatIneffective of BC §5c) **plus** a
  `CombatIneffective` report; the reserve gets `ClearBuilding` with a new optional field **`Cleared`**: the room centres the platoon holds
  Cleared; the squad starts with those rooms marked, so no room is cleared twice. The spent squad holds the foothold.
- D2: the reserve re-clears everything. ⛔ CB-9.

### E — the demos' acceptance

- **E1 (lean):** `bc-platoon-house` as §8.1 with ① per A1 and ⑥ per B2. `bc-platoon-house-relief`: three assault men downed **after the
  second RoomClear** (the twin sets their health; the live check stages it over HTTP); ③ becomes **"the reserve clears only the rooms the
  platoon does not hold Cleared — no room twice"** (the as-built order is hall → A → C → D → B → E, so "C, D, E" was never the remainder).
- E2: keep §8.1's wording. ⛔ It cannot pass on the as-built order.

### F — CB-18's interim rule

- **F1 (lean):** strike "until B-15 lands, the support fires only at an exit or window of a room the platoon holds Unknown": the friend-in-
  the-line hold it waited for exists (CE-321, `LineOfFire.cs:10`; refined by CE-2338), and B2 gives the room rule itself.
- F2: keep it as an extra restriction on top of B2.

## 3. If approved — what I build, in order

1. `BuildingClearance` + the staff fold + the survey of the objective (C1) — rails on the fold.
2. B2: the replicated record and `IsNoFire` reading it (a rail per side of a room wall; a storey above a cleared room still open).
3. `IsolateBuilding` (A1) and the preset's phases: Isolate (support to the corners, the assault to the door) → Clear → Consolidate (the
   support enters after BuildingClear).
4. D1: the `Cleared` field and the relief.
5. `bc-platoon-house` + `-relief` scenarios on bt-range's CQB house, their in-process twins, the live checks; UML and as-built folded into
   Building Clearing §7.
