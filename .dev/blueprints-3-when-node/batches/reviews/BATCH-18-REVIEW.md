# BATCH-18 REVIEW — Phase M11 UI Registration

**Batch:** BATCH-18  
**Tasks:** WHEN-M11-T1, WHEN-M11-T2, WHEN-M11-T3  
**Status:** ❌ **CHANGES REQUIRED**  
**Reviewer:** Dev Lead  
**Date:** 2026-05-26

---

## Summary

BATCH-18 successfully created the **bootstrap infrastructure** for editor wiring but **did not complete the actual integration into editor startup**. The `BlueprintEditorBootstrap` class exists with factory methods, but they are not called from any production editor code paths (`EditorSubsystem`, `BlueprintEditorModule`, or equivalent).

**Critical gap:** The three new node kinds (`WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode`) are still **not visible to designers** because the bootstrap code is never invoked during editor initialization.

---

## What Was Done Well ✅

1. **Infrastructure created:**
   - `BlueprintEditorBootstrap.cs` with four static factory methods
   - `BlueprintNodeDrawerRegistry.cs` (new type-based registry)
   - Well-organized DI surface for dependencies

2. **Tests created and passing:**
   - 10 unit tests covering bootstrap method functionality
   - Tests verify registries return expected objects
   - Debug-mode guard for `WhenFiringPulseRenderer` properly tested

3. **Code quality:**
   - Clean design with no compilation errors
   - Comments explaining the purpose of each registration
   - Follows existing patterns in codebase (HSM/BTree editors)

---

## Critical Issues ❌

### Issue 1: No Production Caller for Bootstrap Methods

**Problem:**
- `BlueprintEditorBootstrap.CreateNodeDrawerRegistry()` is only called from `WhenNodeEditorWiringTests.cs` (test code)
- `BlueprintEditorBootstrap.CreatePaletteRegistry()` is only called from tests
- `BlueprintEditorBootstrap.CreateAttachmentProviders()` is only called from tests
- No calls from `EditorSubsystem.cs`, `BlueprintEditorModule.cs`, or any production startup path

**Evidence:**
```bash
$ grep -r "BlueprintEditorBootstrap\|CreateNodeDrawerRegistry" --include="*.cs" \
    | grep -v "Tests.cs" | grep -v "Tests/"
# Returns only: BlueprintEditorBootstrap.cs definition
# (factory methods defined but never called)
```

**Impact:** 
- Designers cannot use the new nodes because the drawer registry is never populated
- Palette entries never appear in the right-click menu
- Visual attachment providers are never registered with the canvas

**Task requirement violated:**
- WHEN-M11-T1 success condition 3: "`trace_path(WhenNodeDrawer, direction=inbound)` returns at least one production caller after the fix"
  - Currently returns: zero production callers (only test callers)
  
- WHEN-M11-T2 success condition 1: "in the running editor, right-click an empty canvas spot... assert the menu contains the three entries"
  - Currently: entries never reach the menu because palette registry is never populated

- WHEN-M11-T3 success condition 1: "opening a graph with a WhenNode... shows the condition pill"
  - Currently: never shows because providers never registered

### Issue 2: Unit Tests ≠ Integration Tests

**Problem:**
The batch report claims "Integration tests" but the tests are **unit tests of the bootstrap infrastructure**, not **actual editor integration tests**.

**Current test pattern:**
```csharp
[Fact]
public void DrawerRegistry_Contains_WhenNodeDrawer()
{
    var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(...);
    var drawer = registry.GetDrawerFor(new WhenNode { ... });
    Assert.IsType<WhenNodeDrawer>(drawer);  // ← just verifies method returns correct type
}
```

**Required test pattern (per TASK-DETAIL WHEN-M11-T1):**
```csharp
[Fact]
public void EditorBootstrap_BootsAndWiresDrawers()
{
    // Boot an actual editor instance (editor harness, not bootstrap factory)
    var editor = BootEditorHarness();
    
    // Select a WhenNode in the inspector
    var whenNode = new WhenNode { Id = Guid.NewGuid() };
    editor.SelectNode(whenNode);
    
    // Assert the rendered inspector drawer is the actual WhenNodeDrawer
    var inspectorDrawer = editor.GetInspectorDrawer();
    Assert.IsType<WhenNodeDrawer>(inspectorDrawer);  // ← actual wiring test
}
```

**Impact:**
- Current tests pass, but they don't validate that designers can actually use the nodes
- The bootstrap code works in isolation but has zero visibility into the editor startup flow

---

## Required Changes

### Change 1: Wire Bootstrap into EditorSubsystem (or equivalent)

**Location:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (or similar bootstrap entry point)

**Implementation:**
Find the editor initialization code and call the bootstrap factories during startup:

```csharp
// In EditorSubsystem.Initialize() or similar
private void InitializeBlueprint EditorServices()
{
    // Register node drawers
    var drawerRegistry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
        _channelCommandCatalog,
        _engineEventCatalog,
        _editService,
        _predicateCompiler
    );
    _inspectorHost.SetDrawerRegistry(drawerRegistry);  // or equivalent setter
    
    // Register palette entries
    var paletteRegistry = BlueprintEditorBootstrap.CreatePaletteRegistry();
    _canvasPaletteHost.RegisterEntries(paletteRegistry.EnumerateAll());
    
    // Register attachment providers
    var providers = BlueprintEditorBootstrap.CreateAttachmentProviders(...);
    _canvasHost.RegisterAttachmentProviders(providers);
    
    // Register canvas renderers
    var renderers = BlueprintEditorBootstrap.CreateCanvasRenderers();
    _canvasHost.RegisterRenderers(renderers);
}
```

