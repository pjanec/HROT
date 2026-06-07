# BATCH-A: Breakpoint set + render (KEYSTONE)

**Batch Number:** BATCH-A
**Tasks:** Breakpoint context menu + gutter renderer (TASK-DETAIL Batch A)
**Phase:** Slice-1 — Debug UX wiring
**Estimated Effort:** 8-12h
**Priority:** HIGH
**Dependencies:** Batch 0 (✅ done)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Wire the blueprint debug session into the live canvas so right-clicking a node toggles a breakpoint that **actually pauses the live tick**, and a **red gutter bullet** renders on breakpointed nodes. Mirror the proven BTree pattern — the graph model stays debug-unaware; visuals are drawn by renderers reading the session each frame.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE_claude.md` — How to work with batches
2. **Task Details:** `.dev/blueprint-dbg-1/TASK-DETAIL.md` — Batch A section (lines 47-106)
3. **Architect Briefing:** `.dev/blueprint-dbg-1/ARCHITECT-BRIEFING-01.md` — Q1 resolved (lines 34-45), approach (lines 70-82)
4. **Onboarding:** `.dev/blueprint-dbg-1/ONBOARDING.md` — full context

### Source Code Location
- **Primary Work Area:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/`
- **Renderers:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/`
- **Test Project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- **Call site:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- **Diagnostics tests:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs`

### Report Submission
**When done, submit your report to:**
`.dev/blueprint-dbg-1/reports/BATCH-A-REPORT.md`

---

## Context

The debug back-end is fully wired: `BlueprintDebugSession.SetBreakpoint` already registers with the session AND forwards to `IDataBreakpointManager` (dual-store, automatic). `OnNodeEnter` calls `RequestPause()`. The gap is purely the **canvas UX wiring** — the session isn't injected into the blueprint canvas, so there's no way to set a breakpoint from the UI.

The BTree and HSM siblings already have working debug UX on the same shared canvas (`AiGraphCanvasWindow`). Mirror their pattern exactly — don't invent new approaches.

**Key principle:** the graph model stays debug-unaware (`NodeState` never changes). Debug visuals are drawn by **renderers reading the session each frame**.

---

## 🎯 Batch Objectives

1. Inject `IBlueprintDebugSession` into `BlueprintDocumentFactory.Build()` so the canvas knows about the debug session.
2. Wire the existing `BlueprintBreakpointGutterRenderer` into the renderer list (red bullet on breakpointed nodes).
3. Create `BlueprintBreakpointContextMenuProvider` (right-click node → Toggle Breakpoint → calls `session.SetBreakpoint`/`ClearBreakpoint`).
4. Wire the context menu provider into `BlueprintEditorHostServices`.
5. Update the `EditorSubsystem.cs` call site to pass the session.

---

## ✅ Tasks

### 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task A1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task A2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task A3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task A4:** Implement → Write tests → **ALL tests pass** ✅
5. **Task A5:** Implement (no new tests needed) → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including pre-existing tests)

---

### Task A1: Extend `BlueprintDocumentFactory.Build` with debug params

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` — UPDATE

**Template (read first):** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` — lines 79-86 (Build signature), lines 107, 126-127 (how params flow into BuildRenderers and host services).

**Description:**
Add an optional `IBlueprintDebugSession? debugSession = null` parameter to `Build()`. Thread it into:
1. `BuildRenderers()` — so the gutter renderer gets `SetSession(debugSession)`.
2. `BlueprintEditorHostServices` constructor — so it can install the context menu provider.

**Specific changes:**

**A1a.** Add parameter to `Build()` signature (after `behaviorActions`):
```csharp
IBlueprintDebugSession? debugSession = null
```
Also add the using for `Hrot.Blueprints.Core.Debug` if not already present.

**A1b.** Pass `debugSession` to `BuildRenderers(extraRenderers, debugSession)`.

**A1c.** After creating `hostServices`, call a new method to install the context menu provider:
```csharp
if (debugSession != null)
    hostServices.SetBreakpointContextMenu(debugSession, bpAsset);
```

---

### Task A2: Extend `BuildRenderers` to include gutter renderer

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` — UPDATE the private `BuildRenderers` method.

**Description:**
The `BlueprintBreakpointGutterRenderer` already exists at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/BlueprintBreakpointGutterRenderer.cs`. Wire it into the renderer list.

