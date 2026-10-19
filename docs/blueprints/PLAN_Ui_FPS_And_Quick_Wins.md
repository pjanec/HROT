<!--STATUS
state: LIVE
updated: 2026-10-19
current-answer: §1 the order (the user's, reordered 2026-10-19: the quick wins first) · §2 how each step is done · §3 approved earlier the same day, after the list · §4 progress log
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Control.md — OWNS the FPS station (§8d–§8i): the design home of #11–#15 and #16–#20 (UML before building)
  - docs/DESIGN_Map_3D_Mode.md — OWNS the figures, kits, wrecks and the scenario camera (#2) (#1, #3, #4, #5, #6, #9)
  - docs/DESIGN_Visual_Effects.md — OWNS impacts and bursts (#1, #5)
  - docs/DESIGN_People_Display.md — OWNS what a person holds and his swings (#3, #5, #9)
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — OWNS the SUMO road scene (#7, cross-lane)
-->

# PLAN — the ui lane: first-person improvements and quick wins *(`2026-10-19`)*

🔒 User, `2026-10-19`: *"File all of them as tracker rows, starting with your leans … Add the hit marker and damage direction task as
number 5."* — then: *"Add the cheap ones at the end of our list after number 5 and make this a plan for you."*

## 1. The order

🔒 User, `2026-10-19`, then: *"And please plan for doing the cheap ones before the first 5 ones."* ⇒ the quick wins (#1–#10) come first; the five first-person items (#11–#15, designed in Entity Control §8j, keys approved R-1009) after them.

| # | id | what | lane | design first? |
|---|---|---|---|---|
| 1 | `CE-1199` | impact marks sized by the ammo (the impact passes damage 0 today) | ui | no — a fix against VE-D |
| 2 | `CE-1221` | after a load with no saved camera, the 2-D map frames all the scenario's entities | ui | Map 3-D §6i row |
| 3 | `CE-1169` / `CE-1179` *(pistol part)* | a hand weapon carried or lying drawn by its `ItemLook`, not the HMG kit | ui | no — approved U7/U8/U18 |
| 4 | `CE-1146` *(first slice)* | the weather in the free 3-D camera as in first person: sampled at the look-at point, the fog counted from the look-at distance | ui | Map 3-D §6aa row |
| 5 | `CE-1209` | the doors-off UH-60's open cabin; riders and door gunners in the openings | ui | Map 3-D §6m C-3 row |
| 6 | `CE-1194` + `CE-1195` | a stone lands as dust, not a burst; the throw's arm swing | ui | VE / People Display rows |
| 7 | `CE-1198` | a destroyed weapon goes black and broken, no flames | ui | Map 3-D §6n row |
| 8 | `CE-1196` | SUMO roads stop flickering against our ground (depth bias) | ui (the coordinator's layer — say it) | no |
| 9 | `CE-1208` | run and walk speeds from the TKB | ⛔ backend — a NOTE, not built here | — |
| 10 | `CE-1171` *(fist part)* | the melee swing (backend's `StrikeNotification` is on the wire) | ui | People Display U10 |
| 11 | `CE-1211` | zoom / aim down sights (hold right mouse; magnification per optic) | ui | ⭐ §8j |
| 12 | `CE-1212` | lean keys (backend's replicated `Lean`; a lean byte in the control frame) | ui (+ the frame field) | ⭐ §8j |
| 13 | `CE-1213` | command wheel for a possessed leader (orders at the crosshair through the one order service, R-270) | ui | ⭐ §8j |
| 14 | `CE-1214` | team switch — Tab to the next man of the squad | ui | ⭐ §8j |
| 15 | `CE-1215` | hit marker and damage direction | ui | ⭐ §8j |
| 16 | `CE-1216` | suppression, sway and recoil you can see | ui | ⭐ §8j |
| 17 | `CE-1217` | compass bar and friendly name markers | ui | §8j |
| 18 | `CE-1218` | the map key — to the 2-D map centred on the possessed unit and back | ui | §8j |
| 19 | `CE-1219` | binoculars / rangefinder and designating from them | ui | ⭐ §8j |
| 20 | `CE-1220` | hit down or killed: fade, switch to a squad mate or a kill cam | ui | ⭐ §8j |

🔒 User, `2026-10-19`: *"I'm not asking for much more than just showing the same weather as in FPS as the first slice of this."* — placed #4.

🔒 User, `2026-10-19`: *"one extremely useful feature …"* (`CE-1221`) — placed second, right after the item already being built.

## 2. How each step is done

- **Design first where marked:** one new section, Entity Control **§8j**, carries the INVENTORY, the class / sequence / module diagrams and the
  leans for #11–#15 (and later #16–#20). A lean that changes what the user approved goes back to the user.
- **Per item:** the feature's own rails first (T-1), a new rail red-proved, the frame screenshot where it is visible, the as-built folded
  into the owning design, the tracker row ticked with the as-built in one line.
- **Simple, mirror-pattern items** (#1, #6, #7) may go to a Sonnet subagent in a worktree; the diff is reviewed and the gates re-run here.
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
| `2026-10-19` | #11–#15 | Entity Control §8j designed (INVENTORY, claim table, class / sequence / module diagrams, leans Z-1…H-2); ✅ keys approved (`R-1009`) |
| `2026-10-19` | — | reordered by the user: the quick wins first |
| `2026-10-19` | 2 | `CE-1221` added (frame the entities after a load with no camera) |
| `2026-10-19` | 1, 2 | ✅ `CE-1199` and `CE-1221` built, red-proved; Presentation 773 / 0 (2 skipped, the audio pair) |
| `2026-10-19` | 3 | ✅ the pick-up rifle / pistol / charge drawn as themselves (and hit as themselves); behaviors' D4 ask — a commander's absent picture says so — built with it; Presentation 776 / 0 |
| `2026-10-19` | 4 | ✅ the weather in the free 3-D camera (at the look-at, the fog from there); Presentation 776 / 0, Fdp.Presentation 581 / 0 |
| `2026-10-19` | 5 | ⏸ parked — the doors-off UH-60 shares the closed one's DIS type, so its open cabin needs its own class (and sim boxes) first; the lean is with the user (`CE-1209`) |
| `2026-10-19` | 6 | ✅ a thrown stone lands as dust (a grenade still bursts); the throw's arm swing; Presentation 778 / 0 |
| `2026-10-19` | 5 | ▶ un-parked by the user — every UH-60 drawn doors-off (the one `Helicopter` kit), done after #7 |
| `2026-10-19` | 7 | ✅ a destroyed weapon system goes dark, it does not burn; Presentation 778 / 0 |
| `2026-10-19` | 5 | ✅ every UH-60 drawn doors-off: the cabin seen through, riders and gunners in its openings; the sim's boxes regenerated; Presentation 778 / 0, parity 6 / 0, Toolkits bodies 46 / 0 |
| `2026-10-19` | 8 | ✅ the roads stop flickering from afar: the 3-D camera's near clip 0.1 % → 0.5 % of its distance (the depth step from 2 km under the road's lift); Presentation 779 / 0, Fdp.Presentation 581 / 0 |
| `2026-10-19` | 9 | 📨 `CE-1208` sent to backend as a [NOTE](batches/NOTE_Ui_To_Backend_Walk_And_Run_Speeds.md) — the walk / run speeds belong in the TKB |
| `2026-10-19` | 10 | ✅ a blow struck is drawn: the striker's 0.45 s fist punch / knife stab / baton swing, from `StrikeNotification`; Presentation 781 / 0 |
| `2026-10-19` | 11 | ✅ hold RMB to zoom (the scope's field of view, else the iron sights' 30°; the mouse slowed alike); the lock cancel is X. No wire change: a possessed man is already Aimed. Found and filed `CE-1222` (backend — the operator's weapon choice never reaches `ActiveMount`); Presentation 782 / 0 |
| `2026-10-19` | 12 | ✅ hold Q / E to lean (a level on the control frame, appended through the wire; the brain writes `dtLean` on a change; it goes with the possession); use moved E → F, the next round F → R; cross-lane: one line in behaviors' `OperatorControlBehavior`. Presentation 783 / 0, SimHost 1688 / 0 |
| `2026-10-19` | 13 | ✅ hold T: the command wheel — the orders every one of his men fulfils, the mouse picks a sector, letting go sends one draft per man through the RTS tool's order path, completed from the crosshair; Presentation 784 / 0 |
