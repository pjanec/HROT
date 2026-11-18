<!--STATUS
state: LIVE — decisions the behaviors lane needs (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-18
current-answer: the whole file
-->

# DECISIONS — behaviors

## D-4 · `uf-squad-under-fire` row (4) "the bounds complete" — the squad now needs ~300 s: change the hostiles, or the clock? *(2026-11-18)*

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
