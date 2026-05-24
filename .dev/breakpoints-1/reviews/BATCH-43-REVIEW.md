# BATCH-43 Review — UBP-P6T1 + UBP-P6T2

**Status: APPROVED**

---

## Test quality against DESIGN §6.5

### P6T1 — `BlueprintVariablePredicate_SerializesRoundTrip`
- Serializes a `BlueprintVariablePredicateDto` with non-trivial values (`TargetBlueprintAssetId`, `VariableName`, `Operator`, nested `NumericPredicateDto`)
- Deserializes back via the polymorphic `SearchPredicateDto` chain and asserts all five fields
- Confirms `"$type":"BlueprintVariable"` is honoured by the `[JsonPolymorphic]` infrastructure
- **PASS — real, non-trivial**

### P6T2-SC1 — `Compile_BlueprintVariable_NoSlotPresent_ReturnsFalse`
- Initialises the BB1024 header correctly (so `TryGetSlotOffset` sees a valid structure), but deliberately omits `TryAttach`
- Directly exercises the DESIGN §6.5 requirement "short-circuits to false if not found"
- **PASS — real, aligned**

### P6T2-SC2 — `Compile_BlueprintVariable_SlotPresent_EvaluatesField`
- Uses the real `TryAttach` + raw pointer write to seed AmmoCount=0
- Asserts true for `AmmoCount == 0`, then mutates to 99 and asserts false — confirming the delegate reads fresh memory on every call (no caching artefact)
- **PASS — real, has positive + negative coverage**

### P6T2-SC3 — `Compile_BlueprintVariable_TierUpgrade_StillWorks`
- Uses the real `CopyToLargerTier` production function (not a mock)
- Re-fetches the BB1024 pointer after `AddComponent(BB4096)` — correctly handles archetype chunk relocation
- Calls `RemoveComponent<BB1024>` then verifies the same compiled delegate finds AmmoCount=5 via BB4096
- Directly validates the DESIGN §6.5 guarantee: "The partition allocator's tier-upgrade / defragmentation never invalidates the delegate — the delegate re-runs the slot scan every evaluation"
- **PASS — real, non-trivial, highest value test in the batch**

---

## Implementation correctness

| Check | Result |
|-------|--------|
| `[JsonDerivedType(typeof(BlueprintVariablePredicateDto), "BlueprintVariable")]` on base class | ✓ |
| `PredicateCompiler` constructor extended with optional `BlueprintRegistry?` — existing callers unchanged | ✓ |
| Switch case `case BlueprintVariablePredicateDto blueprintVar:` in `Compile()` | ✓ |
| `CompileBlueprintVariablePredicate` returns `static (_, _) => false` when registry/def/field missing | ✓ |
| `BuildBlueprintVariableMatcher<TField>` bakes typeIds for all three tiers at compile time | ✓ |
| Tier probing order: BB1024 → BB4096 → BB16384 | ✓ |
| `TryGetSlotOffset` called; returns false on miss | ✓ |
| `Unsafe.AsRef<TField>(memory + payloadOffset + fieldOffset)` — exactly DESIGN §6.5 formula | ✓ |
| `CollectMandatoryComponents` unchanged (no single mandatory tier) | ✓ |
| `DataBreakpointManager.TryMountDelegate` has `case BlueprintVariablePredicateDto _:` | ✓ |
| `[Collection("ComponentRegistry")]` + `ComponentTypeRegistry.Clear()` in test | ✓ |
| BB16384 not registered in tests | ✓ |
| Zero new warnings (`TreatWarningsAsErrors` active in Fdp.Toolkits) | ✓ |

---

## Test count

- Before BATCH-43: 54 passing (Breakpoints tests)
- After BATCH-43: 57 passing (Breakpoints tests) + 1 (Toolkits tests)
- Net new: +4 tests
