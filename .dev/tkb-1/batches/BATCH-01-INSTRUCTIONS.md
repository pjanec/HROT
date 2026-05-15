# BATCH-01: Domain Schema & VFS Transport Tier

**Batch Number:** BATCH-01
**Tasks:** TKB-001, TKB-002, TKB-003, TKB-004, TKB-005
**Phase:** Phase 1 (Domain Schema) + Phase 2 (VFS and Transport Tier)
**Estimated Effort:** 12-14 hours
**Priority:** HIGH
**Dependencies:** None (all new files, no modifications to existing code)

---

## Onboarding & Workflow

### Developer Instructions

This is the first batch of the TKB workstream. You are building the foundational layers of the
Transient Knowledge Base: the pure C# attribute/DTO vocabulary (Phase 1) and the file-system
abstraction that provides those files to the runtime (Phase 2). Both phases consist exclusively
of NEW files -- you will not modify any existing source files in this batch.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/tkb-1/DESIGN.md` — Read Sections 1 (Overview), 2 (Architectural
   Principles), 3 Phase 1 (§1.1, §1.2, §1.3), and Phase 2 (§2.1, §2.2, §2.3, §2.4) in full.
2. **Task Definitions:** `.dev/tkb-1/TASK-DETAIL.md` — Read TKB-001, TKB-002, TKB-003, TKB-004,
   TKB-005 in full.
3. **Existing Toolkit structure:** `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs` — understand
   the existing namespace conventions.
4. **Test examples:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDatabaseTests.cs` — understand
   the test style used in this project.
5. **FdpJsonOptionsRegistry:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs` —
   understand `DefaultRelaxed` which is used for DTO deserialization in TKB-002 tests.

### Source Code Location

- **Primary Work Area:** `FDP/Toolkits/Fdp.Toolkits/Tkb/` (create sub-folders as needed)
- **Test Project:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/`
- **Project file:** `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
- **Test project file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`

### Build & Test Commands

```powershell
# From repo root (d:\Work\IOS-IG-SimHost-FDP-2):
cd FDP
dotnet build FDP.sln
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Tkb"
```

### Report Submission

**When done, submit your report to:**
`.dev/tkb-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/tkb-1/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch establishes the two lowest layers of the TKB pipeline:

1. **Domain Schema (Phase 1):** Pure C# POCOs + attributes. No ECS. No MessagePack. No transport.
   These are the types that all higher layers reference.
2. **VFS Transport (Phase 2):** `ITkbStorageStrategy` abstraction + two implementations
   (directory-backed and ZIP-backed) + a factory facade. This is how the runtime reads TKB
   entity files regardless of whether they come from a raw folder or a staged ZIP archive.

---

## Tasks

### Task 1: `[TkbDescriptor]` and field-level attributes (TKB-001)

**Full spec:** See `.dev/tkb-1/TASK-DETAIL.md` section **TKB-001**.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/TkbDescriptorAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/WeaponRefAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/AmmoRefAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/ModelRefAttribute.cs`

**Target namespace:** `Fdp.Toolkit.Tkb.Attributes`

Key constraints (from spec):
- `TkbDescriptorAttribute.HierarchicalName` must not be null or whitespace — throw
  `ArgumentException` in the constructor.
- `HierarchicalName` must NOT contain `#` — throw `ArgumentException` with a message explaining
  that `#PartId` is a runtime delimiter and must not appear in schema-level names.
- No ECS types, no MessagePack, no framework references.

**Test file to create:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/Attributes/TkbDescriptorAttributeTests.cs`

Tests required:
- `[TkbDescriptor("")]` throws `ArgumentException`
- `[TkbDescriptor(null)]` throws `ArgumentException`
- `[TkbDescriptor("Platform#1")]` throws `ArgumentException` (# character forbidden)
- `[TkbDescriptor("Gen.VehicleParameters")]` constructs successfully; `HierarchicalName` is
  correct
- `WeaponRefAttribute`, `AmmoRefAttribute`, `ModelRefAttribute` each apply to a property
  without error (smoke test: decorate a dummy property and reflect on it)

---

### Task 2: Concrete descriptor DTOs (TKB-002)

**Full spec:** See `.dev/tkb-1/TASK-DETAIL.md` section **TKB-002**.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/TkbMasterDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/VehicleParametersDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/WeaponCapabilitiesDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/AmmoWeaponBallisticsDto.cs`

**Target namespace:** `Fdp.Toolkit.Tkb.Domain`

Key constraints:
- All DTOs are `record` types with `init`-only properties and default values.
- All DTOs carry `[TkbDescriptor(...)]`.
- Use `[EditUnit("...")]` from `StructEdit` for numeric fields (already a project dependency).
- Use `[Description("...")]` from `System.ComponentModel` for documented fields.
- No ECS base classes, no `[MessagePackObject]`, no transport types.

