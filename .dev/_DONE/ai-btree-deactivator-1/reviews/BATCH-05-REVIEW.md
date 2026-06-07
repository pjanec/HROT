# BATCH-05 Review — Interpreter Cleanup + Editor Integration (EQL-012)

**Status: APPROVED**

---

## Review Summary

All five EQL-012 deliverables implemented correctly. The `_deactivatorDelegates` pre-built array
has been removed and replaced with a stored `_registry` reference. The generated catalog, factory
wiring, and visualizer indicator all meet the spec. Tests are well-targeted and sufficient.

---

## Deliverable Checklist

### 1. Interpreter.cs - `_deactivatorDelegates` removal

PASS. Field is gone (confirmed by grep: no match). `_registry` field added. Constructor stores
it. V1 fallback correctly uses `_registry.TryGetDeactivator(_blob.MethodNames[pi], out _)`.
`SweepExitedNode` correctly uses `_registry.TryGetDeactivator(_blob.MethodNames[pi], out var deactivator)`.
`BindDeactivators` method fully deleted.

### 2. BTreeBuilder.Compile overload (deviation — approved)

PASS. The subagent added `Compile(string treeName, Func<string, bool>? isResourceOwning)`
overload. This is a gap from EQL-010: the design requires generated catalog code to call
`.Compile(treeName, isResourceOwning)`, which requires a 2-arg overload. The implementation
falls back to the registry delegate when `null` is passed, preserving backward compatibility.
Code is clean and matches the spec intent.

### 3. BTreeDefinitionGenerator.cs

PASS. Both builder-returning and blob-returning `Get*` method variants now accept
`global::System.Func<string, bool>? isResourceOwning = null`. Builder-returning forwards to
`.Compile(treeName, isResourceOwning)`. Blob-returning accepts but ignores it (correct — the
blob is returned directly).

### 4. AiBehaviorFactory.cs

PASS. `Func<string, bool> isResourceOwning = name => actionRegistry.TryGetDeactivator(name, out _);`
added immediately after `FbtActionRegistrar.RegisterAll(actionRegistry)`. All 7
`FbtTreeCatalog.Get*()` calls pass the delegate. Wiring is correct.

### 5. BTreeVisualizerRenderer.cs

PASS. `ColorPurple = new Vector4(0.8f, 0.4f, 0.8f, 1.0f)` added. `[R]` indicator placed
after `TreeNodeEx`, with `ImGui.SameLine()`, `PushStyleColor(ColorPurple)`, `ImGui.Text("[R]")`,
`PopStyleColor()`, and `IsItemHovered()` tooltip. Tooltip text is exact match:
`"Resource Owning Node: Manages standing ECS resources via OnDeactivate."`. Push/pop is
balanced independently of the outer `popColors` scope. ✅

### 6. InterpreterCleanupTests.cs (4 tests)

PASS. Tests cover success conditions T1-T4:
- T1: Branch-switch deactivator fires via registry path after array removal (regression check)
- T2: Reflection check — no `NodeDeactivatorDelegate?[]` field present
- T3: GC.CollectionCount unchanged for 500-node tree with no resource-owning nodes
- T4: Correct deactivator invoked (actionA), other action (actionB) unaffected

All use the compile-after-register pattern with `isResourceOwning` delegate (V2 blob path).
Tests are clear, focused, and adequate for the scope.

### 7. BTreeTickSystemTests.cs fix (deviation — approved)

PASS. Pre-existing BATCH-04 regression: `PayloadIndex = 0` used as settable in object
initializer after it became a read-only computed property. Mechanical fix to `RawPayloadIndex = 0`.
Needed for FDP.sln build. Correctly categorized as a gap fix.

---

## Test Baseline

- **Before BATCH-05:** 203 passing, 9 pre-existing failures (212 total)
- **After BATCH-05:** 207 passing, 9 pre-existing failures (216 total)
- **New tests:** 4 (InterpreterCleanupTests: T1-T4) — all pass
- **Pre-existing failures:** 9 (unchanged — generator-related)
- Both `FastBTree.sln` and `FDP.sln` build without errors

---

## Deviations

| # | Deviation | Assessment |
|---|-----------|------------|
| 1 | Added `BTreeBuilder.Compile(string, Func<string,bool>?)` overload | APPROVED — required by design spec §5.4 (generated catalog calls this 2-arg form). Correct implementation. |
| 2 | Fixed `BTreeTickSystemTests.cs` pre-existing BATCH-04 regression | APPROVED — mechanical fix needed for FDP.sln to build. Simple and correct. |

---

## Manual Verification Items (not automated)

- **T6 (factory wiring at runtime):** Requires running `UrbanCombat` scenario; skipped per instructions.
- **T7 (hot-reload end-to-end):** Requires running simulation with ALC swap; skipped per instructions.
- **T8 (editor indicator visual):** Code verified against spec — tooltip text exact match, purple color correct.
