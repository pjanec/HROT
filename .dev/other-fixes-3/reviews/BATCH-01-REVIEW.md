# BATCH-01 Review

**Batch:** BATCH-01  
**Tasks:** FIX3-001, FIX3-002, FIX3-003  
**Verdict:** ✅ APPROVED — no corrective batch needed

---

## FIX3-001 -- Blueprint Windows Production Wiring

**Verdict: APPROVED**

The production wiring is correct and complete:

- `EditorSubsystem.RegisterWindows` (line 1470) now calls `_blueprintWindowRegistrar?.RegisterWindows(windowManager)` before the `_editorLogic == null` guard.
- In production, `Initialize()` sets `_blueprintWindowRegistrar` via `CreateBlueprintWindowRegistrar()` which builds a fully real `BlueprintWindowRegistrar` with production dependencies (`FileSystemAssetCatalog`, `_blueprintRegistry`, `_blueprintDebugSession`, etc.).
- The production caller chain is: `LocalWindowController → EditorSubsystem.RegisterWindows → BlueprintWindowRegistrar.RegisterWindows → WindowManager`.

**Test quality: GOOD.** `EditorSubsystem_RegisterWindows_RegistersAllBlueprintWindows` uses the exact production entry point (`EditorSubsystem.RegisterWindows`) and asserts all 7 expected window names are present in the real `WindowManager`. Not vacuous -- fails if any of the 7 windows is missing. The `internal` property injection via `InternalsVisibleTo` is an acceptable pattern for integration tests that need to skip full `Initialize()`.

Suite: 887/895 (8 pre-existing skips), 0 failures.

---

## FIX3-002 -- D-BP-04 Deferral

**Verdict: APPROVED**

`GraphEditorWindow.DrawUI` is confirmed to be a canvas placeholder (`ImGui.TextDisabled`). Implementing `PopulateNodeMenu` requires node hit-testing which does not exist yet. The deferral decision is correct. The TODO comment is precise (includes method name, parameter list, and condition). DEBT-TRACKER updated.

No new test needed. Existing suite unaffected.

---

## FIX3-003 -- StateNode Insertion-Order Tests

**Verdict: APPROVED**

Both tests (`StateNode_ChildNodeIds_PreservesInsertionOrder`, `StateNode_ChildNodeIds_IsStableAcrossMultipleReads`) directly exercise the `StateNode.ChildNodeIds` LINQ projection (`Children.Select(c => new NodeId(c.StableId)).ToList()`). Insertion-order assertions use captured GUIDs, not magic constants -- correctly adapted to the actual `Guid`-keyed `StableId` type. The second test using non-alphabetical insertion (c3, c1, c2) is a good guard against a sorted-collection refactor. Tests pass: 2/2.

---

## Technical Debt

No new debt items from this batch. All three tasks resolved fully.

---

## Overall

Round-3 is complete. All 3 remaining stragglers are now closed:
- FIX3-001: production path wired + integration test
- FIX3-002: D-BP-04 formally deferred (canvas batch prerequisite; P3)
- FIX3-003: `StateNode` insertion-order coverage added

Suite totals: 887 blueprints (0 failures), 2 HSM (0 failures).