**Test file to create:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/Domain/TkbDtosTests.cs`

Tests required (using `System.Text.Json` with `FdpJsonOptionsRegistry.DefaultRelaxed`):
- `TkbMasterDto` deserializes correctly from:
  ```json
  { "CustomName": "M1 Abrams", "DisType": "1.1.225.1.1.1.0" }
  ```
- `VehicleParametersDto` deserializes correctly from:
  ```json
  { "Mass": 61000.0, "Length": 7.93, "Width": 3.66,
    "MaxSpeedFwd": 20.0, "MaxSpeedRev": 12.0, "MaxAccel": 2.5 }
  ```
  Assert `Mass == 61000.0f`, `Length == 7.93f`, etc.
- `WeaponCapabilitiesDto` deserializes correctly from:
  ```json
  { "EffectiveRange": 3000.0, "RateOfFire": 6.0, "MagazineCapacity": 40 }
  ```
- `AmmoWeaponBallisticsDto` deserializes correctly from:
  ```json
  { "WeaponGuid": 2001, "MuzzleSpeed": 1500.0, "Damage": 600.0 }
  ```
  Assert `WeaponGuid == 2001L`.
- Each DTO carries `[TkbDescriptor]` (reflection check on the class attribute).
- `AmmoWeaponBallisticsDto.WeaponGuid` carries `[WeaponRef]` (reflection check).
- No DTO type references ECS base classes or `MessagePackObjectAttribute` (negative reflection
  check).

---

### Task 3: `TkbEntityFile`, `ITkbStorageStrategy`, `RawDirectoryTkbProvider` (TKB-003)

**Full spec:** See `.dev/tkb-1/TASK-DETAIL.md` section **TKB-003**.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/TkbEntityFile.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/ITkbStorageStrategy.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/RawDirectoryTkbProvider.cs`

**Target namespace:** `Fdp.Toolkit.Tkb.Vfs`

Key constraints:
- `TkbEntityFile` is a `readonly record struct`.
- `CategoryPath` uses forward slashes; no leading or trailing slash; relative to root.
  Example: for `<root>/Platform/Vehicle/Military/MBT/Merkava Mk4.json`, `CategoryPath` is
  `"Platform/Vehicle/Military/MBT"`.
- `FileName` = filename without extension (`Path.GetFileNameWithoutExtension`).
- `WriteEntityFile`: uses UTF-8 without BOM; creates missing intermediate directories.
- `DeleteEntityFile`: no error if file does not exist.
- Enumeration is lazy (one `FileStream` open at a time); non-`.json` files excluded.

**Test file to create:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/Vfs/RawDirectoryTkbProviderTests.cs`

Tests required:
- `EnumerateEntityFiles()` on a test directory with 3 `.json` files yields exactly 3 results.
- `CategoryPath` is computed correctly (forward slashes, relative, no trailing slash) for a
  file nested 2 levels deep.
- `FileName` equals filename without `.json` extension.
- A `.txt` file in the directory is NOT yielded.
- A `.json` file in a subdirectory IS yielded (recursive scan).
- `WriteEntityFile` then enumerate: the written content is retrievable by reading the returned
  `JsonStream`.
- `DeleteEntityFile` on an existing file removes it; subsequent enumeration does not include it.
- `DeleteEntityFile` on a nonexistent path does not throw.

Use `System.IO.Path.GetTempPath()` + a unique subfolder per test for isolation; clean up in
`Dispose` or use `try/finally`.

---

### Task 4: `ZipTkbProvider` (TKB-004)

**Full spec:** See `.dev/tkb-1/TASK-DETAIL.md` section **TKB-004**.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/ZipTkbProvider.cs`

**Target namespace:** `Fdp.Toolkit.Tkb.Vfs`

Key constraints:
- ALWAYS opened with `ZipArchiveMode.Read`. Never `Update`.
- `WriteEntityFile` and `DeleteEntityFile` throw `NotSupportedException` with a message
  indicating the provider is read-only.
- Skip ZIP entries that are directory markers (entry `FullName` ends with `/`) or not `.json`.
- `CategoryPath` derived from directory portion of entry `FullName`; backslashes replaced with
  forward slashes; trailing slash stripped.

