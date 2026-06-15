# BATCH-03 Review
**Status:** ✅ APPROVED (after 1 corrective round)   **Date:** 2026-06-15

## Summary
S1-2b: build-time struct-DTO size resolution via Roslyn `Compilation`. `StructSizeResolver` (Generators assembly) mirrors `BehaviorParameterSizeAnalyzer.ComputeStructSize` (managed layout: bool=1, enums=underlying, nested structs recursive, explicit layout); injected into `BTreeBlackboardPackHelper.Pack(vars, Func<string,int?> resolver, out total)`. `GenerateOneAsset` builds the size map once and threads the same resolver into struct emit + topology + bridge — single offset source preserved. Validator normalizes nested-type separators (`+`→`.`) so nested DTO bindings validate. Unresolvable / over-budget → BTREE0002 skip (no partial emit).

## Investigation outcome (no fix needed)
I traced whether the `min(size,8)` packer alignment could break with struct DTOs. Conclusion via code trace: the generated `{Asset}Blackboard` struct is **purely nominal** — nothing projects through it at runtime; all offsets flow from the bin-packer consistently (blob keys + thunks; `BrainBlackboardRenderer` reads `BehaviorDefinition.ParamsDtoType`, not the generated struct). Aligning a DTO's *start* to `min(size,8)` is always ≥ its true alignment, so it can only over-pad (caught by the 100 B budget), never misalign or overlap. So the heuristic is conservative-safe; no alignment change and no `Marshal.OffsetOf`-on-aggregate test required (would assert a non-requirement).

## Issue found & fixed (corrective round)
### Alias acceptance (coverage regression)
**Was:** the size tables keyed only on CLR FQNs; alias-authored assets (the committed `T09_BlackboardManaged` uses `float`/`Vector3`/`int`/`bool`) skipped with BTREE0002 instead of emitting their struct.
**Fix:** added the 16 C# primitive/vector alias keys to both `KnownSizes` tables (`StructSizeResolver` + `BTreeBlackboardPackHelper`), and extended the emit-side type mappers (`BTreeEmitCore.ToCsTypeName` — incl. the `bool`→`[MarshalAs(I1)]` guard — and `BTreeBridgeEmitCore.DtoTypeToGlobal`) to accept alias TypeIds. Tests `StructSizeResolver_AcceptsCSharpAliases` (alias==FQN sizes) and `T09Managed_AliasTypes_EmitsStructNoWarning` (real generator run: struct emitted with all 4 fields, 3 files, no warning). Both pass.

## Test quality
`StructDtoVariable_*` run the resolver + generator and assert resolved sizes, packed offsets, blob+registrar `@offset` keys, nested-type validation, and over-100B / unresolvable BTREE0002 skips. Alias tests prove parity and the real T09 shape. Adequate.

## Verified by lead
`Hrot.AiEditor.Generators.Tests` 71/2 (2 = known `MigrationEquivalenceTests`); `Hrot.AiEditor.Persistence.Tests` 129/0 (byte-identity green).

## Verdict
APPROVED. S1-2b complete. Struct-DTO-typed managed variables now size/pack/emit correctly. S1-G's real multi-field-DTO demo is unblocked.

## Commit Message
```
feat(btree-ai-binding): struct-DTO size resolution via Roslyn Compilation (BATCH-03, S1-2b)

Completes S1-2b (user-mandated full solution — no primitive-only shortcut)
- StructSizeResolver (Generators): managed struct sizing (bool=1, enums, nested,
  explicit layout); mirrors BehaviorParameterSizeAnalyzer.ComputeStructSize.
- BTreeBlackboardPackHelper.Pack/WouldOverflow gain an injected size-resolver overload;
  GenerateOneAsset threads one Compilation-backed resolver into struct/topology/bridge emit.
- Validator normalizes nested-type separators so nested struct DTO bindings validate.
- Unresolvable / aggregate-over-100B managed assets skip with BTREE0002 (no partial emit).
- Accept both C# alias (float/Vector3/...) and FQN TypeIds (restores T09 struct emission).
Tests: resolver sizes (primitive/vector/nested/alias), packed offsets, topology+registrar
@offset parity, nested-type validation, over-100B/unresolvable skips, T09 alias emit.
```
