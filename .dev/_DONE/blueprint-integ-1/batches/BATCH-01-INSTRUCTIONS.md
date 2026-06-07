# BATCH-01: Foundations — NodeEdit icon UV + engine adapters
**Tasks:** AIE-001, AIE-002, AIE-003, AIE-004, AIE-005, AIE-006, AIE-007   **Phase:** 0   **Est:** ~14h
**Dependencies:** none (first batch).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (autonomy, test quality, report).
2. `.dev/blueprint-integ-1/DESIGN.md` §4.7 + §5.1 — what these adapters are and why.
3. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-001…AIE-007 — **authoritative success conditions** for each task (your tests must satisfy them).

Use the **codebase-memory MCP** first for exploration (`list_projects` → project `D-Work-IOS-IG-SimHost-FDP-2`, then `search_graph`/`get_code_snippet`). Do not use `search_code`.

## Ground truth — key files & APIs (verified)
- NodeEdit interfaces: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/` — `IIconProvider.cs` (`IconHandle(nint TextureId, uint Width, uint Height)`), `IInputSource.cs` (`MousePosition/MouseDelta/WheelDelta`, `IsMouseDown/Pressed/Released/DoubleClicked(MouseButton)`, `IsKeyDown/Pressed/Released(EditorKey)`, `Modifiers`, `TextThisFrame`), `IEditorTheme.cs`, `IClipboard.cs`, `IDiagnosticsSink.cs` (`Log(DiagnosticSeverity, string, Exception?)`), `IPickerRegistry.cs`.
- NodeEdit primitives (enums `MouseButton`, `EditorKey`, `KeyModifiers`, `NodeCategory`, `PinShape`): `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Primitives/` (locate exact files via graph).
- NodeEdit `DefaultTheme`: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/DefaultTheme.cs`. `PickerRegistry` + `SetServices`: `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerRegistry.cs`.
- Icon consumers to update for UVs: `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Panels/MyBlueprintItemRenderer.cs` (and any other site calling `icons.TryGet(...)` then `ImGui.Image`).
- Engine icon atlas: `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconAtlas.cs` — `IntPtr TextureId`, `Vector2 IconSizeVec`, `(Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coord)` (coord like `"b12"`: letter=row, 1-based number=col). Atlas is famfamfam-silk, 16px cells.
- **New adapter home:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/` (create the folder). Project: `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` (already references NodeEditor.Core/UI). Uses ImGuiNET + Raylib are available transitively in the editor; for headless-testability keep ImGui/Raylib calls out of constructors and behind small methods.
- **Tests:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/` (add `Adapters/` tests here). NodeEdit tests: `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/`, `NodeEditor.UI.Tests/`, and the demo `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/` must still build.

## Tasks (do in order; see TASK-DETAIL for full success conditions)

### Task 1: NodeEdit IconHandle UV-rect (AIE-001) — files: `IIconProvider.cs` + icon draw sites (UPDATE)
Extend `IconHandle` with `Vector2 Uv0` / `Vector2 Uv1`, defaulting to `(0,0)`/`(1,1)` so existing whole-texture constructions keep working. Update icon draw sites to pass the UVs to `ImGui.Image`. Keep `NullIconProvider`, `FakeIconProvider`, demo, and `NodeEditor.*.Tests` compiling.
**Tests required:** `IconHandle_DefaultUvs_CoverWholeTexture`; a renderer/UI test proving a non-default UV is forwarded to the image draw (or a focused unit test on the handle→draw mapping seam).

### Task 2: SilkIconProvider (AIE-002) — file: `Adapters/SilkIconProvider.cs` (NEW)
`IIconProvider` over the engine `IconAtlas`; map NodeEdit icon keys (`bt/sequence`, `bt/selector`, `bt/action`, …, `hsm/state_simple`, `hsm/state_parallel`, …, `bp/*`, and status icons) to silk atlas cells; return a handle with `TextureId` = atlas texture and `Uv0/Uv1` from `GetUvCoordinates`. Unknown key → `false` or a documented fallback cell. Ctor takes an `IconAtlas` (no GPU calls) so tests run headless. Put the key→cell map in a small static table; it's fine to start with a reasonable subset + fallback, but cover all BTree/HSM static node-kind keys used by the existing catalogs (find them via `BTreeNodeCatalog`/`HsmNodeCatalog`).
**Tests required:** `SilkIconProvider_TryGet_KnownKey_ReturnsHandleWithUv` (UVs equal `atlas.GetUvCoordinates(cell)`, TextureId equals atlas), `SilkIconProvider_TryGet_UnknownKey_ReturnsFalseOrFallback`, `SilkIconProvider_CoversAllBTreeAndHsmCatalogKeys`.

