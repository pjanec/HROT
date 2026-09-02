# JSON-PRETTY-REPORT — Blueprint .bp.json Pretty-Print

**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-06  
**Status:** COMPLETE — 0 new test failures

---

## Write Sites Found

All blueprint `.bp.json` writes go through a single code path:

**`SaveActiveBlueprintCommand.Save(BlueprintAsset asset, string path)`**  
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs`, line 102  
`File.WriteAllText(path, json)` — now `File.WriteAllText(path, prettyJson)` after applying `FlattenNumericArrays`.

This method is the single funnel for **all** blueprint save paths:
- **Direct Save button / Ctrl+S** → `SaveFromActiveDocument` → `Save(asset, path)` (EditorSubsystem RegisterWindows ~line 2029)
- **Save-All / Ctrl+Shift+S** → `saveBlueprintDelegate` → `Save(bpAsset, path)` (EditorSubsystem ~line 2084)
- **Full Rebuild** → `_saveAllCallback` which calls `saveBlueprintDelegate` (line 2457)
- **New Blueprint creation** → `Save(newAsset, path)` directly (EditorSubsystem ~line 1968)
- **Shutdown** → `_saveAllCallback?.Invoke()` (line 2687)

`BlueprintJsonServices.Serialize` is NOT changed — it stays minified (dual-target netstandard2.0+net8.0). The formatter is applied exclusively at the editor save path (net8.0 only, in `Hrot.Blueprints.Editor` which already references `Fdp.Toolkits`).

---

## Byte-Stability Test Analysis — Decisive Finding

**TC-4 `Save_FixtureAsset_ByteStable`** (`SaveActiveBlueprintCommandTests.cs`, line 228):

```csharp
Assert.Equal(savedJson, reserialized);
// where:
//   savedJson    = File.ReadAllText(path)   ← written by Save (now pretty)
//   reserialized = BlueprintJsonServices.Serialize(reloaded!)  ← minified
```

This test **would have failed** if left unchanged: `savedJson` (pretty) ≠ `reserialized` (minified).

**Resolution:** Updated TC-4 to apply the same formatter on `reserialized` before comparing:

```csharp
var reserialized = JsonAestheticFormatter.FlattenNumericArrays(
    BlueprintJsonServices.Serialize(reloaded!));
Assert.Equal(savedJson, reserialized);
```

This preserves the semantic invariant (round-trip equivalence) while adapting the comparison to the new on-disk format.

**Other tests are unaffected:**
- `AssetJsonRoundTripTests` — all compare `Serialize` to `Serialize` (no disk), unaffected.
- `SampleAssetLoadTests`, `RecipeIntegrityTests` — load and parse; whitespace-agnostic.
- `BlueprintComparisonSanitizerTests` — sanitizes via `JsonNode.Parse`; whitespace-agnostic.
- `SaveAllAiDocumentsCommandTests` — BTree/HSM only; no blueprint saves.

---

## Approach

1. **`SaveActiveBlueprintCommand.Save`**: Added `using Fdp.Toolkit.Serialization;` and applied `JsonAestheticFormatter.FlattenNumericArrays(json)` after `BlueprintJsonServices.Serialize(asset)`. The result is written to disk.
2. **TC-4 update**: Applied the same formatter in the test's comparison so the byte-stability invariant is preserved at the formatter level.
3. **`BlueprintJsonServices.Serialize`**: Unchanged — stays minified. Compiler golden tests and round-trip tests in `AssetJsonRoundTripTests` are unaffected.
4. **Projection-only (Pins:[])**: Unchanged — the pin-swap logic runs before serialization, before formatting.
5. **`$meta`-first stamping**: Preserved — `FlattenNumericArrays` re-emits properties in the same order as the input DOM.

---

## Committed Assets Reformatted vs Left

### Reformatted (15 files — had non-canonical indentation)

| File | Reason |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/DeepNestedBlueprint.bp.json` | Compact inline objects |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/simple_node.bp.json` | Compact inline objects |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/with_editor_metadata.bp.json` | Compact inline + `"Pan": [10, 20]` numeric array |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/with_peer_call.bp.json` | Compact inline objects |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/BoundingOverwatchSwap.bp.json` | Compact inline objects |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/EditorTypesDemo.bp.json` | Compact inline objects |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/LocomotionMoveToDemo.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/MoveAndFireCombo.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/SquadAwareEngagement.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EditorTypesDemo.bp.json` | Compact inline objects |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EnumDemo.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/LocomotionMoveToDemo.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/MoveAndFireCombo.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SampleWiredDemo.bp.json` | Non-standard indentation |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SquadAwareEngagement.bp.json` | Non-standard indentation |

### Left unchanged — already canonical (32 files)

All remaining committed `.bp.json` files (InstanceCounter, HealthRegen, DoorActor, DoorSensor, LibraryMath, MoveToAndFire, HasVisibleTarget, with-branch, with-delay, etc., and the AI.Behaviors Recipes that were already properly formatted) were already byte-identical after `FlattenNumericArrays`.

### Excluded per task spec — user experiment files (not touched)

- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Counting.bp.json` (Count3)
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Loco1.bp.json`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/InlineEd1.bp.json`
- Count2, Count3 (if any exist)

Note: `Counting.bp.json` had a pre-existing working-tree modification (unrelated to this batch) that was present before this work.

---

## Test Results

### Blueprints suite (`Hrot.Blueprints.Tests`)
- **Total:** 1567 tests
- **Passed:** 1555
- **Failed:** 4 (all pre-existing)
- **Skipped:** 8

Pre-existing failures (unchanged from baseline):
1. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
2. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` (CRLF snapshot flake)
3. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
4. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` (CRLF snapshot flake)

### Hrot.Editor.AiShared.Tests
- **Total:** 832 tests
- **Passed:** 832
- **Failed:** 0

### Hrot.AiEditor.Persistence.Tests
- **Total:** 88 tests
- **Passed:** 88
- **Failed:** 0

### Build
- `Hrot.Blueprints.Editor`: 0 errors, 0 warnings
- `Hrot.Blueprints.Tests`: 0 errors, 8 pre-existing warnings (CS8601, CS0618, CS0649)

---

## Changed Files

### Source code
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs`
  - Added `using Fdp.Toolkit.Serialization;`
  - Applied `JsonAestheticFormatter.FlattenNumericArrays(json)` before `File.WriteAllText`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/SaveActiveBlueprintCommandTests.cs`
  - Added `using Fdp.Toolkit.Serialization;`
  - TC-4 `Save_FixtureAsset_ByteStable`: applies formatter on `reserialized` before `Assert.Equal`

### Data files
- 15 committed `.bp.json` files reformatted to canonical pretty+inlined format (see list above)

---

## Notes / Fast-follows

- **BTree/HSM persistence**: Not changed in this batch. BTree/HSM saves go through `AtomicFileWriter.Write` with `BTreeJsonServices.Serialize` / `HsmJsonServices.Serialize`. Those are already minified and noted as a fast-follow for consistency.
- **No STOP/deviation**: The implementation is clean. The byte-stability invariant was trivially updated (compare at the formatted level). No persistence-unification round-trip guarantees were broken.
- **Compiler goldens**: NOT regenerated. `BlueprintJsonServices.Serialize` (used by compiler/golden tests) stays minified; those tests are unaffected.
