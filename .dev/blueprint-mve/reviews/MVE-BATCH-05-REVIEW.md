# MVE-BATCH-05 Review — compile-on-demand (closes the live loop)
**Status:** ✅ APPROVED   **Date:** 2026-06-04

## Summary
The editor's `_blueprintQuickReloadTrigger` (was `null`) now drives a real `QuickReloadService` that compiles the opened in-memory blueprint and commits it into the **shared `_blueprintRegistry`** the kernel ticks. A "Compile / Reload Blueprint" toolbar action invokes it. So: edit → Compile → Run (MVE-03) → tick (MVE-02) → Save (MVE-04).

## Verification (sonnet self-verify + lead read)
- Build **0 errors** (touched projects clean; `Hrot.Blueprints.Editor` is TreatWarningsAsErrors). `EditorSubsystemBoot` **10/10** (QuickReloadService + coordinator constructed at composition — boot unaffected). New `BlueprintCompileOnDemandMveTests` **5/5**. `Hrot.Blueprints.Tests` **1152/10** (DEBT-006; existing QuickReloadServiceTests still green). `Hrot.Editor.AiShared.Tests` **761/0**.
- **Registry-sharing (the crux) confirmed by read:** the new `qrsCoordinator` (`Fdp.Toolkit.Behavior.AiHotReloadCoordinator`) is constructed with the same `_blueprintRegistry` field that `BlueprintRuntimeWiring.WireBlueprintRuntime` passed to `BlueprintTickSystem` — so compiled blueprints are visible to the running kernel. Compile is from the in-memory asset (no pre-save). `TriggerAsync` is synchronous internally → `GetAwaiter().GetResult()` is safe.

## Decisions / notes
- **Run stays two-click** (Compile then Run): `RunBlueprintOnEntityCommand` is a static command with no QuickReloadService ref; auto-compile-on-NotRegistered would mean threading a compile delegate through it. Acceptable; the NotRegistered message already says "Compile / register first." (Follow-up if a one-click loop is wanted.)
- A separate `qrsCoordinator` (lightweight FDP variant) is used because QuickReloadService needs it (the editor's main `_aiCoordinator` is the file-watching Hrot.Editor variant). Both share `_blueprintRegistry`.

## Debt logged
- **DEBT-MVE-002 (P2, blocks observe):** generated registrars for *compiled* blueprints don't populate `BlueprintDefinition.StateFields`, so `BlueprintStateView.TryGetField<T>` can't read a compiled blueprint's working-state by name. The `Count==N` proof therefore runs through the staging path (`FakeInstanceBp`); the real-compiled test asserts compile→register→attach→tick (slot exists) but not the named field value. Must be addressed for MVE-07 debug-observe (and for the editor showing live values).
- **DEBT-MVE-003 (P2, production robustness):** two `AiHotReloadCoordinator`s (main file-watch `_aiCoordinator` + `qrsCoordinator`) commit into one `_blueprintRegistry`. Verify `BeginStaging`/`CommitStaging` semantics preserve existing (build-time + other) blueprints across a quick-reload commit rather than replacing the snapshot — otherwise a quick-reload could wipe other registered blueprints in production. (Not exercised by the single-blueprint MVE tests.)

## Verdict
APPROVED. Compile-on-demand closes the live loop with proven registry sharing; the two debts are follow-ups (observe + multi-coordinator robustness), not blockers for the MVE. Next: MVE-06 hot-reload (editor-triggered, AiHotReloadCoordinator) and MVE-07 debug-observe (needs DEBT-MVE-002 StateFields).

## Commit Message
```
feat(blueprint-mve): compile-on-demand — register opened blueprint into the kernel's registry (MVE-BATCH-05)

Wire the editor's _blueprintQuickReloadTrigger (was null) to a real QuickReloadService that compiles the
opened in-memory BlueprintAsset and commits it (via a shared-registry Fdp.Toolkit.Behavior.AiHotReloadCoordinator)
into the SAME _blueprintRegistry the kernel's BlueprintTickSystem ticks — so the Run button then resolves +
attaches it and it runs live. "Compile / Reload Blueprint" toolbar action; compile from RAM (no pre-save);
TriggerAsync is synchronous internally. Closes the edit→compile→run→save loop (Compile + Run are two clicks).

Headless BlueprintCompileOnDemandMveTests: in-memory asset → TriggerAsync → registered → attach → PumpFrames
→ Count==N (+ a full real-compiled compile→register→attach→tick test). Build 0 errors; EditorSubsystemBoot
10/10; Blueprints 1152/10 (DEBT-006); AiShared 761/0. DEBT-MVE-002 (compiled StateFields) + DEBT-MVE-003
(two coordinators on one registry) logged.
```
