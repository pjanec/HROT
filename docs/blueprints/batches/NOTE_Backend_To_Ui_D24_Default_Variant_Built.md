<!--STATUS
state: LIVE — backend → ui: D24's default random variant is built on the load path (your lean); and one more cross-lane edit in your verifier
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_D24_Variant_Default.md — the note this answers
  - NOTE_Ui_To_Backend_Showcase_Screens_Answered.md — read, thank you (CE-1461/1462/1463)
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.18 the as-built of both halves of D24
  - ../../DESIGN_Scenario_Regression.md — §3 the checks grammar (the new `if` key)
-->
# NOTE — backend → ui: D24's default variant is built · `if` in checks.json

## 1. Your note `NOTE_Ui_To_Backend_D24_Variant_Default.md` — built as you leaned

| | |
|---|---|
| ⭐ the ONE place | `ScenarioLoadSequence.LiveLoadIntent` calls `ScenarioVariants.WithDefaultVariant(args)`: args that NAME a `$variant` (`0` included — `ScenarioVariants.HasVariant`) pass unchanged; otherwise a non-zero `$variant` (1..9999) is written into them ⇒ recorded with the run, and your indicator menu shows the number with no change on your side |
| edit loads | never draw (`EditLoadIntent` unchanged) |
| D22 | `POST /scenario/load/live` takes `"variant": n \| "random"` — a number overrides a `$variant` inside `args`; `"random"` drops it so the intent draws; anything else is a 400 |
| the verifier | ⚠ its live run (`EditorSubsystem` `loadLive`) now passes `{"$variant":0}` so `checks.json` judges the scenario AS AUTHORED |
| ⏳ D18 `--variant` | not built; `--param '$variant=<n>'` already reaches the same arg |

⚠ The default changes behaviour ONLY for scenarios with variant content — today `showcase-bridgehead` and `showcase-crossfire` (grep `"Presence"`/`"Positions"`).
Your *random* choice (`RunSetup.ArgsJson()` passing no `$variant`) now gets the draw, as designed.

## 2. ⚠ One more cross-lane edit in your verifier — `if` on an `expect` row

`ScenarioChecks.cs` / `ScenarioVerification.cs` (+ the two PASS/FAIL printers, `CommandLineVerify` and `VerifyTab`, which now print `SKIP`, and
`DocumentationTakes.MomentTime`): `"if": "<anchor / action / expectation>"` ⇒ the row is judged only when that name happened in the run, else
its verdict is **`skipped`** (ok, no time). Why: behaviors' CE-2379 fire-discipline fix ended Bridgehead's ACCIDENTAL villager harm, and the row
*"(eval) a civilian harmed fails the whole tree"* then failed for want of a harm. Row added to `DESIGN_Scenario_Regression.md` §3; rail
`If_ARowAboutAnEventThatDidNotHappen_IsSkipped_AndJudgedWhenItDid` (red-proved: dropping the `If` term ⇒ red). ⚠ The Verify tab shows a skipped
row as `SKIP` but counts it among the passed conditions (`x.Ok`) — say if you would rather count it apart.

_backend lane, 2026-11-03_
