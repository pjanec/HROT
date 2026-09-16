# RHS-02 (+03) — Re-anchor all HSM custom renderers off real canvas geometry

**Workstream:** RHS (../RHS-PLAN.md). **Layer:** Hrot.Hsm.Editor renderers. **Depends:** RHS-01 (committed `ac9770b7`). Folds in RHS-03 (transition labels) — same change pattern, disjoint files, one verification.

## Problem

Every HSM custom renderer computes screen geometry as `ctx.Viewport.GraphToScreen(state.Position + …)`. For container-child states, `state.Position` is **interior-LOCAL**, so `GraphToScreen` of it lands at the wrong absolute spot → glyphs/arrows/labels float detached from their nodes (REVIEW-HS / VE-DEBT-007).

RHS-01 added to `ICanvasRenderContext`:
```csharp
bool TryGetNodeScreenRect(NodeId id, out RectF screenRect);     // SCREEN coords, container-resolved
bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos);  // SCREEN coords
```
These return the canvas's authoritative per-frame geometry. **All HSM renderers must anchor off these instead of transforming raw `Position`.**

## The rule (apply uniformly)

For a state's screen geometry: `ctx.TryGetNodeScreenRect(new NodeId(state.StableId), out var rect)`.
- `rect` is already screen-space (zoom/pan + container offset applied). Center = `rect.Min + rect.Size * 0.5f`. Top-center = `new Vector2(rect.Min.X + rect.Size.X*0.5f, rect.Min.Y)`. Etc.
- **If `TryGet…` returns false** (node culled or hidden in a collapsed parent), **skip drawing that element** — do NOT fall back to `GraphToScreen(Position)`.
- Do NOT multiply `rect` dimensions by `ctx.Zoom` again — it's already scaled. Glyph radii / stroke widths that are authored in graph units still scale by `ctx.Zoom` as today.

## Files & required changes

### 1. `Renderers/HsmHistoryGlyphsRenderer.cs`
- Replace `var center = ctx.Viewport.GraphToScreen(state.Position + size * 0.5f);` with center from `TryGetNodeScreenRect`. Drop the `DefaultNodeSize`/`SizeOverride` math for centering.
- **Counter discipline:** `LastGlyphCount` must still count *eligible* glyphs (history/final states) so the existing count-based tests pass. Increment it when the state is eligible (after the `IsHistory||IsDeepHistory||IsFinal` filter), BEFORE the geometry `TryGet`; gate only the actual `DrawList` calls on a successful `TryGet`.

### 2. `Renderers/HsmInitialArrowRenderer.cs`
- `Render`: for each marker, get the initial child's screen rect via `TryGetNodeScreenRect(child.StableId)`; compute the circle/arrow from the rect's top-center (circle `MarkerGap` px above top edge, arrow down to top edge). Keep `MarkerGap`/`MarkerRadius`/etc. scaled by `ctx.Zoom`. Skip the marker if `TryGet` fails.
- `DrawLcaOutline`: replace `GraphToScreen(lca.Position)` / `+ size` with the LCA's screen rect (`rect.Min` / `rect.Max`). Skip if `TryGet` fails.
- Keep `ComputeMarkerGeometry` as a pure helper IF the geometry unit tests use it — but it now must operate in SCREEN space (take the child screen rect, return screen circle/arrow points), OR be bypassed. Preserve the existing `HsmInitialArrowGeometryTests` intent: if those tests assert the relative geometry (circle above, arrow to top edge), refactor them to feed a screen rect. State in your report exactly how you kept those tests meaningful.

### 3. `Renderers/HsmTransitionLabelRenderer.cs`  (RHS-03)
- External transitions: replace the midpoint `GraphToScreen((Source.Position + Target.Position)*0.5)` with the **true wire midpoint**: midpoint of `TryGetPinScreenPosition(Source.HiddenOutputPinId)` and `TryGetPinScreenPosition(Target.HiddenInputPinId)`. If either pin lookup fails, fall back to the midpoint of the two nodes' screen-rect centers; if those also fail, skip the label.
- Internal transitions: the self-loop placement currently uses `GraphToScreen(Source.Position)`/`+size` → use `Source`'s screen rect (upper-right quadrant). Skip if `TryGet` fails.
- **Counter discipline:** `LastLabelCount` / `LastInternalTransitionCount` must reflect eligible transitions (count after `FindTransitionByVisualId`, before geometry gating) so existing tests pass.

### 4. `Renderers/HsmRegionConflictsRenderer.cs` and `Renderers/HsmBreakpointGutterRenderer.cs`
- Read both. Wherever they transform a state's raw `Position` to place a line/marker/gutter, switch to `TryGetNodeScreenRect`. Apply the same skip-on-false rule and counter discipline (any eligible-count seam stays based on the logical filter, not geometry availability).
- If a renderer does NOT use `state.Position` for placement (e.g. purely reads diagnostics), leave it unchanged and say so in the report.

## Tests

- The existing HSM renderer tests use stub `ICanvasRenderContext`s (RHS-01 stubbed `TryGet…`→false). With this change, those stubs make renderers skip drawing → count-based asserts must still pass via the counter-discipline above. Where a test asserts actual draw geometry, update its stub to seed `TryGetNodeScreenRect`/`TryGetPinScreenPosition` with known rects/positions and assert the renderer reads them (this is the real regression guard for VE-DEBT-007).
- Keep all existing tests meaningful — do not delete a test to make it pass; adapt its stub to the new geometry source.

## Verification (run + paste raw output)

1. `dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj -c Debug -v q -nologo` → 0 errors.
2. `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj -c Debug --nologo -v q` → report pass/fail. Baseline before this batch = **458/0**; result must be ≥458 passing, 0 failing (new tests may raise the total).

## Out of scope (do NOT touch)

- Theming / node colors (RHS-04), region divider rendering (RHS-05), showcase JSON (RHS-06).
- `HsmAsset.cs`, `HsmEditorTheme.cs`, NodeEditor library (RHS-01 is done — consume it, don't change it).
- `HsmRuntimeOverlayRenderer.cs` unless it places via raw `Position` — if it does, apply the same rule; if it's debug-session-gated and uses pin/rect already, leave it.

## Report back

Per-file diff summary; for each of the 6 renderers state whether it changed and how; how you kept each existing test meaningful; raw build + test output. Do NOT commit — lead reviews & commits.