**Specific changes:**

Change `BuildRenderers` signature to:
```csharp
private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
    IReadOnlyList<ICustomCanvasRenderer>? extra,
    IBlueprintDebugSession? debugSession = null)
```

Add the gutter renderer (AfterNodes pass, before `WhenFiringPulseRenderer` or replace it):
```csharp
var gutterRenderer = new BlueprintBreakpointGutterRenderer(bpAsset);
if (debugSession != null)
    gutterRenderer.SetSession(debugSession);

var list = new List<ICustomCanvasRenderer>
{
    gutterRenderer,                            // AfterNodes — red bullet for breakpoints
    new WhenFiringPulseRenderer(),             // AfterNodes — WhenNode firing pulse
};
```

**Note:** `BlueprintBreakpointGutterRenderer` requires a `BlueprintAsset` in its constructor, but `BuildRenderers` is a private static method that doesn't currently have access to the asset. You'll need to pass it in OR restructure. The cleanest approach: pass `bpAsset` to `BuildRenderers` as an additional parameter, or move the gutter renderer creation inline in `Build()` before calling `BuildRenderers`.

Actually, `bpAsset` is available in `Build()` but NOT in `BuildRenderers()`. The `WhenFiringPulseRenderer` doesn't need the asset, but the gutter renderer does. So either:
- Pass `bpAsset` to `BuildRenderers`, or
- Create the gutter renderer in `Build()` and pass it as an extra renderer.

**The simplest approach (recommended):** pass `BlueprintAsset bpAsset` as a parameter to `BuildRenderers`.

**Verify the existing renderer works correctly.** Read `BlueprintBreakpointGutterRenderer.cs` (already created as WIP). It:
- Has `SetSession(IBlueprintDebugSession?)` 
- Has `IsActive => _session != null`
- `Render()` iterates `_session.GetBreakpoints()`, filters by `AssetId`, looks up nodes via `FindNode(nodeId)` which searches `_asset.Graphs[].Nodes[]`
- Draws a red filled circle

This looks correct. If any issue is found (e.g., `FindNode` perf could be improved with a dictionary), note it but don't block — the BTree renderer does O(n) lookup too.

---

### Task A3: Create `BlueprintBreakpointContextMenuProvider`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintBreakpointContextMenuProvider.cs` — NEW FILE

**Templates (read first):**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeBreakpointContextMenuProvider.cs` — the pattern to mirror
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomElementContextMenuProvider.cs` — the interface
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorHostServices.cs:56` — `CustomElementContextMenu` property

**Description:**
Create a context menu provider that implements `ICustomElementContextMenuProvider`. When the user right-clicks a node, it offers "Toggle Breakpoint" (or "Set Breakpoint" / "Clear Breakpoint" depending on state).

**Key design decisions (from Q1 — RESOLVED):**
- The provider takes `IBlueprintDebugSession` (NOT `IDataBreakpointManager`).
- `session.SetBreakpoint(assetId, graphId, nodeId)` already handles dual-registration automatically.
- The provider needs to know the `assetId` and `graphId` of the canvas being viewed, plus a way to get the `nodeId` from the `elementKey` string.

**Interface to implement:**
```csharp
public interface ICustomElementContextMenuProvider
{
    string RendererId { get; }
    IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit);
}
```

**Implementation sketch:**
```csharp
internal sealed class BlueprintBreakpointContextMenuProvider : ICustomElementContextMenuProvider
{
    private readonly IBlueprintDebugSession _session;
    private readonly Guid _assetId;
    private readonly Guid _graphId;

    public BlueprintBreakpointContextMenuProvider(
        IBlueprintDebugSession session,
        Guid assetId,
        Guid graphId)
    {
        _session = session;
        _assetId = assetId;
        _graphId = graphId;
    }

    // RendererId must match the gutter renderer's Id so NodeEdit can route
    // context menu requests to this provider.
    public string RendererId => "blueprint.breakpoint_gutter";

