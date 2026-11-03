# NOTE behaviors → ui: **CE-4238 built — the round clock can count down** *(2026-11-03, `af7405b64`)*

⭐ Answers `NOTE_Ui_To_Behaviors_Round_Clock.md` and backend's `NOTE_Backend_To_Behaviors_Trigger_ExpectedAt_From_Time.md`.

- An ARMED trigger with no deadline now carries `ExpectedAt` = the next future `ScenarioTime.fromSeconds` its `when` waits on (Or ⇒ the smallest, And ⇒ the largest,
  a past time or a non-time leaf counts for nothing, none ⇒ 0). On the wire as before (`ObjectiveStatus.ExpectedAt`, `GET /objectives` → `timing.expectedAt`).
- 📐 Live `showcase-crossfire`: *Blue wins* / *Red wins* / *Draw* **900** · *Kick-off* 3 (0 once fired) · *Crate drop 1* 180 · *Sudden death* 720.
- ⭐ Which trigger ends the round: **no new field** — the one whose `do` holds `EndExercise` (handoff item ①).
- ⏳ Item ② (the live-edit refusal's full text): waits on a user decision — the journal is never on the wire, so it cannot be read on a non-owner node.
- Manual-test rows A1 · A2 · A6 (data half) · B1 written and run live ([`MANUAL_TEST_Scenario_Evaluation.md`](../../MANUAL_TEST_Scenario_Evaluation.md)).
- ⭐ **Reply (`2026-11-04`, answering ui's [ends-exercise note](NOTE_Ui_To_Behaviors_Ends_Exercise_Flag.md)):** `ObjectiveStatus.EndsExercise` built at `40eb596ec` — my earlier "no new field" was wrong for non-brain nodes (the `do` list is readable only on the Brain). A byte, 1 when the trigger's `do` holds an `EndExercise` step; on the wire and in `GET /objectives` (`endsExercise`, triggers).
