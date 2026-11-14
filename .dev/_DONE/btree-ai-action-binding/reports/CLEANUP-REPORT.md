# Cleanup Report — Band-Aid Revert (IsAutoManaged Filters)

## Objective

Remove the over-broad `IsAutoManaged` filtering that Batch B added to accommodate
polluted demo assets (`T10_MultiAction`, `T11_Aliasing`), restore the assets to their
pristine committed state, add only the `DefaultValueJson` defaults, and verify the build
is clean without the band-aid.

---

## Step 1 — Asset Restore + Defaults

### Actions taken

1. `git checkout HEAD -- T10_MultiAction.btree.json T11_Aliasing.btree.json`
   - Restored both files to the HEAD-committed clean versions (no `Action_Wander` node,
     no `_auto_…` variable, original `TypeName`).

2. **T10_MultiAction.btree.json** — `DefaultValueJson` added to both variables:
   - `counter` → `"DefaultValueJson": "{\"Counter\":0,\"Threshold\":5}"`
   - `accum`   → `"DefaultValueJson": "{\"Sum\":0,\"Step\":7}"`

3. **T11_Aliasing.btree.json** — `DefaultValueJson` set on `counter`:
   - `counter` → `"DefaultValueJson": "{\"Counter\":0,\"Threshold\":10}"`
   (The file had `"DefaultValueJson": null` from an earlier editor pass; replaced.)

No nodes, variables, or `TypeName` were added or changed. T10/T11 are not golden-snapshot
assets so formatting drift is not a concern for the byte-identity suite.

---

## Step 2 — IsAutoManaged Filter Revert

### BTreeJsonGenerator.cs

Removed from the resolution-check loop:
```csharp
// AUTO-MANAGED COMMENT BLOCK (5 lines) removed
if (v.IsAutoManaged) continue;   // internal slot — skip resolution check
```

Removed from the overflow-check block:
```csharp
// AUTO-MANAGED COMMENT LINE removed
var packableVars = dto.Blackboard.Variables
    .Where(v => !v.IsAutoManaged)
    .ToList();
// packableVars → reverted back to dto.Blackboard.Variables
```

`System.Linq` using retained (still needed by `Where`/`Select` calls at lines 32, 34, 49).

ParseParams emission (added by Batch B) is fully intact:
- `EmitParseParamsLocal` / `EmitParseParamsIfDefaults` logic unchanged.
- `JsonSerializerOptions{IncludeFields=true}` field unchanged.
- `#nullable enable` header unchanged.

### BTreeEmitCore.cs

Removed from `EmitBlackboardStructSource`:
```csharp
// Auto-managed variables are internal bookkeeping slots — exclude from struct emit.
var packableVars = dto.Blackboard.Variables
    .Where(v => !v.IsAutoManaged)
    .ToList();
if (packableVars.Count == 0)
    return null;
// Pack(packableVars, ...) → reverted to Pack(dto.Blackboard.Variables, ...)
```

Removed from `BuildVariableOffsets`:
```csharp
// Auto-managed variables (IsAutoManaged=true) are internal bookkeeping slots...
var packableVars = dto.Blackboard.Variables
    .Where(v => !v.IsAutoManaged)
    .ToList();
if (packableVars.Count == 0)
    return new Dictionary<string, int>();
// Pack(packableVars, ...) → reverted to Pack(dto.Blackboard.Variables, ...)
```

After revert `git diff HEAD -- BTreeEmitCore.cs` produces **empty output** — the file is now
byte-identical to its HEAD committed state.

### BTreeBridgeEmitCore.cs

Removed from `EmitBTreeRegisterMethod`:
```csharp
// Auto-managed variables (IsAutoManaged=true) are internal bookkeeping slots...
// (3 comment lines + filter)
var packableVars = dto.Blackboard.Variables
    .Where(v => !v.IsAutoManaged)
    .ToList();
try { packedFields = BTreeBlackboardPackHelper.Pack(packableVars, sizeResolver, out _); }
// reverted to:
try { packedFields = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, sizeResolver, out _); }
```

All ParseParams additions from Batch B are retained:
- `#nullable enable` pragma emission.
- `__paramJsonOpts` static field emission.
- `EmitParseParamsLocal` private method (full implementation).
- `EscapeCSharpStringLiteral` private helper.
- `ParseParams = __parseParams,` wiring in `BehaviorDefinition` initializer.
- `System.Text.Json` using added to bridge usings when defaults present.

---

## Step 3 — Build + Test Results

### Hrot.AI.Behaviors rebuild (dotnet build -t:Rebuild, after build-server shutdown)

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:52.37
```

T10 and T11 compile correctly without the band-aid — the clean assets have no
auto-managed variables, so the pack/struct-emit/overflow paths run unfiltered and succeed.

### Hrot.AiEditor.Generators.Tests

```
Failed:  2 (MigrationEquivalenceTests — known pre-existing, unrelated to this change)
Passed: 81
Total:  83
```

The 2 failures are:
- `MigrationEquivalenceTests.BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout`
- `MigrationEquivalenceTests.Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout`

These are the same known-unrelated failures documented in the task specification.
ParseParams emission tests (added by Batch B) all pass.

### Hrot.AiEditor.Persistence.Tests

```
Failed:  0
Passed: 129
Total:  129
```

Byte-identity suite fully green. ParseParams-related persistence tests pass.

---

## ParseParams Emission — Confirmed Intact

The following Batch B additions are present and unchanged:
- `BTreeBridgeEmitCore.EmitParseParamsLocal` (full method, ~60 lines)
- `BTreeBridgeEmitCore.EscapeCSharpStringLiteral` helper
- `__paramJsonOpts` static field emission (`IncludeFields = true`)
- `#nullable enable` pragma in bridge source emit
- `ParseParams = __parseParams,` in `BehaviorDefinition` initializer
- `System.Text.Json` using added when defaults present
- T10 and T11 `DefaultValueJson` values (the entire reason for ParseParams)

---

## Known Issues / Notes

- T10/T11 `TypeName` in `Blackboard` is `""` for T11 — that is the HEAD-committed value
  (fallback logic in `BTreeEmitCore` uses `SanitizeIdentifier(dto.Name) + "Blackboard"`).
- The `IsAutoManaged` property still exists on `BTreeBlackboardVariableDto`; removing it
  is a separate clean-up out of scope here. The property is simply no longer consulted
  in the three generator/emitter files.

---

## Suggested Commit Message

```
fix(btree-generator): remove IsAutoManaged band-aid; restore clean T10/T11 assets with defaults
```
