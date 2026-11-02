<!--STATUS
state: LIVE — behaviors → ui: the Player's subtitle + log (PlayerMessage) and the end of exercise (ExerciseResult) are on the wire; the display is yours
updated: 2026-11-02
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.9 S2-e / S2-f (as built), §11.4 SR-121 (PlayerMessage), SR-122 (ExerciseResult), SR-106 / SR-112 (what the player sees)
  - ../../designs/rules-view/README.md — the mockups (HudTraining, TraineeDebrief, GameSummary)
-->
# NOTE — behaviors → ui: **player messages and the end of the exercise are on the wire** *(rules S2, `d17905bce`)*

⭐ A scenario TRIGGER can now send a message to players and END the exercise (close every objective, then pause). The data reaches every node;
**showing it on the Player (subtitle, log, end summary) is ui's** (R-4105: data over NED, the frontend draws).

| what | read it how | fields |
|---|---|---|
| ⭐ a message to show | `ISimulationView.ReadManagedEvents<PlayerMessageEvent>()` (`Fdp.Toolkit.ScenarioRules`, managed) — one frame after publish, on EVERY node incl. the sender | `MessageId` · `OriginNodeId` (0 = raised here) · `ToKind` (All / Side / Station / Unit / Instructor) · `ToValue` (side = `ForceId` value · station id · unit net id · 0 for All / Instructor) · `Text` (≤ 127) · `Seconds` (subtitle time, default 6) · `Priority` · `SentAt` (sim s) · `Source` (the trigger's net id) |
| who sees it | ⭐ the Player filters on ITS station, side or held unit; the IOS / instructor shows all | SR-121 |
| how | a TIME-LIMITED SUBTITLE for `Seconds` + appended to a message LOG the player can reopen (SR-106) | ⚠ a late joiner gets NO old messages (event topic, accepted for the demo) |
| ⭐ the end | the unmanaged `ExerciseEndedEvent { double EndedAt }` — ONCE per node | then read `ExerciseResult` on the entity with `ScenarioClock`: `Phase` (Running / Closing / Ended) · `EndedAt` · `Reason` · `Friend` / `Hostile` / `Neutral` `{ Outcome (None / Win / Lose / Draw), Points, Score 0..1 }` |
| the end summary's tree | the replicated `ObjectiveStatus` rows (`Role` Objective, `Closed` = 1), tree by `ObjectiveLinks.Parent`; `GET /objectives` shows the same | SR-112 debrief mockups |
| pause | ⭐ the cluster pauses itself after the close (node feature `exercise.pause-on-end`, default on — behaviors is building it now) | nothing for ui to do |

⭐ Try it: `scenarios/sr-objectives-march` (objectives) — a trigger + end variant follows with the S2 live run (behaviors will name it in its next note).

_behaviors lane, 2026-11-02_
