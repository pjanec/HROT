<!--STATUS
state: LIVE — coordinator → ui: the rules view (S5), the mission summary, the debrief / HUD screens
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — SR-56 (objectives view), SR-105 / SR-109 (the rules view), SR-110 (ON the one shared timeline), SR-111 (mission summary), SR-112 (debrief + HUD)
  - ../../designs/rules-view/README.md — the nine mockups; the rows win where they differ
  - ../DESIGN_Director_Timeline_Editor.md — TE-21 / R-4075: ONE timeline panel; CE-4167 extends it
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_And_View_Data.md — the data lane; items marked ⏳ wait for its part named
-->
# HANDOFF — coordinator → ui: **rules view + mission summary + debrief / HUD**

**Dispatched at** `474077b06` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106). After CE-4210 (in progress).
🔒 **User, `2026-11-02`:** *"yes write both handoffs"*.

## What the data already gives (measured `2026-11-02`)
`ObjectiveStatus` (replicated, S1/S2): role, state, closed, owner, progress cur/need, value / target, `Score`, points, changedAt, reason, title, kind, required, scored, weight, visible sides, fire count, last fired, trigger state · `ObjectiveLinks` (Subject, Parent) · `ExerciseResult` (427) · `MacroOrigin` · `GET /objectives[?tree=1]` (a JSON tree, debug API).

## Items, in order
| # | item | data | mockup |
|---|---|---|---|
| ① | **CE-4167** — the ONE shared timeline panel's extensions TL-X1…X5 (connectors · item styles dashed / hatched / diamond · tree row headers with status · drag-to-connect · …) | — | `Main` |
| ② | the **rules view** window: tree by macro / subject / side with the state in words; the Gantt from ACTUAL times (past, NOW line); click = jump to the subject; *"objectives about this unit"* in the subject's inspector (SR-56) | ✅ now | `Main`, `ObjectiveDetails` (Status tab) |
| ③ | connectors, dashed EXPECTED bars, the *Coming up* strip | ⏳ behaviors A1 + A2 | `Main` |
| ④ | the trigger **When** tab with a live value per leaf; the **Do** tab | ⏳ behaviors A3 (Do from A1) | `TriggerDetails` |
| ⑤ | **edit at runtime**: fire / disarm / re-arm, set a state or a param (this run only) | ⏳ behaviors A4 | `EditAtRuntime` |
| ⑥ | the **mission summary** — first half: per-side cards (required n / N with states · forces: strength, casualties, macro-inserted · the overall result) | ✅ now | `MissionSummary` |
| ⑦ | the summary's second half: headline `Score` + trend + pass mark; scores by level (command chain · objectives · roles) with *why this score*; ATTENTION items | ⏳ behaviors B3 + B4 | `MissionSummary` |
| ⑧ | the trainee **debrief** and the **HUD** in the training and game profiles (each element a `hud.*` Node Feature — CE-4148's pattern) · plus behaviors' note: `PlayerMessage` subtitle + log, the `ExerciseResult` end summary | ⏳ behaviors B5 (messages / end summary: ✅ now) | `TraineeDebrief`, `HudTraining`, `HudGame`, `GameSummary` |

⛔ ONE timeline (R-4075), ONE summary model read by the IOS window and the debrief alike — never a second copy of the aggregation in the UI (it is behaviors' B3).
## Gates *(Rule 8)*
Touched classes; frame rails under `xvfb-run -a` for every drawn panel; proof proportionate (visual = one key rail).

_coordinator lane, 2026-11-02_
