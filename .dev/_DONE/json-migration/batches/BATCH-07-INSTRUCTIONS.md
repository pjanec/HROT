# BATCH-07: MigrationServices + MigrationBootstrap

**Batch Number:** BATCH-07
**Tasks:** JM-P1-013
**Phase:** Phase 1 — Core Infrastructure
**Estimated Effort:** 4-6 hours
**Priority:** HIGH
**Dependencies:** BATCH-06 (completed, committed af98d4ea)

---

## Onboarding & Workflow

### Developer Instructions

Implement the bootstrap entry point for the migration system: `MigrationBootstrap` (factory) and
`MigrationServices` (bundle type). These are the last two types needed to complete Phase 1 of the
migration system. They are deliberately small — most of the hard work is already in the existing
components.

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Task definition:** `.dev/json-migration/TASK-DETAILS.md` — section `JM-P1-013`
3. **Design — Bootstrap wiring (§8):** `.dev/json-migration/Migration-system.md` lines 2517–2670
   - §8.1 `MigrationServices` — record type
   - §8.2 `MigrationBootstrap` — both static methods
   - §8.3 Per-subsystem usage — context only, no code needed here
4. **Design — Test plan (§4.4):** `.dev/json-migration/Migration-system.md` lines 4495–4520 — T2-100..T2-103
5. **Correction C-6:** `.dev/json-migration/TASK-DETAILS.md` line 21 — `AssemblyInformationalVersionAttribute` pattern
6. **Pattern reference:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` lines 604–607
7. **Previous review:** `.dev/json-migration/reviews/BATCH-06-REVIEW.md` — read it

### Source Code Location

- **Primary work area:** `FDP/Engine/Fdp.Core/Serialization/Migrations/`
- **New files directory:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/`
- **Test project:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/`
- **Build:** `dotnet build FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj -q`
- **Test:** `dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations" --no-build -q`

### Report Submission

When done, submit your report to: `.dev/json-migration/reports/BATCH-07-REPORT.md`

---

## Context

`MigrationBootstrap.Build` and `BuildForProduction` are the public factory API that host processes
(SimHost, Editor, ClusterRunner, etc.) will use to wire up the migration system. They return
`MigrationServices` — a record bundling all four migration components.

