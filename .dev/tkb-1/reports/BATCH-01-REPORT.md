# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2026-05-15  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TKB-001 | Complete | 4 attribute files; 9 tests |
| TKB-002 | Complete | 4 DTO files; 10 tests |
| TKB-003 | Complete | 3 VFS files; 9 tests |
| TKB-004 | Complete | 1 ZipTkbProvider file; 9 tests |
| TKB-005 | Complete | 1 TkbUnifiedLoader file; 8 tests |

---

## Testing Results

**Unit Tests Passed:** 62 / 62 (filter `FullyQualifiedName~Tkb`)  
**Build:** `dotnet build FDP.sln` — 0 errors, 2 pre-existing NuGet warnings (unrelated to this batch)

**Key Test Scenarios Verified:**
- [x] `TkbDescriptorAttribute` rejects null, empty, whitespace, and `#`-containing names
- [x] `WeaponRefAttribute`, `AmmoRefAttribute`, `ModelRefAttribute` are reflectable on properties
- [x] All four DTOs deserialize correctly from JSON via `FdpJsonOptionsRegistry.DefaultRelaxed`
- [x] All DTOs carry `[TkbDescriptor]`; `AmmoWeaponBallisticsDto.WeaponGuid` carries `[WeaponRef]`
- [x] No DTO has ECS base classes or `[MessagePackObject]` (negative reflection check)
- [x] `RawDirectoryTkbProvider` enumerates exactly the JSON files (skips `.txt`), resolves correct `CategoryPath` (forward slashes, no trailing slash), correct `FileName` (no extension)
- [x] Write→enumerate round-trip; delete removes from enumeration; delete of nonexistent does not throw
- [x] Root-level files produce empty `CategoryPath`
- [x] `ZipTkbProvider` enumerates JSON entries, skips directory markers and non-JSON entries
- [x] `ZipTkbProvider` `_archive.Mode == ZipArchiveMode.Read` (verified via reflection)
- [x] `WriteEntityFile`/`DeleteEntityFile` on ZIP throw `NotSupportedException` with "read-only" message
- [x] ZIP-from-directory and directory-provider yield equal logical entity sets
- [x] `TkbUnifiedLoader` routes `.zip` to `ZipTkbProvider` and directory to `RawDirectoryTkbProvider`
- [x] Case-insensitive `.ZIP` extension still routes to `ZipTkbProvider`
- [x] Non-existent path and existing-non-zip-non-dir path throw `ArgumentException`
- [x] `Dispose()` is safe to call

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