### Task 3: ImGuiInputSource (AIE-003) — file: `Adapters/ImGuiInputSource.cs` (NEW)
`IInputSource` mapping ImGuiNET → the interface. Isolate the enum mapping into pure static helpers (`MapMouseButton`, `MapEditorKey`, `MapModifiers`) so they're unit-testable without an ImGui context. Frame-snapshot members read ImGui (guard for no-context).
**Tests required:** `ImGuiInputSource_Maps_AllMouseButtons`, `_Maps_CommonEditorKeys` (cover the keys `CanvasInput` uses — arrows, Delete, Esc, Tab, Space, Ctrl/Shift/Alt combos), `_Maps_Modifiers`.

### Task 4: EngineEditorTheme (AIE-004) — file: `Adapters/EngineEditorTheme.cs` (NEW)
`IEditorTheme` wrapping `DefaultTheme` for geometry/colors; implement `GetFontForSize` against the engine's ImGui fonts (return `IntPtr.Zero` when none registered). Align palette to the engine theme; keep attachment/container defaults unless overriding.
**Tests required:** `EngineEditorTheme_Implements_IEditorTheme_FullSurface` (colors non-NaN, sizes > 0), `_GetFontForSize_ReturnsZeroOrValidPtr` (never throws), `_GetCategoryHeaderColor_DistinctPerCategory`.

### Task 5: ImGuiClipboard (AIE-005) — file: `Adapters/ImGuiClipboard.cs` (NEW)
`IClipboard` over `ImGui.GetClipboardText`/`SetClipboardText`; guard for headless (no throw).
**Tests required:** `ImGuiClipboard_Implements_IClipboard` + non-throwing behavior (round-trip behavior may be deferred to manual run if ImGui context is required — state that in the report).

### Task 6: NLogDiagnosticsSink (AIE-006) — file: `Adapters/NLogDiagnosticsSink.cs` (NEW)
`IDiagnosticsSink` routing each `DiagnosticSeverity` to the matching engine log level (NLog). No throw on null exception.
**Tests required:** `NLogDiagnosticsSink_Log_RoutesAllSeverities` (assert mapping via an injected/captured logger or a severity→level mapping helper).

### Task 7: AiEditorAdapterBundle (AIE-007) — file: `Adapters/AiEditorAdapterBundle.cs` (NEW)
Build all five adapters + a `PickerRegistry`, call `pickers.SetServices(icons, theme)`; expose `Icons/Theme/Input/Clipboard/Diagnostics/Pickers`.
**Tests required:** `AiEditorAdapterBundle_Build_PopulatesAllServices` (all non-null), `_Pickers_HaveServicesSet` (observable that SetServices was called with the bundle's icons+theme).

## Success Criteria
- [ ] AIE-001..007 implemented per TASK-DETAIL success conditions.
- [ ] All new tests pass **and** the full test suites for `Hrot.Editor.AiShared.Tests`, `NodeEditor.Core.Tests`, `NodeEditor.UI.Tests` are green; `NodeEditor.Demo` builds.
- [ ] No warnings (TreatWarningsAsErrors); public APIs documented; no leftover TODO/debug code.
- [ ] Report submitted at `.dev/blueprint-integ-1/reports/BATCH-01-REPORT.md`.

## Execution rules
- Complete tasks **in sequence**; do NOT start the next task until the current task's implementation is done, its tests are written, and ALL tests (including prior tasks' and the existing suites named above) pass.
- Run the relevant `dotnet test` suites yourself and fix root causes to completion — do not ask permission. Never swallow errors or fake a pass.
- A large amount of unrelated working-tree change is already committed as the baseline; ignore it. Only create/modify the files this batch needs.

## Report Requirements
In `reports/BATCH-01-REPORT.md` answer: issues encountered & fixes; weak points spotted; design decisions beyond spec (e.g. the icon key→cell mapping choices, font-handle strategy); edge cases discovered; any clipboard/ImGui-context testability limitation; performance notes; **actual test-run counts/output**; suggested one-line commit message. Do not ask comprehension questions.
