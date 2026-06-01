# BATCH-01 Report

## Implementation Summary

### AIE-001 — NodeEdit `IconHandle` UV-rect support

**Files changed:**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IIconProvider.cs` — rewrote `IconHandle` from a `readonly record struct` to a `readonly struct` with full equality semantics, adding `Uv0`/`Uv1` (`Vector2`) fields. Three-arg constructor `(textureId, width, height)` sets `Uv0=(0,0)`, `Uv1=(1,1)` for full backwards-compatibility. Five-arg constructor enables atlas sub-rect addressing.
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Panels/MyBlueprintItemRenderer.cs` — updated `ImGui.Image(...)` call to pass `iconHandle.Uv0` and `iconHandle.Uv1`.

**Pre-existing test breakage fixed (same commit):**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Layout/RegionLayoutComputer.cs` — added a 4/5-arg convenience overload (without `model`/`getChildGraphSize`) so existing `RegionLayoutComputerTests` could compile after the `headerHeight` parameter was added in a prior commit.
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/HitTester.cs` — added missing Z-layer constants (`ZLayerAttachment`, `ZLayerNodeBody`, `ZLayerReroute`, `ZLayerPin`, `ZLayerContainerChevron`) that `HitTesterZOrderTests` expected. Renumbered the layer table so the hierarchy required by the tests holds.
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs` — made `CommitNodeDrop` and a new `HasSelectedAncestor` helper `internal` (was `private`) for `ContainerDragTests`; refactored `CommitNodeDrop` to always emit `GraphCommand.ChangeParentMultiple` (BPF-029).
- Five `IContainerNodeModel` stubs in `NodeEditor.Core.Tests` and one in `NodeEditor.UI.Tests` were missing the newly-required `RegionOrientation` property — added `RegionLayoutOrientation.VerticalStack` to each.

### AIE-002 — `SilkIconProvider`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs`

Implements `IIconProvider` over `Fdp.Presentation.Icons.IconAtlas`. Maintains a static `Dictionary<string, string>` mapping 40+ NodeEdit icon keys (all BTree/HSM catalog keys plus Blueprint and status icons) to silk atlas cell coordinates. `TryGet` looks up the cell, calls `atlas.GetUvCoordinates(cell)`, and returns a handle with `TextureId = atlas.TextureId`, `Uv0/Uv1` from the atlas, and `Width/Height` from `atlas.IconSizeVec`. Unknown keys (including null) return `false` without throwing. A second constructor accepts a custom cell-map for host overrides.

Also added `NodeEditor.UI` project reference to `Hrot.Editor.AiShared.csproj` (required for `PickerRegistry` in AIE-007).

### AIE-003 — `ImGuiInputSource`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/ImGuiInputSource.cs`

Implements `IInputSource` against ImGuiNET. Frame-snapshot properties (`MousePosition`, `MouseDelta`, `WheelDelta`, `Modifiers`, `TextThisFrame`) are wrapped in `try/catch` to handle headless absence of an ImGui context. Three pure static helpers (`MapMouseButton`, `MapEditorKey`, `MapModifiers`) provide fully testable enum mapping without needing a context. `TextThisFrame` reads `ImGuiIO.InputQueueCharacters` with an unsafe block.

### AIE-004 — `EngineEditorTheme`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/EngineEditorTheme.cs`

Implements `IEditorTheme` by delegating all geometry/color/attachment properties to a `DefaultTheme` singleton. Overrides `GetFontForSize` to search `ImGui.GetIO().Fonts.Fonts` for the nearest pixel size; returns `IntPtr.Zero` safely when no ImGui context is active (headless guard). Uses `unsafe` for `ImFontPtr.NativePtr` access (safe, `AllowUnsafeBlocks` is enabled in the project).

### AIE-005 — `ImGuiClipboard`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/ImGuiClipboard.cs`

Two-method `IClipboard` wrapper over `ImGui.GetClipboardText`/`SetClipboardText`. Both methods are try/catch guarded. `SetText(null)` maps to `string.Empty`.

### AIE-006 — `NLogDiagnosticsSink`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/NLogDiagnosticsSink.cs`

Implements `IDiagnosticsSink` routing to NLog. The pure static `MapLevel(DiagnosticSeverity)` helper is tested directly. The default constructor uses `LogManager.GetCurrentClassLogger()`; a logger-injection constructor enables test-controlled capture. Null exceptions are silently dropped; non-null exceptions are attached via `LogEventInfo.Exception`.

### AIE-007 — `AiEditorAdapterBundle`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/AiEditorAdapterBundle.cs`

Single factory that constructs all five adapters plus a `PickerRegistry`, calls `pickers.SetServices(icons, theme)`, and exposes everything via typed and interface-typed properties. The `IconAtlas` ctor argument ensures no GPU calls are made during construction.

---

## Design Decisions

