# RESUME / HANDOFF — Editor "save-on-close" fix + CA-07 state (2026-08-02)

Self-contained handoff written before a preventive compaction. Two threads: (1) the **CA-07
collections** workstream (done + committed), (2) the **editor save-on-close** bug (investigated,
design approved by user, **NOT yet implemented** — implement next).

**Branch:** `claude/blueprint-component-read`. **Working tree:** clean. **HEAD:** `ea001bd9`.

---

## THREAD 1 — CA-07 collections (A2 Unreal collection-pin, R1 curated-accessor) — DONE

Designer-facing `ComponentForEach` / `ComponentItemGet` / `ComponentItemCount` nodes consuming a
`GetComponent` collection out-pin. Full detail: `Blueprint_Component_Access_TASK_TRACKER.md`
running log + `Architect_Question_17_*.md`.

| Batch | Commit | State |
|-------|--------|-------|
| CA-07a collection pin + GetComponent out-pin | `66cf4432` | ✅ gated |
| CA-07b consumer nodes + IR + emit | `3b294552` | ✅ gated |
| CA-07c wire-bake + palette + titles | `3d480e5b` | ✅ gated |
| array-pin editor fix (no scalar editor on array pins) | `98463ac9` | ✅ |
| wire-orientation normalize (drag-from-input) | `fc1222e5` | ✅ **user-confirmed working** |
| emit typed `default` + **removed demo** | `ea001bd9` | ✅ gated |

- **Feature is functionally complete + green.** User visually confirmed the wire-bake + the
  drag-from-input fix work. Consumer nodes are covered by `ComponentCollectionConsumerLoweringTests`
  (in-code fixtures) — the `ComponentCollectionDemo.bp.json` was **removed** (`ea001bd9`) because the
  editor's save-on-close kept corrupting it (see THREAD 2); re-add a demo only AFTER the save bug is
  fixed (and author it editor-created, not hand-authored explicit-GUID).
- **Gate:** serial `Hrot.AiEditor.Generators.Tests` = **184/184 byte-identical** + `Hrot.Blueprints.Tests`
  Component/Stage4/TypeSystem green (sole red = pre-existing `TypeResolve_UnknownFieldType_EmitsBP1500`).
- **Deferred: CA-07d** (managed collections + `Contains`/`Find`).
- **Pending: PR** `claude/blueprint-component-read` → `main` for the whole workstream (CA-01..CA-07c).

---

## THREAD 2 — Editor "save-on-close" bug — INVESTIGATED, APPROVED, **IMPLEMENT NEXT**

