# BATCH-18 CORRECTIVE — Complete Editor Integration Wiring

**Original Batch:** BATCH-18  
**Phase:** M11 — Corrective: Production wiring  
**Tasks:** WHEN-M11-T1 (complete), WHEN-M11-T2 (complete), WHEN-M11-T3 (complete)  
**Type:** Corrective  
**Reason:** Infrastructure created but not integrated into actual editor startup  

---

## Review Findings

The BATCH-18-REVIEW identified three critical gaps:

### Gap 1: Bootstrap Methods Not Called from Production Code
- `BlueprintEditorBootstrap` exists with factory methods
- **BUT:** These methods are never called from any editor startup path
- Designers cannot use the new nodes because registries are never populated

### Gap 2: Tests Are Unit Tests, Not Integration Tests
- Current tests only verify that `BlueprintEditorBootstrap` methods return objects
- **Missing:** Tests that boot actual editor harness and verify end-to-end workflows
- Example: "Select WhenNode in inspector → assert drawer is WhenNodeDrawer (not generic fallback)"

### Gap 3: No Production Caller for Bootstrap Methods
- `trace_path(WhenNodeDrawer, direction=inbound)` currently returns zero production callers
- Task success condition requires at least one production caller

---

## Required Corrective Changes

### Change 1: Wire Bootstrap into EditorSubsystem

**Objective:** Call `BlueprintEditorBootstrap` factory methods during editor initialization.

