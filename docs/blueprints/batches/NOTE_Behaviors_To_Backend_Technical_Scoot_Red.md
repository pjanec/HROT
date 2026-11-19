<!--STATUS
state: LIVE — for backend (a red that came with your merge) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6e (VA-S5) is one of the candidate commits
-->
# NOTE behaviors → backend: `ua-technical-scoot` went red with the backend merge — bisected to your range (`2026-11-19`)

| commit (stepped editor, `scripts/scenario-regression.sh ua-technical-scoot`) | "the ATGM technical fires" |
|---|---|
| `2068f9f32` (behaviors, before D-9) | ✅ at **110.3 s** (of runFor 120 — already late) |
| `07049e64c` (behaviors, D-9 + D-10, before merging backend) | ✅ at 110.3 s |
| `5b5358928` (= `07049e64c` + merge of `origin/backend` at `640daf162`; ⛔ no pop smoke yet) | ❌ never (the other 3 rows pass) |
| `fba750f00` (+ behaviors' pop smoke) | ❌ never |

⇒ the change is in `07049e64c..5b5358928` — your commits `2627cc743` (VA-S4) · `921522b7e` (VA-S6) · `1daca1a98` (CE-4674 re-plan) · `640daf162`
(VA-S5 dischargers: a new last mount on the Bradley, `IsSelfProtection` in the armed / weapon-choice readers) and the golden re-captures.
📐 The golden's FIRST difference is at **t = 10 s**, before any shot: the two technicals' positions differ by 1–2 m (`ATGM Technical` golden
[293.4, 641.2] vs run [294.4, 643.4]) ⇒ a MOVEMENT change, before combat — `1daca1a98` is my first suspect, untested. ⚠ The pass at base was
marginal (110 s of 120): the row may only need a longer `runFor` once the cause is known. Not bisected further — your range.
