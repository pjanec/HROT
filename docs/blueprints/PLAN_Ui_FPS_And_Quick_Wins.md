<!--STATUS
state: LIVE
updated: 2026-10-20
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
| `CE-1196` | ⛔ reopened (`R-1014` put the near plane back): the SUMO roads flicker in 3-D — 🔒 user, `2026-10-20`: *"Keep road flicker in the plan, btw same flicker is for multiple gizmos showing filled circle on the ground in 3d."* ⇒ one depth bias for everything drawn ON the ground (the road mesh and the draped gizmo fills) | — |

## 3a. Overnight `2026-10-21` — every open ui point up to `CE-1197`

🔒 User, `2026-10-21`: *"Please put to your plan all of them from the beginning till CE 1197 inclusive. work autonomously overnight."*
⇒ the open points listed that morning, the cheap ones first. ⭐ Each one per §2 (T-1, a red-proved rail, a frame where visible, the
as-built folded, the row ticked). A lean that changes what the user approved goes back to the user, written down; the night goes on with
the next item (`R-106`).

| # | id | what | design home | notes |
|---|---|---|---|---|
| 21 | `CE-1197` | sidewalks drawn as their own light grey, a kerb above the road | Map 3-D (the SUMO road scene) | cross-lane: the SumoSharp scene kind |
| 22 | `CE-1170` | U9 — aimed at a person: a thin line muzzle → target (`UnitDetails`, off by default) | People Display §5 U9 | data on the wire |
| 23 | `CE-1173` | U12 — a toy gun drawn orange | People Display §5 U12 | the catalogue toy type is G6 |
| 24 | `CE-1174` | U13 — the baton-round launcher's yellow band; grenades in hand | People Display §5 U13 | taser / pepper spray wait on G5 |
| 25 | `CE-1172` | U11 — the police look: cap (riot helmet for the shield man), vest, belt, the baton in hand | People Display §5 U11 | user live `2026-10-20`: no baton while striking |
| 26 | `CE-1179` | U18 rest — a laid-down weapon at his feet; the placed charge's red blink | People Display §5 U18 | |
| 27 | `CE-1169` | U7 rest — the held-item poses (aimed pistol, phone, binoculars, a throwable in hand); the ExCon Concealed layer | People Display §5 U7 | phone / binoculars only once G7 sets them |
| 28 | `CE-1176` | U15 — speech marks; the WARNING now | People Display §5 U15 | the rest waits on G1 |
| 29 | `CE-1177` | U16 — mood face + dot; pain now | People Display §5 U16 | fear / anger wait on G2 |
| 30 | `CE-1202` | the gunner turns with his mounted weapon | Map 3-D §6n N-2 | a defect; measure which case fails first |
| 31 | `CE-1200` | first person in a vehicle draws the vehicle, from a crew eye | Entity Control §8 | supersedes "the own vehicle is not drawn" |
| 32 | `CE-1201` | an optional over-the-shoulder view (menu switch) | ⭐ Entity Control §8 — design first, with leans | |
| 33 | `CE-1158` | the use key — the live cluster run (CGF + IG) | Entity Control §8h | built; a run, not code |
| 34 | `CE-1119` | U-2 — the ejection seat and its flame | Map 3-D §6w | waits on backend B-7…B-9 — check, else record blocked |
| 35 | `CE-1146` | the rest of the weather: cloud layers, moon, precipitation, lights at night | Map 3-D §6aa | 🔒 "discuss before design": leans written, only the uncontroversial slice built |

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
| `2026-10-19` | 14 | ✅ Tab / Shift+Tab jumps to the next / previous man of his squad — one possession, which returns the man he leaves to his brain; Presentation 785 / 0 |
| `2026-10-19` | 15 | ✅ the hit marker (a cross round the crosshair; red when the one hit is down) and the damage arc (red, at the screen's edge toward where a hit came from) — rounds and blows, every node; no new system (the effects spawner records them); Presentation 786 / 0. **#11–#15 done.** |
| `2026-10-19` | 16–20 | Entity Control §8k designed (INVENTORY, claim table, class / sequence / module diagrams, leans S-1…K-1, keys **M** map and **B** binoculars, **K** kill cam) — ⏸ waiting for the user's nod |
| `2026-10-19` | 16 | ✅ the spread ring (the sim's own σ), the edges darkening on a near miss (the audio's pass rule, extracted as `RoundPasses`), the recoil kick; filed `CE-1223` (replicate `UnderFire`) and `CE-1224` (binoculars for leaders) for backend; Presentation 788 / 0 |
| `2026-10-19` | 17 | ✅ the compass strip (every scheme) and his side's names within 50 m over their heads, his own men brighter; Presentation 789 / 0 |
| `2026-10-19` | 18 | ✅ M: the 2-D map centred on him (he holds still, still possessed), M again back to his eyes; Presentation 790 / 0 |
| `2026-10-19` | 19 | ✅ hold B: the binoculars (raised by his brain, refused without them), 10° view when up, the range, LMB designates for his men; cross-lane: `OperatorControlBehavior` + the frame byte through the wire; Presentation 791 / 0, SimHost 1689 / 0 |
| `2026-10-19` | 20 | ✅ when he falls: the view stays and fades, "KILLED - by <name>", Tab a squad mate / K a kill cam / Esc the map; DOWN dims the view; Presentation 793 / 0. **#16–#20 done.** |
| `2026-10-20` | — | `CE-1204` (a person's mouse look) designed — Entity Control §8l, leans LK-2…LK-7 with the user; `CE-1196` kept, widened to the draped ground circles |
| `2026-10-20` | — | ✅ `CE-1225` the Sectors checkbox (Command Display §8; Presentation 795 / 0); `CE-1203` finer layers + a set per view + remembered — APPROVED `R-1011`, ⛔ not started (Tuning §5e). Next: §3's items, each designed first — see [RESUME_UI_Lane](RESUME_UI_Lane.md) "SESSION 2026-10-20" |
| `2026-10-20` | — | ✅ `CE-1203` finer layers (Tuning §5e.1), `CE-1231`/`CE-1232` charges (VE §11), `CE-1234` the 3-D layer panel; ✅ `CE-1204` a person's mouse look BUILT (Entity Control §8l.5, `R-1017`): V frees the look within his neck (the TKB's, 0 ⇒ 70°), A/D turn him, every node draws his head where his eyes point. Next: `CE-1196` |
| `2026-10-20` | — | ✅ `CE-1196` ground flicker (one ground depth bias: roads + gizmo fills, fills without depth writes; Map 3-D §6z CA-4) and `CE-1228` (a 2-D fill drawn once); Presentation 813 / 0, GizmoMap 47 / 0. Next: `CE-1207` + `CE-1205` design |
| `2026-10-20` | — | ✅ `CE-1207` + `CE-1205` designed and built (Entity Control §8m, §8m.5): Take a lying portable weapon, Pick up a lying stone / round (the station chooses it; a new executor, action 12), Drop on the hold list, a weapon is manned never possessed (menu greyed, order refused). Next: `CE-1210` seat switching (design first) |
| `2026-10-20` | — | `CE-1210` seat switching designed (Entity Control §8n, leans MS-1…MS-7 — ⚠ MS-2 amends Weapon Systems W-H) and `CE-1230` the shared ORBAT menu designed (canvas context menu §11, leans OM-1…OM-5) — ⏸ both wait for the user |
| `2026-10-21` | — | ✅ `CE-1210` seat switching built (MS-1…MS-7 approved, `R-1018`; Entity Control §8n.5 — G cycles driver ⇄ gunner seats by possessing the man in the seat, look clamped to the station arc, W-H amended for possessed gunners) and ✅ `CE-1230` the shared ORBAT menu built (OM-1…OM-5 approved, `R-1019`; canvas context menu §11.4 — the ORBAT row borrows the map menu, Embark… / Disembark through one `EmbarkOrders`). ⚠ Windows visual check owed for both |
| `2026-10-21` | — | ✅ `CE-1235` a soldier's side picks his uniform's cloth (friend woodland, hostile desert; Map 3-D §6q.1, `R-1020`) — a soldier's TYPE colour no longer wins (R-1021); Windows visual check owed |
| `2026-10-21` | — | ✅ `CE-1236` every soldier's weapon drawn as what it is + the MortarTeam's mortar on the ground (People Display U7a / §7b, `R-1022`) — the hand-weapon half of `CE-1169`; Windows visual check owed |
| `2026-10-21` | — | ✅ `CE-1237` Blueprint Tools is a normal panel (close box, Blueprint perspective) · ✅ `CE-1238` (cross-lane, user's request) the mortarman crouches; `bt-mortar-roof` fires crewed 81 mm tubes (live PASS); a round no longer kills its own crew |
| `2026-10-21` | 21, 22 | ✅ `CE-1197` sidewalks light grey (pedestrian-only lanes split out of the asphalt; the corner walking areas filled as polygons; AQ93 §11.1) · ✅ `CE-1170` the aim line (`AimLineGizmo`, People Display §7c); Traffic 58 / 0, Presentation rails 10 / 0. ⭐ Found: behaviors / backend LANDED G1 `Utterance`, G2 `Demeanour`, G7 held items (phone, stone, binoculars) and airborne B-7…B-9 (the ejection) — #27–#29 and #34 build in full |
| `2026-10-21` | 23–25, 27 | ✅ `CE-1172` the police look (cap / riot helmet + visor, vest, belt, baton, shield) · ✅ `CE-1173` the toy drawn orange · ⏳ `CE-1174` the launcher's yellow band built (taser / spray wait on G5) · ⏳ `CE-1169` phone / binoculars / a stone in hand built (the ExCon layer left) — People Display §7c; Presentation 811 / 0 |
| `2026-10-21` | 26, 28, 29 | ✅ `CE-1179` the laid-down weapon at his feet, the placed charge blinking · ✅ `CE-1176` speech marks · ✅ `CE-1177` the mood dot and the face (a `faceMood` shader uniform) — People Display §7c. ⚠ G1 / G2 have no production WRITER yet (behaviors CE-2249 / CE-2250): live today only the warning and pain |
| `2026-10-21` | 30 | ✅ `CE-1202` the station gunner turns with his weapon (the vehicle-station case was the one failing) — Map 3-D §6n N-2; Presentation 814 / 0 |
| `2026-10-21` | 31 | ✅ `CE-1200` first person in a vehicle draws it, from a crew eye (Entity Control §8o, leans CR-1…CR-3 taken overnight); Presentation 815 / 0 |
| `2026-10-21` | 32 | ✅ `CE-1201` the over-the-shoulder view — designed (Entity Control §8p, leans OS-1…OS-6 taken overnight) and built: View menu + O, his head drawn, aim unchanged; Presentation 816 / 0 |
| `2026-10-21` | 34 | ✅ `CE-1119` U-2 the ejection seat (the seat, its rocket flame, the seat falling away) — Map 3-D §6w.U2; Presentation 817 / 0 |
| `2026-10-21` | 35 | ✅ `CE-1146` PR-1 precipitation over the 3-D scene (rain streaks with the wind, snow flakes, from the same sample as the sky) — Map 3-D §6aa.PR-1; leans CL / MO / NL written in §6aa.next, not built; Presentation 818 / 0 |
| `2026-10-21` | 35 | ✅ `CE-1146` PR-2 a wreck's smoke drifts on the world's wind (the code's own *"until weather exists"* placeholder) — Map 3-D §6aa.PR-2 |
| `2026-10-21` | 33 | ✅ `CE-1158` the use key's cluster run — Drive and Get out from a pure IG station across SimHost / IG / ExCon / CGF (`EntityOrdersAcrossNodesTests.CE1158_…`, red-proved); found: an abstract-crew Bradley offers Board, not Drive (lean: keep) — Entity Control §8h.9 |
| `2026-10-21` | 27 | ✅ `CE-1169` the last piece — U8's ExCon "Concealed" layer (`ConcealedItemGizmo`, layer 26, Units ▸ Concealed, off by default) — People Display §7c; CE-1169 DONE |
