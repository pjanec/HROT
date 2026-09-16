# BCP-BATCH-02-FIX3 Review — unify wire-drop picker + auto-connect + duplicate-var-name guard
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Verification (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → 0 Errors.** Warnings: a full (`--no-incremental`) rebuild shows ~26 **pre-existing** warnings, all in test projects untouched by any BCP batch (`Fdp.Core.Tests` migration tests, `Hrot.Common.Tests`, `Hrot.Utility.Editor.Tests`, plus `BlueprintTestFixture.cs`/benchmarks with CS0618/CS8601 and xUnit2013 analyzer hints). **None are from the BCP changes.** Correction: prior FIX/FIX2 reviews reported "0 warnings" from *incremental* builds, which don't recompile unchanged projects — the coders' warning counts were correct; this work adds zero new warnings but the pre-existing test-debt is real.
- `Hrot.Blueprints.Tests` **1104 / 10 / 8** (10 = DEBT-006; perf flake passed this run); golden + byte-stability unchanged. `Hrot.Editor.AiShared.Tests` **761 / 0**, `Hrot.BTree.Editor.Tests` **382 / 0**, `Hrot.Hsm.Editor.Tests` **333 / 0**, `EditorSubsystemBoot` **10 / 0**.

## Code read
- **`BlueprintNodeCatalog.DescriptorToEntry`** now derives the entry's `Inputs`/`Outputs` `PinSignature`s via `NodePinSchema.GetCanonicalPins(defaultNode, _registry)` when `defaultNode.Pins` is empty (the 24 FIX2 kinds), reusing `PinToSignature`; keeps `defaultNode.Pins` for the hand-authored When/EQS. (Method made instance to reach `_registry`.) This single change makes `QueryForPinContext` return the full compatible set AND gives the wire-drop flow (`CanvasInput`) a compatible pin to link to. Auto-connect then resolves via `BlueprintGraphModel`'s two-pass slow-path binding the new pinless node's first compatible canonical pin to the link's pin GUID. No NodeEdit-core change.
- **Duplicate variable name:** `CreateVariable` returns `VariableDecl?`, rejecting blank or case-insensitive duplicates (no silent suffix) via `IsDuplicateVariableName`; `VariableCreateModal` shows an inline warning + disables Create on collision/blank. (The quick-add `+`/`AddVariable` path keeps auto-uniquify so repeated clicks still work.)

## Test quality
Task 1: `QueryForPinContext` for an exec-output returns >3 kinds incl. Branch/Sequence/ChannelCommand with a compatible exec-input; add-node-pinless + add-link-to-fresh-pin → after Rebuild the link resolves both ends and connects to the new node. Task 2: `CreateVariable` rejects an existing name (no new decl) + accepts a unique one. Real assertions.

## Issues / debt
- **DEBT-BCP-004 (P3):** ~26 pre-existing test-project warnings (CS0618 obsolete-usage, CS8601 nullable, xUnit2013 analyzer) surface on full rebuild. Unrelated to the editor work; clean up opportunistically.
- Note the `+` quick-add still auto-uniquifies (only the **modal** path enforces the up-front duplicate warning). If the `+` should always go through the modal, that's a small follow-up.

## Verdict
APPROVED. Wire-drop picker unified with TAB (full compatible set) + auto-connect works; duplicate variable names are rejected with an up-front warning. Hand back for re-test. Remaining: fonts (S4, engine), BATCH-03 (mini-editors, comments/reroutes).

## Commit Message
```
fix(editor): unify wire-drop picker + auto-connect + reject duplicate variable names (BCP-BATCH-02-FIX3)

BlueprintNodeCatalog.DescriptorToEntry derives pin signatures via NodePinSchema when the descriptor's
default node has empty Pins (the 24 FIX2 palette kinds), so QueryForPinContext returns the full
compatible node set (wire-drop picker == TAB, pin-filtered) and the wire-drop flow finds a compatible
pin → forms the link → BlueprintGraphModel's two-pass binding connects it to the new node. No NodeEdit
core change.

Variable creation: CreateVariable rejects blank/duplicate (case-insensitive) names instead of silently
appending a numeric suffix; VariableCreateModal warns inline + disables Create on collision.

Build 0 errors. Blueprints 1104/10 (DEBT-006), AiShared 761/0, BTree 382/0, Hsm 333/0, Boot 10/0.
Projection-only intact (byte-stability + compiler golden unchanged). (~26 pre-existing test-project
warnings remain, unrelated — DEBT-BCP-004.)
```