**Verification:**
After this change, `trace_path(WhenNodeDrawer, direction=inbound)` should return a call from `EditorSubsystem` initialization.

### Change 2: Add True Integration Tests

**Location:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Integration/WhenNodeEditorSmokeTest.cs` (new file, or add to existing integration harness)

**Tests required (per TASK-DETAIL WHEN-M11-T6):**

```csharp
[Fact]
public void EditorBootstrap_WhenNodeDrawer_SelectedInInspector()
{
    // Boot real editor harness
    using var editor = new BlueprintEditorTestHarness();
    editor.Initialize();
    
    // Verify drawer registry is populated (WHEN-M11-T1)
    var registry = editor.DrawerRegistry;
    Assert.NotNull(registry.GetDrawerFor(new WhenNode { Id = Guid.NewGuid() }));
}

[Fact]
public void EditorBootstrap_PaletteMenu_ShowsThreeEntries()
{
    using var editor = new BlueprintEditorTestHarness();
    editor.Initialize();
    
    // Verify palette entries are registered (WHEN-M11-T2)
    var menuEntries = editor.GetPaletteMenuEntries();
    
    Assert.Contains(menuEntries, e => e.Name == "When" && e.Category == "Reactive Guards");
    Assert.Contains(menuEntries, e => e.Name == "ReadEqsResult" && e.Category == "EQS");
    Assert.Contains(menuEntries, e => e.Name == "SpawnEqsSensor" && e.Category == "EQS");
}

[Fact]
public void EditorBootstrap_CanvasAttachments_AllProvidersRegistered()
{
    using var editor = new BlueprintEditorTestHarness();
    editor.Initialize();
    
    // Verify attachment providers are registered (WHEN-M11-T3)
    var providers = editor.GetAttachmentProviders();
    
    Assert.Contains(providers, p => p is WhenNodeAttachmentProvider);
    Assert.Contains(providers, p => p is CrossAssetDependencyAttachmentProvider);
    Assert.Contains(providers, p => p is EqsTemplateAttachmentProvider);
}
```

These tests should boot a real editor instance (likely via an existing `BlueprintEditorTestHarness` or similar), **not** just call the bootstrap methods directly.

### Change 3: Verify Production Callers

After implementing Changes 1-2, verify:

```bash
$ trace_path(WhenNodeDrawer, direction=inbound)
# Should return:
#   EditorSubsystem.InitializeBlueprint EditorServices() → EditorSubsystem.cs:XXX
```

---

## Test Quality Assessment

### Current Test Coverage: ⚠️ Insufficient

The 10 tests verify that `BlueprintEditorBootstrap` methods return correct objects, but they:
- ❌ Do NOT boot a real editor instance
- ❌ Do NOT validate designer workflows (select node → see drawer)
- ❌ Do NOT validate production wiring (bootstrap called from startup)
- ❌ Do NOT exercise the full pipeline (palette menu → create node → see drawer)

### What Tests Should Cover

Per TASK-DETAIL WHEN-M11-T1/T2/T3:
1. ✅ Bootstrap infrastructure works (CREATE factory methods) — current tests verify this
2. ❌ **Bootstrap is called during editor startup** — MISSING
3. ❌ **Designer can use the nodes end-to-end** — MISSING
4. ❌ **Production code has inbound callers** — MISSING

### Minimum Test Coverage for Approval

Each task (T1, T2, T3) should have at least one integration test that:
1. Boots editor harness
2. Calls the operation that SHOULD trigger the registration (e.g., select node, right-click palette, open canvas)
3. Asserts the registration has taken effect

---

## Recommended Path Forward

### Option A: Changes Required (Recommended)

**Effort:** 4-6 additional hours

1. Locate EditorSubsystem or equivalent startup entry point
2. Add calls to `BlueprintEditorBootstrap` factory methods
3. Inject/resolve required DI dependencies for factories
4. Write 3-4 integration tests that boot editor and verify end-to-end workflows
5. Verify `trace_path` returns production callers
6. Update BATCH-18-REPORT.md with corrective changes

**Submit as:** BATCH-18 updated report (or BATCH-18-CORRECTIVE if simpler to track separately)

### Option B: Create Corrective Batch (Alternative)

If locating the exact wiring site is blocked:
1. Accept BATCH-18 as "infrastructure scaffold"
2. Create BATCH-18-CORRECTIVE for "integrate bootstrap into EditorSubsystem"
3. Mark current batch status as "approved pending corrections"

---

## Blocking Issues for Approval

**BATCH-18 is BLOCKED on:**

1. ❌ **Bootstrap not called from production code**  
   Drawers, palette, attachments must be registered at editor startup, not just in tests

2. ❌ **No integration tests of actual editor wiring**  
   Unit tests of bootstrap methods are insufficient; need end-to-end tests

3. ❌ **No inbound production caller for the drawers**  
   Per TASK-DETAIL success conditions, `trace_path(WhenNodeDrawer)` must show a production caller

---

## Recommendations

✅ **Keep** `BlueprintEditorBootstrap.cs` as-is (infrastructure is well-designed)  
✅ **Keep** `BlueprintNodeDrawerRegistry.cs` (new type is solid)  
❌ **Replace** `WhenNodeEditorWiringTests.cs` with true integration tests  
➕ **Add** production wiring in EditorSubsystem or equivalent

---

## Sign-Off

**Current Status:** ❌ **NOT APPROVED**  
**Reason:** Infrastructure exists but not integrated; designers cannot yet use the nodes

**Next Action:** Developer submits corrective changes addressing Issues 1, 2, and 3, OR plan BATCH-18-CORRECTIVE for final wiring.

**Reviewer:** Dev Lead  
**Date:** 2026-05-26
