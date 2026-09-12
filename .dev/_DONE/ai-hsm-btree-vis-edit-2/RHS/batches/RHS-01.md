# RHS-01 — Expose per-frame layout geometry on `ICanvasRenderContext`

**Workstream:** RHS (see ../RHS-PLAN.md). **Layer:** NodeEditor core (shared infra — BTree/HSM/Blueprint all consume it). **Depends:** none. **Keystone:** unblocks RHS-02/03.

## Goal

Custom canvas renderers currently cannot ask the canvas where a node/pin was actually drawn this frame (especially container-children, whose `Position` is interior-LOCAL). They guess from asset space and render detached. Fix the **enabler only**: add read accessors to the render context that surface the canvas's already-computed per-frame screen geometry. **Additive, behavior-preserving** — no existing renderer changes behavior in this batch.

## Files (primary)

- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICanvasRenderContext.cs` — add two members.
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderContextImpl.cs` — implement; gain a `CanvasLayout` reference via `BeginFrame`.
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs` — pass `_layout` into `_renderCtx.BeginFrame(...)` (call site ~line 240; `_layout` is built at ~line 227, before BeginFrame).

## Exact API (locked — do not redesign)

Add to the `ICanvasRenderContext` interface:

```csharp
/// <summary>
/// Screen-space bounding rect of a node as laid out this frame (post pan/zoom,
/// container position resolved). Returns false if the node was not laid out
/// (e.g. hidden inside a collapsed parent, or unknown id).
/// </summary>
bool TryGetNodeScreenRect(NodeId id, out RectF screenRect);

/// <summary>
/// Screen-space attachment point of a pin as laid out this frame.
/// Returns false if the pin was not laid out.
/// </summary>
bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos);
```

`RectF` is `NodeEditor.Primitives.RectF`; `NodeId`/`PinId` are `NodeEditor.Primitives`. These are SCREEN coords (already zoom/pan applied), matching `CanvasLayout.NodeScreenRects` / `PinScreenPositions`.

## Implementation

1. **Interface:** add the two members to `ICanvasRenderContext`. Leave `IHitTestContext` untouched (hit-test already gets what it needs).
2. **CanvasRenderContextImpl:**
   - Add an internal field `internal CanvasLayout? _layout;`.
   - Extend `BeginFrame(...)` with a `CanvasLayout layout` parameter and assign `_layout = layout;`.
   - Implement:
     ```csharp
     public bool TryGetNodeScreenRect(NodeId id, out RectF screenRect)
     {
         if (_layout != null && _layout.NodeScreenRects.TryGetValue(id, out screenRect)) return true;
         screenRect = default; return false;
     }
     public bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos)
     {
         if (_layout != null && _layout.PinScreenPositions.TryGetValue(id, out screenPos)) return true;
         screenPos = default; return false;
     }
     ```
   - Add `using NodeEditor.Core.Canvas;` / `using System.Numerics;` as needed (`CanvasLayout` is `internal` in `NodeEditor.UI.Canvas`; `CanvasRenderContextImpl` is in the same namespace — fine).
3. **CanvasRenderer.cs:** update the `_renderCtx.BeginFrame(view, dl, visibleNodeIds, visibleLinkIds)` call (~line 240) to pass `_layout` too. `_layout` is the field built one step earlier by `_layoutBuilder.Build(...)`.

## Other `ICanvasRenderContext` implementers — MUST find & satisfy

Search the whole repo for other implementers of `ICanvasRenderContext` (interface change is binding). Known candidates: NodeEditor demo (`NodeEditor.Demo`), `FakeHostServices`, any test doubles, and possibly HSM/BTree/Blueprint test fakes. Each must implement the two new members (a trivial `screenRect = default; return false;` stub is acceptable for fakes/tests). **Do not** leave any implementer uncompiled. List every file you touched for this in the report.

## Acceptance / verification (the agent MUST run these and paste raw output)

1. `dotnet build` the NodeEditor.UI project AND `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and the Blueprint editor host project — all **0 errors**. (The interface is additive but binding; prove nothing downstream broke.)
2. `dotnet test` `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests` — report pass/fail counts. If a test fake implements the interface and needed the new members, that's expected churn; note it.
3. Add a focused unit test in NodeEditor.UI.Tests proving the accessors return the laid-out rect/pin for a simple two-node + one-link graph after a render/layout pass (mirror an existing CanvasLayout/render test's setup). If wiring a full render in a unit test isn't feasible with existing harnesses, instead add a test that constructs a `CanvasRenderContextImpl`, calls `BeginFrame` with a hand-populated `CanvasLayout`, and asserts the accessors return the seeded values + false for unknown ids. State which approach you used and why.

## Out of scope (do NOT touch)

- Any HSM/BTree renderer behavior (that's RHS-02/03).
- Theming, region dividers, showcase data.
- `HsmEditorTheme`, `HsmAsset`, `HsmShowcase.hsm.json`.

## Report back

- Diff summary per file; the full list of `ICanvasRenderContext` implementers found and how each was satisfied; raw build + test output; which test approach you used. Do NOT commit — the lead reviews and commits.
