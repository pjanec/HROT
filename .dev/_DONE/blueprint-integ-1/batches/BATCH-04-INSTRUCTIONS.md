# BATCH-04: Corrective AV fix + EditorSubsystem composition rewrite
**Tasks:** Corrective Task 0 (AV fix), AIE-015   **Phase:** 1   **Est:** ~12h
**Dependencies:** BATCH-01..03 (adapters, catalog builder, document manager, perspective registrar).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/blueprint-integ-1/reviews/BATCH-03-REVIEW.md` — the **P1 / Corrective Task 0** details.
3. `.dev/blueprint-integ-1/DESIGN.md` §3.2, §4.1–4.5 — composition root + perspectives.
4. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-015 — authoritative success conditions.

Use the **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`.

## Corrective Task 0 — fix the AccessViolation crash (P1, from BATCH-01)
**Problem:** `Hrot.Editor.AiShared.Tests` (no filter) **aborts** with `System.AccessViolationException`. Source: ImGui-touching adapters call ImGui APIs with **no ImGui context** → native AV. `try/catch` cannot catch a corrupted-state exception, so the process dies. Confirmed offender: `EngineEditorTheme.GetFontForSize` (`ImGui.GetIO().Fonts`); likely also `ImGuiClipboard.GetText/SetText`.
**Fix (files in `Hrot/Editor/Hrot.Editor.AiShared/Adapters/`):** before any ImGui dereference, guard with a **safe pointer check**:
```csharp
if (ImGui.GetCurrentContext() == IntPtr.Zero) return <fallback>;  // GetFontForSize → IntPtr.Zero; GetText → null; SetText → no-op
```
Apply to `EngineEditorTheme.GetFontForSize`, `ImGuiClipboard.GetText`/`SetText`, and any `ImGuiInputSource` member that dereferences ImGui (mouse/wheel/keys/text). Keep the pure mapping helpers unchanged. Remove dependence on `try/catch` for the no-context path (a catch may remain for genuinely managed failures, but the no-context guard must prevent the AV).
**Success conditions:**
- `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` (NO filter) **runs to completion, 0 crashes, 0 failures**, deterministically (run it 2–3×).
- Add `EngineEditorTheme_GetFontForSize_NoContext_ReturnsZero` and `ImGuiClipboard_NoContext_GetReturnsNull_SetNoThrow` tests that assert the guarded behavior **without** a context (these must not crash).
- (Optional but encouraged) one test that creates a context via `ImGui.CreateContext()`/`SetCurrentContext` and exercises the real path, disposing it after.

## Task 1: EditorSubsystem composition rewrite (AIE-015) — file: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE) + test migration
Replace the AI/Blueprint editor wiring with the shared composition. See DESIGN §4.1–4.5 and TASK-DETAIL AIE-015.

**Build & wire (in `Initialize` / `RegisterWindows` as appropriate):**
- A shared `AssetCatalog` via `AiAssetCatalogBuilder` with the three contributors: `BTreeAssetContributor`, `HsmAssetContributor`, `BlueprintAssetContributor` (blueprints dir = `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blueprints")`, as the old `CreateBlueprintWindowRegistrar` used). Call `RefreshFromAssembly(<loaded Hrot.AI.Behaviors assembly>)` after `_aiCoordinator.TriggerInitialLoad()` and again on `_aiCoordinator.OnReloadCompleted`. (Obtain the assembly via the coordinator/AppDomain; if not directly exposed, load by name.)
- `AiDocumentManager` + `WindowManagerPerspectiveSwitcher` (wired to the `WindowManager` in `RegisterWindows`).
- **Three** per-perspective `EditorSelectionStore` instances (BTree/HSM/Blueprint) and **three** `PerspectiveWorkspaceRegistrar`s registering each perspective's **side panels** (Inspector/Runtime/Trace/Find/Blackboard/Diagnostics). **Do NOT** add the graph canvas or kind-specific panels (My Blueprint, HSM Globals) — those are Phase 2/4; the registrar's extension seam stays unused for now.
- The **global** `AssetBrowserWindow` (with the `AiDocumentManager`), registered once.

