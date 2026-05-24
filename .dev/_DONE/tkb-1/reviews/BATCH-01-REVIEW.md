# BATCH-01 REVIEW

**Batch:** BATCH-01
**Tasks:** TKB-001, TKB-002, TKB-003, TKB-004, TKB-005
**Review Date:** 2026-05-15
**Reviewer:** Dev Lead
**Verdict:** APPROVED ✅

---

## Build & Test Results

- `dotnet build FDP.sln` — 0 errors, 2 pre-existing NuGet warnings (unrelated)
- `dotnet test --filter "FullyQualifiedName~Tkb"` — **62/62 passed**, 0 failed

---

## Implementation Review

### Scope Check

All 5 tasks implemented. All 13 required production files created. All 5 test files created.
No existing files were modified. Matches batch scope exactly.

### Design Alignment

- `TkbDescriptorAttribute`: constructor validation correct (null, empty, whitespace, `#`). No
  ECS or framework references. **Aligned.**
- DTOs: all `record` types with `init`-only properties, correct `[TkbDescriptor]` names, correct
  `[EditUnit]` annotations, `[WeaponRef]` on `AmmoWeaponBallisticsDto.WeaponGuid`, no ECS bases.
  **Aligned.**
- `TkbEntityFile`: `readonly record struct`. **Aligned.**
- `RawDirectoryTkbProvider`: uses `Path.GetRelativePath` for robust cross-platform `CategoryPath`
  computation. UTF-8 no-BOM write. Lazy enumeration. Root-level empty `CategoryPath` handled.
  **Aligned.**
- `ZipTkbProvider`: always `ZipArchiveMode.Read` (verified by test via reflection). Both write
  methods throw `NotSupportedException` with "read-only" in message. Directory markers and
  non-JSON entries skipped. Backslash normalization present. **Aligned.**
- `TkbUnifiedLoader`: case-insensitive `.zip` check. `ArgumentException` for invalid paths.
  Dispose delegates to strategy. **Aligned.**

### Test Quality

Tests are **high quality**:
- Value assertions used throughout (`Assert.Equal` on specific field values, `Assert.Single`,
  `Assert.Empty`)
- Reflection-based attribute presence checks verify actual metadata
- `ZipArchiveMode.Read` verified via private field reflection (not just "it worked")
- Exception tests use `Assert.Throws<T>` and verify message content
- Proper isolation: each test class uses `IDisposable` + temp directory with `Guid` suffix
- Edge cases covered: root-level files (`CategoryPath == ""`), directory markers in ZIP,
  case-insensitive `.ZIP` extension, non-JSON files skipped
- No silent swallowing of failures

### Early Failure / Error Handling

- `TkbDescriptorAttribute` throws on construction — correct; attribute validation fails early.
- `ZipTkbProvider` write methods throw immediately — correct.
- `TkbUnifiedLoader` throws `ArgumentException` on bad path — correct.

---

## Developer Insights Summary

Key issues and observations from developer report recorded below:

1. **ZIP test helper:** `using var` (C# 8 declaration form) defers disposal to end of scope,
   causing `IOException` when creating the next entry. Fixed using explicit `using { }` blocks.
   This is worth noting in future ZIP-related batch instructions.

2. **Root-level `CategoryPath = ""`:** Not explicitly called out in spec. Developer added a
   dedicated test. Future phases (especially `GetEntitiesByCategory("")`) must handle this.

3. **ZIP entry separators:** `.FullName` on Windows can return backslash separators. Normalization
   via `.Replace('\\', '/')` is in place. The equivalence test exercises this path.

---

## Technical Debt

No P2/P3 debt items identified from this batch.

---

## Task Tracker Updates

- TKB-001: **DONE** ✅
- TKB-002: **DONE** ✅
- TKB-003: **DONE** ✅
- TKB-004: **DONE** ✅
- TKB-005: **DONE** ✅

---

## Suggested Git Commit Message

```
feat(tkb): Phase 1+2 - domain schema attributes, DTOs, and VFS transport tier

TKB-001: [TkbDescriptor], [WeaponRef], [AmmoRef], [ModelRef] attributes
         with null/empty/whitespace/hash-character validation.
TKB-002: TkbMasterDto, VehicleParametersDto, WeaponCapabilitiesDto,
         AmmoWeaponBallisticsDto as pure record POCOs (no ECS, no MessagePack).
TKB-003: TkbEntityFile record struct, ITkbStorageStrategy interface,
         RawDirectoryTkbProvider with lazy JSON enumeration, UTF-8 write, delete.
TKB-004: ZipTkbProvider (read-only; NotSupportedException on write/delete).
TKB-005: TkbUnifiedLoader factory (.zip vs directory auto-detection).

All 62 TKB tests pass. Solution builds clean (0 errors).
```
