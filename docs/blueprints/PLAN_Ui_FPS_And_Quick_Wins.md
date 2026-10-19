<!--STATUS
state: LIVE
updated: 2026-10-19
current-answer: §1 the order (the user's) · §2 how each step is done · §3 approved earlier the same day, after the list · §4 progress log
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Control.md — OWNS the FPS station (§8d–§8i): the design home of #1–#5 and #14–#18 (UML before building)
  - docs/DESIGN_Map_3D_Mode.md — OWNS the figures, kits and wrecks (#6, #7, #9, #10, #11, #13)
  - docs/DESIGN_Visual_Effects.md — OWNS impacts and bursts (#6, #9)
  - docs/DESIGN_People_Display.md — OWNS what a person holds and his swings (#7, #9, #13)
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — OWNS the SUMO road scene (#11, cross-lane)
-->

# PLAN — the ui lane: first-person improvements and quick wins *(`2026-10-19`)*

🔒 User, `2026-10-19`: *"File all of them as tracker rows, starting with your leans … Add the hit marker and damage direction task as
number 5."* — then: *"Add the cheap ones at the end of our list after number 5 and make this a plan for you."*

## 1. The order

| # | id | what | lane | design first? |
|---|---|---|---|---|
| 1 | `CE-1211` | zoom / aim down sights (hold right mouse; magnification per optic) | ui | ⭐ §8j |
| 2 | `CE-1212` | lean keys (backend's replicated `Lean`; a lean byte in the control frame) | ui (+ the frame field) | ⭐ §8j |
| 3 | `CE-1213` | command wheel for a possessed leader (orders at the crosshair through the one order service, R-270) | ui | ⭐ §8j |
| 4 | `CE-1214` | team switch — Tab to the next man of the squad | ui | ⭐ §8j |
| 5 | `CE-1215` | hit marker and damage direction | ui | ⭐ §8j |
| 6 | `CE-1199` | impact marks sized by the ammo (the impact passes damage 0 today) | ui | no — a fix against VE-D |
| 7 | `CE-1169` / `CE-1179` *(pistol part)* | a hand weapon carried or lying drawn by its `ItemLook`, not the HMG kit | ui | no — approved U7/U8/U18 |
| 8 | `CE-1209` | the doors-off UH-60's open cabin; riders and door gunners in the openings | ui | Map 3-D §6m C-3 row |
| 9 | `CE-1194` + `CE-1195` | a stone lands as dust, not a burst; the throw's arm swing | ui | VE / People Display rows |
| 10 | `CE-1198` | a destroyed weapon goes black and broken, no flames | ui | Map 3-D §6n row |
| 11 | `CE-1196` | SUMO roads stop flickering against our ground (depth bias) | ui (the coordinator's layer — say it) | no |
| 12 | `CE-1208` | run and walk speeds from the TKB | ⛔ backend — a NOTE, not built here | — |
| 13 | `CE-1171` *(fist part)* | the melee swing (backend's `StrikeNotification` is on the wire) | ui | People Display U10 |
| 14 | `CE-1216` | suppression, sway and recoil you can see | ui | ⭐ §8j |
| 15 | `CE-1217` | compass bar and friendly name markers | ui | §8j |
| 16 | `CE-1218` | the map key — to the 2-D map centred on the possessed unit and back | ui | §8j |
| 17 | `CE-1219` | binoculars / rangefinder and designating from them | ui | ⭐ §8j |
| 18 | `CE-1220` | hit down or killed: fade, switch to a squad mate or a kill cam | ui | ⭐ §8j |

## 2. How each step is done

- **Design first where marked:** one new section, Entity Control **§8j**, carries the INVENTORY, the class / sequence / module diagrams and the
  leans for #1–#5 (and later #14–#18). A lean that changes what the user approved goes back to the user.
- **Per item:** the feature's own rails first (T-1), a new rail red-proved, the frame screenshot where it is visible, the as-built folded
  into the owning design, the tracker row ticked with the as-built in one line.
- **Simple, mirror-pattern items** (#6, #10, #11) may go to a Sonnet subagent in a worktree; the diff is reviewed and the gates re-run here.
- **Gates per push:** `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check`; the touched test projects built and run
  `--no-build`.

## 3. Approved earlier the same day — after the list

| id | what | ruling |
|---|---|---|
| `CE-1204` | a person's switchable mouse-look mode | `R-1006` |
| `CE-1207` + `CE-1205` | the operator picks up stones / rounds and takes weapons; a lying weapon is not possessed | `R-1007` |
| `CE-1210` | seat switching in a vehicle with several crewed weapons | `R-1008` |
| `CE-1206` | the ZU-23 technical that would not fire — reproduce live first | — |

## 4. Progress log

| date | # | state |
|---|---|---|
| `2026-10-19` | — | plan written |
| `2026-10-19` | #1–#5 | Entity Control §8j designed (INVENTORY, claim table, class / sequence / module diagrams, leans Z-1…H-2); ⏳ the key layout awaits the user |