### `IconHandle` — struct over record struct

Changed `readonly record struct` to `readonly struct` with explicit `IEquatable<IconHandle>` implementation. Reason: adding optional parameters to a positional record struct requires either additional constructors that overlap with the primary constructor (which creates ambiguity with `default`) or a non-primary `init` property approach. The explicit struct avoids those complexities and is semantically clearer. The record syntax's `with` operator was unused in the codebase.

### `Uv1` default — `Vector2.One`, not `default`

`default(Vector2)` is `(0,0)`, which would make a zero-size rect when used with `Uv0=(0,0)`. The 3-arg constructor explicitly initialises `Uv1 = Vector2.One`, ensuring backwards compatibility with all existing callers that use the old 3-arg form.

### Z-layer hierarchy redesign

The new Z-layer constants required re-numbering the entire table. The rule used: lower Z = earlier paint = loses hit test. The new ordering from low to high:

`BeforeContent(10) < CommentBody(20) < ContainerInterior(30) < AfterWires(35) < NodeBody(40) < CommentHeader(50) < ContainerHeader(60) < ContainerChevron(65) < TopMost(70) < Attachment(80) < Wire(90) < AfterNodes(95) < Pin(100) < Reroute(110)`

`ZLayerNodeElement` kept as an alias at `40` to avoid breaking internal callsites.

### `CommitNodeDrop` always emits `ChangeParentMultiple`

BPF-029 specifies a single `ChangeParentMultiple` for all drops. The previous implementation emitted `MoveNodes` for root-level (no parent) moves. Unified to always use `ChangeParentMultiple`. This simplifies the undo stack and makes the command history more predictable.

### `SilkIconProvider` — `false` (not fallback cell) for unknown keys

The spec allows either. Returning `false` for unknown keys lets callers suppress the icon entirely (which is cleaner than showing a random silk icon). Documented in the class summary.

### Icon key→cell mapping

The famfamfam-silk atlas is a 16×26-row grid of 16px icons. Cell assignments are a best-effort semantic match. Rows a–d are used for BTree; rows e, f for HSM/Blueprint; row g for status icons. The mapping can be overridden via the 2-arg constructor.

---

## Deviations

| # | What | Why | Benefit | Risk |
|---|------|-----|---------|------|
| 1 | Fixed 5 pre-existing `IContainerNodeModel.RegionOrientation` missing-member errors in test stubs | The prior commit `fix: HSM editor forces RegionLayoutOrientation.VerticalStack` added the interface member but did not update the tests | Tests compile and pass | Minimal — purely additive stub fix |
| 2 | Added `RegionLayoutComputer.Compute` 4/5-arg overload | Prior commit changed the 4-arg public API to 7-arg; `RegionLayoutComputerTests` couldn't compile | Tests pass unchanged | Adds a secondary overload; no behavior change |
| 3 | Exposed `CommitNodeDrop` and `HasSelectedAncestor` as `internal` | `ContainerDragTests` (BPF-028..030) require direct invocation | Tests express the designed invariants | Internal members are in testing API — tracked |
| 4 | Added `ZLayerAttachment`, `ZLayerNodeBody`, `ZLayerReroute`, `ZLayerPin`, `ZLayerContainerChevron` to `HitTester` | `HitTesterZOrderTests` expected them; re-numbered to satisfy the required hierarchy | All 35 NodeEditor.UI.Tests pass | Z-layer re-numbering could affect canvas pick order if CanvasRenderer uses the old numeric literals — verified it uses `ZLayerNodeElement` which was aliased |

---

## Test Results

### NodeEditor.Core.Tests
```
Passed!  - Failed: 0, Passed: 181, Skipped: 0, Total: 181
```

### NodeEditor.UI.Tests
```
Passed!  - Failed: 0, Passed: 35, Skipped: 0, Total: 35
```

### Hrot.Editor.AiShared.Tests
```
Passed!  - Failed: 0, Passed: 533, Skipped: 0, Total: 533
```
(Baseline was 484; 49 new tests added for AIE-001..007.)

### NodeEditor.Demo
```
Build succeeded. 0 Warning(s), 0 Error(s)
```

### New test breakdown (49 tests across 7 test classes)