**Investigation hints:**
- Look in `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- Search for existing `Inspector` or `Canvas` initialization code
- Look for where other editor services (e.g., HSM drawer registry, BTree drawer registry) are initialized
- Find the DI container/service host to resolve required dependencies

**Implementation requirements:**

1. **Locate the bootstrap site:**
   - EditorSubsystem has an initialization method (likely `OnInitialize()` or similar)
   - OR there's a BlueprintEditorModule that initializes Blueprint-specific services
   - Look for existing code that wires up drawers or palette entries for other node types

2. **Resolve dependencies for bootstrap factories:**
   Each factory requires specific DI dependencies:
   
   ```csharp
   CreateNodeDrawerRegistry(
       IChannelCommandCatalog channelCatalog,
       IEngineEventCatalog engineCatalog,
       IEditService editService,
       IPredicateCompiler predicateCompiler
   )
   ```
   
   - `IChannelCommandCatalog`: Likely `BuiltInChannelCommandCatalog.Instance` or injected
   - `IEngineEventCatalog`: Likely `BuiltInEngineEventCatalog.Instance` or injected
   - `IEditService`: Should already exist in editor services
   - `IPredicateCompiler`: Likely from data breakpoints infrastructure

3. **Wire the registries:**
   ```csharp
   // In EditorSubsystem or BlueprintEditorModule
   var drawerRegistry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(...);
   _inspectorFrame.SetDrawerRegistry(drawerRegistry);  // or equiv
   
   var paletteRegistry = BlueprintEditorBootstrap.CreatePaletteRegistry();
   _paletteHost.RegisterEntries(paletteRegistry.EnumerateAll());
   
   var providers = BlueprintEditorBootstrap.CreateAttachmentProviders(...);
   _canvasHost.RegisterAttachmentProviders(providers);
   
   var renderers = BlueprintEditorBootstrap.CreateCanvasRenderers();
   _canvasHost.RegisterCanvasRenderers(renderers);
   ```

4. **Verify the wiring:**
   - Solution compiles
   - No new warnings or errors
   - DI container can resolve all dependencies
   - `trace_path(WhenNodeDrawer, direction=inbound)` shows production caller

---

### Change 2: Upgrade Tests to True Integration Tests

**Objective:** Replace unit tests with tests that boot real editor and verify end-to-end workflows.

**Implementation requirements:**

1. **Keep** the current unit tests (they validate bootstrap infrastructure)
2. **Add** new integration tests in the same file or new file that:

   **Test 1: Drawer Registration (WHEN-M11-T1)**
   ```csharp
   [Fact]
   public void Integration_EditorBootstrap_DrawerRegistration_WhenNode()
   {
       // Boot real editor harness
       var editor = new BlueprintEditorTestHarness();
       editor.Initialize();  // This calls EditorSubsystem startup
       
       // Create and select a WhenNode
       var graph = editor.CreateTestGraph();
       var whenNode = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ValueChanged, ... };
       graph.AddNode(whenNode);
       
       // Open inspector for the node
       editor.SelectNode(whenNode);
       var drawer = editor.GetSelectedNodeDrawer();
       
       // Assert: drawer is WhenNodeDrawer, not generic fallback
       Assert.NotNull(drawer);
       Assert.IsType<WhenNodeDrawer>(drawer);
   }
   ```

   **Test 2: Palette Entries (WHEN-M11-T2)**
   ```csharp
   [Fact]
   public void Integration_EditorBootstrap_PaletteEntries()
   {
       var editor = new BlueprintEditorTestHarness();
       editor.Initialize();
       
       // Right-click canvas to open palette menu
       var canvasMenu = editor.OpenCanvasPaletteMenu();
       
       // Assert: all three entries present with correct categories
       var entries = canvasMenu.GetMenuItems();
       
       Assert.Contains(entries, e => 
           e.Text == "When" && 
           e.Category == "Reactive Guards");
       
       Assert.Contains(entries, e => 
           e.Text == "ReadEqsResult" && 
           e.Category == "EQS");
       
       Assert.Contains(entries, e => 
           e.Text == "SpawnEqsSensor" && 
           e.Category == "EQS");
   }
   ```

   **Test 3: Attachment Providers (WHEN-M11-T3)**
   ```csharp
   [Fact]
   public void Integration_EditorBootstrap_CanvasAttachments()
   {
       var editor = new BlueprintEditorTestHarness();
       editor.Initialize();
       
       // Verify attachment provider registration via canvas rendering
       var providers = editor.GetAttachmentProviders();
       
       Assert.Contains(providers, p => p is WhenNodeAttachmentProvider);
       Assert.Contains(providers, p => p is CrossAssetDependencyAttachmentProvider);
       Assert.Contains(providers, p => p is EqsTemplateAttachmentProvider);
   }
   ```

   **Test 4: Debug-Mode Pulse Renderer**
   ```csharp
   [Fact]
   public void Integration_EditorBootstrap_WhenFiringPulseRenderer_DebugOnly()
   {
       var editor = new BlueprintEditorTestHarness();
       editor.Initialize();
       
       var renderers = editor.GetCustomCanvasRenderers();
       
   #if DEBUG
       Assert.Contains(renderers, r => r is WhenFiringPulseRenderer);
   #else
       Assert.DoesNotContain(renderers, r => r is WhenFiringPulseRenderer);
   #endif
   }
   ```

2. **Test invocation:**
   - If `BlueprintEditorTestHarness` doesn't exist, look for similar harnesses in other editor tests
   - Or use existing integration test patterns from HSM/BTree editor tests
   - The key difference: tests must call editor initialization code (which now calls `BlueprintEditorBootstrap`)

---

## Success Criteria for Approval

After corrective changes:

- [ ] `BlueprintEditorBootstrap` factory methods are called from `EditorSubsystem` (or equivalent)
  during editor initialization

- [ ] `trace_path(WhenNodeDrawer, direction=inbound)` returns a production caller 
  (EditorSubsystem or similar)

- [ ] New integration tests boot editor harness and verify:
  - WhenNode renders WhenNodeDrawer (not generic fallback)
  - Palette menu contains three entries under correct categories
  - Attachment providers are registered with canvas
  - WhenFiringPulseRenderer only appears in Debug builds

- [ ] Solution compiles without errors or warnings

- [ ] All existing tests still pass (no regression)

---

## Deliverables

Submit:

1. **Updated source files:**
   - `EditorSubsystem.cs` (or equivalent) with bootstrap wiring
   - `WhenNodeEditorWiringTests.cs` updated with integration tests

2. **Updated BATCH-18-REPORT.md:**
   - Append "Corrective" section describing the wiring changes
   - Show integration test results (all passing)
   - Confirm `trace_path` shows production caller

---

## References

- [BATCH-18-REVIEW.md](./reviews/BATCH-18-REVIEW.md) — full list of gaps and requirements
- [TASK-DETAIL.md § M11-T1/T2/T3](../TASK-DETAIL.md) — original success conditions
- [When_Reactivity_Iteration_Design_v2_2.md](../When_Reactivity_Iteration_Design_v2_2.md) — design context
