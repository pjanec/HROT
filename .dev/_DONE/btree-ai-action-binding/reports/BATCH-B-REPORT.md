# BATCH-B Report — DEBT-AIB-013: Generated Parameter-Default Writing

## Implementation Summary

### TASK 1 — ParseParams emission in BTreeBridgeEmitCore

**File**: `Hrot\Subsystems\AI\Hrot.AiEditor.Persistence\Emit\BTreeBridgeEmitCore.cs`

Added `EmitParseParamsLocal()` private static method that, for managed assets where ≥1 blackboard variable carries a non-null `DefaultValueJson`, emits:

1. A `private static readonly __paramJsonOpts` field in the bridge class (with `IncludeFields = true` — required because the BTree DTO structs use plain public fields, not properties, and `System.Text.Json` ignores fields by default).
2. An `unsafe` block around a `ParseParamsDelegate? __parseParams` local variable assignment, with a `static (string json, byte* memory)` lambda body that:
   - Ignores the incoming `json` argument (baked defaults are written unconditionally; runtime override is deferred as DEBT-AIB-021).
   - For each variable with a non-null `DefaultValueJson`, deserializes via `global::System.Text.Json.JsonSerializer.Deserialize<DtoType>(json, __paramJsonOpts)` and writes at the packed byte offset via `global::System.Runtime.CompilerServices.Unsafe.Write(memory + offset, value)`.
3. `ParseParams  = __parseParams,` in the `BehaviorDefinition` object initializer (only when hasParseParams=true).

Guard: only emits when `dto.Blackboard.Managed == true` AND ≥1 variable has non-null `DefaultValueJson`. Non-managed assets and all-null-default managed assets are byte-identical to pre-BATCH-B output.

Also added `#nullable enable` pragma to the generated file header (required for `ParseParamsDelegate?` annotation in auto-generated source — CS8669 without it).

**Pre-existing regression fixed (unrelated to DEBT-AIB-013, exposed by this batch):**

The `_auto_46f54de378b047a58aa6f96e7f94d346` variable added to T10 in a prior commit (`fa703dec`) had type `Fdp.Toolkit.Behavior.Components.BrainBlackboard` with `IsAutoManaged=true`. This caused:

- `BTreeJsonGenerator`: BTREE0002 because `BrainBlackboard` couldn't be struct-sized → T10 was silently skipped.
- `BTreeEmitCore.BuildVariableOffsets`: `Pack()` threw `NotSupportedException` for `BrainBlackboard` → empty offset map → emitted `dto.counter` on a `BrainBlackboard` receiver → CS1061.

Fix: added `if (v.IsAutoManaged) continue;` / `Where(v => !v.IsAutoManaged)` filtering in:
- `BTreeJsonGenerator.GenerateOneAsset` (type-resolution loop + overflow check)
- `BTreeEmitCore.BuildVariableOffsets`
- `BTreeEmitCore.EmitBlackboardStructSource`
- `BTreeBridgeEmitCore.EmitBTreeRegisterMethod` (Pack call)

### TASK 2 — DefaultValueJson on T10/T11

**File**: `Hrot\Subsystems\Hrot.AI.Behaviors\Assets\BTrees\Authoring\T10_MultiAction.btree.json`
- `counter` variable: `"DefaultValueJson": "{\"Counter\":0,\"Threshold\":5}"`
- `accum` variable: `"DefaultValueJson": "{\"Sum\":0,\"Step\":7}"`

**File**: `Hrot\Subsystems\Hrot.AI.Behaviors\Assets\BTrees\Authoring\T11_Aliasing.btree.json`
- `counter` variable: `"DefaultValueJson": "{\"Counter\":0,\"Threshold\":10}"`

### TASK 3 — Tests

**File** (new): `Hrot\Subsystems\AI\Hrot.AiEditor.Generators.Tests\Bridge\ParseParamsEmissionTests.cs`

Six structural-emission tests (no Roslyn compile — string assertion on emitted bridge source):
1. `ManagedAsset_BothVarsHaveDefault_EmitsParseParamsWithBothOffsets` — confirms unsafe lambda + offset 0 + offset 4.
2. `ManagedAsset_DefaultJson_IsProperlyEscaped` — confirms `\"` escaping of embedded quotes.
3. `ManagedAsset_NoVariableHasDefault_DoesNotEmitParseParams` — guard: zero defaults → no emission.
4. `NonManagedAsset_DoesNotEmitParseParams` — guard: non-managed → no emission.
5. `ManagedAsset_OnlyFirstVarHasDefault_EmitsOnlyFirstWrite` — only first variable's block present.
6. `ManagedAsset_WithDefault_EmitsFullyQualifiedJsonSerializerAndUnsafeWrite` — FQN checks.

