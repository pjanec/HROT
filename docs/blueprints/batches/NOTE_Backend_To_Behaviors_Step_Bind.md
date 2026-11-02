<!--STATUS
state: LIVE — backend → behaviors: four items on the action machinery, found building macros S3b B1
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Scenario_Macros.md — §16 (InsertMacro as built), §16.2 (step Bind), §16.3 (the live-world ref scopes)
  - docs/DESIGN_Scenario_Rules.md — §00.9 the action machinery (ActionList, StepRunner, ActionArgs) these items live in
-->
# NOTE — backend → behaviors: a step's `Bind`, and one live-world ref scope

**At** `backend` after merging `coordinator@474077b06`. ⭐ `InsertMacro` (macros S3b B1) is built as a `[SharedAiAction]` in
`Hrot.AI.Behaviors/Brains/Objectives/Verbs/MacroVerbs.cs` (a new file in your folder — said in its commit). Its macro args travel as the step's nested
`"params"` ⇒ `ArgsRef`, the route your `Order` uses — no second lift.

## 1. A `do` entry's `Bind` is applied nowhere (SR-94 / SR-160) — yours to type, lean below

SR-160 makes a step a typed `BindableNode`; the trigger's params contract carries `do` as a free-form `JsonNode`, so `ParamBinder` (the ONE binder) never
reaches a step and a step's `Bind` is silently ignored at load.
⭐ **Lean:** type the step in the trigger contract — `ActionStep : BindableNode { Act, Args (JsonObject), Bind }` and `List<ActionStep> Do` — and the scenario
load binds it with no new code (`ParamBinder.ApplyJson` walks a typed `BindableNode` tree; the editor's `do`-list chip writes `Bind` on the step). ⚠ What would
change the lean: a reason the `do` list must stay untyped JSON (then the binder needs a `JsonNode` arm that recognises a step — a second form, so worse).

## 2. One live-world ref scope (`CLAUDE.md` "shared and unified")

There were three: `MacroExpansionSystem.WorldRefScope` (backend S3a) — ✅ now folded into **your** `Fdp.Toolkit.ScenarioRules.Actions.WorldRefScope`
(`2d94b8fc6`; it gained an `Ambiguous` view, its "a duplicate key is a loud error" comment corrected to SR-188 — a cross-lane edit, said in the commit) — and
ui's `Fdp.Toolkit.Scenario.LiveRefScope` (CE-4206 R1: the same lookups plus the reverse maps and a rebuild cache).
⭐ **Lean:** `StepRunner` resolves through `LiveRefScope` and `WorldRefScope` retires (a per-step scope rebuilt from scratch is the uncached special case).
Not done here: two other lanes' files.

## 3. ✅ Fixed in your `StepRunner` (found live) — a `"@Key"` POINT was never resolved

`StepRunner` chose to resolve a step's links by `EntityRefRemap.HoldsRefs(typeof(TP))` — the load's *"does it hold a network id to remap"* question,
which leaves out `PointLink`. ⇒ `InsertMacro {"at":"@Drop"}` stood at (0,0,0) live. Now `HoldsLeaves` (resolution is an identity on a plain point or
bearing). Rail `ScenarioRulesTests.InsertMacro_AtAKey_StandsAtThatEntity` (red-proved). ⚠ Any other verb whose only reference is a place had the same hole.

## 4. FYI — your proficiency code gained the side AMPLIFIER (macros S3b B3, SR-100 / R-4106 / P-5) — `90d55774d`

`CombatProficiency` (your CE-2273) has one more field, `SideTiers` (sbyte, on the wire dt 150); `ProficiencyOf.Value` now returns
`Amplify(Own(p), p.SideTiers)` — with 0 tiers it is EXACTLY the old value (`Own` is the old body), so every existing reader and rail is unchanged.
`ProficiencyFillSystem` gained `FillSideTiers`: every man with a brain gets his SIDE's tiers from the World Environment's `SkillAmplifier` (480),
written only on a change. The verb `SetSkillAmplifier(side, tiers ±2)` is in your Verbs folder (`SkillVerbs.cs`). Design: macros §17.

_backend lane, 2026-11-02_
