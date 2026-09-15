# BATCH-06 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
PU-601 (`RegenerationScheduler.FlushNow()`), PU-602 (`SaveAllAiDocumentsCommand` + `AtomicFileWriter`),
PU-603 (Ctrl+Shift+S + Save-All button + flush-on-close + Shutdown flush). Purely additive; the debounced
`.cs` auto-emit routing is deliberately UNCHANGED (regression-safe). **Phase 6 complete.**

## Verified (read source + assertions, ran suites)
- **FlushNow:** shared `Drain()` with `Tick()`; drains immediately, no debounce guard; debounce `Tick()`
  unchanged (RegenerationSchedulerTests 5/5). ✅
- **Save-All:** `SaveAllAiDocumentsCommand.Execute` iterates open docs, dispatches by Kind via INJECTED
  delegates (circular-ref-safe, mirrors `AiAssetEmitService`); BTree/HSM via mapper→JsonServices→
  `AtomicFileWriter` (temp-then-move, ns2.0); Blueprint via the unchanged `SaveActiveBlueprintCommand.Save`.
  Tests assert: clean doc not written; no-path doc skipped with `[WARN]` + LEFT DIRTY (not silently cleaned);
  dirty BTree/HSM written to disk + JSON round-trips (Name/AssetId) + `MarkClean`; null-manager no-op;
  AtomicFileWriter create/overwrite/mkdir/null-throw. Gold-standard. ✅
- **flushAction UNCHANGED (regression-safety):** confirmed `EditorSubsystem.cs:2293` still `emitService.Emit`
  for BTree/HSM (+ the explicit comment at :1960); the `.cs`→JSON switch is correctly deferred to PU-401.
  Blueprint `_blueprintQuickReloadTrigger` arm unchanged. ✅
- **Flush-on-close:** `AiDocumentManager.BeforeDocumentClosed` event (manager stays persistence-agnostic);
  EditorSubsystem subscribes + Shutdown FlushNow + Save-All. ✅
- **Blueprint Save path untouched** (SaveActiveBlueprintCommandTests 8/8; Blueprints 7 pre-existing/0 new). ✅
- **Ran myself:** build 0 errors/0 warnings; AiShared 789/789; SaveActiveBlueprintCommand 8/8;
  EditorSubsystemBoot 10/10; Blueprints 7 pre-existing/0 new.

## Issues / Debt
- **PU-D11 (P2, deferred-by-design):** the debounced `flushAction` still writes `.cs` for BTree/HSM (edit-to-
  live). The switch to writing JSON (retiring the `.cs` auto-emit) lands with migration at PU-401 — flipping
  it before assets are `.json` would break BTree/HSM edit-to-live. Recorded; not a defect.
- **Out-of-scope strays found in the working tree (not part of this batch; committed separately as a hygiene
  commit):** a defensive ImGui `BeginChild/EndChild`-pairing fix across the NodeEdit Picker layouts (6 files)
  + a NodePinSchema null-guard (no-op; `?.` already null-safe). A `Program.cs` whitespace-only edit was
  discarded. These accumulated from session sub-agents and were never staged; reviewed as low-risk/correct.

## Verdict
APPROVED. Completes PU-601/602/603. Phase 6 done — explicit Save-All writes JSON for dirty path'd docs,
FlushNow drains the debounce, flush-on-close + Shutdown safety net; Blueprint path and edit-to-live untouched.

## Commit Message
```
feat(persistence): unified Save-All + FlushNow + flush-on-close (BATCH-06, Phase 6)

Completes PU-601, PU-602, PU-603. Purely additive (debounced .cs auto-emit routing UNCHANGED —
the .cs->JSON switch is deferred to PU-401 to avoid regressing BTree/HSM edit-to-live).
- RegenerationScheduler.FlushNow(): immediate drain (shared Drain() with Tick(); debounce unchanged).
- SaveAllAiDocumentsCommand.Execute: iterate open docs, dispatch by Kind via injected save delegates
  (circular-ref-safe); BTree/HSM -> mapper.ToDto -> JsonServices.Serialize -> AtomicFileWriter (temp-then-
  move, netstandard2.0); Blueprint -> unchanged SaveActiveBlueprintCommand.Save. No-path docs skipped with
  a [WARN] report + left dirty; per-doc failures caught + reported, never thrown.
- EditorSubsystem: _saveAllCallback (FlushNow + Execute) on Ctrl+Shift+S + "Save All" button;
  AiDocumentManager.BeforeDocumentClosed event drives flush-on-close; Shutdown flushes. Ctrl+S unchanged.
Tests (20): FlushNow immediate/empty + debounce-unaffected; Save-All write+round-trip+MarkClean (BTree/HSM),
no-path skip+warn+left-dirty, clean-not-written, null-manager no-op; AtomicFileWriter create/overwrite/mkdir/
null. AiShared 789/789; SaveActiveBlueprintCommand 8/8; boot 10/10; Blueprints 7 pre-existing/0 new.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
```
