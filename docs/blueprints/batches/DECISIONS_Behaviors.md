<!--STATUS
state: LIVE — decisions the behaviors lane needs (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-19
current-answer: the whole file
-->

# DECISIONS — behaviors

## D-12 · CE-4666 — while the base of fire gets SET, the assault and reserve squads have NO order: they stand idle in the open *(2026-11-19)*

**Measured** (`pc-corridor-fight`, behaviors `88e055e88`, with D-11 and Red's `SearchRadius` 25): every Red and base-of-fire row now passes; the two
red rows are Blue Squads 1 and 3 *"never wholly in the open after 90 s"*. 📐 Probe: at 60 s both squads' task is **nothing** — every man standing
where the platoon's approach left him (x ≈ 236–242, the open corridor), until the support reports Set (~90 s) and `BoundingOverwatch` starts.

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| the support phase orders only the support squad | ✅ PlatoonAttack's `ASupport` (AT-4: the support `SquadDefend`s / `SupportByFire`); the assault's first order is AT-5's approach, after support Set | ⛔ searched Defend & Attack §8f (AT-1…AT-13) and Assault Under Fire §9: no row says what the assault and reserve do while they wait |
| a waiting element takes cover | ✅ `SquadDefend` already posts a squad on cover round a point (`DefencePlan`) | ✅ doctrine (ATP 3-21.8): the assault element waits in a COVERED attack position while the support element sets |

**My lean: ① in `ASupport`, the assault and reserve squads each `SquadDefend` where they stand** (their centre, `SearchRadius` 15, facing the
objective) — the same order consolidation uses (AT-8), so each man takes a covered post; AT-5's approach replaces it at support Set (the
one order writer, AT-12). Blast radius: every `PlatoonAttack` (its goldens move: the waiting squads now take cover instead of standing).
- ② a fixed `TakeCover` per man: no shared sector, the men pick cover independently — a second way to post a squad (ruling 9).
- ③ leave it and relax the check's window (*"after 120 s"*): hides the idle wait the check exists to catch.

## D-11 · CE-4666 — the street fight (`pc-corridor-fight`): Red's line breaks before it reaches its cover; Blue's base of fire bunches on one point. Two calls *(2026-11-19)*

**🛠 BUILT `2026-11-19` (behaviors): A — `StressModel.HeadingToPost` defers the break while a superior's `CombatPosture` move carries him to his post (stalled < 0.3 m/s or arrived ⇒ he breaks), `PostRetreat` first; B — `SupportByFire` phase 0 posts its men with `DefencePlan` over a `SupportCrew` facing the reported posts (`IDefenceCrew.Facing`). Rails red-proved; scenarios being measured.** **DECIDED `2026-11-19` (user, via coordinator: "approved"): A ① — a man MOVING TO HIS ORDERED POST takes near misses as "go to cover" (his post is his cover); the break waits until he is there or his move has stalled; a broken man's retreat point is first his own post when it is behind cover · B ① — `SupportByFire`'s phase 0 places its men with the squad's own post planner (`SquadDefend`'s `DefencePlan`: a line across the line of fire at the support point, a cover search per post, a sector each) and fires from the posts. Defence and attack goldens move (a real behaviour change) — report the diff shape; `pc-corridor-fight` is the judge.**

**Measured (terrain's `pc-corridor-fight`, CE-4672, stepped; reproduces terrain's verdicts):**
1. Red's posts ON the corridor's centreline with `SearchRadius` 10 find no cover (terrain: the nearest is 13.5–16 m). With 25 the defence plan DOES post the men at the corridor's edge, on cover: (399, 185) · (385, 185) · (404, 181).
2. **They never get there:** Blue opens fire at **5.4 s from ~160 m**; Red Squad 1's men BREAK (TS-6) at **11.45 · 13.92 · 14.5 s** and lie prone where they are — 4–8 m short of their posts, in the open grass — the whole first minute. ⇒ the line rows stay red with 25 too.
3. **Blue Squad 2 is the base of fire:** `SupportByFire` phase 0 holds EVERY man at the ONE support point (`SquadManeuverRunner.Hold(…, p.Position)` — Assault Under Fire §9.1 as built: *"hold at the point (CombatPosture)"*) ⇒ bunched by construction (the 2 m row red at 30 s), and in the open where the point is.

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| a man's stress rises per near miss regardless of the range it was fired from | ✅ `StressSystem` (stimulus × reach × nerve); the break line = effective nerve, default obedience 0.8 pulls it to ≤ ~0.8 (D-4's finding) | ⚠ Temperament & Stress TS-2/TS-6 set the model; nothing names how fast a line should break at 160 m |
| a broken man lies where he broke unless a retreat point is found | ✅ `StressSystem` RetreatMetres 30 / gain 3 m (CE-4666 noted it "finds nothing or does not run", unmeasured) | ⛔ searched: no design says a man moving to his ordered post under fire should break before reaching it |
| the base of fire's men share one point | ✅ `SupportByFireBehavior.cs` phase 0 | ✅ the handoff: *"the base of fire takes a covered position on the line of fire … prone / crouched, spread"*; the squad's post planner (`SquadDefend` / `DefencePlan`: per-man posts with a cover search and a sector) exists |

**A — the line breaks before its cover. My lean: ① a man MOVING TO HIS ORDERED POST takes the near misses as "go to cover" (his post IS his cover) — the break waits until he is there or has stopped trying (a stalled move), and a broken man's retreat point is first his own post when it is behind cover.** Cost: every defence under fire on its way in breaks later ⇒ defence goldens move. 
- ② slow the stimulus with range (a near miss from 160 m frightens less than one from 20 m): physically right, but a model-wide calibration change — every stress scenario moves.
- ③ keep it (a line caught in the open under fire does break): then the scenario's posts belong on the cover from the start (author `SearchRadius` 25 and start the men there) — tests the fight, not the deployment.

**B — the base of fire bunches. My lean: ① `SupportByFire`'s phase 0 places its men with the squad's own post planner (`SquadDefend`'s `DefencePlan`: a line across the line of fire at the support point, each post with a cover search, a sector on the posts) — then fires from the posts.** Reuse, not a new planner (shared and unified). Cost: every attack's base of fire spreads and takes cover ⇒ attack goldens move.
- ② spread only (fixed intervals across the line of fire, no cover search): smaller, but still in the open on a corridor.
- ③ per-man cover pick around the one point (CombatPosture's own search): no line; men may cluster on the one cover nearest the point.

⛔ Not built. Each half independent.

## D-10 · CE-4670 — who decides a crewman's head-out and a vehicle's light discipline? (backend built the asks; the deciding is ours) *(2026-11-19)*

**✅ BUILT `2026-11-19` (behaviors, `4df705ac1`): A — `BasicVehicleSop`'s side step `VehicleCrewNodes.HatchByThreat` (commander OUT while calm, IN on a hit / near miss / first threat / new contact within 30 s, one ask per change); B — the standing discipline is the vehicle's authored `VehicleLights.Discipline` (⚠ an SOP takes no params, CE-2132), `MoveOrder` / `ConvoyOrder` gain `Lights` (`OrderLights`), `VehicleCrewNodes.LightsByOrder` asks for it while the order runs and the standing one after. Rails red-proved; `va-crew-switches` 7/7, `va-lights-night` 4/4, `uc-convoy-hit` 10/10. Vehicle Appearance §6a/§6b.** **DECIDED `2026-11-19` (user, via coordinator: "approved"): A ① the vehicle's SOP decides head-out (out while calm; in on the contact / hit / under-fire rows that start VehicleFightFromCover; out again after ~30 s quiet; one ask per change, an event) · B ① an SOP parameter `LightDiscipline` (default Normal — no golden moves), set per side / unit by the scenario; an order carrying one overrides it while it runs**

**Built (backend, `NOTE_Backend_To_Behaviors_Vehicle_HeadOut_And_Light_Discipline.md`):** `VehicleRequests.HeadOut(world, vehicle, man, out)` (the boarding arbiter opens his seat's hatches, `CrewExposed`) and `VehicleRequests.SwitchLights(world, vehicle, discipline)` (`LightRuleSystem` lights by discipline × darkness × engine). Both are EVENTS — safe from the parallel brain phase. A scenario trigger can already ask (`va-crew-switches`). ⏳ Nothing in a brain asks today: hatches open only on boarding; every vehicle's discipline is whatever the scenario authored (default Normal).

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| the vehicle's reaction table already has the contact / hit / under-fire rows | ✅ `BasicVehicleSop.cs:85–98` (Hit, Contact → `VehicleFightFromCover`) | ✅ Vehicle Exposure Cycle §8 |
| the deciding is the crew's | — | ✅ Vehicle Appearance VA-3 (*"written by the vehicle's BRAIN (the crew decides)"*), VA-4 (*"a crew member's head out (observing, an order, the SOP) is a request"*) |
| no design names WHEN | ⛔ searched `docs/` + graph (`buttoned`, `head out`, `hatch`, `light discipline`, `blackout`): only this design and its notes | — |

**A — head-out. My lean: ① the vehicle's SOP decides, from what it already knows:** the commander is HEAD OUT while the vehicle is calm (no threat in its memory, not under fire), and goes IN — buttoned up — on the same rows that start `VehicleFightFromCover` (contact / hit / under fire); out again after a quiet spell (e.g. 30 s, like the stress calm). One ask per change, as an event (no cross-entity write). Cost: every crewed vehicle in every scenario shows an open commander's hatch while calm ⇒ goldens move (the opening state); a hit model that reads `CrewExposed` would make the head-out commander vulnerable — that IS the point of the trade, and today no hit model reads it.
- ② only an order / trigger asks (as now): nothing changes, but a convoy in a quiet zone looks buttoned up for ever.
- ③ an observation behaviour of its own (an OP task): too much for the default; can come later on top of ①.

**B — light discipline. My lean: ① a vehicle SOP parameter `LightDiscipline` (default Normal ⇒ no golden moves), set per side / unit by the scenario, and an ORDER that carries one overrides it** (a convoy order at night: Convoy; a covert move: Blackout). The order's executor calls `VehicleRequests.SwitchLights` when it starts and restores the SOP's on its end.
- ② the ROE carries it (`Roe` gains a field): the ROE is a wire component every node reads — too wide for a lights switch.
- ③ automatic Blackout at night for every military vehicle: realistic for a combat zone, wrong for a road march; moves every night golden.

⛔ Not built. Each half is independent — approve one, both, or name another.

## D-9 · CE-2451 — an insurgent fires his RPG at a MAN: one rocket into the stack ends `ts-house-defenders`' clearance. Should the weapon brain keep the RPG for vehicles? *(2026-11-19)*

**✅ BUILT `2026-11-19` (behaviors, `c8d3f1f7b`): `WeaponSelectionDecision` gains the Step consideration `MountSuitsTarget` — 0 for an anti-armour mount (ATGM / shaped charge) against a person or animal while another loaded mount can hurt him. Rail red-proved; `ua-weapon-choice` 4/4, `ua-ifv-standoff` 6/6. Re-measured: `ts-house-defenders` — no rocket into the stack any more, but 3/6: the Waverer still never breaks and the Fanatic now BREAKS at 77.75 s (the squad lives long enough to fire on him); `bc-house-defended` 3/4 ("both insurgents are down" still red) — the next layer, being measured. `ua-rpg-window` 3/5 is NOT D-9 (its targets are vehicles; WindowDuel retirement, CE-2366).** **DECIDED `2026-11-19` (user, via coordinator: "approved"): ① make R-3 true: an anti-armour mount (shaped charge / ATGM / RPG) scores 0 against an UNARMOURED target while another mount with rounds can hurt him; his only loaded mount may still fire; a rail (rifle at a 13 m soldier, RPG at a tank, RPG with an empty rifle); re-measure `ts-house-defenders` and `bc-house-defended`**

**Measured (`ts-house-defenders`, editor, live probe, `/combat/detonations` + `/combat/shots`):**
1. The six-man squad stacks at the front door (~39, 105–109).
2. At ~9.1 s the Coward (Insurgent, TKB 2003: RPG-7 + rifle), in room A ~13 m away, fires his **RPG-7** (ammo 6009, shaped charge 0.73 kg) at Rifleman 1.
3. It bursts 0.23 m from Rifleman 1. Riflemen 1 and 2 die; Rifleman 3 and the Leader take 40–55 blast damage, Rifleman 4 is hurt too. Only ONE rifle round was fired before it.
4. Riflemen 3, 4 and 5 die by 36–67 s. The Leader (hp 59) walks away north.
5. ⇒ nobody reaches closet E: the Waverer's two rows (breaks, surrenders) can never pass. *(My earlier "stall 9.5 m short" note was this, mis-read.)* The same insurgent type defends `bc-house-defended` (CE-2451) — likely the same root, not yet measured.

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| the brain picks the mount by a utility product (effectiveness × rounds-left × range) | ✅ `WeaponChoice.Choose` → `WeaponSelectionDecision` (`StarterPack/WeaponSelectionDecision.cs`) | ✅ Utility AI Demo §360: *"the 25 mm wins on infantry, the TOW on a tank"* |
| nothing rules an AT round out against a man; vs a 100-hp man the RPG's effectiveness is 1 (one hit kills), so only rounds-left (ln 4 / ln 301 ≈ 0.24 vs the rifle's ≈ 0.84) holds it back ⇒ a near tie that tips with stance or hit chance | ✅ `StandardInputs.WeaponEffectivenessVsTarget` / `RoundsLeft` (`StandardInputs.cs:421, 480`); ⚠ the exact scores not printed | — |
| the RPG is meant for vehicles | — | ✅ `UrbanCombatTkbCatalog` M5: *"and a rifle, for men (the RPG for vehicles: the weapon brain picks)"*; ✅ Aggregate Squad **R-3**: *"`WeaponChoice` already scores it 0 against men"* — ⛔ that premise is FALSE live, and no rail pins it |
| 2003's mounts did not change recently | ✅ CE-4648 (`032465362`, 2026-11-17) moved them into one template, unchanged | — |

**My lean: ① — make R-3 true: an anti-armour mount (shaped charge / ATGM / RPG round) scores 0 against an UNARMOURED target (a person) while another mount with rounds can hurt him.**
- If it is his only loaded mount, he may still fire it (he does not stand idle).
- Built as one weapon-selection consideration (or a factor inside the effectiveness input), with a rail: an RPG + rifle man at a 13 m soldier picks the rifle, at a tank picks the RPG, and with an empty rifle picks the RPG.
- Cost: goldens move wherever RPG men face infantry (urban battle, ambushes). `ts-house-defenders` and `bc-house-defended` re-measured after.
- ② raise `RoundsLeft`'s weight: still a soft tie, and it moves every other weapon choice (the 25 mm vs the TOW).
- ③ keep it ("insurgents do fire RPGs at troops"): then R-3 and M5 are wrong and the two clearance scenarios need a rifle-only cast (type 2010). Realistic for a group in the open; not for one man at 13 m indoors (arming distance, back-blast — not modelled).

⛔ Not built. Meanwhile `ts-house-defenders` stays red (CE-3462 row).

## D-8 · CE-4666 — a shot heard through our own window becomes an ENEMY at our window: SPOT → flank → the line faces away. Where should it stop? *(2026-11-18)*

**✅ BUILT `2026-11-19` (behaviors): both halves — the via mark ear → event/wire → memory (`Anonymous` 2) → pool (`HeardThroughFlag`) → `ComposeSpot` skips it (Sensing §8.6c), and a turned squad turns back when its flank goes stale (DA-60 as built). `pc-defend-house`: Red's line opens fire at 32.9 s stepped (was 143 s).** **DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — the ear keeps AQ106 F; the memory slot and the squad pool carry "heard through an opening"; `ComposeSpot` leaves such contacts out of an element's position (where to LOOK, not where the enemy IS); the man's own reactions unchanged. ALSO: a squad turned by `Supplement` is turned back when its flank element goes stale**

**Measured (`pc-defend-house`, editor, live probes):**
1. Blue fires from ~125 m south.
2. Each Red man remembers an ANONYMOUS SmallArms slot at his own window or post. This is AQ106 F as designed: a sound is heard from the last opening on its path (`Architect_Question_106_Sound_Paths.md` F).
3. `SquadContacts.IsEnemy` counts every anonymous contact as an enemy, so the reserve squad SPOTs an Individual at its own post: (104.95, 205.65), at 7.24 s.
4. In the platoon's picture that point lies at ~342°, outside the authored sector of 135°→225°, within `FlankMetres` 150. So `FlankTarget` fires `Supplement` (DA-60).
5. Line Squad 2 is turned to 308°→358°, and later Squad 1 to 297°→27°. Their `SquadDefend` re-plans its window posts facing NORTH.
6. Blue is all south: the line holds fire from ~45 s to 113 s live and ~143 s stepped. At 60 s the picture also holds a "Firing" Individual inside Red's own house, at (117.95, 155.98).

⛔ No design says what a heard-through-an-opening point MEANS to the chain. AQ106 F rules only on what the ear perceives (searched `docs/` for heard from / last opening / SPOT anonymous).

**My lean: ① — the ear keeps AQ106 F; the CHAIN learns the difference.** The memory slot gets a bit, "heard through an opening" (the ear knows the sound came in through that window). Then:
- (a) the squad pool carries the bit;
- (b) `ComposeSpot` leaves such contacts out of an element's position. They say where to LOOK, not where the enemy IS. A squad hearing only that reports nothing new;
- (c) the man's own reactions are unchanged: look, take cover, a hunter's last-known point.

Cost: one memory flag + one pool flag + one SPOT filter, with rails. `pc-defend-house` should open fire south much earlier; goldens of defence scenarios with an all-round reserve move.
- ② place the heard estimate FURTHER OUT, along the path's last leg (opening + direction × a guess). That bends AQ106 F, misleads a hunter in a house, and the guess is arbitrary.
- ③ `FlankTarget` ignores an element within the platoon's own position radius. Smallest, but it leaves the SPOT wrong for every other reader (fire support, HQ), and the turned squads still see a phantom.
- Also, either way: a squad turned by `Supplement` is never turned back when the flank element goes stale (`TriggerFreshSeconds` 30 only stops a NEW turn). Fix that with ①, or leave it?

⛔ Not built.

## D-7 · CE-4667 — `SupportByFire` has no exit for an empty weapon or dead targets: what should the base of fire do then? *(2026-11-18)*

**✅ BUILT `2026-11-19` (behaviors): both exits in `SupportByFire.Run` — (a) an empty man holds where he is; (b) every post's KNOWN men down for 10 s ⇒ hold + PROGRESS `TargetsDown`, resumes if one gets up; ⚠ a post with nobody known near it keeps the blind fire (AU-1's premise). Assault Under Fire §9.1a.** **DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — two exits inside `SupportByFire`, the platoon's lift unchanged: (a) a man with no rounds left ⇒ the squad's hold-with-return-fire (`CombatPosture`, takes cover); (b) every reported post with no live contact within 25 m for 10 s ⇒ the play stops firing (Hold, PROGRESS `TargetsDown`) and keeps its position. Resupply stays a later capability**

**Measured / read:** the design gives the base of fire ONE exit — the platoon LIFTS it when the assault holds a foothold (DA-11, `DESIGN_Assault_Under_Fire.md`
§9.1); `Suppress` runs "until the order is taken away" (Squad Tactics §7.5) and already stops firing at a dead target (it holds). ⛔ No design names an exit for
"out of ammo" or "every reported post is dead"; no resupply exists (searched `docs/`: resupply / out of ammo — none for infantry). The live report (real-1):
Blue Squad 2 kept `Suppress` 434 s with the weapon "out of ammo" and every contact at hp 0 — the assault never reached its foothold, so the lift never came.

**My lean: ① — two exits inside `SupportByFire`, the platoon's lift unchanged:** (a) a man with NO rounds left (`StaffComposerSystem.RoundsOf` = 0) is
re-ordered from `Suppress` to the squad's hold-with-return-fire (`CombatPosture` at his position: he takes cover, may pick up a weapon later); (b) every
reported post with no live contact within 25 m for 10 s ⇒ the play stops firing (Hold, PROGRESS `TargetsDown` to the platoon) and keeps its position —
the platoon still decides what comes next. Cost: two rules + rails; touches every SupportByFire run (`pc-*`, `cc-*` goldens move).
- ② only (a) — an empty man stops; dead targets keep everyone holding in `Suppress` (already not firing) — smallest change, but the squad never says "done".
- ③ resupply (a new capability: an ammo bearer / a vehicle's stowage) — not built anywhere; a design of its own.

⛔ Not built.

## D-6 · CE-3462 — `ts-house-defenders`' Waverer is killed before he can break: how should a low-resolve man surrender in a room clearance? *(2026-11-18)*

**✅ BUILT `2026-11-19` (behaviors, `0946a2e37`): the TS-6 cornered row (resolve < 0.3 + an enemy rifle within 5 m ⇒ Surrender, calm or not), rail + mutant. ⚠ `ts-house-defenders` still red for ANOTHER reason, measured: the clearing squad stalls ~9.5 m short of closet E (at ease from ~40 s, the Waverer untouched at hp 100) — the clearance, not the surrender (CE-3462 row).** **DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — a cornered low-resolve man gives up when a rifle is on him at close range (TS-6 row: resolve < 0.3 + `Covering.By` within ~5 m ⇒ Surrender), without waiting for his stress**

**Measured** (editor, after CE-3462's re-author — the Coward now breaks at 30.75 s): the Waverer (resolve 0.2, the type's nerve) in closet E peaks at
stress **0.11** and is killed when the squad enters (Health 0 by ~120 s). TS-6 lets a man surrender only as the OUTCOME of a break (effective resolve
< 0.3 and a rifle on him), and a CQB entry is too fast for the near-miss stress to reach his break line first.

**My lean: ① — a cornered low-resolve man gives up when a rifle is on him at close range, without waiting for his stress** (TS-6 gains a row: resolve
< 0.3 + `Covering.By` within ~5 m ⇒ Surrender) — it is what the scenario's Judge describes (*"once broken with a rifle on him"* is the stress path; the
room entry is the cornered path). Cost: a StressSystem rule + a rail; touches every low-resolve man in a clearance.
- ② re-author: give the Waverer low nerve too (e.g. 0.2), so the entry's fire breaks him before he is shot — keeps the model, changes the character.
- ③ loosen the row: drop the Waverer's rows — loses the demo's point.

⛔ Not built.

## D-5 · CE-9023 — `uc-urban-battle` "the convoy moves on": the platoon holds while the ambush lasts — what should the row wait for? *(2026-11-18)*

**✅ BUILT `2026-11-19` (behaviors): a scenario Trigger at 100 s orders the fire team to withdraw north out of the street (`AdvanceTo` (720, 330)); `uc-urban-battle` 20/20 — the convoy moves on at 194.75 s (runFor 200). ⚠ close to the end: a later withdrawal or a longer remount would miss it.** **DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — author the ambush's END (the fire team withdraws or is killed by a fixed time); runFor stays 200; DA-100 unchanged**

**Measured** (editor, after the boarding fix `b7a088964`): one defect found and FIXED — a KILLED man (body, Health 0) held a squad's boarding back:
Squad A's leader boarded at 79 s, yet SquadEmbark failed on its 60 s timeout and the remount waited its 120 s (the move-on at 195 s). ⚠ What remains is
by design: the platoon SOP's contact row (`PlatoonHold`) pauses `PlatoonConvoy` while an enemy element (team+) is reported within 400 m in the last
30 s — and DA-100 (§11a) approved exactly that and REJECTED a "not convoying" exemption. Two runs of the SAME scenario diverge: the regression run
remounted at 147 s; a live run held from 14.9 s and was still holding at 250 s, the ambush still reported (the editor run does not repeat — cf. CE-2438).
So *when* the convoy moves on is how long the ambush lives, not the convoy play.

**My lean: ① — the row waits for the AMBUSH TO END, not a clock:** author the fire team's end (e.g. it withdraws or is killed by a fixed time — a
`do` that hurts it, as other scenarios do) and keep runFor 200; the row then tests the move-on, which is what it is for.
- ② runFor 400+: the scenario is already ~20 min wall; and it may still not end (250 s measured, still in contact).
- ③ revisit DA-100 (a convoy ignores the platoon hold): reverses an approved decision.

⛔ Not changed. The boarding fix is in (rail `SquadManeuverTests.CE9023_*`).

## D-4 · `uf-squad-under-fire` row (4) "the bounds complete" — the squad now needs ~300 s: change the hostiles, or the clock? *(2026-11-18)*

**✅ BUILT `2026-11-19` (behaviors): both hostiles run `BasicInfantrySop` with an authored temperament `nerve 0.97, resolve 0.9, obedience 0` (a break line of 0.97 — the effective nerve is `v + obedience × (0.75 − v)`, so with the default obedience 0.8 ANY authored nerve breaks at ≤ 0.8). `uf-squad-under-fire` 5/5: rounds near the squad 4.5 s · a mover drops 3.5 s · crouched under fire 4.5 s · covered 4.5 s · the bounds complete 78 s (runFor 120); golden re-captured. 📐 measured on the way: a soldier's SOP alone (default temperament) ⇒ both break by 3.5–5 s under the squad's fire and only one round reaches a rifleman (rows 1/3 red); nerve 1 + obedience 0 (never breaks) ⇒ rows 1–4 pass but the bounds miss 120 s — so the hostiles' break time IS what sets this scenario. ⚠ the margin: rows 1/3 at 4.5 s vs a break at ~5–7 s; a change to stress gain moves it.** **DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — the two hostiles get a real soldier's SOP (they react to fire); runFor stays 120; the near-miss row must still pass**

**Measured** (live, after CE-4652's fixes): the two InertSop hostiles now lie down and fire the whole run (Hostile 2: 103 rounds by 80 s,
empty at 200 s — the old passing golden's ~100). Every near miss drops a mover (row 2 now PASSES), so the squad closes ~45 m by 120 s, ~60 m by
200 s, and a rifleman reaches the objective area (25 m) only at ~300 s — after both hostiles have emptied their weapons. The old pass (2026-10-22)
came from hostiles that BROKE and fell silent; an InertSop man no longer breaks (CE-4652 ①, by design: InertSop = no reactions). And as InertSop
they ignore suppression, so the squad's covering half cannot quiet them — the bound never gets the cover it waits for.

**My lean: ① — give the two hostiles a real soldier's SOP (they react to fire: duck, suppressed while the covering half fires)** — that is what
fire and movement is tested against; runFor stays 120. Cost: two entities re-authored, the golden re-captured; the near-miss row must still pass.
- ② runFor 300: passes, but only because the hostiles run dry — it tests their ammunition, not the bounds.
- ③ fewer rounds per hostile (a fire schedule): the same problem with a shorter wait.

⛔ Not changed. CE-4652's own row (a near-missed mover drops) passes at 5.75 s.

## D-3 · CE-4654 — an AP mine now KILLS the man on it; should it maim him instead (alive, legs gone)? *(2026-11-18)*

**✅ DONE `2026-11-18` (behaviors): `AreaEffectSystem.Maim` — the man on an AP mine left at 0.75 of his incapacitation line, the damage on his shins; `mf-strike-and-halt` 8 / 8.** **DECIDED `2026-11-18` (user, via coordinator): ① — the trigger man is MAIMED, not killed: the burst's share on him goes to the Leg zone so he lands incapacitated, alive, and the casualty drill carries him. Terrain's area effect gets the trigger-man rule (cross-lane edit allowed, say it); goldens move.**

**Measured** (`mf-strike-and-halt` live after terrain's CE-2446): the burst leaves the struck man at Health 0 — dead, no move, no fire; the three
others untouched. The user's complaint (no health, no mobility lost) is answered. But MF-5 says *"the casualty cared for in place"* and the
handoff's lean says *"wounds the LEGS … the squad's casualty drill then runs"* — a dead man gets no drill.

**My lean: ① — a mine's burst on the man who TRIGGERED it is a leg wound, not a whole-body blast:** the damage goes to the Leg zone
(`AreaEffectSystem` already publishes one event per part struck, S3-4; the trigger man gets his share on the legs) so he lands INCAPACITATED (under
30 %, alive, bleeding — the existing casualty care carries him), not dead. Cost: terrain's area effect (a "trigger man" rule) + a rail; every
mine scenario's casualty becomes a live man (the goldens move).
- ② leave it (dead): nothing to build; the drill never cares for anyone after a mine.
- ③ lower the AP blast value: blunt — it also weakens the burst on everyone near.

⛔ Not built. The behaviors part of CE-4654 (verify, pin) is done.

## D-2 · CE-2442 — who answers an `Engineers` request, and with what? *(2026-11-18)*

**✅ DONE `2026-11-18` (behaviors): DESIGN_Minefields §13 (MS-B5) built; demo `mf-engineers-answer` 7 / 7.** **DECIDED `2026-11-18` (user, via coordinator): ① — `SupportAsset.Engineers` (a free unit with a TKB 116 clearer or an engineer squad); the HQ answers with `ReconMinefield` at the request's point, then the clearer's `Breach` along the requester's route once the field is marked. Add a demo scenario with checks.**

**The question:** `MinefieldAhead` halts a squad and queues `RequestKind.Engineers` up the chain (MS-B4, approved), but no HQ answers it:
`SupportCommandBehavior.Command` answers only `FireSupport` and `Reinforce` (`SupportCommandBehavior.cs:90`, `:131`) and there is no engineer
asset kind (`SupportAsset`). `DESIGN_Minefields.md` shows the engineers recon and the clearer cut a lane, but not WHO orders them — a design gap,
not a defect.

**My lean** (the row's own, CE-2442): `SupportAsset.Engineers` = a free unit whose roster holds a TKB 116 clearer or an engineer squad; the answer is
`ReconMinefield` at the request's point (the field resolved from the side's knowledge there, `MinefieldKnowledgeOps`), then — once the field is
marked — the clearer's `Breach` along the requester's route. The request today carries only a point; the field's id and the far side are derived,
not added to the request.

| option | costs |
|---|---|
| ① the lean | one asset kind, one answer branch, the field lookup; a demo (`mf-*` + an HQ) to prove it |
| ② the scenario orders the engineers (today) | nothing to build; the squad waits for an order no one gives unless authored |
| ③ the squad itself asks its own attached engineers (no HQ) | a different chain; skips the request queue every other ask uses |

⛔ Not built (a new capability; the design does not say who orders them).

## D-1 · `ts-fire-discipline` (CE-3461 follow-up): the REGULARS break under the scenario's 40 over-head rounds — re-tune what? *(2026-11-18)*

**✅ DONE `2026-11-18` (behaviors): 22 rounds at 1.3 s; `ts-fire-discipline` 7 / 7.** **DECIDED `2026-11-18` (user, via coordinator): ③ — keep the stress model (R-2076); change `ts-fire-discipline`'s fire to what the Judge means (e.g. 25 rounds or one every 2 s) and edit its description + checks. The militia must still break.**

**The question:** the scenario (and `DESIGN_Temperament_And_Stress.md` §6) says *no regular breaks* under 40 rounds passing over a team every 1.3 s from
120 m. Measured now, the two CENTRE regulars break at ~29–33 s. Which number moves: the near-miss stimulus, the calming, or the scenario's fire?

| measured (live, editor, `2026-11-18`, after CE-4652's two fixes) | value |
|---|---|
| per round, each regular inside the 3 m band | +0.066 (= `NearMiss` 0.08 × his reach ≈ 0.83) |
| calm | 0.03 / s; ×2 near cover ⇒ the two FLANK men (near a cover point) peak ~0.40, the two CENTRE men rise +0.021 / s net |
| the centre regulars' break level | 0.70 (R-2076: trained nerve) ⇒ crossed at ~33 s; the gunner fires for 52 s |
| R-2076's record (`2026-10-24`) | regulars peaked 0.51–0.57 — ⚠ with the near-miss DOUBLE-COUNT present (fixed today) and **the regulars firing 0 rounds back** (cause never found); today they fire back. ⇒ the old peaks are not comparable |

**My lean: ③ — leave the model, change the scenario's fire to what the Judge means** (e.g. 25 rounds, or one every 2 s): a regular rising
under SUSTAINED fire that passes within 3 m of him for 50 s and breaking near its end is plausible, and the model's numbers were approved
(R-2076, §4). Cost: the scenario's description and checks edited; the militia must still break (they do at 0.43, by ~5 s).
- ① halve the near-miss stimulus for a round that passes ABOVE head height (the band is 2-D today) — models "over the heads" better; touches every
  stressed man in every scenario (all goldens move).
- ② raise the calm — weakens every suppression scenario (`uf-*`, `rf-*`).

⛔ Not built. The militia half of CE-3461 is fixed and closed; this is only the regulars' row (1) and row (3) (a regular still broken at the end).
