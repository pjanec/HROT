# MVE-BATCH-04: editor Save (active blueprint → disk), projection-only
Save the opened blueprint back to its `.bp.json`. Completes the kernel+button+save slice.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md`.
2. Reuse: `BlueprintJsonServices.Serialize(BlueprintAsset)` (Hrot.Blueprints.Compiler); the editor hotkey dispatcher `EditorHotkeyDispatcher` + `IEditorCommands`/`BuiltinCommandHandlers` (added in BCP-BATCH-02-FIX); active document plumbing `AiDocumentManager.Active` → `AiCanvasContext.AssetRef` (live `BlueprintAsset`) and `AiDocument.Asset` (the `BlueprintFileAsset`, which has `SourceFilePath`).
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## Projection-only rule for SAVE (critical)
Loaded assets store `"Pins": []`; pins are an editor projection (`NodePinSchema`), and newly wire-dropped nodes carry in-memory pins (DEBT-BCP-005). **Save must NOT persist projected/hydrated pins** — serialize each node with `Pins` cleared so saved files stay `Pins:[]` (the links keep their `FromPinId`/`ToPinId`; on reload the two-pass binding rehydrates pins from the links). This preserves byte-stability and keeps the compiler on its current (pins-empty) path. Persist what the editor genuinely owns: graphs/nodes(kind+props)/links/positions(`EditorMetadata`)/variables/etc. Do the pin-clear on a COPY or restore after serialize — do NOT mutate the live in-memory asset's pins (the canvas still needs them).

## Task 1 — Save command
- A `SaveActiveBlueprintCommand` (in `Hrot.Blueprints.Editor`): given the active `BlueprintAsset` + its `SourceFilePath`, serialize (with pins cleared per above) via `BlueprintJsonServices.Serialize` and `File.WriteAllText`. Headless-testable core (no ImGui): `Save(BlueprintAsset asset, string path)` (+ a resolver that pulls asset+path from the active document). Mark the document clean / clear dirty if there's a dirty flag.
- Wire it in `EditorSubsystem`: register a `Ctrl+S` shortcut via the `EditorHotkeyDispatcher`/`IEditorCommands` (and/or a toolbar/menu "Save" entry via `IWindowRegistrar`). Resolve the active blueprint from `AiDocumentManager.Active` (`Asset` → `BlueprintFileAsset.SourceFilePath`; `ViewState`→`AiCanvasContext.AssetRef`→`BlueprintAsset`). No-op + status if no blueprint open.

## Task 2 — headless round-trip test
- Load a blueprint (e.g. a TestAsset or build one), mutate it in memory (add a node + link + set a position + add a variable), Save to a temp path, reload from disk, and assert: the mutations persisted (node/link/position/variable present) AND every node's `Pins` is `[]` in the saved file (projection-only preserved). 
- Byte-stability: load an existing fixture → Save → reload → Serialize again → equal (modulo the `$meta` envelope, as the existing byte-stability test does). Confirm the live in-memory asset's pins are NOT mutated by Save.

## Success Criteria
- [ ] Ctrl+S (and/or a Save button) writes the active blueprint to its `.bp.json`, `Pins:[]` preserved, edits persisted; no-op+status when nothing's open.
- [ ] Headless round-trip + projection-only-on-save tests pass; live asset pins untouched by Save.
- [ ] Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2.
- [ ] Green: new tests; `EditorSubsystemBoot` filter; `Hrot.Blueprints.Tests` (DEBT-006 unchanged — incl. the existing byte-stability test); `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-04-REPORT.md`.

## Execution rules — YOU (the sonnet agent) run the full implement→build→test→fix loop yourself
- Verify the active-document/asset/SourceFilePath plumbing + `BlueprintJsonServices.Serialize` + the hotkey/command API against the code FIRST (cite file:line). Reuse the dispatcher/commands from BCP-BATCH-02-FIX. Keep the Save core ImGui-free + testable; gate any ImGui.
- Pin-clear on save must NOT mutate the live asset (copy or save-then-restore). Build + run the suites yourself; reach green; never fake a pass.

## Report
Document: the Save command + active-doc resolution (cited members); how pins are cleared without mutating the live asset; the Ctrl+S/toolbar wiring; round-trip + byte-stability test results + counts; build status; EditorSubsystemBoot unaffected. Suggested commit message. Note remaining MVE steps (compile-on-demand so the run-button resolves arbitrary opened blueprints; hot-reload; debug-observe). No comprehension questions.
