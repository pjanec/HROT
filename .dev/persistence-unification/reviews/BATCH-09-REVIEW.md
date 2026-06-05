# BATCH-09 Review (PU-402, Phase 4 part 2 — COMPLETES Phase 4)
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
Atomic decommit of the two editor-owned assets to JSON + flushAction→JSON switch (resolves PU-D11).
SampleScout/SampleGuard `.cs` deleted; `.btree.json`/`.hsm.json` now the source of truth; the generator emits
the runtime topology-core + `[BlueprintRegistrar]` bridge; layout served from JSON. **Migration critical path
(Phases 1→2→3→4) COMPLETE** — the core promise (save always works; assets reopen even when C# won't compile)
is realized for editor-owned BTree/HSM.

## Verified (read source + assertions, ran suites + a CLEAN rebuild myself)
- **Atomic swap (git status):** `Trees/SampleScout.cs` + `Machines/SampleGuard.cs` DELETED; the staged artifacts
  `git mv`'d into live `Trees/SampleScout.btree.json` + `Machines/SampleGuard.hsm.json` (history preserved). ✅
- **CLEAN-BUILD SAFE (I verified, not just incremental):** nuked `obj/Debug` + `obj/GeneratedFiles` for
  Hrot.AI.Behaviors and rebuilt `--no-incremental` → 0 errors/0 warnings; the 4 generated files
  (`SampleScout.g.cs` + `.Registrar.g.cs`, `SampleGuard.g.cs` + `.Registrar.g.cs`) emit; and **`FbtTreeCatalog.g.cs`
  drops to 0 SampleScout refs** (confirms the cross-generator-invisibility prediction — the earlier "present" was a
  STALE incremental artifact) yet the build still compiles → `GetSampleScout()` loss is benign (zero callers), as
  researched. This was the key risk; it's clean. ✅
- **Bridge regression anchor green:** `BlueprintRegistrarBridgeIntegrationTests` incl.
  `BTree_SampleScout_Bridge_Register_TreeIsTickable` pass → JSON-owned asset still discovered→registered→ticks via
  the masquerade bridge (wired into real editor startup `TriggerInitialLoad → ScanForRegistrars`). ✅
- **flushAction→JSON (PU-D11 resolved):** BTree/HSM branch now writes JSON to `SourceFilePath` via the SAME
  `saveBTreeDelegate`/`saveHsmDelegate` as Save-All (mapper.ToDto→JsonServices.Serialize→AtomicFileWriter), runs
  the BATCH-07 collision guard first, skips no-path, never throws; **Blueprint branch + `_blueprintQuickReloadTrigger`
  UNCHANGED**; `emitService` suppressed (NOT removed). This was safe to flip because SampleScout/SampleGuard were
  the only editor-owned BTree/HSM assets → the PU-D11 deferral reason (un-migrated `.cs`) is gone. ✅
- **Test dispositions (read the diffs — honest, not delete-to-go-green):**
  - MIGRATED (coverage preserved): `*_LoadFrom_*_LayoutIsApplied` (BTree+HSM) now assert layout via the JSON
    contributor reading the live `.json` (layout is now JSON-owned). ✅
  - CONVERTED: the 2 `MigrationJson_*_CarriesLayout` tests now read the live committed `.json` from disk (round-trip
    byte-stable + layout) instead of regenerating from the assembly (which no longer has `[*Layout]`). ✅
  - DELETED (genuinely obsolete — method gone / metadata pruned): `SampleScout.Layout()`/`SampleGuard.Layout()`
    direct-call tests; `SampleGuard_LayoutNamespace_IsResolvableFromBehaviorsAssembly` + the AiContracts-reference
    assertion in `BTreeEmitter_*` (after decommit the runtime assembly no longer references AiContracts for layout —
    reference pruning; the emitter test was rewritten to assert valid-C#-output instead). All justified. ✅
  - New `FlushActionJsonWriteTests` (4): flush writes valid JSON not C# (asserts no `//`/`using`; has `$meta`/
    `docType`/Name/AssetId), round-trips byte-stable, no-path skips silently, Blueprint routed separately.
    Mirrors the production wiring inline (EditorSubsystem isn't headless; boot test covers real init). ✅
- **Ran myself:** clean rebuild 0/0; Generators.Tests 41/41; BTree.Editor 391/391; Hsm.Editor 339/339;
  AiShared 822/822 (+4 flush); EditorSubsystemBoot 10/10; Blueprints 7 pre-existing (DEBT-006)/0 new.

## Issues / Debt
- **PU-D11 → RESOLVED** (flushAction writes JSON).
- **Manual-verify (carried):** the end-to-end edit→MSBuild-regen→hot-reload **latency/reload loop** for migrated
  assets is NOT headlessly verifiable — covered by the user's running-editor smoke (deferred) + Phase 9 (≤100 ms).
  Persistence correctness (valid JSON, no clobber) IS proven headlessly.
- `AiAssetEmitService` (the old `.cs` writer) is now unused from the flush path; retained per spec (may serve a
  future hand-authored direct-emit path). Low-pri cleanup candidate.
- **PU-D12/PU-501 now UNBLOCKED:** assets are JSON + flush writes JSON → path-at-creation (writing `.json` at
  creation + setting `SourceFilePath`) is now safe. PU-501 can be picked up.

## Verdict
APPROVED. PU-402 complete; **Phase 4 COMPLETE**; migration critical path delivered. Clean-build-safe (verified),
bridge-registered, layout-from-JSON, flush-writes-JSON. The remaining edit-to-live reload loop is a manual/Phase-9 item.

## Commit Message
```
feat(persistence): decommit SampleScout/SampleGuard to JSON + flushAction->JSON (BATCH-09, PU-402; Phase 4 complete)

Completes the migration critical path. SampleScout.cs/SampleGuard.cs DELETED; Trees/SampleScout.btree.json +
Machines/SampleGuard.hsm.json are now the source of truth — the BTree/HSM JSON generators emit the runtime
topology-core + [BlueprintRegistrar] bridge (registered at editor startup via ScanForRegistrars); layout served
from JSON. Verified CLEAN-build-safe: FbtTreeCatalog drops GetSampleScout (cross-generator invisibility) yet
compiles (zero callers); bridge "tickable" anchor green.
- flushAction (PU-D11 resolved): BTree/HSM now write JSON to SourceFilePath via the Save-All delegates
  (mapper.ToDto -> JsonServices.Serialize -> AtomicFileWriter) + collision guard + never-throw; writing C# would
  have clobbered the .json. Safe now that the only two editor-owned assets are migrated. Blueprint path unchanged.
- Tests: LayoutIsApplied (BTree+HSM) migrated to the JSON contributor; MigrationJson tests read the live .json;
  obsolete .Layout()/AiContracts-reference tests removed; new FlushActionJsonWriteTests (writes JSON not C#,
  round-trips, no-path skip, blueprint routed). AiAssetEmitService now unused from flush (retained).
Clean build 0/0; Generators 41/41; BTree 391; HSM 339; AiShared 822; boot 10/10; Blueprints 7 pre-existing/0 new.
Manual-verify: end-to-end edit->reload latency is Phase 9 / running-editor smoke.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
