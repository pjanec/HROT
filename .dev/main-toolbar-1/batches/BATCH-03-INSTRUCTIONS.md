# BATCH-03: Toolbar manager + icon widgets + icon keys
**Tasks:** MTB-P1-T1, MTB-P1-T2, MTB-P1-T4   **Phase:** 1 — Toolbar & Icon Infrastructure   **Est:** ~14h
**Dependencies:** Phase 0 complete (AssetRoots/AssetKind in `Hrot.Editor.AiShared`).

> Three independent, additive, headless-testable pieces. Do them in sequence; do NOT start the
> next task until the current task's impl + tests are done and ALL tests (incl. prior batches')
> pass. MTB-P1-T3 (WindowManager/Program.cs integration) is a SEPARATE later batch — do NOT touch
> `WindowManager.cs` or `Program.cs` here.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §4 (Main Toolbar Framework) and §5 (Icon Infrastructure).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P1-T1, MTB-P1-T2, MTB-P1-T4.
4. Mirror existing patterns:
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/StatusBarManager.cs` (+ its test
     `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/StatusBarManagerTests.cs`).
   - `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs` (+ `IconWidgetsTests.cs`).
   - `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs`.

---

## Task 1 — `MainToolbarManager` (MTB-P1-T1) — §4.1
**File (NEW):** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MainToolbarManager.cs`
Sibling of `StatusBarManager`, rendered as a top band. API exactly per §4.1:
```csharp
public void RegisterEntry(string id, int sortOrder, float declaredHeight, Action renderDelegate, string? perspective = null);
public void RegisterSeparator(string id, int sortOrder, string? perspective = null);
public float Height { get; }
public void Render(string currentPerspective = "");
```
- Last-write-wins on duplicate `id`; entries sorted ascending by `sortOrder`; perspective filter
  (`null` = global, else only when `perspective == currentPerspective`) — mirror `StatusBarManager`.
- **§4.1.1 jitter-free Height = max `declaredHeight` over ALL registered entries** (NOT just
  visible/current-perspective ones), so it is constant across perspective switches.
- Entries render left-to-right (`SameLine`); separators draw a vertical divider over the band.
- **Split UI logic from ImGui draw calls** so the registry/order/height/filter logic is unit-testable
  headlessly without a live ImGui context (mirror how `StatusBarManagerTests` test without rendering;
  use a recording `Action` list to verify which entries render and in what order).

**Tests required (NEW `MainToolbarManagerTests.cs` in `Fdp.Presentation.Tests/ImGui/WindowManager/`)** — assert runtime values, not existence:
- `RegisterEntry_DuplicateId_LastWriteWins` — second registration of an id replaces the first
  (verify by which render delegate fires / which sortOrder/height wins).
- `Entries_RenderInAscendingSortOrder` — register out of order; a recording list of invoked ids
  comes back ascending.
- `PerspectiveFilter_NullIsGlobal_NamedOnlyWhenMatch` — null entry always renders; a "combat" entry
  renders only when `Render("combat")`, not `Render("strategic")`.
- `Height_IsMaxDeclaredOverAllRegistered_RegardlessOfCurrentPerspective` — register a 64px global
  and an 80px entry bound to perspective "X"; `Height == 80` even when rendering perspective "Y".
- `Separator_RegisteredAndOrdered` — a registered separator participates in ordering (verify it sits
  at its sortOrder slot in the render sequence).

## Task 2 — Icon widget `IconHandle` + size overloads (MTB-P1-T2) — §4.2
**File (UPDATE):** `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs`
Add `IconHandle`-based overloads with explicit size (keep all existing `IconAtlas`/coordinate
overloads intact). `IconHandle` lives in `NodeEditor.Core` (`IIconProvider.cs`) — if
`Fdp.Presentation` does not already reference `NodeEditor.Core`, add the ProjectReference (this is
in-scope for T2; note it in the report).
```csharp
public static bool IconButton(in IconHandle icon, string id, Vector2 size, bool enabled = true, Vector4? tint = null);
public static bool ToggleIcon(in IconHandle icon, string id, Vector2 size, ref bool isToggled, bool enabled = true, Vector4? tint = null);
public static void Tooltip(string text);   // if (ImGui.IsItemHovered()) ImGui.SetTooltip(text)
```
- **Disabled** (`enabled == false`): passive `Dummy` (NO click hit-area) + dimmed draw — mirror
  `DrawTransportButton`'s `dim` path. A disabled button must NEVER return true and must register no
  hit area.
