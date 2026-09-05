# BATCH-02 Review
**Status:** ✅ APPROVED (after 1 corrective round)   **Date:** 2026-06-15

## Summary
S1-2 (per-asset blackboard struct + topology-over-struct), S1-3 (baked-offset registrar/adapter), S1-4 (validator unblock `ThreeParamReusable`) implemented. Offsets are single-sourced through `BTreeBlackboardPackHelper` (struct emit, blob keys, registry keys all derive from one `Pack` call), so blob key == registry key == `Marshal.OffsetOf`.

## Issues Found (both fixed in corrective round)
### Issue 1 (P1, FIXED): over-100B silent overflow
**Was:** `GenerateOneAsset` emitted the managed struct with no overflow guard; the named test `ManagedAsset_MasterDtoOver100Bytes_HardErrors` only called `WouldOverflow` in isolation, never the generator — so a real >100 B managed asset silently emitted an oversized struct.
**Fix:** `GenerateOneAsset` now calls `BTreeBlackboardPackHelper.WouldOverflow` before emit; on overflow it reports a `BTREE0002` Warning and skips the asset (no struct, no oversized topology). Test rewritten to run the generator (13×Vector3) and assert (a) one BTREE0002 / zero errors, (b) no `.Blackboard.g.cs` emitted. Verified.

### Issue 2 (P2, FIXED): emitted registrar source unverified
**Was:** the S1-3 runtime test hand-rolls the thunks ("manually reproduce what EmitManagedActionThunks would emit") — a bug in the real emitter wouldn't fail it.
**Fix:** added `ManagedAsset_Registrar_RegistersBakedOffsetThunks` — runs `BTreeBridgeEmitCore.EmitBridge` on the Counter@0/Threshold@4 asset and asserts the emitted registrar source carries keys `"{fqn}@0"`/`"{fqn}@4"` (== blob keys) AND `Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0)` / `(nint)4` (per-binding baked offset, not @0 for all). Verified.

## Test Quality (notable strengths)
- `ManagedAsset_GeneratesStruct_OffsetsMatchBinPacker` runs the real emitter AND cross-checks every offset against the runtime `BlackboardBinPacker` for `{int,Vector3,bool}` → partially closes DEBT-AIB-002 (the two packers provably agree for that shape).
- `ManagedAsset_TopologyBuiltOverGeneratedStruct_BlobKeysCarryOffsets` asserts a non-first `@4` key and the absence of the legacy `dto => dto.X` lambda.
- S1-3 runtime test ticks a real `Interpreter` and proves no cross-DTO bleed (offset 0 action does not touch offset 8).
- S1-4: type-matched validates, mismatch + missing-target → BTREE0002 skip (no build break).

## Architecture note (acceptable deviation, recorded as debt)
The build-time packer is a **second** implementation (`BTreeBlackboardPackHelper`, string-typed) because the netstandard2.0 source-gen path cannot reference the editor's `BlackboardBinPacker` (runtime `Type`). The two are kept in agreement by the cross-check test above, but only for `{int,Vector3,bool}`. → DEBT-AIB-011 (extend the cross-check to more shapes incl. a `fixed`/array case before relying on heavy/array variables).

## Verified by lead
`Hrot.AiEditor.Generators.Tests` 63/2 (the 2 = known `MigrationEquivalenceTests`, DEBT-AIB-007); `Hrot.AiEditor.Persistence.Tests` 129/0 (byte-identity green); `Fdp.Toolkits.Tests` Behavior slice 146/0. The 24 other `Fdp.Toolkits.Tests` failures are in unrelated runtime subsystems (Replication/Gizmos/Combat/Geographic/CarKinem/Replay/Orchestration); this batch does not modify `Fdp.Toolkits` runtime (only Hrot.AiEditor.* + one new test file) → not regressions. Recorded as DEBT-AIB-010 to avoid masking.

## Verdict
APPROVED. All three tasks meet their success conditions; the two review issues are fixed and re-verified.

## Commit Message
```
feat(btree-ai-binding): per-asset blackboard struct + baked-offset registrar + validator (BATCH-02)

Completes S1-2, S1-3, S1-4
- BTreeBlackboardPackHelper: build-time bin-packer (netstandard2.0, string-typed),
  single source of byte offsets for struct emit + blob keys + registry keys.
- BTreeEmitCore.EmitBlackboardStructSource: per-asset [StructLayout(Sequential)]
  struct for Managed==true assets (bool→[MarshalAs(I1)]); topology emits offset-keyed
  {Fqn}@{offset} blob keys (guarded; Managed==false byte-identical).
- BTreeBridgeEmitCore: real Unsafe.As/AddByteOffset baked-offset thunks registered
  into the injected ActionRegistry (replacing stub fallbacks) for managed assets.
- BTreeJsonGenerator: skip managed assets exceeding 100B inline budget with BTREE0002.
- BTreeMethodCompatibilityValidator: accept type-matched ThreeParamReusable; else BTREE0002.
Tests: generator offset/blob-key/registrar-thunk assertions + runtime cross-talk/gating
(no-DTO-bleed) + over-100B generator-skip; cross-checked vs runtime BlackboardBinPacker.
```