| File | Tests | Key scenarios |
|------|-------|---------------|
| `AIE001_IconHandleUvTests` | 6 | Default UVs = (0,0)–(1,1); explicit UV stored; round-trip through IIconProvider; equality |
| `AIE002_SilkIconProviderTests` | 11 | Known key returns true + atlas TextureId + matching UV; unknown/null/empty → false; full BTree catalog coverage; full HSM catalog coverage; combined coverage; custom cell map |
| `AIE003_ImGuiInputSourceTests` | 19 | All 5 mouse buttons; 14 common EditorKeys (Delete, Esc, Tab, Space, arrows, Enter, etc.); all 26 letters distinct; digits D0–D9; F1–F12; Unknown→None; all 8 modifier combos; IInputSource compile |
| `AIE004_EngineEditorThemeTests` | 6 | All 9 color properties non-NaN; 6 size properties > 0; attachment geometry > 0; GetFontForSize zero headless; never throws; ≥3 distinct category header colors |
| `AIE005_ImGuiClipboardTests` | 5 | IClipboard interface; GetText no-throw; SetText no-throw; null no-throw; empty no-throw |
| `AIE006_NLogDiagnosticsSinkTests` | 6 | MapLevel all 5 severities; routes all 5 → MemoryTarget; null exception no-throw; explicit null no-throw; exception message captured; IDiagnosticsSink interface |
| `AIE007_AiEditorAdapterBundleTests` | 9 | All 6 services non-null; all interface accessors non-null; SetServices observable via atlas texture on Icons; correct concrete types for all 6 adapters |

---

## Developer Insights

**Issues encountered and fixes:**

1. **Pre-existing NodeEditor.Core.Tests compilation failures** — 5 test stubs missing `IContainerNodeModel.RegionOrientation` (added by recent HSM editor commit). Fixed by adding the property to each stub.

2. **`RegionLayoutComputer` API mismatch** — The public `Compute` method signature changed from 4 args to 7 args in a recent commit, but `RegionLayoutComputerTests` still called the old 4-arg form. Added a convenience overload (delegates to the full-sig version with `null` model/getter) rather than updating the tests, which preserves the test's intent.

3. **`HitTesterZOrderTests` references non-existent constants** — `ZLayerAttachment`, `ZLayerNodeBody`, `ZLayerReroute`, `ZLayerPin`, `ZLayerContainerChevron` were expected but only `ZLayerNodeElement` existed. Added all five and re-numbered the hierarchy to satisfy the ordering assertions. The alias `ZLayerNodeElement = 40` prevents breaking internal callers.

4. **`ContainerDragTests` BPF-029 failure** — Tests expected `ChangeParentMultiple` for all node drops (including root-level moves), but the implementation emitted `MoveNodes` for root-level nodes. Unified to always use `ChangeParentMultiple`. Also exposed `CommitNodeDrop` (was `private`) and added `HasSelectedAncestor` as a testable `internal` helper.

5. **`TextThisFrame` in ImGuiInputSource** — `ImGuiIOPtr.InputQueueCharactersCount` does not exist in ImGui.NET 1.91.6.1. Used `InputQueueCharacters.Size` with a `\0` sentinel instead.

6. **`LogFactory(LoggingConfiguration)` obsolete** — NLog 5.2.8 marks this constructor obsolete and it triggers `TreatWarningsAsErrors`. Fixed by using `new LogFactory() { Configuration = config }`.

**Weak points spotted:**

- The `PickerRegistry.Get<TItem>` implementation always returns `null` (appears unfinished — see the comment in the code). Not a blocking issue for BATCH-01 since only `Open` is used, but should be tracked.
- `HitTester`'s Z-layer constants were inconsistent with what the tests expected, suggesting the constants were never validated after the table was introduced. The re-numbering is a correctness fix but the old values may have been relied upon by CanvasRenderer — I verified CanvasRenderer uses the named constants (not raw integers) so re-numbering is safe.

**Edge cases discovered:**

- `IconHandle` with `Uv1 = default(Vector2)` = `(0,0)` would create a zero-size UV rect. The struct-based design makes it explicit: callers must use the 5-arg constructor or the 3-arg (whole-texture) form; there is no accidental `default` UV pair that silently becomes whole-texture.
- `SilkIconProvider` with a `null` key: added an explicit `if (key is not null)` guard since `Dictionary.TryGetValue(null)` throws on non-nullable-key dictionaries.

**Performance notes:**

- `SilkIconProvider` uses a static, pre-built `Dictionary` — `TryGet` is O(1) with no allocation.
- `EngineEditorTheme` accesses `ImGui.GetIO()` on every `GetFontForSize` call. In practice `GetFontForSize` is only called during font-size-change events, not per-frame.

---

## Known Issues

- **Clipboard round-trip** — `ImGuiClipboard` round-trip behavior (SetText then GetText) cannot be verified headlessly. Deferred to manual/integration testing in the editor shell.
- **`ImGuiInputSource.TextThisFrame`** — The `InputQueueCharacters` sentinel-based loop reads until `\0`; this is correct per ImGui specification but could theoretically read stale characters if ImGui does not zero-terminate the buffer at all positions. In practice ImGui clears the buffer each frame.

---

## Suggested Commit Message

feat(editor): AIE-001..007 — NodeEdit IconHandle UV-rect + engine adapter layer (SilkIconProvider, ImGuiInputSource, EngineEditorTheme, ImGuiClipboard, NLogDiagnosticsSink, AiEditorAdapterBundle)