    public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
    {
        // elementKey for a gutter hit should be the node id string.
        // Parse it as a Guid (node id).
        if (!Guid.TryParse(elementKey, out var nodeId))
            return Array.Empty<ContextMenuItem>();

        // Check if there's already a breakpoint on this node
        var existing = _session.GetBreakpoints()
            .FirstOrDefault(bp => bp.AssetId == _assetId 
                               && bp.GraphId == _graphId 
                               && bp.NodeId == nodeId.ToString("D"));

        var items = new List<ContextMenuItem>();
        if (existing != null)
        {
            var bpId = existing.Id;
            items.Add(new ContextMenuItem("Clear Breakpoint", () => _session.ClearBreakpoint(bpId)));
        }
        else
        {
            items.Add(new ContextMenuItem("Set Breakpoint", 
                () => _session.SetBreakpoint(_assetId, _graphId, nodeId)));
        }
        return items;
    }
}
```

**IMPORTANT — NodeEdit context menu routing:**
The `ICustomElementContextMenuProvider` gets invoked when the user right-clicks an element rendered by the custom renderer with the matching `RendererId`. The BTree gutter renderer encodes the node's VisualId as the element key. For the blueprint gutter renderer to work with this, the gutter renderer may need to register its drawn elements as hit-testable. Check the `ICustomCanvasHitTester` and `ICustomCanvasSelectable` interfaces on `ICustomCanvasRenderer.cs`.

Actually, looking at the BTree gutter renderer more carefully: the BTree context menu provider creates a `stubNode` with `VisualId = Guid.TryParse(elementKey, out var g) ? g : Guid.Empty` — it doesn't use the gutter renderer's hit-test at all. The context menu is triggered by NodeEdit's own node hit-testing, not the custom renderer. **The `RendererId` property must match the gutter renderer's `Id` so NodeEdit can associate the provider with the renderer.**

For the blueprint case, the gutter renderer `Id` is `"blueprint.breakpoint_gutter"`. The `elementKey` that NodeEdit passes to `GetItemsFor` will be the node's element key (likely the node id as a string). Verify by reading how NodeEdit's context menu system calls `GetItemsFor` — check the BTree provider test or the demo.

**Actually — critical check:** The BTree `BTreeBreakpointContextMenuProvider` delegates to `BTreeBreakpointMenuPopulator.PopulateMenu` which uses `IDataBreakpointManager.AddBreakpoint` directly. The blueprint version should instead use `IBlueprintDebugSession.SetBreakpoint`/`ClearBreakpoint`. Do NOT call `BlueprintBreakpointMenuPopulator` from the new provider — that populator creates conditional data breakpoints (Slice-2), not simple node breakpoints.

---

### Task A4: Extend `BlueprintEditorHostServices` for context menu provider

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEditorHostServices.cs` — UPDATE

**Template:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs:71-91` — `SetBreakpointManager()` and `CustomElementContextMenu` property.

**Description:**
Add a field for the context menu provider and a setter method. Expose it via `IEditorHostServices.CustomElementContextMenu`.

**Specific changes:**

Add a private field:
```csharp
private ICustomElementContextMenuProvider? _bpContextMenuProvider;
```

Add a setter method:
```csharp
/// <summary>
/// Installs the breakpoint context menu provider so right-clicking a node
/// offers breakpoint toggle actions via <see cref="IBlueprintDebugSession"/>.
/// </summary>
public void SetBreakpointContextMenu(ICustomElementContextMenuProvider? provider)
    => _bpContextMenuProvider = provider;
```

Explicitly implement the interface property (add to the class):
```csharp
ICustomElementContextMenuProvider? IEditorHostServices.CustomElementContextMenu 
    => _bpContextMenuProvider;
```

**Note:** The `BlueprintEditorHostServices` already has `IDebugSession? _debug` and `SetDebugSession()`. Those are for NodeEdit's own debug overlay (the `IDebugSession` used by the canvas framework for the executing-node highlight). The `_bpContextMenuProvider` is a separate concern — it provides the right-click menu for breakpoint toggle. Both should coexist.

---

### Task A5: Update `EditorSubsystem.cs` call site

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — UPDATE (lines ~2394-2401)

**Description:**
Pass `_blueprintDebugSession` to the `BlueprintDocumentFactory.Build()` call.

**Current call (line ~2394):**
```csharp
doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
    doc.Asset, adapterBundle, _blueprintEditService,
    _blueprintPaletteEntries,
    channelCommands: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance,
    peerAssetCatalog: blueprintPeerCatalog,
    behaviorActions: _behaviorActionCatalog);
