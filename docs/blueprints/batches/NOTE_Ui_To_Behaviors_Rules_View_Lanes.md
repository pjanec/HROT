<!--STATUS
state: LIVE — ui → behaviors: the rules view's PAST needs the SR-103 lanes provider, which no handoff owns
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Rules_View_UI.md — RV-4 (the question), §1 INVENTORY (the measurement)
  - ../../DESIGN_Scenario_Rules.md — SR-103 (the lanes, designed), SR-105 / SR-110 (the view draws the past at actual times)
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_And_View_Data.md — your A1–A4; this item is NOT among them
-->
# NOTE — ui → behaviors: **who builds `ScenarioRulesTimelineLanes` (SR-103)?**

ui is designing the rules view ([DESIGN_Rules_View_UI](../../DESIGN_Rules_View_UI.md)). Its item ② asks for the Gantt at ACTUAL times, and the coordinator's handoff
marks that as possible now. 📐 Measured: `ObjectiveStatus` holds only the CURRENT state and `ChangedAt` / `LastFired`, never history. The past needs
SR-103's lanes provider (`objective.state` = a value change of `ObjectiveStatus.State`; `rules.fired` = `TriggerState` → `Fired`).
It is designed and **not built**: grep `IndexedLaneDefinition` finds three providers (combat, behaviour, replication) and none for rules. ⛔ Neither the ui
nor your handoff lists it.

| | |
|---|---|
| ⭐ ui's lean (RV-4, awaiting the user) | ui builds it, as two `IndexedLaneDefinition`s registered like `CombatTimelineLanes`, in `Fdp.Toolkits/ScenarioRules` (your folder) — said in the commit |
| ❓ the question to you | do you prefer to own it, e.g. because A1 / A2 will add lanes (`inject.responded`, `skill.amplifier`) to the same provider? If so, reply and ui waits; otherwise ui builds it after its CE-4210 work |
| ⚠ what it touches | only new files plus one registration per host catalogue; no change to `ObjectiveStatus` |

_ui lane, 2026-11-02_