One test failure: `EnumerateEntityFiles_NonJsonEntries_AreSkipped` threw
`System.IO.IOException: Entries cannot be created while previously created entries are still open`.
This was a test-only bug — the ZIP creation code used `using var` (C# 8 declaration form) which
defers disposal to the end of the enclosing scope. In a `ZipArchive`, a new entry cannot be
created while a previous entry stream is still open, even when the previous stream has not been
explicitly written to after its `StreamWriter` flushed. Fixed by switching to explicit
`using (var w = ...) { }` blocks so each entry stream was closed before the next was created.

**Q2: Did you spot any weak points or potential improvements in the existing codebase structure
that are relevant to this batch?**

The `TkbDatabase` does not implement `ITkbDatabase` — it uses the interface from `Fdp.Interfaces`
namespace. Since `ITkbDatabase` lives in `Fdp.Interfaces` and the current file shows the class
implements it, future phases (TKB-007) will need to extend that interface carefully to avoid
breaking existing concrete implementations and test doubles.

**Q3: What design decisions did you make beyond the task spec?**

- Added an `ArgumentException` message that names the `#` character explicitly in
  `TkbDescriptorAttribute`, which is slightly more helpful than the spec's minimum "explains
  that `#PartId` is a runtime delimiter".
- Used `Path.GetRelativePath` (not string subtraction) in `RawDirectoryTkbProvider` for
  computing `CategoryPath`. This is more robust on all platforms and handles edge cases like
  trailing separators on the root path.
- Used `using (file.JsonStream) { }` in test enumeration helpers to ensure each stream is
  consumed before the enumerator advances. This matches the stated contract and keeps streams
  properly bounded.

**Q4: What edge cases did you discover during testing that were not in the spec?**

- A root-level entity (no subdirectory) yields `CategoryPath == ""`. The spec shows an example
  of a nested file but does not explicitly call out the empty-string case. Added a dedicated test
  (`CategoryPath_AtRoot_IsEmptyString`) to lock this behavior.
- `Path.GetDirectoryName` on a relative path with no directory component returns `""` on some
  .NET versions and `null` on others. Used `?? string.Empty` to be safe.
- `using var` (C# 8 declaration form) defers disposal to end of enclosing block. Inside a
  `foreach` body this means after `yield return`; the stream IS disposed before the next
  iteration begins. However inside a single `using` scope where two entries are created
  sequentially, the first stream is NOT disposed until the enclosing scope ends, which caused the
  ZIP creation failure. Documented above in Q1.

**Q5: Are there any concerns about the `CategoryPath` convention or ZIP enumeration behavior
that future phases should be aware of?**

- **CategoryPath == "" for root entities.** The phase-3 `GetEntitiesByCategory("")` requirement
  handles this (an empty prefix returns all), but the `TkbDeserializer` and `TkbTemplate` should
  be tested with root-level entities to ensure they store and retrieve `CategoryPath = ""` cleanly.
- **ZIP entry path separator portability.** `ZipFile.CreateFromDirectory` on Windows produces
  entries with backslash separators in some .NET versions. The `ZipTkbProvider` normalizes
  backslashes to forward slashes via `.Replace('\\', '/')`, which covers this. The equivalence
  test `EnumerateEntityFiles_ZipFromDirectory_MatchesDirectoryProvider` exercises this path.
- **ZIP entry `.FullName` vs `.Name`.** `entry.Name` is the filename-only portion; `entry.FullName`
  is the full relative path. Using `.Name` for `Path.GetFileNameWithoutExtension` and `.FullName`
  for path splitting ensures correctness even when entry names contain dots that are part of the
  directory hierarchy.
- **Lazy enumeration and ZIP archive lifetime.** `ZipTkbProvider` keeps `_archive` open for the
  lifetime of the provider. Consumers must not dispose the `ZipTkbProvider` while still
  iterating `EnumerateEntityFiles()`. This matches `RawDirectoryTkbProvider`'s contract but
  should be documented in API-level comments in later phases.

**Q6: Suggested git commit message for this batch**

```
feat(tkb): Phase 1+2 — domain schema attributes, DTOs, and VFS transport tier

TKB-001: Add [TkbDescriptor], [WeaponRef], [AmmoRef], [ModelRef] attributes
         with constructor validation (null/empty/# guards).
TKB-002: Add TkbMasterDto, VehicleParametersDto, WeaponCapabilitiesDto,
         AmmoWeaponBallisticsDto as pure record POCOs.
TKB-003: Add TkbEntityFile record struct, ITkbStorageStrategy interface,
         and RawDirectoryTkbProvider (lazy file enumeration, write, delete).
TKB-004: Add ZipTkbProvider — read-only ZIP-backed strategy; Write/Delete
         throw NotSupportedException.
TKB-005: Add TkbUnifiedLoader factory facade (.zip vs directory dispatch,
         case-insensitive extension check).

All 62 TKB unit tests pass. Solution builds with 0 errors.
```

---

## Outstanding Issues / Next Steps
- None for this batch. All success criteria met.
- Phase 3 (TKB-006, TKB-007) will modify existing files (`TkbTemplate.cs`,
  `ITkbDatabase.cs`). Callers of `ApplyTo()` must be identified before that batch begins.