**Test file to create:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/Vfs/ZipTkbProviderTests.cs`

Tests required:
- Build a test ZIP in memory (use `ZipArchive` + `MemoryStream`) containing entries at two
  different category paths. `EnumerateEntityFiles()` yields all `.json` entries.
- `CategoryPath` matches the expected forward-slash path for a nested entry.
- `FileName` matches without extension.
- `WriteEntityFile(...)` throws `NotSupportedException`.
- `DeleteEntityFile(...)` throws `NotSupportedException`.
- ZIP opened with `ZipArchiveMode.Read` (verify the constructor does not pass `Update`).
- Directory markers (entries ending in `/`) are skipped.
- Non-`.json` entries are skipped.
- A ZIP produced from a known directory (using `RawDirectoryTkbProvider`) yields the same
  logical entity names and category paths as the directory provider.

---

### Task 5: `TkbUnifiedLoader` (TKB-005)

**Full spec:** See `.dev/tkb-1/TASK-DETAIL.md` section **TKB-005**.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/TkbUnifiedLoader.cs`

**Target namespace:** `Fdp.Toolkit.Tkb.Vfs`

Key constraints:
- `.zip` extension check is case-insensitive (`StringComparison.OrdinalIgnoreCase`).
- Throws `ArgumentException` for any path that is neither a `.zip` file nor an existing
  directory.
- `Dispose()` delegates to the underlying strategy.

**Test file to create:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/Vfs/TkbUnifiedLoaderTests.cs`

Tests required:
- Given an existing `.zip` path, `EnumerateEntityFiles()` returns results (backed by
  `ZipTkbProvider`).
- Given an existing directory path, `EnumerateEntityFiles()` returns results (backed by
  `RawDirectoryTkbProvider`).
- Given a `.ZIP` path (uppercase extension), loader still picks `ZipTkbProvider` (case-insensitive
  check).
- Given a nonexistent path, throws `ArgumentException`.
- Given a path that exists but is neither a `.zip` nor a directory (e.g., a `.txt` file),
  throws `ArgumentException`.
- `Dispose()` can be called without error after construction.

---

## Mandatory Test Quality Standards

- Every test must use meaningful assertions on **values and behavior**, not just "no exception
  thrown".
- Use `Assert.Equal(expected, actual)` on specific field values.
- Use `Assert.Contains` / `Assert.Single` / `Assert.Empty` on collections with specific
  predicates.
- Tests that verify exceptions must use `Assert.Throws<T>` (not try/catch).
- Test directory setup/teardown must be deterministic and not leave temp files behind.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks strictly in sequence. Do NOT move to the next task until ALL tests
for the current task pass.**

1. **TKB-001:** Implement attributes → Write tests → `dotnet test` passes ✅
2. **TKB-002:** Implement DTOs → Write tests → `dotnet test` passes ✅
3. **TKB-003:** Implement VFS types → Write tests → `dotnet test` passes ✅
4. **TKB-004:** Implement ZipTkbProvider → Write tests → `dotnet test` passes ✅
5. **TKB-005:** Implement TkbUnifiedLoader → Write tests → `dotnet test` passes ✅

**Do NOT ask for permission to run tests. Do NOT ask if it is OK to fix a compile error. Do NOT
stop because a test fails. Fix the root cause until ALL tests pass, then write your report.**

---

## Success Criteria

This batch is DONE when:
- [ ] TKB-001: 4 attribute files created; constructor validation tests pass
- [ ] TKB-002: 4 DTO files created; JSON deserialization tests pass; attribute reflection tests pass
- [ ] TKB-003: 3 VFS files created; RawDirectoryTkbProvider tests pass (including write/delete)
- [ ] TKB-004: 1 ZipTkbProvider file created; read/throw tests pass
- [ ] TKB-005: 1 TkbUnifiedLoader file created; dispatch tests pass
- [ ] All tests pass: `dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Tkb"`
- [ ] The solution builds without errors: `dotnet build FDP\FDP.sln`
- [ ] Report submitted to `.dev/tkb-1/reports/BATCH-01-REPORT.md`

---

## Developer Insights (Report Section)

In your report, address:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or potential improvements in the existing codebase structure
that are relevant to this batch?

**Q3:** What design decisions did you make beyond the task spec (e.g., edge cases you handled
that weren't mentioned, or alternatives you considered)?

**Q4:** What edge cases did you discover during testing that were not in the spec?

**Q5:** Are there any concerns about the `CategoryPath` convention or ZIP enumeration behavior
that future phases should be aware of?

**Q6:** Suggested git commit message for this batch.

---

## Reference Materials

- **Design doc:** `.dev/tkb-1/DESIGN.md` — Sections 1, 2, Phase 1, Phase 2
- **Task definitions:** `.dev/tkb-1/TASK-DETAIL.md` — TKB-001 through TKB-005
- **Existing TKB code:** `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs`
- **Existing TKB tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDatabaseTests.cs`
- **JSON options:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
- **StructEdit attributes:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/` (for `[EditUnit]`,
  `[EditRange]`)
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
