# RHS — HSM Canvas Render-Geometry & Theming (REVIEW-HS remediation)

> **Origin:** REVIEW-HS visual review (2026-06-13). The HSM showcase canvas renders correct *container nesting* but everything overlaid on the nodes (transition wires, labels, initial-state arrows, H/F glyphs, reroute dots) appears in the wrong coordinate space and detached; states have no theming.
> **Decision (user):** Option 1 — do it properly. Extend NodeEditor so custom renderers can anchor off the canvas's real per-frame geometry, then re-anchor the HSM renderers; add state theming + region dividers.
> **Companions:** [../DEBT-TRACKER.md](../DEBT-TRACKER.md) (VE-DEBT-007) · design [../../../docs/blueprints/HSM_Editor_NodeEditor_Host_Design.md](../../../docs/blueprints/HSM_Editor_NodeEditor_Host_Design.md) §7,§8,§15 · [../../../docs/blueprints/NodeEditor_Extension_CustomCanvasRenderer.md](../../../docs/blueprints/NodeEditor_Extension_CustomCanvasRenderer.md)
> **Execution:** lead writes batch specs + hard-verifies; coding via sonnet agents (user-authorized). Visual milestones gated by user screenshot.

---

## Root cause (confirmed in code, REVIEW-HS)

NodeEditor's container layout treats a **child node's `Position` as interior-LOCAL** (parent interior-origin + local offset — `CanvasLayout.GetVisualCanvasPosition`, `CanvasLayout.cs:256-316`). The HSM custom renderers instead read the **raw asset `StateNode.Position` / transition `Waypoints` and `GraphToScreen` them as if absolute** (e.g. `HsmInitialArrowRenderer.cs:46-50`). So:

- Container boxes render correctly (NodeEditor places + auto-resizes them).
- Glyphs / arrows / labels land at the *authored* coords → detached "floating debris" in the top-left.
- Wires route from correct pins **through** absolute-authored waypoints → wild detours.
- Blue dots = reroute waypoint markers drawn at absolute waypoint coords (`WireRenderer.cs:128`).

**Enabler:** `ICanvasRenderContext` (`ICanvasRenderContext.cs`) exposes only `Viewport`/`CanvasToScreen` — **not** the canvas's computed `NodeScreenRects`/`PinScreenPositions`. Renderers literally cannot ask where a nested node was actually drawn, so they guess from asset space.

Independent gaps: (B) all states hardcode `Category => NodeCategory.Custom` → theme maps to transparent/gray (`HsmAsset.cs:753`, `HsmEditorTheme.cs:43`); (C) parallel region dividers not rendered.

---

## Canonical coordinate convention (decision — enforce everywhere)

1. **Child `StateNode.Position` = interior-local** offset within its parent container; **top-level state `Position` = absolute canvas.** Matches NodeEditor's existing container math — no conversion layer added. (Supersedes the absolute-position examples in HSM host design §4.1 for *child* states; document there if/when that doc is revised.)
2. **Transition `Waypoints` = absolute graph coords** (same space as reroute dots + resolved pin positions). Empty waypoints ⇒ straight bezier. The showcase's mis-authored waypoints are dropped/fixed in RHS-06.
3. **Custom canvas renderers anchor off the canvas-computed absolute screen geometry** (RHS-01 accessors) — never raw `Position`/`Waypoints` transformed by hand.

---

## Tasks

