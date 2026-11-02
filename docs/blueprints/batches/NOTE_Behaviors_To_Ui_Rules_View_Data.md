<!--STATUS
state: LIVE — behaviors → ui: the rules view / mission summary DATA is on the wire for every node; and the SR-103 lanes are yours to build
updated: 2026-11-02
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.10 (V-1…V-4, as built), §00.11 (S4, in build)
  - ../../DESIGN_Rules_View_UI.md — your design (RV-4)
  - NOTE_Ui_To_Behaviors_Rules_View_Lanes.md — the question this answers
-->
# NOTE — behaviors → ui: **the rules view's data is on every node** · ⭐ build the SR-103 lanes yourselves

## Your RV-4 question
⭐ **Build `ScenarioRulesTimelineLanes` yourselves** (two `IndexedLaneDefinition`s in `Fdp.Toolkits/ScenarioRules`, registered like `CombatTimelineLanes` — fine,
said in the commit). Behaviors adds no lane to it now; if S4 needs one later we extend YOUR provider (one place).

## What every node now reads (user: *"displayable on any node"*) — all on the existing state topics, written by the owner (Brain), change-only
| what | where | fields |
|---|---|---|
| status + links + timing | `ObjectiveStatus` (component 425) ← topic `EntityObjectiveStatus` (171) | as before + `TriggerState` · `DependsOn[≤8]` (the entities a trigger's `when` names) · `ActsOn[≤8]` (the targets its `do` names) · `HoldFor` · `Delay` · `Cooldown` · `Deadline` · ⭐ `ExpectedAt` (sim s of the next thing the owner KNOWS will happen; 0 = unknown) · `FireCount` · `LastFired` |
| live condition values | `RuleLeaves` (431) ← `EntityRuleLeaves` (174) | ≤ 8 `{Label, Value 0/1, Measured}` per trigger — ⚠ only while the node feature **`ui.rules-live-values`** is ON (AtLoad off, Live on demand): switch it on when the When tab opens; when it goes off the last sample stays (draw it greyed) |
| runtime control | write `RuleControl` (430) through `EntityWriteRouter.For(repo)` — `Rule.Request` (Fire · Disarm · Rearm · Reset · Activate · Deactivate) + `Rule.Seq` (+1) + `Rule.Origin`; or `POST /objectives/{id}/control {"request":"Disarm"}` | the owner acts ONCE per Seq and journals it; a non-owner's write is a change-request to the owner (recorded) |
| the end | `ExerciseResult` + `ExerciseEndedEvent` | see `NOTE_Behaviors_To_Ui_Player_Messages_And_Exercise_End.md` |
| one JSON view of all of it | `GET /objectives` (any node) | rows + `dependsOn` / `actsOn` / `timing` / `leaves` |

⏳ S4 (per-member evaluation, measures, `group`) is in build — `group` will join the status topic; I will note you when it lands.

_behaviors lane, 2026-11-02_
