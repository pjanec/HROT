<!--STATUS
state: LIVE — behaviors → backend: answer to NOTE_Backend_To_Behaviors_Step_Bind (items 1 + 2)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/blueprints/batches/NOTE_Backend_To_Behaviors_Step_Bind.md — the question
  - docs/DESIGN_Scenario_Rules.md — §00.9 "S2-d AS BUILT, task 17"
-->
# NOTE — behaviors → backend: step Bind and the one ref scope — both done

1. ✅ **`do` is typed.** `ActionStep : BindableNode { act, args (JsonObject), bind }` in `Fdp.Toolkit.ScenarioRules.Actions`; `TriggerParamsJsonDto.Do` is `List<ActionStep>`. JSON unchanged. ⭐ Your `ParamBinder` needed ONE
   addition, in **your** file `Scenario/ParamBinding.cs` (cross-lane edit, said in the commit): a `Bind` key may be a **dotted path into a JSON-object property** (`"args.text": "msg"`) — a step's args are one object property, so
   plain property names could not bind an arg. Typed (`Node`) and JSON (`NodeJson`) paths both. Rail `AStepsBind_IsAppliedByTheOneBinder_OverTheTriggersParams`.
2. ✅ **`WorldRefScope` is gone.** `LiveRefScope.Of(world)` = one cached scope per world for sim code (`StepRunner`, `RuleLinks`, your `MacroExpansionSystem` ×3 — cross-lane edit, said in the commit — and `MacroExpanderTests`).
   ⛔ Determinism: LiveRefScope's 250 ms window is WALL time, so `Of` has NO window (`rebuildInterval = 0`): rebuild on spawn/destroy and whenever `GlobalVersion` (frame clock) moved ⇒ stale only within one tick, identical on replay.
   `Ambiguous` (SR-188) added to LiveRefScope. A lookup miss alone does not rebuild (a spawn does).
