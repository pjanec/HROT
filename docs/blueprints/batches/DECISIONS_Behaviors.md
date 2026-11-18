<!--STATUS
state: LIVE — decisions the behaviors lane needs (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-18
current-answer: the whole file
-->

# DECISIONS — behaviors

## D-8 · CE-4666 — a shot heard through our own window becomes an ENEMY at our window: SPOT → flank → the line faces away. Where should it stop? *(2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — the ear keeps AQ106 F; the memory slot and the squad pool carry "heard through an opening"; `ComposeSpot` leaves such contacts out of an element's position (where to LOOK, not where the enemy IS); the man's own reactions unchanged. ALSO: a squad turned by `Supplement` is turned back when its flank element goes stale**

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

**DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — two exits inside `SupportByFire`, the platoon's lift unchanged: (a) a man with no rounds left ⇒ the squad's hold-with-return-fire (`CombatPosture`, takes cover); (b) every reported post with no live contact within 25 m for 10 s ⇒ the play stops firing (Hold, PROGRESS `TargetsDown`) and keeps its position. Resupply stays a later capability**

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

**DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — a cornered low-resolve man gives up when a rifle is on him at close range (TS-6 row: resolve < 0.3 + `Covering.By` within ~5 m ⇒ Surrender), without waiting for his stress**

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

**DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — author the ambush's END (the fire team withdraws or is killed by a fixed time); runFor stays 200; DA-100 unchanged**

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

**DECIDED `2026-11-18` (user, via coordinator: "approved"): ① — the two hostiles get a real soldier's SOP (they react to fire); runFor stays 120; the near-miss row must still pass**

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