| ID | Layer | Title | Depends | Status |
|---|---|---|---|---|
| RHS-01 | NodeEditor core | Expose per-frame layout geometry (node screen rects + pin screen positions) on `ICanvasRenderContext` | — | ✅ DONE — `TryGetNodeScreenRect`/`TryGetPinScreenPosition` added; 6 implementers satisfied (5 test fakes stubbed); NodeEditor.UI.Tests 70/0; BTree/HSM/Blueprint editors + their test projects build clean |
| RHS-02 | HSM renderers | Re-anchor initial-arrow / history-glyph / region-conflict / breakpoint-gutter / runtime-overlay renderers off RHS-01 geometry | RHS-01 | ✅ DONE — all 6 renderers now anchor off `TryGetNodeScreenRect`; skip-on-cull; counter discipline preserved; HSM tests 458/0. (Folded RHS-03.) Minor follow-up: no render-ctx-seeded regression test added — RHS-06 visual gate is the proof. |
| RHS-03 | HSM renderers | Transition labels at true wire midpoint via pin screen positions | RHS-01 | ✅ DONE — folded into RHS-02; external label = midpoint of source-output/target-input pin screen positions (fallback to node-rect centers). |
| RHS-04 | HSM theming | State-flag → NodeCategory/color mapping (composite/parallel/simple colored; history/final keep transparent body for glyph bypass) | — | ✅ DONE — `StateNode.Category`: history/deepHistory/final→Custom (transparent, glyph bypass); parallel→Event; composite→Macro; simple→Function. DefaultTheme palette used (no hue tuning). HSM tests 464/0. |
| RHS-05 | HSM projection | Parallel-state region dividers + headers render | — | ✅ DONE — `HsmAssetMapper.ToModel` now attaches each region to the parent of its `InitialChild` (sorted by `RegionIndex`); purely additive, round-trip preserved. `StateNode.RegionIndex` was already wired. HSM tests 478/0, persistence 123/0. (NodeEditor `ContainerRenderer.DrawRegions` already drew the dashed dividers — no NodeEditor change.) |
| RHS-06 | Data + visual gate | Re-author HsmShowcase.hsm.json to the coordinate convention; **user screenshot confirmation** | RHS-02..05 | 🔄 waypoints removed (committed e7e3d8db). Layout positions now being hand-tuned **by the user in the editor** (auto-saved via the debounced regeneration scheduler) — lead is not re-authoring positions. |
| RHS-08 | NodeEditor + HSM | Make transition wires render — pins positioned but glyph-less | — | ✅ DONE + **VISUAL-CONFIRMED 2026-06-13** — root cause: HSM hidden pins were `IsAdvanced=true` + `ShowAdvancedPins=false` → skipped by `CanvasLayout`/`PinRenderer` → no pin positions → `WireRenderer` skipped every link (no wires at all; labels came from RHS-03 node-center fallback). Fix: added `PinShape.None` (NodeEditor; PinRenderer skips glyph/label but CanvasLayout still positions it) + `HsmPinModel` now `IsAdvanced=false, Shape=None, Label=""` (Data kind → visible mid-blue wires). NodeEditor.UI 70/0, HSM 481/0, BTree/Blueprint/NodeEditor.UI build clean. User screenshot confirms wires + labels render; canvas reads as an HSM. |

**RHS-06 VISUAL GATE: PASSED (2026-06-13).** User confirmed the canvas now renders correctly — wires + labels, per-kind colors, region dividers (RegionA/B/C), H/F glyphs on their nodes, initial-state arrows. VE-DEBT-007 resolved.

### Follow-ups
- **RHS-09:** ✅ DONE — target-end arrowheads on HSM transition wires (mid-blue filled triangle, source→target, via `HsmTransitionLabelRenderer.ComputeArrowheadGeometry`). HSM tests 486/0. Pending user visual confirm.
- **Reroutes:** user requests draggable reroute/waypoint points on wires in ALL editors → split into the new **RR workstream** ([../RR/RR-PLAN.md](../RR/RR-PLAN.md)). NodeEditor UI already emits InsertReroute/MoveReroute/RemoveReroute; host sinks drop them.
- **Layout:** EndState (top-level, IsFinal) visually overlaps ParallelWork's grown bbox; user hand-tunes positions in the editor (auto-saved). Not a code defect.
- **Autosave UX:** debounced regeneration scheduler auto-persists edits with no explicit Save (EditorSubsystem.cs:1550) — user accepted as-is.

RHS-01 is the keystone (unblocks 02/03/possibly 05). RHS-04 is independent and can run in parallel. RHS-06 is the visual sign-off gate.

### RHS-01 API (locked by lead)

Add to `ICanvasRenderContext` (additive, all editors benefit):

```csharp
/// <summary>Screen-space bounding rect of a node this frame (post pan/zoom, container-resolved). False if not laid out / culled.</summary>
bool TryGetNodeScreenRect(NodeId id, out RectF screenRect);

/// <summary>Screen-space attachment point of a pin this frame. False if the pin wasn't laid out.</summary>
bool TryGetPinScreenPosition(PinId id, out Vector2 screenPos);
```

Impl: `CanvasRenderContextImpl` (`CanvasRenderContextImpl.cs`) gains a reference to the per-frame `CanvasLayout` (passed via `BeginFrame`; layout is built at `CanvasRenderer.cs:227` before `BeginFrame` at :240). Accessors delegate to `_layout.NodeScreenRects` / `_layout.PinScreenPositions`. No change to existing members. `IHitTestContext` need not gain these.

**Verify (hard, lead):** NodeEditor.UI.Tests green; `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and the Blueprint editor host all still build (additive interface member — confirm no other `ICanvasRenderContext` implementers break; `FakeHostServices`/demo contexts may need the new members).

---

## Visual checklist (RHS-06 gate, from HSM host design §18.3)

- State boxes colored & distinguishable by kind.
- Transition wires = clean state-edge→state-edge arrows with `Event[Guard]/Action (P:n)` label at midpoint.
- `⦿─→` initial markers sit on each composite's initial child (+ per region in parallel).
- HistoryPseudo = circled **H** on the node; EndState = ⊙ final glyph.
- ParallelWork shows 3 dashed region dividers + headers.
- No floating/detached overlays; no stray reroute dots.
