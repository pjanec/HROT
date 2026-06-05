# BATCH-07 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
PU-502 (base-name collision guard, design §3 D5). Pure netstandard2.0 `AssetBaseNameCollisionGuard`
+ wiring into the BTree/HSM JSON write branch of `SaveAllAiDocumentsCommand` (block-not-throw, leave dirty).
**PU-501 (path-at-creation) deliberately NOT attempted — deferred to PU-401 (debt PU-D12), see below.**

## Verified (read source + assertions, ran suites myself)
- **Guard core** (`Hrot.AiEditor.Persistence/AssetBaseNameCollisionGuard.cs`): pure/static, ns2.0-safe (no net8 APIs).
  `GetLogicalBaseName` strips longest compound suffix (`.btree.json`/`.hsm.json`/`.bp.json`) → then `.cs` →
  fallback final-extension; casing preserved; suffix match case-insensitive. `CheckCollision` = same dir, same
  logical base (case-insensitive), OPPOSITE rep-class (CS↔JSON); self-name skipped; same-class/Other not a
  collision; error names both files + dir. `CheckCollisionOnDisk` lists only the target's own dir; lister-throws
  → null (no dir, no collision). ✅
- **Wiring** (`SaveAllAiDocumentsCommand.Execute`): collision check runs **before** the save delegate for BTree
  and HSM; on collision → `[BLOCKED]` report + `break` (delegate NOT called, doc left dirty, never throws).
  Blueprint branch UNCHANGED (no guard wired — §16 constraint honored). flushAction UNCHANGED; no `SourceFilePath`
  pointed at `.json`; no BTree/HSM creation command added. ✅
- **Tests:** guard unit tests (`AssetBaseNameCollisionGuardTests`, 21) — both directions × all three suffixes,
  case-insensitive base compare, same-class non-collision, self-skip, empty list, disk-lister scoping, dir-absent.
  Genuine (no tautologies). Wiring tests (`SaveAllWithCollisionGuardTests`) — real temp-dir: plant `Foo.cs`,
  assert `.btree.json`/`.hsm.json` NOT written + delegate NOT called + `[BLOCKED]` reported + doc stays dirty;
  plus no-collision regression (writes + round-trips). ✅
- **Review correction:** removed a vacuous stub test the coder flagged
  (`Execute_BTree_CollisionWithSiblingCs_Blocked_DocStaysDirty` — asserted nothing, `_ = …` discards; same
  vacuous-test smell as PU-D05). Removed the now-unused `MakeBTreeAsset`/`MakeHsmAsset`/dir-const helpers it used.
  Re-ran: AiShared 818/818 (was 819 with the stub), build 0 warnings.
- **Ran myself:** full `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 warnings; AiShared 818/818;
  Blueprints 7 pre-existing (DEBT-006) / 0 new; EditorSubsystemBoot 10/10.

## Issues / Debt
- **PU-D12 (P2, deferred-by-design):** PU-501 path-at-creation blocked on PU-D06/PU-401 — setting `SourceFilePath`
  to a `.json` would be clobbered by the unchanged `.cs`-writing flushAction (PU-D11) on the next edit; BTree/HSM
  also have no creation flow today. Recorded; rides with the migration batch.
- Guard not yet wired into a *creation* path (none exists for BTree/HSM; Blueprint creation isn't wired into
  EditorSubsystem). The guard is ready for the migration/creation batch to call at creation time.

## Verdict
APPROVED. Completes PU-502. Phase 5 partially done (PU-502 ✅, PU-501 deferred → PU-401/PU-D12). The guard
closes the "no duplicate-name guard anywhere" gap the research surfaced and is the safety net for the eventual
`.cs`↔`.json` coexistence during migration.

## Commit Message
```
feat(persistence): base-name collision guard (D5) + Save-All wiring (BATCH-07, PU-502)

Completes PU-502. PU-501 (path-at-creation) deferred to PU-401 (debt PU-D12): setting SourceFilePath to a
.json would be overwritten by the unchanged .cs-writing flushAction (PU-D11) on the next edit, and BTree/HSM
have no creation flow yet. Purely additive; flushAction + Blueprint write path UNCHANGED.
- AssetBaseNameCollisionGuard (netstandard2.0, pure): a .cs and an editor-owned .{btree|hsm|bp}.json must not
  share a logical base name in the same directory (design §3 D5). Longest-compound-suffix base extraction;
  CS<->JSON opposite-class detection; same-dir scoping; self-skip; case-insensitive.
- SaveAllAiDocumentsCommand: BTree/HSM JSON write now runs CheckCollisionOnDisk first; a collision is BLOCKED
  ([BLOCKED] report, delegate not called, doc left dirty, never throws). Blueprint branch unchanged.
Tests (30): guard both-directions x 3 suffixes + case/self/empty/disk-scoping/dir-absent; Save-All real-temp-dir
block (BTree+HSM) + no-collision write+round-trip regression. Build 0/0; AiShared 818/818; Blueprints 7 pre-
existing/0 new; EditorSubsystemBoot 10/10.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
