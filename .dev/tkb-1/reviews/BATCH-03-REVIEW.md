# BATCH-03 Review

**Status: APPROVED**  
**Reviewer: Dev Lead**  
**Tasks reviewed:** TKB-009, TKB-010

---

## Implementation Quality

### TkbDescriptorRegistry.cs

APPROVED. Static registry is minimal and correct.
- OrdinalIgnoreCase dictionary as required.
- `TryGetParser(ReadOnlySpan<char>, out thunk)` — correctly falls back to `key.ToString()` on
  net8.0 with a clear doc-comment explaining the .NET 9+ upgrade path.
- `internal static Clear()` — correctly scoped to test use only.
- No unnecessary complexity.

### TkbFormatException.cs

APPROVED. Two constructors, sealed, no over-engineering.

### TkbDeserializer.cs

APPROVED. Key correctness points verified:
- `using var doc = JsonDocument.Parse(file.JsonStream)` — stream is consumed and document is
  disposed before method returns (correct lifecycle).
- Missing `$guid` throws `TkbFormatException` with entity name and category in message (helps
  debugging).
- `!char.IsLetter(name[0])` correctly skips `$guid` (`$` is not a letter), `_EditorMetadata`
  (`_` is not a letter), and any other non-letter-prefixed metadata.
- `#` split uses `ReadOnlySpan<char>` slicing; `partId` defaults to 0 if no `#` present.
- Unknown descriptor keys silently skipped (no exception).
- `db.Register(template)` called after all properties are dispatched.

---

## Test Quality

### TkbDeserializerTests.cs (10 tests)

APPROVED. All required coverage is present:

| Test | Coverage | Verdict |
|---|---|---|
| `ParseAndRegister_ValidEntity_TemplateHasCorrectTkbType` | TkbType = 100 parsed from $guid | OK |
| `ParseAndRegister_ValidEntity_TemplateHasCorrectCategoryPath` | CategoryPath passed through | OK |
| `ParseAndRegister_ValidEntity_HasVehicleParametersDto` | Mass = 61000f | OK — asserts specific value |
| `ParseAndRegister_ValidEntity_HasTkbMasterDto` | CustomName = "M1 Abrams" | OK — asserts specific value |
| `ParseAndRegister_ValidEntity_HasWeaponCapabilitiesDto` | EffectiveRange = 3000f, MagazineCapacity = 42 | OK — asserts two values |
| `ParseAndRegister_MissingGuid_ThrowsTkbFormatException` | Error path | OK |
| `ParseAndRegister_UnknownDescriptors_ParsesWithoutThrowing` | Silent skip + absence assertions | OK — negative assertions confirm no phantom data |
| `ParseAndRegister_MetadataKey_IsSkipped` | `_EditorMetadata` skipped, standard descriptors present | OK |
| `ParseAndRegister_MultiplePartIds_BothAmmoBallisticsStored` | partId=1 WeaponGuid=10, partId=2 WeaponGuid=11 | OK — verifies both partIds with specific values |
| `ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap` | 10,000 entities, avg < 85KB | OK — heuristic regression guard, finally block clears db |

Fixture pattern with `IClassFixture<TkbDeserializerFixture>` + `[Collection("TkbDeserializerTests")]`
correctly isolates the static `TkbDescriptorRegistry` from parallel test runs.

### TkbDescriptorRegistryTests.cs (4 tests)

APPROVED.

| Test | Coverage | Verdict |
|---|---|---|
| `RegisterParser_ThenTryGetParser_ReturnsTrueAndThunk` | Round-trip + `Assert.Same` for thunk identity | OK |
| `TryGetParser_UnregisteredName_ReturnsFalse` | Negative path | OK |
| `RegisterParser_CaseInsensitive_FoundWithDifferentCase` | lowercase register, mixed-case lookup | OK |
| `RegisterParser_Overwrite_ReturnsLatestThunk` | Last-write-wins | OK |

Registry tests use constructor + `Dispose()` for isolation (clear before/after each test),
and share the `[Collection("TkbDeserializerTests")]` annotation to prevent parallel runs.

---

## Issues Found

None blocking. Two P3 debt items added:

- **D-004 (P3):** `TkbDescriptorRegistry.TryGetParser` allocates one `string` per property name
  on the deserializer hot path (net8.0 limitation). When upgraded to .NET 9+, replace with
  `Dictionary.GetAlternateLookup<ReadOnlySpan<char>>()`.
- **D-005 (P3):** LOH test is a heuristic regression guard; GC measurement can vary slightly
  between environments. 85,000-byte threshold is very conservative (observed << 1KB average).

---

## Decision

**APPROVED — all 14 new tests pass, build clean, no regressions in 94-test TKB suite.**

Carry forward debt items D-004 and D-005 to DEBT-TRACKER.md.
