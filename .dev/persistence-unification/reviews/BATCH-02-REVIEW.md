# BATCH-02 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
Emit-core extraction (PU-101) + emit-test re-base (PU-105 remainder). The deterministic BTree/HSM emission
moved into `Hrot.AiEditor.Persistence/Emit/` (`AiEmitCoreBase`/`BTreeEmitCore`/`HsmEmitCore`, netstandard2.0,
DTO-driven); the editor emitters are now thin adapters (model → DTO → core). **Phase 1 (keystone) complete.**

## Verified (read source + assertions, ran suites)
- **Dependency isolation:** emit core lives in the existing `netstandard2.0` `Hrot.AiEditor.Persistence`
  project; csproj diff is empty (no new project ref — still only `System.Text.Json`). No editor/ImGui dep. ✅
- **Byte-identical gate (the crux, design §6.4):** `ByteIdenticalGateTests` asserts full string-equality of
  `core.Emit(mapper.ToDto(model))` vs `BTreeFluentEmitter.Emit(model)` / `HsmFluentEmitter.Emit(model)` for
  `SampleScout` + `SampleGuard` — **the complete set** (only two editor-owned `.cs` exist under
  `Trees/`+`Machines/`, verified). Includes `[*Layout]` + the const `[HsmDefinition(... AssetId = "…")]`
  form; plus determinism, `WriteAtomic` no-op/write, and marker-sync (`AiEmitCoreBase.EditorGeneratedMarker`
  == `FluentCSharpEmitterBase.EditorGeneratedMarker`). ✅
- **DTO extensions** (folded into BATCH-01 files, with new round-trip assertions): `EventDefinitionDto`
  `EventId`+`IsDeferrable`; `StateNodeDto.DeferredEventNames` — needed for byte-identity (emitter emits
  literal EventIds + deferred-event lists). Reasonable; covered by `HsmMapperRoundTripTests`. ✅
- **Compiler-root traversal:** `HsmEmitCore` skips the compiler-inserted `__Root` and emits its children as
  top-level states — covered by the byte-identical gate (would fail otherwise). ✅
- **Ran myself:** solution build 0 errors/0 warnings; `Hrot.AiEditor.Persistence.Tests` 88/88; SaveBTreeEmit
  7/7; SaveHsmEmit 8/8; EditorSubsystemBoot 10/10; Hrot.Blueprints.Tests 1357 pass / 7 pre-existing / 0 new.

## Issues Found
No issues. `SaveBTree/HsmEmitTests` re-based onto the core with **no re-baseline** (output byte-identical).

## Verdict
APPROVED. Completes PU-101 and PU-105. Phase 1 done — round-trippable JSON + relocated emit core, zero
behavior change.

## Commit Message
```
feat(persistence): emit-core extraction to netstandard2.0 + emit-test re-base (BATCH-02)

Completes PU-101, PU-105 (emit-core portion). Phase 1 keystone complete.
Relocates the deterministic BTree/HSM C# emission into Hrot.AiEditor.Persistence/Emit
(AiEmitCoreBase/BTreeEmitCore/HsmEmitCore, netstandard2.0, driven by the persisted DTO);
BTreeFluentEmitter/HsmFluentEmitter become thin adapters (model→DTO→core); FluentCSharpEmitterBase
delegates the marker + WriteAtomic to the core. Two DTO extensions for byte-identity:
EventDefinitionDto.EventId/IsDeferrable, StateNodeDto.DeferredEventNames (round-trip asserted).
Zero behavior change (relocation only).
Tests: byte-identical gate (core.Emit(ToDto(model)) == legacy emitter, all editor-owned fixtures,
incl. [*Layout] + const AssetId form), determinism, WriteAtomic, marker-sync; SaveBTree/HsmEmitTests
re-based onto the core (no re-baseline).
Build 0 warnings (touched); persistence 88/88; SaveBTree/HsmEmit 15/15; boot 10/10; Blueprints
7 pre-existing/0 new.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
