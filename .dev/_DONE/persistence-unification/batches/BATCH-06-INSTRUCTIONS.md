# BATCH-06: Unified Save / Save-All + FlushNow + flush-on-close
**Tasks:** PU-601, PU-602, PU-603  **Phase:** 6 (unified Save)  **Est:** ~11h
**Dependencies:** BATCH-01 (JSON services + mappers), BATCH-05 (dual-load; editor-owned assets carry SourceFilePath). **Independent of PU-D06.**

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/_DONE/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — **§8** (Save-All, FlushNow, Ctrl+Shift+S, flush-on-close; "C# regeneration stays a separate build/on-demand step"), §5.2 (projection-only). Cite.
3. `.dev/_DONE/persistence-unification/TASK-DETAIL.md` — PU-601/602/603 success conditions.
4. `reviews/BATCH-05-REVIEW.md` (mappers' `ToDto`/`ToModel`; SourceFilePath now set for editor-owned).
5. Codebase Memory MCP first; never `search_code`.

## Verified seams (lead-confirmed via research — re-verify, cite)
- `SaveActiveBlueprintCommand.Save(asset, path)` (`Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs:66`): projection-only via temp pin-swap → `BlueprintJsonServices.Serialize` → `File.WriteAllText`; `SaveFromActiveDocument` (:124) = active doc only, returns `NoPath` when `SourceFilePath` empty. **Headless-testable.**
- `AiDocumentManager.OpenDocuments` (:77), `.Active` (:74), `Close(doc)` (:216 — no flush today), `Shutdown` (no flush). `AiDocument.IsDirty`/`MarkClean` (:56-62), `.Kind` (:24), `.Asset`. `IEditableAsset` has `Kind`/`SourceFilePath`/`IsDirty`.
- `RegenerationScheduler.cs`: `_pending` dict, `Tick()` (:104 — debounce-gated drain), called per-frame at `EditorSubsystem.cs:1429`. **No `FlushNow` today.** `flushAction` (EditorSubsystem.cs ~2153): BTree/HSM → `emitService.Emit` (writes **.cs**); Blueprint → `_blueprintQuickReloadTrigger` (in-memory compile, no `.bp.json` write).
- Mappers exclude runtime-only fields (§5.2) — the projection-only step for BTree/HSM (no temp-swap needed). `CommandCatalog.SaveAll = "editor.save-all"` already exists. Ctrl+S wired at `EditorSubsystem.cs:~1520` + a "Save Blueprint" button.

## CRITICAL scope decision (regression safety) — read carefully
**Do NOT change the debounced `flushAction` BTree/HSM routing in this batch.** Today it writes `.cs` (→ file-watcher → MSBuild → edit-to-live). The generated-`.cs`→JSON switch only becomes safe once real assets are `.json` (migration PU-401, currently blocked on PU-D06); flipping it now would **break BTree/HSM edit-to-live** for the existing un-migrated `.cs` assets. So this batch is **purely additive**: an explicit Save-All that writes JSON for dirty *path'd* docs, FlushNow, and flush-on-close. The auto-emit `flushAction`→JSON switch is **deferred to the migration batch (PU-401+)** — note it in the report. (Today most BTree/HSM docs are assembly-loaded with `SourceFilePath=""` → Save-All skips them with a warning; Blueprint docs are path'd and save fine — the BTree/HSM JSON-save path is built + tested via synthesized path'd docs, dormant for real assets until migration.)

## Tasks (sequence; don't start the next until the current's tests pass.)

### Task 1 — PU-601: `RegenerationScheduler.FlushNow()` — file: `Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs` (UPDATE)
Add `public int FlushNow()` that drains `_pending` **immediately** (no debounce-elapsed guard), invoking `_flushAction` per asset; returns the count. Extract the copy-and-clear drain body shared with `Tick()`. Re-entrancy-safe (copy then clear before invoking).
**Tests required:** inject a fake clock + spy flushAction; `Schedule(a)` then `FlushNow()` (without advancing the clock) flushes `a` immediately and empties the queue; `FlushNow()` on an empty queue returns 0; existing debounce `Tick()` tests stay green.

### Task 2 — PU-602: unified Save-All command — files: NEW `SaveAllAiDocumentsCommand` in `Hrot.Editor.AiShared/` + a `netstandard2.0` atomic writer (UPDATE/NEW)
New static `SaveAllAiDocumentsCommand.Execute(AiDocumentManager manager, ...injected save delegates..., Action<string>? report)`: iterate `manager.OpenDocuments`, skip non-dirty; for each dirty doc dispatch by `doc.Kind`:
- `Blueprint` → resolve the `BlueprintAsset` (as `SaveFromActiveDocument` does, but for ANY doc) → `SaveActiveBlueprintCommand.Save(asset, path)` + `doc.MarkClean()` (+ the blueprint `DirtyTracker.MarkClean` if applicable). **Keep `SaveActiveBlueprintCommand` behavior unchanged** (don't touch the Blueprint write path).
- `BTree` → `BehaviorTreeAssetMapper.ToDto((BehaviorTreeAsset)doc.Asset)` → `BTreeJsonServices.Serialize` → atomic write to `SourceFilePath` + `doc.MarkClean()`.
- `Hsm` → `HsmAssetMapper.ToDto((HsmAsset)doc.Asset)` → `HsmJsonServices.Serialize` → atomic write + `doc.MarkClean()`.
- **No path → skip with a `report(...)` WARNING** (not silent): "Skipped '{Name}': no source path (awaiting migration/path-at-creation)". Never throw.
Avoid circular assembly refs: inject the BTree/HSM (and Blueprint) save as `Func`/delegates wired in `EditorSubsystem` (mirror `AiAssetEmitService`'s injected-delegate pattern), OR place the command where it can see the mappers. Add an `AtomicFileWriter.Write(path, content)` (temp-file-then-move) in `Hrot.AiEditor.Persistence` (netstandard2.0) for the BTree/HSM JSON writes (do NOT change Blueprint's existing `File.WriteAllText`).
**Tests required (headless):** open N dirty docs of mixed kinds (BTree+HSM with synthesized `.json` paths in a temp dir; a Blueprint via the existing stub pattern) → `Execute` → each path'd doc's JSON written to disk + deserializes back equal + `doc.IsDirty==false`; a no-path doc is **skipped with a report warning** and left dirty (not thrown). A clean doc is not written.

### Task 3 — PU-603: wire Ctrl+Shift+S + flush-on-close — files: `EditorSubsystem.cs` (+ `AiDocumentManager` close hook) (UPDATE)
- Add a `_saveAllCallback` (set in `RegisterWindows`) that calls `_regenerationScheduler?.FlushNow()` then `SaveAllAiDocumentsCommand.Execute(...)`. Wire **Ctrl+Shift+S** (gated like the existing Ctrl+S block: `ImGui.GetCurrentContext() != IntPtr.Zero`) + a "Save All" toolbar button. Keep `Ctrl+S` = active-only (recommended; unchanged).
- **Flush-on-close:** when a document is closed (find the tab-close call site → `_aiDocumentManager.Close(doc)`) and on `Shutdown()`, if `doc.IsDirty` save it to JSON first (reuse the same per-doc save logic). Add a `BeforeClose`/callback hook on `AiDocumentManager` OR do it at the EditorSubsystem call site (keep the manager persistence-agnostic). `Shutdown` also `FlushNow()`s.
**Tests required (headless):** the close hook saves a dirty path'd doc before close (spy/temp-file); the underlying Save-All command is invoked by the callback. (The ImGui keybinding itself is not headless — test the command + callback, not the key.)

## Success Criteria
- [ ] PU-601: `FlushNow()` drains immediately; debounce `Tick()` unaffected. + tests.
- [ ] PU-602: Save-All writes JSON for every dirty **path'd** doc by Kind (BTree/HSM via mappers+services+atomic write; Blueprint via existing `Save`), `MarkClean`s them; **no-path docs skipped with a report warning, left dirty, never throws**. + tests.
- [ ] PU-603: Ctrl+Shift+S + "Save All" button + flush-on-close + Shutdown flush wired; `Ctrl+S` unchanged. + command/callback tests.
- [ ] **Debounced `flushAction` BTree/HSM `.cs` routing UNCHANGED** (edit-to-live not regressed); deferral to PU-401 noted in the report.
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings (touched); new tests green; `SaveActiveBlueprintCommandTests` + `RegenerationSchedulerTests` green (no regression); `EditorSubsystemBoot` 10/10; `Hrot.Editor.AiShared.Tests` green; `Hrot.Blueprints.Tests` only pre-existing (0 new — **Blueprint save path unchanged**). Report exact counts.
- [ ] Report → `.dev/_DONE/persistence-unification/reports/BATCH-06-REPORT.md`.

## Report Requirements
The Save-All command location + how circular refs are avoided (injected delegates?); the atomic writer; the no-path skip+warn behavior; confirmation the debounced `flushAction` `.cs` routing is UNCHANGED + why (edit-to-live), with the PU-401 deferral noted; the close/Shutdown flush hook approach (manager callback vs call-site); whether Blueprint's `Save` was touched (should be no); weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. No `editor_stride`. **Do NOT change the debounced `flushAction` routing; do NOT touch `BlueprintJsonServices`/`BlueprintAsset`/the Blueprint `Save` write path (§16 risk). Do NOT decommit `.cs` (PU-402).** Do NOT commit (the lead commits).