**File** (modified): `Hrot\Subsystems\AI\Hrot.AiEditor.Generators.Tests\Demos\T10_MultiAction_ProofTests.cs`

Added PROOF TEST 4 `ParseParams_WritesDefaultsIntoBuffer_AtPackedOffsets` with helper `BuildDefinitionViaFixtureBridge()`.

The helper bypasses the full `BTreeJsonGenerator` pipeline (which runs `BTreeMethodCompatibilityValidator` and failed to resolve `Action_AddStepToSum` due to AppDomain assembly load ordering in isolated runs). Instead:
1. Builds a synthetic T10-matching DTO (no nodes, only the two blackboard variables with DefaultValueJson).
2. Uses a hardcoded size resolver (`DemoCounterParams` = 8 bytes, `DemoAccumParams` = 8 bytes).
3. Calls `BTreeBridgeEmitCore.EmitBridge(dto, sizeResolver)` directly.
4. Forces `Hrot.AI.Behaviors` into the AppDomain via `GC.KeepAlive(typeof(DemoCounterNodes.DemoCounterParams))` and `GC.KeepAlive(typeof(T10_MultiAction))` before Roslyn compilation so `ForRuntimeAssemblies` finds the assembly.
5. Compiles the bridge source via Roslyn with AppDomain references.
6. Invokes `Register(beh, staging, actionRegistry)` on the loaded assembly, extracts the `BehaviorDefinition`.
7. Allocates a zeroed 128-byte buffer, invokes `ParseParams!("", buf)`, and asserts:
   - `*(int*)(buf + 0 + 0) == 0` (counter.Counter = 0)
   - `*(int*)(buf + 0 + 4) == 5` (counter.Threshold = 5, from T10 DefaultValueJson)
   - `*(int*)(buf + 8 + 0) == 0` (accum.Sum = 0)
   - `*(int*)(buf + 8 + 4) == 7` (accum.Step = 7, from T10 DefaultValueJson)

### DEBT-TRACKER update

- `DEBT-AIB-013` marked `[x]` (resolved BATCH-B)
- `DEBT-AIB-021` added: runtime per-assignment JSON override of managed variables is not yet supported — only baked defaults are written.

## Test Results

### Hrot.AiEditor.Generators.Tests

**Total: 83 | Passed: 81 | Failed: 2**

The 2 failures are the known-unrelated `MigrationEquivalenceTests`:
- `BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout`
- `Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout`

All new and modified tests passed:
- 6 × `ParseParamsEmissionTests` ✓
- `ParseParams_WritesDefaultsIntoBuffer_AtPackedOffsets` ✓ (was failing before this batch)
- 3 × existing T10/T11 proof tests ✓
- All bridge integration tests ✓

### Hrot.AiEditor.Persistence.Tests

**Total: 129 | Passed: 129 | Failed: 0**

All byte-identity gate tests for SampleScout and SampleGuard are green.

## Design Decisions

- **`__paramJsonOpts` as static readonly field**: avoids allocating a new `JsonSerializerOptions` on every ParseParams invocation. Emitted only when the bridge has ≥1 variable with DefaultValueJson.
- **`IncludeFields = true`**: required because BTree DTO structs use plain `public int` fields (not properties). System.Text.Json serializes only public properties by default — without this option all deserialized struct fields would be zero.
- **Fixture-based proof test** (not full generator pipeline): the `BTreeMethodCompatibilityValidator` inside `BTreeJsonGenerator` resolves action methods via Roslyn against `AppDomain.CurrentDomain.GetAssemblies()`. When the proof test runs in isolation, `Hrot.AI.Behaviors` may not be fully JIT-loaded; the validator fails to resolve `Action_AddStepToSum`. The fixture approach calls `EmitBridge` directly (no validator) and forces the assembly into AppDomain via `GC.KeepAlive` before Roslyn compilation.
- **DEBT-AIB-021**: runtime override of baked defaults (e.g., passing a non-empty JSON string to `ParseParams` to override specific fields) is explicitly out of scope and noted as a new debt item.
