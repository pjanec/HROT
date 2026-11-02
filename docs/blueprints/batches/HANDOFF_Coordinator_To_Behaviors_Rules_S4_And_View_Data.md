<!--STATUS
state: LIVE — coordinator → behaviors: rules S4 (per-member evaluation) + the data the rules view and mission summary need
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — THE spec: §00 S4 row, SR-72 / 72a (MemberEvaluation), SR-20 (InputAbove / Sample), SR-75 (Score), SR-111 (mission summary), SR-112 (debrief + HUD), SR-109 (the rules view)
  - ../../designs/rules-view/README.md — the mockups (MissionSummary, TraineeDebrief, HudTraining, HudGame, Main, TriggerDetails, ObjectiveDetails, EditAtRuntime) — the rows win where they differ
  - HANDOFF_Coordinator_To_Behaviors_After_Rules_S2.md — CE-4155 there is S4's prerequisite
  - HANDOFF_Coordinator_To_Ui_Rules_View_And_Summary.md — the parallel ui lane that draws what this produces
-->
# HANDOFF — coordinator → behaviors: **rules S4 + the rules view's data**

**Dispatched at** `474077b06` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106).
🔒 **User, `2026-11-02`:** *"Is all the data sources already available for the objectives view and instructor summary view? I guess this is not just UI's work."* → *"yes write both handoffs"*.

## Start
Finish `HANDOFF_Coordinator_To_Behaviors_After_Rules_S2.md` item ② (**CE-4155** — S4 needs it) first; merge `coordinator` (backend's macros S3a + S3b are in — S4's other prerequisite).

## Part A — the rules view's data (ui is drawing the view in parallel; each item unblocks one of its parts)
| # | item | unblocks (mockup) | note |
|---|---|---|---|
| A1 | ⭐ MEASURE first: does a trigger's definition (`TriggerBehavior`'s `when` / `do`, in the brain params) and an objective's params (deadline, delay, cooldown) reach a NON-Brain node (the IOS / editor)? No trigger translator exists on the wire today | — | if not: publish a read-only, replicated, UNMANAGED summary per trigger / objective — its predecessors (the entities its `when` / `do` names) and its timing (deadline, delay, cooldown) — ⛔ not the whole managed definition |
| A2 | the dependency edges + expected timing from A1 | `Main`: connectors, dashed expected bars, *Coming up* strip | the ui view derives the layout; the data says WHO depends on WHOM and WHEN it may happen |
| A3 | **live value per condition leaf** — the Brain publishes each armed trigger's leaf values (true / false + the measured number) at a modest rate, only while a viewer asks (a feature, `ui.rules-live-values`, AtLoad off / Live on demand — Node Features) | `TriggerDetails` When tab | ⛔ never every frame for every trigger |
| A4 | **runtime control**: fire · disarm · re-arm a trigger; set an objective's state / a param — as commands routed to the OWNER (`EntityWriteRouter` pattern), recorded and journaled | `EditAtRuntime`; game-master Z2 (SR-104) | this run only (SR-109) |

## Part B — rules S4 (the per-trainee evaluation)
| # | item | spec |
|---|---|---|
| B1 | `MemberEvaluation` — on a commander, one evaluation macro per subordinate by role (`TacticalDesignation`, else class), following the chain LIVE (a reinforcement gets one; a casualty's stays and keeps its result) | SR-72 / 72a; uses macros S3a (`InsertMacro`-style runtime instances, transient) |
| B2 | the measures `HitRatio` · `Count` · `ReportLatency` · `TimeTo` + the adapters `InputAbove` / `Sample` | SR-20, §00 S4 row; attribution from CE-4155 |
| B3 | the ROLL-UP up the `UnitSubordinate` chain (a node's score = the aggregation its evaluation macro declares, default the mean) and by ROLE; each status's measure GROUPS (mission · fire · reports …) taken from the evaluation, not hard-coded; a per-side headline `Score` with its pass mark | SR-75, SR-111 |
| B4 | the score HISTORY a trend needs — ⭐ prefer the existing timeline index / recording over a new store; measure what it already holds | SR-111 (trend) |
| B5 | the data of the trainee DEBRIEF and the two HUD profiles (training / game: GO / NO-GO per standard, own vs squad vs platoon, kills / assists) | SR-112 |
⛔ the SCREENS are ui's (the parallel handoff). Done when (S4 row): per-trainee results reach the HUD live and the debrief, on a cluster.

## Gates *(Rule 8)*
Touched classes, new rails red-proved · one live run of a scenario with a `MemberEvaluation` on a platoon · `ned-wire-inventory.py --check` for every new wire type (R-134 twins) · the gate scripts · fold the as-built into `DESIGN_Scenario_Rules.md`.

_coordinator lane, 2026-11-02_
