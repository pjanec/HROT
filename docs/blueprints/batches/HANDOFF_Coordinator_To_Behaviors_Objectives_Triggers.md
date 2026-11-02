<!--STATUS
state: LIVE — coordinator → behaviors: scenario rules S1 (objectives) + S2 (triggers)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — THE spec: §00 build spec (slices S1, S2), §8–§11, SR-158 (when shares the compiler's level arms), SR-166 (the trigger record is an immutable record)
  - ../../DESIGN_Scenario_Macros.md — §5.1 (BindableNode / Bind, SR-160), §5.1.2 (ParamBinder, SR-164) — built by backend as its item ① (the shared piece)
  - HANDOFF_Coordinator_To_Backend_Scenario_Macros_S3a.md — the parallel lane
-->
# HANDOFF — coordinator → behaviors: **objectives (rules S1) + triggers (rules S2)**

**Dispatched at** `d358a8728` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106 on a later invalidation).
🔒 **User, `2026-11-02`:** *"the objectives/triggers could be handled by the behavior lane."*

## Start
Merge `coordinator` (S0, S9′, S8, Node Features S1 are on it); push `chore: started rules S1 at <sha>`.

## Items, in order
| # | item | spec | note |
|---|---|---|---|
| ① | **rules S1** — objective entity type, the kinds (`ReachArea` / `HoldArea` / `ClearArea` / `DestroyTargets` / `Protect` / `NoHarm`), `Composite` + `WeightedMean` / `AllMustPass`, `ObjectiveStatus` on the wire | rules §00 S1 row | ⚠ `NoHarm`'s indirect harm needs CE-4155 (damage names its cause) — gunfire and blasts work without it |
| ② | **CE-4157** (mission-plan triggers: no silent fallback, no silently dropped trigger) · **CE-4159** (the `PredicateCompiler` gap the PoC found) | tracker rows | S2's prerequisites |
| ③ | **rules S2** — CE-4160 … CE-4166 (lift the PoC: action machinery, `when` leaves `OnEntity` / `ScenarioTime`, actions `Message` / `SetObjectiveActive` / `EndExercise` / `Order` / `MissionCommand`) | rules §00 S2 row, SR-158 | ⭐ `TriggerDefinition` + its `When` / `Do` tree are `record`s with `init`-only members, replaced whole (SR-166; CLAUDE.md *NEW COMPONENTS*) |

⭐⭐ **The ONE coupling with the backend lane:** predicate nodes and `do` entries take the shared `BindableNode` base (`Bind`, SR-160) that backend builds FIRST (its item ①). ⇒ before you edit the predicate DTOs in ③, **merge backend's item-① commit** (its note gives the sha); ⛔ never write a second binder or base. ① and ② do not wait for it.
⛔ NOT here: `InsertMacro` (macros S3b — after this S2 and backend's S3a) · the rules VIEW (S5, ui).

## Gates *(Rule 8)*
Touched test classes, new rails red-proved · the trigger PoC rails (`P5_*`) green · one live run with an objective and a trigger firing · the gate scripts · fold the as-built into `DESIGN_Scenario_Rules.md`.

_coordinator lane, 2026-11-02_
