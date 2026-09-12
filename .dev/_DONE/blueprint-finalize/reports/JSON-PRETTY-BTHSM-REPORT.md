# JSON-PRETTY-BTHSM Report

**Batch:** JSON-PRETTY-BTHSM  
**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-06  
**Status:** DONE — lead commits

---

## Save-Site Locations

Both BTree and HSM are saved via delegates in `EditorSubsystem.cs` (lines ~2139–2157).
These delegates are shared by:
- **Save-All** (`_saveAllCallback` → `SaveAllAiDocumentsCommand.Execute`)
- **Flush-on-close** (the `BeforeDocumentClosed` handler)
- **Debounced flushAction** (the `RegenerationScheduler` flush for dirty BTree/HSM assets)

### BTree save delegate (was)
```csharp
// EditorSubsystem.cs:2139-2147
var dto  = BehaviorTreeAssetMapper.ToDto(btreeAsset);
var json = BTreeJsonServices.Serialize(dto);          // minified
AtomicFileWriter.Write(path, json);
```

### BTree save delegate (now)
```csharp
var dto        = BehaviorTreeAssetMapper.ToDto(btreeAsset);
var json       = BTreeJsonServices.Serialize(dto);
var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json);
AtomicFileWriter.Write(path, prettyJson);
```

### HSM save delegate (was)
```csharp
// EditorSubsystem.cs:2149-2157
var dto  = HsmAssetMapper.ToDto(hsmAsset);
var json = HsmJsonServices.Serialize(dto);            // minified
AtomicFileWriter.Write(path, json);
```

### HSM save delegate (now)
```csharp
var dto        = HsmAssetMapper.ToDto(hsmAsset);
var json       = HsmJsonServices.Serialize(dto);
var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json);
AtomicFileWriter.Write(path, prettyJson);
```

`Fdp.Toolkit.Serialization.JsonAestheticFormatter` (net8.0, in `FDP/Toolkits/Fdp.Toolkits/`)
is already directly referenced by `Hrot.Editor.csproj` — no new project reference needed.

---

## Byte-Stability Test Findings

### Test: `ByteStabilityTests` (Hrot.AiEditor.Persistence.Tests)
- `BTree_Serialize_Deserialize_Serialize_IsByteIdentical`
- `BTree_Serialize_CalledTwice_IsByteIdentical`
- `Hsm_Serialize_Deserialize_Serialize_IsByteIdentical`
- `Hsm_Serialize_CalledTwice_IsByteIdentical`
- `BTree_FullCycle_SampleScout_IsByteIdentical`
- `Hsm_FullCycle_SampleGuard_IsByteIdentical`

**These tests compare `BTreeJsonServices.Serialize(...)` output directly** — not the editor-save
output. Since `FlattenNumericArrays` is applied at the _delegate_ level (not inside `Serialize`),
these tests are unaffected. All 88 tests pass unchanged.

### No TC-4 analog needed
The blueprint TC-4 pattern (`SaveActiveBlueprintCommandTests.Save_FixtureAsset_ByteStable`) was
required because `SaveActiveBlueprintCommand` is the only test-reachable save entry point.
For BTree/HSM: the only "save" tests (`SaveBTreeEmitTests`, `SaveHsmEmitTests`) test the **C#
emitter** path (BTreeFluentEmitter/HsmFluentEmitter), not the JSON persistence path. There are no
tests that:
- call `saveBTreeDelegate` / `saveHsmDelegate` directly, or
- compare on-disk bytes from a JSON save

No existing test needed updating.

---

## Committed Assets Reformatted

### `Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.btree.json`
- **Before:** minified single line (1251 bytes, with BOM)
- **After:** pretty-printed + numeric arrays inlined, 78 lines
- **Semantic identity proof:** `JToken.Parse(before).ToString(Formatting.None)` == `JToken.Parse(after).ToString(Formatting.None)` (verified by formatter tool, exit 0)
- **Key values preserved:** AssetId `54ef3847...`, 3 nodes (Sequence, Wait×2), positions intact

### `Hrot/Subsystems/Hrot.AI.Behaviors/Machines/SampleGuard.hsm.json`
- **Before:** minified single line (2997 bytes, with BOM)  
- **After:** pretty-printed + numeric arrays inlined, 168 lines
- **Semantic identity proof:** same Newtonsoft round-trip equality check (verified, exit 0)
- **Key values preserved:** AssetId `979df4a4...`, states Idle/Scanning at X:100,Y:100 and X:400,Y:100 (passes `HsmAssetContributor_LoadFrom_SampleGuard_LayoutIsApplied` — at least one non-zero position)

**User experiment files excluded:** `EnumDemo.bp.json`, `Counting.bp.json` — untouched (both out-of-scope: blueprint files, not BTree/HSM).

---

## Downstream Deserialization

`BTreeJsonServices.Deserialize` and `HsmJsonServices.Deserialize` use
`System.Text.Json.JsonSerializer.Deserialize` with `AllowTrailingCommas=true,
PropertyNameCaseInsensitive=true` — whitespace-agnostic. Pretty-printed JSON round-trips
identically. `BehaviorIngressSystem` / the generator reads from the committed `.cs` files
(generated from JSON), not the JSON directly. The JSON is only consumed by
`BTreeJsonAssetContributor` / `HsmJsonAssetContributor` for editor-side layout loading — both
are whitespace-agnostic.

---

## Test Results

| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| `Hrot.AiEditor.Persistence.Tests` | 88 | 0 | all byte-stability tests green |
| `Hrot.BTree.Editor.Tests` | 399 | 0 | SampleScoutDiscovery green |
| `Hrot.Hsm.Editor.Tests` | 348 | 0 | SampleGuardDiscovery green |
| `Hrot.Editor.Tests` | 116 | 0 | EditorSubsystem boot tests green |
| `Hrot.Editor.AiShared.Tests` | 856 | 0 | |
| `Hrot.Blueprints.Tests` | 1563 | 4 | 4 pre-existing (ScoreCrossed, AllocatesZeroBytes, LibraryMath CRLF, LibraryMathSnapshot) — 0 new |

**Build:** 0 CS errors, 0 warnings (`Hrot.Editor.csproj --no-restore -v quiet`).

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Added `FlattenNumericArrays` call in both `saveBTreeDelegate` and `saveHsmDelegate` |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.btree.json` | Reformatted (semantic-identical) |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Machines/SampleGuard.hsm.json` | Reformatted (semantic-identical) |

No new project references, no new test files, no changes to `BTreeJsonServices.Serialize` or `HsmJsonServices.Serialize`.