### Root cause (confirmed)
`EditorSubsystem.cs:2508` subscribes to `AiDocumentManager.BeforeDocumentClosed` and, for **any dirty
document**, silently writes it to disk (`PU-603 flush-on-close`):
```csharp
_aiDocumentManager.BeforeDocumentClosed += doc => {
    if (!doc.IsDirty) return;
    var path = doc.Asset.SourceFilePath; if (string.IsNullOrEmpty(path)) return;
    switch (doc.Kind) {
        case Blueprint: saveBlueprintDelegate(asset, path); doc.MarkClean(); break;
        case BTree:     saveBTreeDelegate(asset, path);     doc.MarkClean(); break;
        case Hsm:       saveHsmDelegate(asset, path);       doc.MarkClean(); break;
        default: break; // scenario/etc not saved here
    }
};
```
So **closing an edited doc persists it — no explicit Save**. For blueprints the save is *projection-only*
(`SaveActiveBlueprintCommand.Save` strips every node's `Pins` to `[]`); editor-created assets round-trip
(deterministic pin GUIDs) but **hand-authored** assets (arbitrary GUIDs) get dangling links → the
`BP1601`/`CS8716`/`CS0266` errors we chased on the demo.

### Ruled out
- **`RegenerationScheduler`** (500ms debounce, `EditorSubsystem.cs:3170`): blueprint branch only
  *optionally in-memory recompiles* (opt-in `_blueprintAutoReloadOnEdit`, default **off**) and returns —
  **never writes `.bp.json`**. Only BTree/HSM get auto-written by it.
- **Tab-close prompt** (`AiGraphCanvasWindow.RequestTabClose` → modal): correct. "Don't Save"
  (`ResolveCloseDiscard`) calls `doc.MarkClean()` so the flush's `if(!doc.IsDirty)` skips. The unwanted
  write comes from a close path that **bypasses the prompt** (closing the whole editor/window).
- **`AiDocument.IsDirty => _isDirty`**, `MarkClean(){_isDirty=false}` — simple flag, so MarkClean truly
  gates the flush.

### APPROVED FIX (user: **all doc kinds**, **silent-discard on app-exit for now**)
**Decouple save from close: make the prompt's "Save" save explicitly, then REMOVE the auto-flush.**
Net: only an explicit prompt-"Save" ever writes; every other close path (Don't Save, app-exit,
programmatic) discards.

1. **`Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`**
   - Add an injected save delegate: `Action<AiDocument>? _saveDocument` (constructor param appended at
     the END with default `null` so existing 3-arg/5-arg call sites + tests still compile; ctor is at
     ~line 71-ish region — verify the exact signature). Store it.
   - `ResolveCloseSave()` (~494): change from "close-while-dirty (relies on flush)" to explicit:
     ```csharp
     var doc = _pendingCloseDoc; _pendingCloseDoc = null;
     if (doc != null) { _saveDocument?.Invoke(doc); doc.MarkClean(); _docManager.Close(doc); }
     ```
   - `ResolveCloseDiscard()` (~502): unchanged (`MarkClean` + `Close`).
   - `RequestTabClose` clean-branch (~488): unchanged.
2. **`Hrot.Editor/EditorSubsystem.cs`**
   - **Delete** the `BeforeDocumentClosed += …` flush handler (lines **2505–2542**).
   - Build a `saveDocument` delegate (dispatch by `doc.Kind`, reusing `saveBlueprintDelegate`/
     `saveBTreeDelegate`/`saveHsmDelegate`, guard empty `SourceFilePath`, try/catch — literally the
     switch that was in the flush) and pass it into **all three** `AiGraphCanvasWindow` ctors
     (BTree ~2752, HSM ~2761, Blueprint ~2771).
3. **Tests** `Hrot.Editor.AiShared.Tests/Windows/AiGraphCanvasWindowTabBarTests.cs`
   - `ResolveCloseSave_ClosesWhileDirtySoUpstreamFlushPersists` (~272): rewrite — inject a **spy
     `saveDocument`**; assert it was invoked once for the doc, doc closed, marked clean. (No longer
     asserts the `BeforeDocumentClosed` flush.)
   - `ResolveCloseDiscard_ClosesWithoutSaving` (~253): keep; assert the spy `saveDocument` was **not**
     invoked (replace the `flushedDirty` BeforeDocumentClosed assertion).
   - Grep the test project for any other `BeforeDocumentClosed`-flush assertions and update.
   - `AiGraphCanvasWindow` is constructed in tests as `new AiGraphCanvasWindow("BTree", dm, seam)` and
     `(kind, dm, seam, pickers, input)` — the new param is optional, so these compile; add the spy only
     where needed.
4. **Gate:** `dotnet build Hrot.Editor.AiShared + Hrot.Editor`; `dotnet test Hrot.Editor.AiShared.Tests`
   (tab-bar tests green). Sanity: explicit Ctrl+S / Save-All path (`ShellSaveCommands`/
   `SaveAllAiDocumentsCommand`) is UNAFFECTED — it saves directly, not via the removed flush.

### Follow-ups to record (do NOT lose)
- **[USER-REQUESTED] Proper app-exit "You have unsaved changes" prompt.** After this fix, app-exit
  silently discards dirty docs. User explicitly wants a real unsaved-changes prompt on app/window
  close **later**. Track it.
- **Lossless projection-only save for hand-authored assets.** The pin-strip save breaks hand-authored
  explicit-GUID links. Fix later by re-keying links to deterministic GUIDs on save (or preserving
  explicit pins). This is why the demo kept corrupting.
- **CA-07d** (managed collections + `Contains`/`Find`) — deferred.
- **PR** `claude/blueprint-component-read` → `main`.

---

## Session workflow notes
- Batched CA-07 via **Sonnet builds + Opus reviews/gates/commits** (no Zoo). Save-on-close is
  novel editor-UX — do it **hands-on** (it's load-bearing shared code).
- **Use targeted `git add <path>`**, NOT `git add -A`, while the user has the editor open — `-A`
  swept the user's editor-saved (corrupted) demo into a commit this session.
- Serial-184 gate: `xunit.runner.json {parallelizeTestCollections:false,maxParallelThreads:1}` in the
  Generators.Tests `bin/Debug/net8.0/`, then `dotnet test --no-build`.