**Retire (replace, per user direction "current blueprint integration … we can replace it"):**
- `CreateBlueprintWindowRegistrar()` and the `_blueprintWindowRegistrar` registration path.
- Blueprint's own `Hrot.Blueprints.Editor.EditorSelectionStore` and `FileSystemAssetCatalog` usage from the composition root (the legacy `FileSystemAssetCatalog` class may remain in the Blueprints assembly but must no longer be the catalog used here; remove if nothing else references it).

**Preserve (do NOT break):** everything non-AI-editor — the legacy editor windows (toolbar/browser/cluster/inspector/etc.), gizmo subsystem, **universal breakpoint stack** (`_bpManager`, `DataBreakpointManagerWindow`), `_blueprintDebugSession` + `DebugProbe.Sink` wiring (used by breakpoints), the WHEN-node bootstrap registries, the ECS world/kernel/time controller, and `_aiCoordinator`. The editor must still boot headless.

**Adapters/canvas note:** the NodeEdit `AiEditorAdapterBundle` is **not** required in this batch (it feeds the canvas host services in Phase 2). Do not wire it yet unless trivially needed.

**Test migration:** `EditorSubsystemBlueprintWindowsTests` and `BlueprintWindowRegistrarTests` test the retired registrar — update them to assert the **new** behavior (three perspectives registered, global Asset Browser registered, side panels per perspective with correct `OwningPerspective`) or remove the obsolete ones and add `EditorSubsystem_RegisterWindows_RegistersThreePerspectives_AndGlobalBrowser`. Keep `EditorSubsystemBootTests` green.
**Success conditions (TASK-DETAIL AIE-015):**
- `EditorSubsystem_RegisterWindows_RegistersThreePerspectives_AndGlobalBrowser` (three distinct `OwningPerspective` groups BTree/HSM/Blueprint + a Global Asset Browser present in the WindowManager).
- `EditorSubsystem_Boot_Headless_Succeeds` (`EditorSubsystemBootTests` green).
- No remaining composition-root references to `BlueprintWindowRegistrar` or `Blueprints.Editor.EditorSelectionStore` (grep-clean).

## Success Criteria
- [ ] Corrective Task 0: full `Hrot.Editor.AiShared.Tests` runs to completion, 0 crashes/failures (verified 2–3×).
- [ ] AIE-015 implemented per success conditions; editor boots headless; three perspectives + global browser registered; Blueprint parallel infra retired.
- [ ] Green: `Hrot.Editor.AiShared.Tests`, `Hrot.ClusterRunner.Integration.Tests` (esp. `EditorSubsystemBootTests`), `Hrot.Blueprints.Tests` (the 10 pre-existing golden failures from DEBT-006 may remain — do NOT regress beyond them; document the exact count).
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-04-REPORT.md`.

## Execution rules
- Do Corrective Task 0 **first**; confirm the AV is gone before starting AIE-015.
- Then AIE-015; run the named suites yourself and fix root causes. Never swallow errors or fake a pass.
- This batch heavily edits a large existing file (`EditorSubsystem.cs`, ~1900 lines) — make surgical changes, preserve unrelated wiring, and re-run boot tests frequently.

## Report Requirements
In `reports/BATCH-04-REPORT.md`: the AV fix approach + proof it's deterministic; exactly what was retired vs preserved in `EditorSubsystem`; how the AI-behaviors assembly handle was obtained for `RefreshFromAssembly`; which Blueprint tests were migrated/removed and why; the exact `Hrot.Blueprints.Tests` pass/fail counts (confirm no new failures beyond DEBT-006's 10); actual test counts for all suites; suggested commit message. No comprehension questions.
