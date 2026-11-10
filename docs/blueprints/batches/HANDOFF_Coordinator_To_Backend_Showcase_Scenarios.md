<!--STATUS
state: LIVE — coordinator → backend: build the two showcase scenarios (Bridgehead, Crossfire) + the infrastructure they still lack
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../SHOWCASE_Scenarios.md — THE spec: §1 Bridgehead (training), §2 Crossfire (game), §1.6 / §2.6 acceptance checks, §3 coverage (⚠ its "today" column is STALE — item ⓪)
  - ../../DESIGN_Scenario_Rules.md — every mechanism (objectives S1, triggers S2 built; S4 evaluation building on behaviors; S6 the game profile)
  - ../../DESIGN_Scenario_Macros.md — macros S3a + S3b (yours, built)
  - ../../DESIGN_Player_Join.md — players, Deploy, respawn (D-14), score (D-15)
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_And_View_Data.md · HANDOFF_Coordinator_To_Ui_Rules_View_And_Summary.md — the parallel lanes
-->
# HANDOFF — coordinator → backend: **the two showcase scenarios**

**Dispatched at** `d90994316` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106).
🔒 **User, `2026-11-02`:** *"There was a design for two scenarios, one for military training and the other for gameplay. Maybe the backend can implement and test those if there are already enough infrastructure available for that, or backend can also add the missing infrastructure, whatever is needed."*

## What exists (measured `2026-11-02`) — the `today` column of SHOWCASE §3 predates it
✅ built: macros (S3a + S3b: instances, placement, params + bindings, run variants, `InsertMacro`, the director as a macro, `SetSkillAmplifier`) · RefKeys · objectives `ReachArea` / `HoldArea` / `ClearAreaObjective` / `DestroyTargets` / `Protect` / `NoHarm` + `Composite` (S1) · triggers + `Message` / `SetObjectiveActive` / `EndExercise` / `Order` / `MissionCommand` + pause-on-end (S2) · Node Features. Two small demos exist: `scenarios/demos/rules-objectives-directors/sr-objectives-march`, `scenarios/demos/rules-objectives-directors/sr-triggers-march`, `scenarios/tests/test-macro-two-teams`.
⏳ elsewhere: per-member evaluation + measures + roll-up (rules S4 — **behaviors**) · kill attribution CE-4155 (**behaviors**) · the rules view, mission summary, debrief, HUD (**ui**). ⛔ do NOT build those here; use them as they land.

## Items, in order
| # | item | note |
|---|---|---|
| ⓪ | refresh SHOWCASE §3's `today` column to the measured state above | one commit, docs only |
| ① | ⭐ **Bridgehead v1** (§1): the map, forces, phases, objectives, the triggers of §1.3 (all in macros), the QRF by `InsertMacro` at the variant's edge, the ease-off director, run variants, civilians, `NoHarm` | acceptance §1.6 lines 2, 3, 5 now as `checks.json` lines; line 1 (chain) as a status check now — the "rules view draws it" half is ui's; lines 4 and 6 (debrief / summary numbers) wait for S4 + ui — add them when those land |
| ② | ⭐ **the game-profile infrastructure Crossfire needs** (rules **S6** — unassigned, yours): domination points (capture with more men for 10 s), tickets (drain 1 / 10 s per held point), respawn + score (Player Join D-14 / D-15), team-kill −200 never counted as a kill | ⚠ a ticket COUNT may overlap behaviors' S4 measure `Count` (their B2) — read their branch first; ⛔ never a second counting mechanism; team-kill attribution needs CE-4155 (behaviors) |
| ③ | ⭐ **Crossfire v1** (§2): teams, points, crate drops (4 at most, 3 min apart, one of 4 spots per variant — `InsertMacro` + a trigger), the comeback boost (the director + `SetSkillAmplifier`, losing team only, never twice within 2 min) | acceptance §2.6 lines 1, 2, 4 now; line 3 after CE-4155; line 5 (awards macro) after S4 |
| ④ | anything else a scenario needs and the engine lacks — ⭐ FIND it first (graph + grep, CLAUDE.md seam law), build it in the shared place, name it in the report | ⛔ never scenario-specific code (SR-? "nothing compiled per scenario", R-4112) |

## Gates *(Rule 8)*
Each scenario: its `checks.json` green on a live `--mode all` run (the scenario-regression verifier) + a recording; the touched classes; `ned-wire-inventory.py --check` for new wire; the gate scripts; fold the as-built into SHOWCASE (and `DESIGN_Scenario_Rules.md` for S6).

_coordinator lane, 2026-11-02_