```

**Updated call:**
```csharp
doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
    doc.Asset, adapterBundle, _blueprintEditService,
    _blueprintPaletteEntries,
    channelCommands: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance,
    peerAssetCatalog: blueprintPeerCatalog,
    behaviorActions: _behaviorActionCatalog,
    debugSession: _blueprintDebugSession);
```

**Verify:** `_blueprintDebugSession` is declared at line 183 and assigned at line 897. It's accessible within the `RegisterWindows` method (where the Build call is). Use the existing field — no need to add anything.

**No new tests needed** for this task (integration wiring — tested via the existing boot test).

---

### Task A6: Mark `BlueprintBreakpointMenuPopulator` as superseded

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintBreakpointMenuPopulator.cs` — UPDATE (add comment only)

**Description:**
The `BlueprintBreakpointMenuPopulator` is now superseded by `BlueprintBreakpointContextMenuProvider` for the live canvas UX. It is still referenced by `Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs` (which tests the `IDataBreakpointManager` conditional-breakpoint path — a Slice-2 concern). **Do NOT delete it.** Add a comment at the top:

```csharp
// SUPERSEDED for canvas UX: the live canvas now uses
// Host.BlueprintBreakpointContextMenuProvider (ICustomElementContextMenuProvider) which calls
// IBlueprintDebugSession.SetBreakpoint/ClearBreakpoint (dual-store automatic per Q1).
// This static populator remains for IDataBreakpointManager-based conditional data
// breakpoints (Slice-2) and is still tested by BlueprintContextMenuTests.
```

---

## 🧪 Testing Requirements

**Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Tests for Task A2 (Gutter Renderer — use existing file or create new one)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/Renderers/BlueprintBreakpointGutterRendererTests.cs` — NEW FILE

**Use existing test infrastructure:** The test project already has `CapturingDebugSession` in the `Debug/` directory. Check its API before writing tests.

1. **`GutterRenderer_IsActive_False_WhenNullSession`**
   - Create `BlueprintBreakpointGutterRenderer` with a minimal `BlueprintAsset`
   - Assert `IsActive == false` (no session set)

2. **`GutterRenderer_IsActive_True_WhenSessionSet`**
   - Call `SetSession(capturingSession)`
   - Assert `IsActive == true`

3. **`GutterRenderer_DoesNotThrow_WhenRenderingWithNoBreakpoints`**
   - Set a session with no breakpoints
   - Call `Render()` — should not throw (even with a headless/mock render context)

### Tests for Task A3 (Context Menu Provider)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/Host/BlueprintBreakpointContextMenuProviderTests.cs` — NEW FILE

Use `CapturingDebugSession` (or create a minimal mock) to verify the menu provider.

4. **`ContextMenuProvider_SetBreakpoint_CallsSessionSetBreakpoint`**
   - Create provider with a test session
   - Call `GetItemsFor(nodeIdString, hit)` where no breakpoint exists
   - Assert the returned items contain "Set Breakpoint"
   - Execute the callback → assert `session.GetBreakpoints()` now includes the breakpoint

5. **`ContextMenuProvider_ClearBreakpoint_CallsSessionClearBreakpoint`**
   - Pre-register a breakpoint on the session for the test node
   - Call `GetItemsFor(nodeIdString, hit)`
   - Assert the returned items contain "Clear Breakpoint"
   - Execute the callback → assert `session.GetBreakpoints()` no longer includes it