- Toggle/active background retained. Draw via `drawList.AddImage(icon.TextureId, pos, pos+size, icon.Uv0, icon.Uv1, tintU32)`.
- Keep the logic headless-testable as the existing `IconWidgetsTests` do (mirror their harness for
  invoking the click/enabled/toggle logic without a live GPU/ImGui frame).

**Tests required (extend `IconWidgetsTests.cs`)** — mirror existing style:
- `IconButton_Handle_WhenNotClicked_ReturnsFalse`.
- `IconButton_Handle_Disabled_NeverReturnsTrue_AndRegistersNoHitArea`.
- `ToggleIcon_Handle_TogglesState_OnClick` and `ToggleIcon_Handle_WhenDisabled_StateUnchanged`.
- `*_DoesNotThrow` for valid args at 64×64.

## Task 3 — Icon keys + `AssetKind → IconKey` (MTB-P1-T4) — §5.1, §5.2
**File (UPDATE):** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs` (+ a small new
`AssetKind → IconKey` map type in `Hrot.Editor.AiShared`, e.g. `Identity/AssetKindIcons.cs`).
- Extend the provider's key map (or register an additional provider behind `IIconProvider`) with the
  §5.1 keys: `debug/continue`, `debug/step_back`, `debug/step_over`, `debug/step_into`,
  `debug/step_out`, `asset/scenario`, `asset/blueprint`, `asset/btree`, `asset/hsm`,
  `asset/blackboard`, `asset/utility`, `browser/open`, `asset/new`, `folder`, `folder_open`.
  (Time uses vector shapes — no `toolbar/*` keys required.) Unknown key → `TryGet` returns false.
- Add an `AssetKind → IconKey(string)` map (§5.2): Blueprint→`asset/blueprint`, BTree→`asset/btree`,
  Hsm→`asset/hsm`, Blackboard→`asset/blackboard`, Utility→`asset/utility`.

**DEV-LEAD decision (DEC-2 — `AssetKind.Scenario` does not exist yet):** Scenario is added later in
MTB-P5-T2. For now: register the `asset/scenario` provider key, and expose the scenario mapping via a
dedicated constant (e.g. `AssetKindIcons.ScenarioIconKey => "asset/scenario"`) rather than an
`AssetKind.Scenario` arm. Adapt the "IncludingScenario" test accordingly (assert the 5 real kinds map
correctly AND `ScenarioIconKey == "asset/scenario"`) — do NOT add `AssetKind.Scenario` and do NOT
weaken the test's intent.

**Tests required (NEW `IconKeysTests.cs` in `Hrot/Editor/Hrot.Editor.AiShared.Tests/`):**
- `TryGet_EachNewKey_ReturnsHandle` — every §5.1 key above resolves to a handle (`TryGet` true).
- `AssetKindToIconKey_CoversAllKinds_IncludingScenario` — the 5 `AssetKind` values map to their
  expected keys AND the dedicated scenario constant equals `asset/scenario`.
- `TryGet_UnknownKey_ReturnsFalse` — a bogus key returns false (and out handle is default).

> Note: `SilkIconProvider` may load real atlas textures. If `TryGet` cannot return a meaningful
> handle headlessly, structure the provider so the **key→(atlas,coords) resolution table** is
> testable without GPU upload (the test asserts the key is known/mapped), and document the seam.

## Hard constraints
- Do NOT touch `WindowManager.cs` or `Program.cs` (that's MTB-P1-T3, a later batch).
- Do NOT delete/modify legacy/assembly-loading code. Keep all existing IconWidgets overloads.
- No scope creep beyond the three tasks' files (+ the NodeEditor.Core ProjectReference for T2 if
  genuinely needed).
- Do NOT weaken/skip/auto-pass tests or add a Stability trait to dodge a failure. Fix root causes.

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings — TreatWarningsAsErrors is on in these projects).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. These suites 0-failed
  with `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`:
  `Fdp.Presentation.Tests`, `Hrot.Editor.AiShared.Tests`, plus the hot suites
  `Fdp.Toolkits.Tests` and `Hrot.SimHost.Tests`.
- Write `.dev/main-toolbar-1/reports/BATCH-03-REPORT.md`: files changed, every new test + what it
  asserts, the headless-testability seam you used for each of T1/T2/T4, paste actual test-run
  summaries, and answer the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
