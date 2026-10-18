<!--STATUS
state: LIVE
updated: 2026-10-19
current-answer: §1 where it stands · §2 the build order (start here) · §3 what binds you
stale-below: nothing — but every "open / not built / in flight" line is a STATE claim: verify it against git and the tracker before acting
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — the command machinery (CC-A…CC-E), built first
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — the attack / defend / clear programme on top of it (§11 slices, §13 waves)
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — the recorded urban ambush demo (not scheduled) and the infrastructure it needs (§4)
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md · DESIGN_Defend_And_Attack_Position.md · docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — the neighbours each wave builds from
-->

# RESUME — behaviors lane: build the command chain, then the platoon programme *(written 2026-10-18 for the next session)*

**RELEARN** — run the re-learn and open your first reply with the DESIGN BRIEF (`CLAUDE.md`).

⭐ **Lane:** `behaviors` (stable branch `behaviors`). Ids from your blocks only: tracker next free **`CE-2301`**, rulings next free
**`R-2054`** (check the block tables — `Blueprint_Issues_Tracker.md` "ID blocks", `RULINGS.md` §0a — they may have moved).
⭐ **Written at** `dd27d3b28` (behaviors), after merging `coordinator` at `10667905e`.

## 1. Where it stands *(updated 2026-10-19)*