6. **`ContextMenuProvider_NoItems_ForNullSession`**
   - If the provider handles null session gracefully, test it
   - OR: the provider is never created with a null session (factory doesn't create it)

7. **`ContextMenuProvider_RendererId_MatchesGutterRenderer`**
   - Assert `provider.RendererId == "blueprint.breakpoint_gutter"`

### Tests for Task A4 (Factory integration)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/Host/BlueprintDocumentFactoryTests.cs` — UPDATE or NEW FILE

8. **`Build_WithDebugSession_IncludesGutterRendererInCustomRenderers`**
   - Call `BlueprintDocumentFactory.Build()` with a `debugSession` (use `CapturingDebugSession`)
   - Assert that `context.View` host services' `CustomCanvasRenderers` includes a `BlueprintBreakpointGutterRenderer`

9. **`Build_WithDebugSession_SetsContextMenuProvider`**
   - Call `Build()` with `debugSession`
   - Assert that `IEditorHostServices.CustomElementContextMenu` is non-null
   - Assert it's a `BlueprintBreakpointContextMenuProvider`

10. **`Build_WithoutDebugSession_NoContextMenuProvider`**
    - Call `Build()` without `debugSession`
    - Assert `CustomElementContextMenu` is null

### Test quality notes:
- Use real `BlueprintAsset` with at least one graph and one node (use `BlueprintAssetBuilder` from the test helpers).
- Use `CapturingDebugSession` — it already exists in the test project. Study its API.
- For headless `Render()` tests: you may need a minimal `ICanvasRenderContext` stub/mock. If creating one is complex, focus tests on `IsActive`, `SetSession`, and provider tests (which don't need rendering).
- The goal is to verify the wiring, not to render pixels.

---

## ⚠️ Common Pitfalls to Avoid

1. **Do NOT mutate `NodeState` in the graph model.** The graph model stays debug-unaware. All debug visuals come from renderers.
2. **Do NOT wire `IDataBreakpointManager` into the context menu provider.** The session's `SetBreakpoint` already forwards to the manager. Adding a manager reference would be redundant.
3. **Do NOT delete `BlueprintBreakpointMenuPopulator`.** It's still tested and serves a different purpose (conditional data breakpoints).
4. **Do NOT change the namespace of existing files.** Keep `Hrot.Blueprints.Editor.Host` for host services and providers.
5. **Do NOT import `Fdp.Toolkits` types in netstandard2.0 paths.** The editor project is net8, so this is fine — but be aware.
6. **Match the existing `BlueprintBreakpointGutterRenderer`'s use of `bp.NodeId`.** The `Breakpoint` record uses `string NodeId` (not `Guid`). The gutter renderer already compares `bp.NodeId` with `node.Id` — verify this matches how `SetBreakpoint` stores the node id (it takes a `Guid nodeId` — check if it's stored as `ToString("D")` or `ToString()`).
7. **The gutter renderer `FindNode` searches ALL graphs.** This is O(n) but fine for now — mirror the BTree pattern.
8. **Context menu routing via `RendererId`:** Verify how NodeEdit routes context menu requests. The `RendererId` on the provider must match the gutter renderer's `Id` for NodeEdit to associate them. If NodeEdit uses a different mechanism to route context menus (e.g., based on the element type rather than the renderer), adapt accordingly.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `BlueprintDocumentFactory.Build()` accepts `debugSession` param and threads it to renderers + host services
- [ ] `BlueprintBreakpointGutterRenderer` is wired into the renderer list with `SetSession`
- [ ] `BlueprintBreakpointContextMenuProvider` is created and implements `ICustomElementContextMenuProvider`
- [ ] `BlueprintEditorHostServices` exposes the context menu provider via `IEditorHostServices.CustomElementContextMenu`
- [ ] `EditorSubsystem.cs` passes `_blueprintDebugSession` to `BlueprintDocumentFactory.Build()`
- [ ] All new tests pass
- [ ] **Build 0/0:** `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors, 0 warnings
- [ ] **Blueprints tests:** `Hrot.Blueprints.Tests` → 7 pre-existing failures, 0 new
- [ ] **AiShared tests:** `Hrot.Editor.AiShared.Tests` → all pass
- [ ] **Boot test:** `EditorSubsystemBoot` → 10/10
- [ ] **Diagnostics tests:** `Hrot.Diagnostics.Breakpoints.Tests` → existing tests still pass (we didn't break `BlueprintContextMenuTests`)

---

## 📊 Report Requirements

Fill every section per DEV-GUIDE_claude.md §4. Be honest about what works and what doesn't.

---

## 📚 Reference Materials
- **Task Details:** `.dev/blueprint-dbg-1/TASK-DETAIL.md` — Batch A section (lines 47-106)
- **Architect Briefing:** `.dev/blueprint-dbg-1/ARCHITECT-BRIEFING-01.md` — Q1 resolved, approach
- **BTree Template:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs`
- **BTree Template:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs`
- **BTree Template:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeBreakpointContextMenuProvider.cs`
- **BTree Template:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs`
- **Existing blueprint renderer:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/BlueprintBreakpointGutterRenderer.cs`
- **Existing host services:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEditorHostServices.cs`
- **Existing factory:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`
- **Call site:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (~lines 893, 2394)
- **Debug session interface:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
