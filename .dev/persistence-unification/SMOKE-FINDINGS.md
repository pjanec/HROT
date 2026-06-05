# Manual smoke-test findings (2026-06-05) — root causes + fix plan
Status: INVESTIGATED, NOT YET FIXED (paused for user restart). No code changed.

## Symptom → root cause → classification → fix

| # | Symptom | Root cause (file:line) | Ours? | Fix |
|---|---------|------------------------|-------|-----|
| 1 | SampleScout/SampleGuard open with nodes piled at origin (no layout) | `EditorSubsystem.Initialize` calls `btreeJsonContrib.Discover(...)`/`hsmJsonContrib.Discover(...)` (~line 601-602) but never `LoadAll` → JSON contributor contributes **zero** assets → the **assembly** contributor's asset opens, which now has NO `[BTreeLayout]`/`[HsmLayout]` (deleted in PU-402) → `Project` gets `layout==null` → auto-layout pile. | **OURS (migration)** | Change `Discover(rootDirectory:…)` → `Refresh(rootDirectory:…)` (Discover+LoadAll+ContributorChanged) for both BTree & HSM JSON contributors. Verify the root path resolves at runtime (`BaseDirectory/../../../Hrot/Subsystems/Hrot.AI.Behaviors/Trees`). |
| 2 | Save-All button doesn't persist layout across restart (BTree+HSM+Blueprint) | The `doc.Asset.Changed` handler (`EditorSubsystem.cs` ~2204) calls `schedulerRef.Schedule(doc.Asset)` but **never `doc.MarkDirty()`** → `SaveAllAiDocumentsCommand.Execute` skips all docs (`if (!doc.IsDirty) continue;`). | **OURS (missing wire)** | Add `doc.MarkDirty();` in the `asset.Changed` handler before `Schedule`. (Re-verify node-move→`asset.MarkDirty()`→`Changed` actually fires, and that ToDto reads the moved positions from the model — confirm the canvas syncs positions back to the asset model.) |
| 3 | Opening HSM doesn't switch perspective (Blueprint does) | Case mismatch: `AssetKind.Hsm.ToString()` = `"Hsm"` but the perspective is registered as `"HSM"` (`EditorSubsystem.cs` ~1802 + HSM canvas window ~2108) → `WindowManager.SwitchPerspective` no-op. | **OURS (typo)** | Make the registered name match the enum: use `"Hsm"` for the HSM `PerspectiveWorkspaceRegistrar` + canvas window (or map Kind→perspective canonically). |
| 4 | `[x]` in OPEN section doesn't close | Code is correctly wired (`AssetBrowserWindow.cs` ~164-165 → `mgr.Close(doc)`); `BeforeDocumentClosed` flush is benign. Likely an ImGui interaction/frame artifact, not a wiring bug. | Not a code bug (needs live repro) | Re-verify live; check for a modal/overlapping item stealing the click. |
| 5 | SampleWireDemo "blueprint is not registered" on Run | `BlueprintAttachService.cs:100-104` — registry populated by the Compile/Reload step (`QuickReloadService`). Must click "Compile / Reload Blueprint" first. | **Pre-existing** | None (workflow: compile before run). |

Note: SampleScout is a BTree — "Run blueprint on selected entity" is Blueprint-only by design (not a bug).

## Caveats to verify during the fix batch
- Bug 1 fix assumes the JSON contributor, once `LoadAll`'d, **wins** the AssetId collision over the assembly contributor (research said JSON added last → wins). Confirm in `AiAssetCatalogBuilder` the JSON contributor is added AFTER and overrides. If not, the layout-less assembly asset could still win.
- Bug 2: confirm the **canvas node-move writes positions back into the asset model** (asset.Nodes[].Position / state.Position) before `ToDto`, else save persists stale positions even once dirty. This is the deeper half of symptom 2 and must be checked, not assumed.
- "blue circles" missing on SampleGuard HSM + overlapping "(P:0)" text — likely the same null-layout symptom (states/transitions/markers at origin). Re-check after bug 1 fix.

## Plan (next session)
One small batch (call it BATCH-10, "editor smoke fixes"): fix #1, #2, #3 in `EditorSubsystem.cs`; re-verify the canvas→model position sync (#2 deeper half) and the JSON-vs-assembly collision winner (#1 caveat); headless tests where possible + a manual re-smoke by the user. #4/#5 are not our regressions.
