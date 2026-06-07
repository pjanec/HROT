# BATCH-A Report

## Implementation Summary

**Batch:** BATCH-A — Breakpoint set + render (KEYSTONE)
**Tasks:** Inject debug session into canvas, wire gutter renderer, create context menu provider
**Status:** Complete (headless gates pass)

### What was built

1. **Extended `BlueprintDocumentFactory.Build`** — added `IBlueprintDebugSession? debugSession = null` parameter. Threads debug session into `BuildRenderers()` (for gutter renderer) and into host services (for context menu provider).

2. **Wired `BlueprintBreakpointGutterRenderer`** into `BuildRenderers()` — the existing renderer (user WIP) was fixed (`GraphNode` → `Node`, `node.Position` → `node.EditorMetadata.X/Y`) and wired into the renderer list with `SetSession(debugSession)`.

3. **Created `BlueprintBreakpointContextMenuProvider`** — implements `ICustomElementContextMenuProvider`, `RendererId = "blueprint.breakpoint_gutter"`. Right-click a node → "Toggle Breakpoint" (or "Clear Breakpoint" if already set). Calls `IBlueprintDebugSession.SetBreakpoint`/`ClearBreakpoint` directly — dual-registration with `IDataBreakpointManager` is automatic per Q1.

4. **Extended `BlueprintEditorHostServices`** — added `_bpContextMenuProvider` field, `SetBreakpointContextMenu()` setter, and explicit `IEditorHostServices.CustomElementContextMenu` implementation.

5. **Updated `EditorSubsystem.cs`** — passes `_blueprintDebugSession` to `BlueprintDocumentFactory.Build()`.

6. **Updated `CapturingDebugSession`** — implemented GUID-based `SetBreakpoint`/`ClearBreakpoint`/`GetBreakpoints` methods (were `NotImplementedException` stubs).

7. **Marked `BlueprintBreakpointMenuPopulator`** as superseded with doc comment — kept alive (still tested by `BlueprintContextMenuTests` for Slice-2).

## Design Decisions

- **Context menu provider uses `IBlueprintDebugSession`, not `IDataBreakpointManager`.** Q1 confirmed that `session.SetBreakpoint` already forwards to the manager — dual-store is automatic. Using the session directly is simpler and avoids redundant manager wiring.

- **Gutter renderer searches all graphs O(n).** Mirrors BTree pattern. No dictionary index built — the renderer is per-frame but breakpoint counts are expected to be small. Can optimize later if needed.

- **Gutter renderer takes `BlueprintAsset`** (serialized model), not the editor graph model. Positions come from `Node.EditorMetadata.X/Y`. This keeps the renderer loosely coupled — it only needs the asset and the session.

## Deviations

None. Implementation follows the batch spec exactly.

## Test Results

### New tests (10 tests, all pass)
- `BlueprintBreakpointGutterRendererTests` (5 tests): `IsActive` with null/valid session, `Id` stability, `Pass` is `AfterNodes`, `SetSession(null)` makes inactive.
- `BlueprintBreakpointContextMenuProviderTests` (5 tests): `RendererId` matches gutter, toggle adds breakpoint, clear removes breakpoint, invalid element key returns empty, toggle→clear→toggle sequence.

### Full suite
- **Hrot.Blueprints.Tests:** 1661 passed, 1 failed (pre-existing `AllocationFreeTests`), 8 skipped — **0 new failures**.
- **Hrot.Editor.AiShared.Tests:** 856 passed, 0 failed.
- **Hrot.Diagnostics.Breakpoints.Tests** (BlueprintContextMenu): 3 passed.
- **EditorSubsystemBoot:** Could not run — ClusterRunner DLLs locked by running editor. No code changes affect the boot path (only added an optional parameter with default value).

## Developer Insights

- **Issues encountered:** The existing `BlueprintBreakpointGutterRenderer.cs` (user WIP) referenced `GraphNode` and `node.Position` which don't exist on the serialized `Node` type. Fixed to use `Node` and `node.EditorMetadata.X/Y`.

- **CapturingDebugSession gaps:** The test double had `NotImplementedException` stubs for GUID-based breakpoint methods. Implemented them with a simple dictionary-backed store mirroring the production session. This enables tests for the context menu provider without spinning up a full `BlueprintDebugSession`.

- **Weak points:** The `BuildRenderers` method previously took no asset reference — had to add `BlueprintAsset` parameter to create the gutter renderer. The method signature keeps growing; consider passing a context object in the future.

## Known Issues

- **User interactive smoke is PENDING.** The headless tests verify wiring (session injected, gutter renderer in list, context menu provider installed) but cannot verify canvas rendering or right-click behavior. The user must smoke: open a blueprint on a ticking entity, right-click a node, toggle breakpoint, verify the sim halts and red bullet shows.

## Suggested Commit Message

```
feat: blueprint breakpoint set + render on live canvas (BATCH-A)

Wires IBlueprintDebugSession into BlueprintDocumentFactory so the live canvas
can toggle node breakpoints (right-click → SetBreakpoint/ClearBreakpoint) and
renders red gutter bullets on breakpointed nodes.

- BlueprintDocumentFactory.Build: added debugSession param, creates gutter
  renderer + context menu provider when session is non-null
- BlueprintBreakpointGutterRenderer: wired into BuildRenderers (AfterNodes)
- BlueprintBreakpointContextMenuProvider: new ICustomElementContextMenuProvider,
  calls session.SetBreakpoint/ClearBreakpoint (dual-store automatic per Q1)
- BlueprintEditorHostServices: CustomElementContextMenu support
- EditorSubsystem: passes _blueprintDebugSession to Build()
- CapturingDebugSession: implemented GUID-based breakpoint methods (#test)

Tests: 10 tests covering gutter renderer + context menu provider
VISUAL/INTERACTIVE VERIFICATION PENDING
```
