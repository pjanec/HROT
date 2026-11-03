<!--STATUS
state: LIVE — behaviors → backend: CE-3364 (your row) is fixed — one kill authority
updated: 2026-11-03
current-answer: the whole note
related-designs:
  - ../../DESIGN_Man_Downed_Event.md — the design (user-approved 2026-11-03): class / sequence / module UML, decisions, as built §5
  - ../../DESIGN_Scenario_Rules.md — §00.13 S7 (yours): the CE-3364 paragraph points to the design
-->
# NOTE — behaviors → backend: **CE-3364 fixed — `ManDownedEvent`, the one kill authority** (`908dc63ad`)

| | |
|---|---|
| what | `HealthApplicationSystem` publishes `ManDownedEvent {Victim, Source, OriginalSource, State, BodyPart, VictimSide}` ONCE when a hit takes a man out of the fight — never for a corpse, never for the bleed-out (helper `Fdp.Toolkit.Combat.ManDowned`; `DamageSystem` in the FDP examples uses the same helper) |
| your S7 | `Count kills / teamKills / assists` now read it (`MeasureEvidence`); the per-measure guessing (`Foes` / `Settle` / `Killed` …) is deleted. Every S7 rail in `ScenarioRulesTests.Score` stays green; rails that downed a man by editing Health go through `ObjWorld.Kill` (production `ManDowned`) |
| unchanged | `Deaths` (state-based, any cause — a different question), `Captures`, hits / shots / fratricide / civiliansHarmed (harm, on `DamageAssessedEvent`) |
| ⚠ re-measure | Crossfire's per-man kills vs deaths live (the 20 vs 9) — on your next live round |
| later | a wire topic for the kill feed (PH-7) — design D5; none now (Health is applied on the Brain, where the measures run) |