| | |
|---|---|
| ⭐⭐ **the command chain is BUILT and runs live** | steps 1–7 done: `CE-2289` ROE up the chain · `CE-2262` SL-0 · `CE-2291` CC-A · `CE-2292` CC-B · `CE-2293` CC-C · `CE-2295` CC-E · `CE-2294` CC-D — `cc-platoon-bound` passes all five end conditions live (`utility-demo-check.py cc-platoon-bound --launch`) and as an in-process twin. As-built: [Command Chain §8a](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| ⭐ wave 0 | `CE-2273` proficiency ✅ (§8a) · `CE-2282` sectors ✅ — the mechanism (§13c; leans DA-26…DA-30 await the user; nothing writes a sector until the plays) · `CE-2281` roles 🟡 part (the one role reader; AR / MG into the base of fire — §12c; the fire-team split and role takeover are BLOCKED, see §6) — all in [Defend & Attack](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) |
| still open from the chain | `CE-2288` — one read route over every behaviour slot (its first consumer is the platoon demos watching a hosted squad play) |
| other lanes | backend built BR-3 (reports on the wire), BR-12 (WorldSurvey), BR-13 (ROE / reporting across nodes), LD-1 / LD-2 (look); a note tells them proficiency landed (BR-6 / BR-7 may start). A note tells the coordinator lane one editor rail is red since CE-4031 |
| civilians (`CE-2235`…`CE-2240`, `CE-2256` TC-7) | a different programme on this lane, still open; not part of this resume |

## 2. The build order — start here

⭐ The order is the platoon design's waves ([§13](../designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md)), with the
command machinery ([Command Chain §8](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md)) as wave 1. **Read each slice's
design section end to end before building it** (R-129) — the diagrams there are the spec; fold every deviation back (as-built rule).

| step | id | what (one line) | needs | notes |
|---|---|---|---|---|
| 1 ✅ | `CE-2289` | **ROE up the chain**: `RoeOf` reads the squad's and platoon's ROE when a man has none of his own | — | small; today ROE set on a squad never reaches its men |
| 2 ✅ | `CE-2262` | **SL-0**: the squad layer one echelon up — a platoon of squads (the body-less platoon commander) | — | CC-C needs it |
| 3 ✅ | `CE-2291` | **CC-A**: `CommandPicture` (Elements ×8, Subordinates ×6, Requests ×4), `ReportingRequirements` (+ `PirArea`), `ReportOutbox`; `SetReportingEvent` + its system | — | ⭐ every buffer a fixed-list wrapper (`Count` + `[InlineArray]`) so `GET /entities/{id}` shows it (R-2050) |
| 4 ✅ | `CE-2292` | **CC-B**: `OrderSubordinate` / `AssignSubordinate` (one helper with `SquadManeuverRunner.Order`), `SetSubordinateReporting`, `SendReport`, `ProgressReached`, `ElementReported`, `TimeInState` | CC-A | — |
| 5 ✅ | `CE-2293` | **CC-C**: the staff — `StaffComposerSystem` (PROGRESS on task end, SPOT, SITREP) + `StaffInboxSystem` | CC-A, SL-0 | ⭐ on ONE node first, with a local loopback; the wire (BR-3) comes later |
| 6 ✅ (`CE-2288` open) | `CE-2295` + `CE-2288` | **CC-E**: reports over the debug HTTP (watch, fake, mute), `GET /entities/{id}/order` for every origin; **one read route over every behaviour slot** (R-2050) | CC-C | — |
| 7 ✅ | `CE-2294` | **CC-D**: the first chain runs — `PlatoonBound` + `BasicPlatoonSop` + `PlatoonHold`, scenario `cc-platoon-bound` | CC-B, CC-C | its five end conditions are in Command Chain §8 |
| 8 | wave 0 rest | proficiency `CE-2273` ✅ · sectors `CE-2282` ✅ · roles `CE-2281` | — | independent of steps 1–7; any order |
| 9 | wave 2 → 4 | `SquadDefend` `CE-2275`, clearing `CE-2257`…`CE-2259`, sensors `CE-2283`, LD-3 `CE-2297`, LD demos `CE-2298` → `PlatoonDefend` `CE-2276`, `PlatoonAttack` `CE-2277`, PL-1 `CE-2264`, smoke `CE-2278`, sniper `CE-2279`, urban `CE-2284` → repertoire `CE-2280` | waves 0–1 + the other lanes (§4) | the demo harness is `CE-2296` |
| later | urban battle | [§4](../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md) items ①–⑨: the headquarters play, mortar sound + locating, armed search, IED trigger man + ambush, convoy halt, insurgent mortar cell | the platoon programme | ⛔ not scheduled (R-2051); file each row when you start it |

⭐ **Each slice: the feature's own rails first, then new rails in the feature's suite; builds and tests in the background; the
report names the design section it folded.**

## 3. What binds you

| ruling | in one line |
|---|---|
| R-2042 / R-2043 | a commander's knowledge lives by SCOPE: the unit's in three components, each play's in its own slots |
| R-2044 | CC-2…CC-11: requirements by a `SetReportingEvent` (the ROE twin); the staff sends everything; orders abstract or concrete; `Initiative` gates what a unit does alone |
| R-2045 | CC-12…CC-15: reports watchable and fakeable over HTTP; a fake is marked `Synthetic` |
| R-2046 / R-2047 | the platoon programme PC-1…PC-16 (plays, NoFire rooms, the lift handshake, window switching) |
| R-2048 / R-2049 | look direction: the existing look-at channel; the head only glances, the body follows (R-3003 for aiming) |
| R-2050 | a commander's memory reads like any component; one route over every behaviour slot |
| R-2051 | the urban battle is recorded, infrastructure first |
| R-2052 / R-2053 | demo-city gets quarters (ui); street cover is per scenario; our buildings may be made any suitable way |

## 4. What other lanes owe *(check `git log origin/<lane>` for each before you wait on it)*

| lane | item | what you need it for | where |
|---|---|---|---|
| backend | **BR-12** WorldSurvey (rooms, faces, points, solved on the muscle) | every clearing / defence plan | [geometry note](batches/NOTE_Behaviors_To_Backend_Geometry_On_The_Muscle.md) |
| backend | **BR-3** the `SquadReport` topic (+ the [report note](batches/NOTE_Behaviors_To_Backend_Report_Ageing.md)'s fields) | reports across nodes; CC-C runs on a loopback until it lands | [frame](batches/HANDOFF_Behaviors_To_Backend_Building_Clearing_Reports.md) |
| backend | ⭐ **BR-13** ROE / reporting changes forwarded to the subordinate's brain node | ⚠ today a superior on another node sets them **with no effect** (`CE-2299`) — the lean awaits the user | [platoon note §4.1](batches/NOTE_Behaviors_To_Backend_Platoon_Programme.md) |
| backend | **LD-1 / LD-2** the look executor, the cone follows the head | LD-3, sectors | same note |
| backend | BR-6…BR-9, BR-2, BR-4 / BR-5 | proficiency, roles' weapons, snipers, stun, search / lean | [defend frame](batches/HANDOFF_Behaviors_To_Backend_Defend_And_Attack.md) · [interactions](batches/HANDOFF_Behaviors_To_Backend_Building_Clearing_Interactions.md) |
| ui | **UI-1…UI-7** buildings, `house-3f`, the CQB house, `cqb-village`, the command-picture map, the Reports window | the platoon demos | [hand-off](batches/HANDOFF_Behaviors_To_Ui_Platoon_Demos.md) |
| ui | **UI-8…UI-12** demo-city quarters | the urban battle (later) | [hand-off](batches/HANDOFF_Behaviors_To_Ui_Demo_City_Quarters.md) |

## 5. Measured traps *(2026-10-18 — re-measure before relying on one)*

| trap | where it bit or would bite |
|---|---|
| ⛔ `SetRoeEvent` is applied only on the node that raised it; an order crosses nodes as `TacticalIntentRequest`, ROE does not | CC-2 / CC-11 copy the ROE pattern ⇒ BR-13 |
| ⛔ mission plans: ≤ 8 phases on ONE unit, triggers only on that unit (timer, under attack, health, behaviour finished); operator-only | not a way to sequence other units' events |
| ⛔ an insurgent with no order only hides (`BasicInfantrySop` → `TakeCover`); a static ambusher has no library form | any insurgent demo authors his behaviour |
| ⛔ mortar tubes and guns have no firing sound ⇒ a launch is never heard | the urban battle §4 ② |
| ⛔ `GET /orders` is the ORDER CATALOGUE, not issued orders — read `GET /entities/{id}/order` | CC-E |
| ⚠ the Humvee type has no health block; the generic APC is unarmed | the urban battle §4 ⑧ |
| ⚠ the live debug MCP servers may be down; `codebase-memory-mcp cli` works but has no `check_index_coverage` | say so in any exhaustive claim |
| ⚠ demo-city's 34 houses are all one storey; only bt-range has `house-2f` | no upper windows until UI-1 / UI-8…UI-12 |

## 6. Open with the user

- ⭐ **2026-10-19, from the overnight run:**
  - the sector leans **DA-26…DA-30** (Defend & Attack §13c): a sector travels as the ROE-twin event (not inside each order); `Allow` only in the fire picks, never in `TopThreat` / `TopContact`; self-defence 50 m / 5 s by timestamps; tiers; network ids for areas.
  - **CE-2281's fire-team carrier** — lean: a `FireTeam` field on `EntityInfo` (the wire); and **role takeover** needs a weapon pick-up design first (§12c).
  - **component-id blocks per lane** — two parallel collisions in one night; backend wrote the proposal with a lean: [NOTE_Backend_To_All_Lanes_Component_Id_Space](batches/NOTE_Backend_To_All_Lanes_Component_Id_Space.md) (behaviors agrees with it).

- **BR-13's lean** (two more `Kind` values on `TacticalIntentRequest`) — sent to backend as a lean, not yet ruled.
- **TC-7** (how far a man holding a throwable counts as lethal, `CE-2256`): backend's lean is per held item; mine agreed; not ruled.
