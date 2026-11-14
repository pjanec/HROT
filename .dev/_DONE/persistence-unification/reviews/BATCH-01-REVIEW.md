# BATCH-01 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
JSON substrate (PU-102/103/104 + PU-105 round-trip) landed cleanly: a new `netstandard2.0`
`Hrot.AiEditor.Persistence` lib (DTOs + `BTree/HsmJsonServices`, System.Text.Json mirroring
`BlueprintJsonServices`), editor⇄DTO mappers in the BTree/HSM editor projects, 75 tests. Zero behavior
change (no load-path switch, no generator, no `.cs` decommit).

## Verified (read source + assertions, ran suites)
- **Dependency isolation (success condition):** `Hrot.AiEditor.Persistence.csproj` is `netstandard2.0`,
  references only `System.Text.Json` — no net8/editor/ImGui ref. ✅
- **Design §5.2 persist/drop:** mapper tests assert topology/layout/pills/sync/suppressions/blackboard
  preserved AND a reflection check that node DTOs carry no `KernelBlobIndex`; `IsDirty` not activated on
  restore. ✅
- **§5.1 JSON:** services mirror Blueprint settings; `$meta` first; `docType` `Hrot.BTree`/`Hrot.Hsm`;
  polymorphic `kind`; header-lazy discovery skips malformed without throwing. ✅
- **Test quality (gold standard):** field-by-field VALUE assertions on a reflection-loaded `SampleScout`
  + a comprehensive hand-built fixture (Root/Sequence/Action/Condition/Subtree + pill + sync binding +
  suppressions + blackboard var + comments); byte-identical serialize→deserialize→serialize + determinism
  over all fixtures. Not string-presence/"exists" tests. ✅
- **Ran myself:** solution build 0 errors / 0 new warnings; persistence 75/75; EditorSubsystemBoot 10/10;
  Hrot.Blueprints.Tests 1357 pass / 7 fail (the pre-existing DEBT-006/014 set) / 0 new; AiShared 761/761.

## Issues Found
No blocking issues. Three forward-looking items (recorded as P3 in DEBT-TRACKER, by-design for "zero
behavior change"): HSM `FromDto` lives in net8 `Hrot.Hsm.Editor` (needs a public/ns2.0 builder seam for the
Phase-2 generator — resolve at PU-202); HSM persists `EventName` not `EventId` (load path PU-301 must match
by name); `HrotDocumentTypes.BTree/.Hsm` added but not registered in the migration system (intentional;
PU-301).

## Verdict
APPROVED. Maps PU-102, PU-103, PU-104, and the round-trip portion of PU-105. (PU-101 emit-core extraction
+ re-basing `SaveBTree/HsmEmitTests` → BATCH-02, by dependency.)

## Commit Message
```
feat(persistence): BTree/HSM persisted DTOs + JSON services + round-trip tests (BATCH-01)

Completes PU-102, PU-103, PU-104, PU-105 (round-trip/determinism portion).
New netstandard2.0 Hrot.AiEditor.Persistence: BTree/HSM persisted DTOs (polymorphic
kind nodes, layout, sync bindings, suppressions, forward-compatible blackboard block
per design §5.4) + BTree/HsmJsonServices mirroring BlueprintJsonServices ($meta first,
docType Hrot.BTree/Hrot.Hsm, header-lazy discovery skipping malformed). Editor⇄DTO
mappers in the BTree/HSM editor projects. HrotDocumentTypes gains BTree/Hsm.
Zero behavior change: no load-path switch, no generator, no .cs decommit.
Tests: 75 (field-by-field model→DTO→model on SampleScout + a comprehensive fixture;
runtime-only field exclusion; serialize→deserialize→serialize byte-identical + determinism).
Build 0 warnings (touched); EditorSubsystemBoot 10/10; Blueprints 7 pre-existing/0 new;
AiShared 761/761.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
