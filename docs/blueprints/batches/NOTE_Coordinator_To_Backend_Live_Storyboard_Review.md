<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §0 R2″, D30–D33 (approved by the user: "Looks good!")
  - ../../UX/DESIGN_Scenario_Lifecycle_Review.md — LC-15 … LC-18 (R-4166), which R2″ agrees with
-->

# NOTE — coordinator → backend: the live-session storyboard, reviewed — two fixes before it is built

✅ **It agrees with the coordinator's rulings**: ■ asks *End the exercise? / Keep running* (R-4166 Q11) and R2″ settles where it lands (Live · ended,
not a restart) — the conflict the coordinator raised with R2′ is gone. The end opens the results (LC-15 / LC-18, built as L-2), the Player gets its
own summary (D32 = LC-18), Recent ▸ AAR = the latest run (LC-19), *Edit* only on an author seat (R-4164).

| # | fix | why |
|---|---|---|
| **1** | ⭐ **draw — or mark *unchanged* — two built run controls the storyboard omits:** the **⏪ Review** button beside ▶ in a running Live (LC-16, R-4166, built by behaviors as L-5) and the mode menu's **Run to …** in Edit / Preview (LC-10, built by behaviors as W-4) | ⛔ R-4151: a mockup specifies ONLY what it shows, and this one's scope NAMES the run controls — a builder matching panels ④ / ② would remove them. Either add them (④: ⏪ beside ⏸ ■; ②: *Run to …* in the ▾) or caption them *"unchanged, not drawn"* |
| **2** | **CE-3413 must change behaviors' L-3 as built**: today ■ → *End the exercise?* → the end → **leaves Live** (`IExerciseEnder` on `ClusterTimeTransportAdapter.Ender`, behaviors' status row L-3). R2″ keeps it IN Live · ended (⏏ Close the session is the second step) | the as-built and the design would otherwise disagree; name the change in CE-3413's row and fold it into behaviors' L-3 as-built note when built |

FYI: **CE-3419 (the Replay Browser as an editor perspective)** touches the two still-open Replay Browser questions (Q-B3 default layout, Q-B6
Federation — [Replay Browser UX](../../UX/DESIGN_Replay_Browser_UX.md)); whoever designs it should take them with it.

---
_coordinator lane, 2026-11-08_
