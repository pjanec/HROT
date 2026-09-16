# BSA-WIRE Review
**Status:** ✅ APPROVED (deterministic parts verified; user live-smoke pending)   **Date:** 2026-06-10
**Agent:** sonnet sub-agent (Agent tool).

## Summary
Fixes the offline-editor scenario blueprint **load-back** by unifying the blueprint genesis/event runtime-system
registration into one shared seam used by BOTH `CgfSubsystem` and `EditorSubsystem`. Root cause was the missing
unification the user predicted: `BlueprintMaterializationSystem` + `BlueprintEventIngressSystem` were registered only
in `CgfSubsystem`, and the offline editor doesn't instantiate `CgfSubsystem`, so the load-time `InitialBlueprintsIntent`
was written but never consumed → entity loaded with no blueprint.

## What changed (verified)
- NEW `Hrot.SimHost/Systems/BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(kernel, registry)` —
  registers `BlueprintMaterializationSystem` + `BlueprintEventIngressSystem`. No new project refs.
- `CgfSubsystem.cs:383` — replaced the two ad-hoc registrations with the seam call (`_context.Kernel, _blueprintRegistry!`);
  behavior identical. Lead removed the now-dead `using Fdp.Toolkit.Blueprints.Systems;` (CGF builds clean).
- `EditorSubsystem.cs:856` — calls the seam with the editor's populated `_blueprintRegistry` field (right after
  `GenesisMaterializationSystem`). **This is the bug fix** — the editor kernel now consumes the intent.
- Tick/maintenance wiring (`WireBlueprintRuntime`) untouched → no execution regression.

## Independent verification
- 4 new seam tests + 4 CGF integration tests: **8/8 pass**.
- `Hrot.Editor.Tests`: **116/116** (no tick regression).
- `Hrot.SimHost.Tests` 40–41 / `Fdp.Toolkits.Tests` 34–37 — within the (bad) pre-existing+flaky baseline ranges; no
  net new failures. Sub-agent reported these honestly (did not falsely claim green — improvement over prior runs).
- The actual editor load-back can NOT be verified headlessly (`EditorSubsystem.Initialize` needs Raylib) → **user
  live-smoke required** (load `C:\FDP_Temp\shared\scenarios\test-blueprint\scenario.json` → blueprint attaches).

## Verdict
APPROVED for commit. The fix is correct by construction (the editor now registers the same consumer system as CGF,
with the same populated registry). Committing; gated on the user's live smoke for final confirmation.

## Commit message
```
fix(blueprints): materialize scenario blueprint assignments in the offline editor (BSA-WIRE)

Unify the blueprint genesis/event runtime systems into one seam — Hrot.SimHost
BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems — registering
BlueprintMaterializationSystem + BlueprintEventIngressSystem. Called by both CgfSubsystem
and EditorSubsystem. Root cause: these were registered only in CgfSubsystem, which the
offline editor never instantiates, so the load-time InitialBlueprintsIntent (written by
BlueprintStateTranslator) was never consumed and entities loaded with no blueprint.
Editor tick/maintenance wiring untouched (no execution regression).

Tests: 4 seam + 4 CGF integration pass; Hrot.Editor.Tests 116/116; suites at baseline.
```
