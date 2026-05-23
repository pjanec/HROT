# FIX1-BATCH-04 Report

## 1. Summary

Implemented three groups of work items for Phases 5 & 6 of the BTree and HSM authoring hosts:

- **TASK-BT-S1-11** — Filled in `ObserverGuardBadgeRenderer.Render()` to iterate links and
  emit one `OBSERVES` badge per wire from `ObserverSelector` to a `Condition` or `Observer`
  child. Badge rendering is gated on `ctx.Viewport.Zoom >= 0.4f` and only fires when the
  ImGui draw-list pointer is non-null (safe-code null-guard via `Unsafe.As`).

- **TASK-HS-S1-08 & TASK-HS-S1-10** — Replaced five `/* TODO */` stubs in
  `HsmCommandSink.cs`:
  - `ApplyAddRegion` — inserts a `RegionDescriptor` at the requested index, marks dirty,
    fires `Changed` with `AffectedNodes`.
  - `ApplyRemoveRegion` — removes the indexed region, migrates its children to region 0,
    marks dirty, fires `Changed`.
  - `ApplyReorderRegions` — applies the permutation array to `Regions`, marks dirty, fires
    `Changed`.
  - `ApplyAddAttachment` — adds an `HsmAttachment` record keyed by `AttachmentId`, marks
    dirty, fires `Changed` with `AffectedAttachments`.
  - `ApplyRemoveAttachments` — removes listed attachment records, marks dirty, fires
    `Changed` with `AffectedAttachments`.
  New supporting types: `HsmAttachment.cs` (holds `AttachmentId`, `HostNodeId`, `Kind`,
  `Properties`).

- **TASK-HS-S1-14** — Updated `HsmTransitionLink.Style` to return `LinkStyle.Hidden` for
  `TransitionKind.Internal` so the default bezier wire renderer skips those links.
  `HsmTransitionLabelRenderer` now handles internal transitions in its `Render()` pass:
  draws a dashed looping arrow (three-segment approximation) entirely inside
  `NodeInteriorBounds` of the source state and renders the event/action label next to the
  loop. Also added `LinkStyle.Hidden` to the `LinkStyle` enum in `ILinkModel.cs` and wired
  the guard in `WireRenderer.cs` to skip hidden links.

Pre-existing build error fixed as a side-effect: `CS0067` in
`ReferenceCatalogTests.FakeEditableAsset.Changed` was suppressed with `#pragma warning
disable CS0067` (the event satisfies the `IEditableAsset` interface contract but is never
fired in the stub).

---

## 2. Task Status

| Task | Status |
|------|--------|
| TASK-BT-S1-11 | Implemented |
| TASK-HS-S1-08 | Implemented |
| TASK-HS-S1-10 | Implemented |
| TASK-HS-S1-14 | Implemented |

---

## 3. Tests

### New tests added

#### Hrot.BTree.Editor.Tests — `Renderers/ObserverGuardBadgeRendererTests.cs`

| Test | Result |
|------|--------|
| `Render_with_observer_selector_parent_and_condition_child_emits_one_badge` | PASS |
| `Render_with_non_observer_parent_emits_no_badge` | PASS |
| `Render_suppressed_at_low_zoom` | PASS |

#### Hrot.Hsm.Editor.Tests — `Host/HsmCommandSinkRegionTests.cs`

| Test | Result |
|------|--------|
| `AddRegion_adds_region_to_state_RegionNodes` | PASS |
| `AddRegion_increments_AllRegions` | PASS |
| `RemoveRegion_removes_region_from_state` | PASS |
| `ReorderRegions_changes_region_order` | PASS |
| `AddAttachment_makes_attachment_findable_by_node` | PASS |
| `RemoveAttachments_removes_attachment` | PASS |

#### Updated existing test

| Test | Change | Result |
|------|--------|--------|
| `HsmGraphModelTests.TransitionLink_internal_is_Hidden` | Renamed from `_is_Dashed`; asserts `LinkStyle.Hidden` per TASK-HS-S1-14 spec | PASS |

### Full suite results for affected projects

```
Passed!  - Failed: 0, Passed: 74, Skipped: 0, Total: 74  - Hrot.BTree.Editor.Tests.dll
Passed!  - Failed: 0, Passed: 150, Skipped: 0, Total: 150 - Hrot.Hsm.Editor.Tests.dll
```

---

## 4. Developer Insights

### Issues encountered

1. **`LinkStyle.Hidden` vs pre-existing test** — `HsmGraphModelTests.TransitionLink_internal_is_Dashed`
   expected `Dashed`, but TASK-HS-S1-14 explicitly says to use `Hidden` so the default
   wire renderer skips internal transitions. The test was written before the render
   architecture was finalised and had to be updated.

2. **ImGui draw-list null check** — `ImDrawListPtr` is an unsafe struct wrapping a raw
   pointer. The project has no `AllowUnsafeBlocks`, so the badge renderer uses
   `System.Runtime.CompilerServices.Unsafe.As<ImDrawListPtr, nint>` to reinterpret the
   pointer as a `nint` without an unsafe block. This is correct but fragile; a future
   ImGui upgrade could change the struct layout.

3. **`HsmAttachment` model gap** — `HsmCommandSink` referenced an attachment model type
   that did not exist. Created `HsmAttachment.cs` as a minimal record holding `AttachmentId`,
   `HostNodeId`, `Kind`, and `Properties`. The `HsmAsset` was extended with an `Attachments`
   dictionary keyed by `Guid`.

4. **Pre-existing `CS0067`** — `ReferenceCatalogTests.FakeEditableAsset.Changed` triggered
   `CS0067` (treated as error by `<TreatWarningsAsErrors>`). Suppressed with `#pragma
   warning disable/restore CS0067`.

### Weak points spotted

- `HsmCommandSink` had no guard against commands referencing non-existent nodes; the
  implementation throws `KeyNotFoundException` rather than providing a diagnostic-friendly
  error. Acceptable for now (early-out rather than silent corruption), but could be
  improved with a `TryGetValue` + warning path.
- The `RegionDescriptor` type has no copy constructor. `ApplyReorderRegions` must
  reorder the list in-place, which is fine, but any future undo/redo stack will need
  snapshot support.
- `WireRenderer` iterates all links to check for `Hidden` — O(n) on every frame. Could be
  cached per-frame if link count grows large.

### Design decisions beyond spec

- The looping arrow in `HsmTransitionLabelRenderer` for internal transitions is drawn as a
  small three-point polyline (top-left corner of `NodeInteriorBounds`). The spec only
  required "dashed curved path or small looping arrow inside NodeInteriorBounds"; no pixel
  dimensions were given. A corner loop was chosen as it is unambiguous and leaves the state
  label area clear.
- The badge renderer uses an internal `_badgeCount` field on the renderer instance (reset
  on each `Render` call) to allow the tests to assert the count without needing to mock the
  ImGui draw list. This is a simple test-observable mechanism that does not affect
  production rendering.

---

## 5. Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:01:55.23
```

Target framework: net8.0. Solution: `IOS-IG-SimHost.sln`.

```
Passed!  - Failed: 0, Passed: 74, Skipped: 0, Total: 74, Duration: 3 s    - Hrot.BTree.Editor.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 150, Skipped: 0, Total: 150, Duration: 45 ms - Hrot.Hsm.Editor.Tests.dll (net8.0)
```