**Related tasks:**
- [JM-P1-013](../TASK-DETAILS.md#jm-p1-013--migrationservices--migrationbootstrap-gate)

---

## Batch Objectives

- Implement `MigrationServices` (public sealed record)
- Implement `MigrationBootstrap` (public static class) with `Build` and `BuildForProduction`
- Tests T2-100..T2-103 all pass
- 228 + 4 = 232 migration tests pass

---

## Tasks

### Task 1: `MigrationServices`

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationServices.cs` (NEW)
**Namespace:** `Fdp.Core.Serialization.Migrations`

The design spec (§8.1) shows:

```csharp
public sealed record MigrationServices(
    MigrationRegistry Registry,
    MigrationPipeline Pipeline,
    ReadOnlyMigrationAdapter ReadOnly,
    PersistentMigrationAdapter Persistent);
```

Note that `PersistentMigrationAdapter` constructor is `internal`. `MigrationBootstrap` is in the
same assembly (`Fdp.Core`), so it can access the `internal` constructor. `MigrationServices` is a
plain record — no special logic.

### Task 2: `MigrationBootstrap`

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs` (NEW)
**Namespace:** `Fdp.Core.Serialization.Migrations`

Implement both static methods exactly as specified in design §8.2.

#### `Build` method

```csharp
public static MigrationServices Build(
    Action<MigrationRegistry> registerFormats,
    IMigrationStorage storage,
    Func<string> engineVersionProvider,
    string writerIdentifier)
```

Steps:
1. Create a new `MigrationRegistry`
2. Auto-register `"Fdp.MigrationJournal"` as passthrough v1:
   `registry.RegisterPassthroughDocType(FdpDocumentTypes.MigrationJournal, 1)`
   (`FdpDocumentTypes` is in `FDP/Engine/Fdp.Core/Serialization/FdpDocumentTypes.cs`, namespace `Fdp.Core.Serialization`)
3. Invoke `registerFormats(registry)` — caller registers their formats
4. Call `registry.Seal()` — no further registrations allowed
5. Create `MigrationPipeline pipeline = new MigrationPipeline(registry)`
6. Create `ReadOnlyMigrationAdapter readOnly = new ReadOnlyMigrationAdapter(pipeline)`
7. Create `PersistentMigrationAdapter persistent = new PersistentMigrationAdapter(pipeline, storage, engineVersionProvider, writerIdentifier)`
8. Return `new MigrationServices(registry, pipeline, readOnly, persistent)`

Null-guard all parameters (throw `ArgumentNullException`).

#### `BuildForProduction` method

```csharp
public static MigrationServices BuildForProduction(
    Action<MigrationRegistry> registerFormats,
    string writerIdentifier)
```

This is a convenience overload. It must:
- Use `new FileSystemMigrationStorage()` as the storage
- Read `AssemblyInformationalVersionAttribute` from `typeof(MigrationBootstrap).Assembly` (this
  is the `Fdp.Core` assembly). Pattern from `WindowManager.cs` lines 604-607:
  ```csharp
  var infoAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)
      System.Attribute.GetCustomAttribute(
          typeof(MigrationBootstrap).Assembly,
          typeof(System.Reflection.AssemblyInformationalVersionAttribute));
  var version = infoAttr?.InformationalVersion ?? "unknown";
  ```
- Delegate to `Build(registerFormats, new FileSystemMigrationStorage(), () => version, writerIdentifier)`

### Task 3: Tests T2-100..T2-103

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationBootstrapTests.cs` (NEW)

All four tests:

| ID | Test method | What to assert |
|----|-------------|----------------|
| T2-100 | `Build_RegistersJournalDocType` | `services.Registry.IsRegistered(FdpDocumentTypes.MigrationJournal)` is true, even when `registerFormats` callback is empty |
| T2-101 | `Build_InvokesRegisterFormatsCallback` | A callback that sets a `bool` flag is invoked; the flag is true after `Build` returns |
| T2-102 | `Build_SealsRegistry` | After `Build` returns, calling `services.Registry.RegisterPassthroughDocType("Any", 1)` throws (sealed registry) |
| T2-103 | `Build_ProductionOverload_ReadsAssemblyInformationalVersion` | Call `BuildForProduction(reg => {}, "Test")` and verify `services.ReadOnly` is not null AND the engine version embedded in the services pipeline is non-null and non-empty. To actually check the version string, load a doc through `services.ReadOnly` and inspect the `$meta.engineVersion` field — or, simpler: verify that `BuildForProduction` returns without throwing and produces non-null services |

For T2-103, the simplest test: call `BuildForProduction` with an empty register callback and verify all four returned properties are non-null. The engine version will read from the `Fdp.Core.Tests` assembly (since that runs the test) — it will be the test binary's version, which is non-null.

Actually: `typeof(MigrationBootstrap).Assembly` inside the test will resolve to the `Fdp.Core` assembly (not `Fdp.Core.Tests`). The `InformationalVersion` may be null if the project doesn't set it. Use `?? "unknown"` fallback so it's always non-empty.

Use `InMemoryMigrationStorage` for T2-100 through T2-102 (use `Build` overload, not `BuildForProduction`). Use the real `BuildForProduction` only for T2-103 (no filesystem I/O in T2-103 because no doc is loaded).

---

## Quality Standards

**CODE:**
- All parameters null-guarded
- `FdpDocumentTypes.MigrationJournal` constant already exists in
  `FDP/Engine/Fdp.Core/Serialization/FdpDocumentTypes.cs` (namespace `Fdp.Core.Serialization`)

**TESTS:**
- Tests must test BEHAVIOR (registration, sealing, callback invocation), not just compilation
- T2-100: verify `IsRegistered` returns true — do not just check no exception thrown
- T2-101: use a lambda that sets a variable; verify the variable was set
- T2-102: verify the exception type (should be `InvalidOperationException` or `MigrationException`)
- T2-103: call `BuildForProduction` and assert all four service properties non-null

---

## Mandatory Workflow

1. Implement `MigrationServices.cs` → build → confirm 0 errors
2. Implement `MigrationBootstrap.cs` → build → confirm 0 errors
3. Write `MigrationBootstrapTests.cs` → run all migration tests → **232/232 must pass**
4. If any test fails, fix root cause before proceeding
5. Write report

**DO NOT stop and ask for permission at any step. Complete all steps and write the report.**

---

## Success Criteria

- [ ] `MigrationServices.cs` created — public sealed record, 4 properties
- [ ] `MigrationBootstrap.cs` created — `Build` + `BuildForProduction` implemented
- [ ] `MigrationBootstrapTests.cs` created — T2-100, T2-101, T2-102, T2-103 all pass
- [ ] All 232 migration tests pass, 0 skipped
- [ ] Build: 0 errors, 0 warnings
- [ ] Report submitted

---

## Reference Materials

- **Task def:** `.dev/json-migration/TASK-DETAILS.md#jm-p1-013`
- **Design §8:** `.dev/json-migration/Migration-system.md` lines 2517–2595
- **Test plan §4.4:** `.dev/json-migration/Migration-system.md` lines 4495–4520
- **Pattern C-6:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` lines 604–607
- **FdpDocumentTypes:** `FDP/Engine/Fdp.Core/Serialization/FdpDocumentTypes.cs` (namespace `Fdp.Core.Serialization`)
- **Previous review:** `.dev/json-migration/reviews/BATCH-06-REVIEW.md`
