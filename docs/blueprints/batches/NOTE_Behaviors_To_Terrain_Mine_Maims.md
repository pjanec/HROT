<!--STATUS
state: LIVE — FYI, a cross-lane edit in the area effect (D-3)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MF-5 (the casualty cared for in place) and the CE-4654 / D-3 row
-->

# NOTE — behaviors → terrain: an AP mine now MAIMS the man on it (D-3, user `2026-11-18`) — a cross-lane edit in `AreaEffectSystem`

| what | why | your side |
|---|---|---|
| ⭐ `AreaEffectSystem.Assess`: for a burst of munition **6051** (`Minefields.MunitionAntiPersonnel`) the man standing on the mine — `TriggerManOf`: the nearest living PERSON within **1.5 m** of the burst (2-D, lower index on a tie) — is treated by `Maim`, not the blast / fragment law: his damage lands on **ShinL + ShinR** (half each, `HasBodyPart`), leaving him at **0.75 of his incapacitation line** (no thresholds: of 30 %) — out of the fight, alive, bleeding; the squad's casualty care carries him | D-3 ① (user, via coordinator): *"the trigger man is MAIMED, not killed"*. 📐 Since your CE-2446 the whole-body blast killed him outright (`mf-strike-and-halt`: Health 0), so MF-5's casualty drill had no casualty | ⭐ nothing to change: `MineTriggerSystem` is untouched (the burst still has no `Target` for an AP mine); everyone else near the burst gets the ordinary blast and fragments |
| AT mines (6052), the line charge (6053), every other warhead | unchanged | — |
| rail | `AreaEffectSystemTests.CE4654_AnApMine_MaimsTheManOnIt_HisLegs_Alive_AndTheManNearbyGetsTheOrdinaryBlast` (red with the rule removed); suite 34 / 0 | — |
| scenarios after it | `mf-strike-and-halt` 8 / 8 (new row (2c): nobody killed by the mine within 30 s) · `mf-at-vs-vehicle` · `mf-recon-and-mark` · `mf-way-round` pass | — |

Commit `3418dd0c8` (behaviors). Folded into DESIGN_Minefields (the CE-4654 / D-3 row).
