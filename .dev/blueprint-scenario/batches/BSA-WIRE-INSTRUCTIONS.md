# BSA-WIRE: Unify blueprint genesis/event runtime-system registration; fix offline-editor load-back
**Goal:** scenario blueprint assignments **materialize in the OFFLINE EDITOR** (they already work on a CGF node).
Root cause = a **missing unification**: the systems that consume the load-time intent are registered in `CgfSubsystem`
but NOT in `EditorSubsystem`. Fix by routing both through ONE shared registration seam.
**Read `.dev/.guides/DEV-GUIDE_claude.md` first; use codebase-memory MCP.**

## Root cause (verified)
On scenario load the `BlueprintStateTranslator.Inject` writes an `InitialBlueprintsIntent` managed component (verified:
the saved `scenario.json` contains `BlueprintAssignments` with the AssetId, and `EditorSubsystem.cs:696` builds the
serializer WITH the blueprint registry, so the intent IS written). `BlueprintMaterializationSystem` consumes that
intent and attaches the `BlueprintBlackboard*` slot — but it is registered **only in `CgfSubsystem.cs:383`**
(alongside `BlueprintEventIngressSystem` at :384). The **offline editor never instantiates `CgfSubsystem`** (it embeds
CGF logic into its own kernel), and `EditorSubsystem` does NOT register `BlueprintMaterializationSystem` /
`BlueprintEventIngressSystem`. So the intent is written and never consumed → the entity loads with no blueprint.

## The unification (the fix)
Create ONE shared seam that registers the blueprint **genesis + event** runtime systems, called by BOTH
`CgfSubsystem` and `EditorSubsystem`.

### Task 1 — shared registration seam (NEW, in `Hrot.SimHost`)
`Hrot.SimHost` is the right home: it owns `BlueprintMaterializationSystem` (`Hrot.SimHost/Systems/`) and is already
referenced by both CGF and the editor (both use `HrotScenarioSerializerFactory`). Add e.g.
`Hrot.SimHost/Systems/BlueprintGenesisRuntimeRegistration.cs`:
```
public static void RegisterBlueprintGenesisSystems(IEcsModuleHostKernel kernel, BlueprintRegistry registry)
{
    kernel.RegisterGlobalSystem(new BlueprintMaterializationSystem(registry)); // [Input]
    kernel.RegisterGlobalSystem(new BlueprintEventIngressSystem(registry));    // (its existing phase)
}
```
Match the exact constructor args + phases the two systems already use. (`BlueprintEventIngressSystem` is in
`Fdp.Toolkit.Blueprints.Systems`; `BlueprintMaterializationSystem` in `Hrot.SimHost.Systems` — both visible from
Hrot.SimHost.) No new project references for CGF or the editor (both already reference Hrot.SimHost).

### Task 2 — CGF uses the seam
In `CgfSubsystem.cs` replace the two ad-hoc lines (:383 `BlueprintMaterializationSystem`, :384
`BlueprintEventIngressSystem`) with one call to `BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(_context.Kernel, _blueprintRegistry!)`.
Behavior must be IDENTICAL (same systems, same phases, same registry instance).

### Task 3 — EDITOR uses the seam (this is the bug fix)
In `EditorSubsystem` (near where `GenesisMaterializationSystem` is registered, ~line 852, and after `_blueprintRegistry`
is created + populated and `_kernel` exists), call the same seam with the editor's populated `_blueprintRegistry`.
The editor's `_blueprintRegistry` is the SAME instance passed to the serializer at :696 and populated via the
AiHotReloadCoordinator initial load — confirm it is non-null and populated at the point you register.
- **DO NOT touch the existing working tick/maintenance wiring** (`WireBlueprintRuntime` at :787, the `bpTick` splice).
  The editor's blueprint *execution* (tick) currently works — do not regress it. This batch only ADDS the
  genesis/event consumers.

## Tests required — PRESCRIBED
- **Unit:** `RegisterBlueprintGenesisSystems` registers exactly `BlueprintMaterializationSystem` +
  `BlueprintEventIngressSystem` into a kernel (assert both present; use a test/fake kernel that records registrations).
- **Reuse/keep green:** the existing `CgfBlueprintRegistryScannerTests` end-to-end (populated registry → materialization
  attaches the slot) must still pass — it proves the consumer works; this batch just changes WHERE it's registered.
- **Editor-path regression guard:** if there is a headless seam to build the editor's embedded kernel/system set,
  assert `BlueprintMaterializationSystem` is now among its registered systems. If no such headless seam exists, say so
  and provide a precise MANUAL smoke checklist (below) — do not fake it.
- The editor's existing blueprint tick/execution tests + `Hrot.Editor.Tests` (116) must stay green (no tick regression).

## Manual smoke (user will run; include in report)
In the offline editor: load `C:\FDP_Temp\shared\scenarios\test-blueprint\scenario.json` → the entity must show the
attached Instance Blueprint (BlueprintBlackboard slot populated; the runtime inspector shows it). Before this fix it
loads with no blueprint.

## Success criteria
- [ ] One shared seam; CGF + editor both call it; CGF behavior unchanged; editor now registers the genesis systems.
- [ ] No new project references; editor tick/maintenance wiring untouched (no execution regression).
- [ ] Existing CGF integration + scanner tests green; `Hrot.Editor.Tests` green.
- [ ] Report at `.dev/blueprint-scenario/reports/BSA-WIRE-REPORT.md` (incl. the manual-smoke checklist + real test counts).

## DO-NOT-STOP-UNTIL-GREEN (with the pre-existing-failure caveat)
Run `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests`, `dotnet test Hrot/Subsystems/Hrot.Editor.Tests`, and
`dotnet test FDP/Toolkits/Fdp.Toolkits.Tests` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`). These suites have MANY pre-existing
+ flaky failures (Fdp.Toolkits.Tests swings ~28-53; SimHost ~41-48) — you are NOT expected to reach Failed:0 and must
NOT touch unrelated/pre-existing failures. Required: your new test passes; the CGF integration + Editor suites show NO
NET NEW failures vs baseline (run twice to rule out flakiness); the 4 CGF tests + 116 Editor tests pass. Report the
real counts; do NOT claim "all green."

## Guardrails
Only the seam + the two call-sites + the test. No editor tick-wiring changes, no asset edits, no pragmas/skips/weakened
assertions, no touching pre-existing failing tests. Fail loud. Return a summary; the Lead commits.
