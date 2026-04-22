# BATCH-03: Stage 3 + Stage 4 Final — Eradicate Legacy Boilerplate + Prove Isolation

**Batch Number:** BATCH-03
**Tasks:** DEBT-004 (P1), MODINIT-S301, MODINIT-S302, MODINIT-S402, DEBT-006 (P3)
**Phase:** Stage 3 (SimHostApp + IgApplication) + Stage 4 final (sever references, prove isolation)
**Estimated Effort:** 10–14 hours
**Priority:** HIGH
**Dependencies:** BATCH-02 committed and green (✅ confirmed — Stage 2 done)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the final batch for the `mod-init` workstream. It:

1. **DEBT-004 (P1):** Migrates `EyesAndMuscleSubsystem` to use `.WithReplication(NodeRole.AllInOne).Build()` instead of manually instantiating `NedReplicationModule`.
2. **MODINIT-S301:** Refactors `SimHostApp` to use `HrotNodeBuilderWithReplication` (`.WithReplication(_role).Build()`); removes the P2 debt field and TODO comment.
3. **MODINIT-S302:** Refactors `IgApplication` to use `NedReplicationModule` via the builder; removes manual translator list and `DeadReckoningSyncSystem` registration.
4. **MODINIT-S402:** Audits that `Hrot.SimHost.csproj`, `Hrot.IG.csproj`, `Hrot.CGF.csproj` have no `<ProjectReference>` to `Hrot.ClusterRunner`; runs isolated builds.
5. **DEBT-006 (P3):** Removes the now-dead internal fields `_replicationConfigured` and `_replicationRole` from `HrotNodeBuilder`.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Design Document:** `.dev/mod-init/DESIGN.md` — Sections §3 Stage 3, §3.1, §3.2, §4.2, §4.3, "Key Decisions", "Success Criteria"
3. **Task Definitions:** `.dev/mod-init/TASK-DETAIL.md` — See MODINIT-S301, MODINIT-S302, MODINIT-S402
4. **Previous Review:** `.dev/mod-init/reviews/BATCH-02-REVIEW.md` — Note the wrapper-type pattern (`HrotNodeBuilderWithReplication`)
5. **DEBT-TRACKER:** `.dev/mod-init/DEBT-TRACKER.md` — DEBT-004 is P1

### Source Code Locations

- **SimHostApp:** `Hrot.SimHost/SimHostApp.cs` — lines 104–106 (P2 debt + field), line 261–263 (builder chain)
- **NodeBootstrapper:** `Hrot.SimHost/NodeBootstrapper.cs` — already updated in BATCH-01; verify namespace updates are present
- **IgApplication:** `Hrot.IG/IgApplication.cs` — `InitializeEcs()` (line ~619), `InitializeNetwork()` (line ~733), `customTranslators` (line ~787–905), `DeadReckoningSyncSystem` (line ~1202)
- **EyesAndMuscleSubsystem:** `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs` — lines 69–89 (builder chain + manual module instantiation)
- **HrotNodeBuilder:** `Hrot.Common/Infrastructure/HrotNodeBuilder.cs` — internal fields to clean up
- **Project files to audit:** `Hrot.SimHost/Hrot.SimHost.csproj`, `Hrot.IG/Hrot.IG.csproj`, `Hrot.CGF/Hrot.CGF.csproj`
- **Test projects:** `Hrot.SimHost.Integration.Tests/`, `Hrot.SimHost.Tests/`, `Hrot.IG.Tests/`, `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission

**When done, submit your report to:**
`.dev/mod-init/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/mod-init/questions/BATCH-03-QUESTIONS.md`

---

## Context

The `HrotNodeBuilderWithReplication` wrapper type (from BATCH-02) enables the fluent chain:
```csharp
using Hrot.Network.Infrastructure;

_context = new HrotNodeBuilder(config)
    .WithRole("subsystemName", NodeRole.X)
    .WithReplication(NodeRole.X)   // returns HrotNodeBuilderWithReplication
    .Build();                      // calls HrotNodeBuilderWithReplication.Build()

// After Build():
_context.NedReplication  // INedReplicationModule — non-null (set by the wrapper Build())
_context.GhostCreationSystem  // GhostCreationSystem — non-null (set by the wrapper Build())
```

Then register the module on the kernel:
```csharp
_context.Kernel.RegisterModule(_context.NedReplication!);
```

Note: `HrotNodeBuilderWithReplication.Build()` uses `context.World.Bus` for the `eventBus` parameter (not `_context.EventBus`). This matches `CgfSubsystem`'s documented pattern and must be consistent across all migrated subsystems.

---

## 🎯 Batch Objectives

- Resolve **DEBT-004** (P1): Migrate `EyesAndMuscleSubsystem` to `.WithReplication()`. This is P1 (blocking) because it demonstrates the old instantiation pattern is still in use.
- **MODINIT-S301**: Remove P2 dead code from `SimHostApp`; wire NedReplicationModule via builder extension.
- **MODINIT-S302**: Remove manual translator list + `DeadReckoningSyncSystem` registration from `IgApplication`; wire module via builder extension.
- **MODINIT-S402**: Prove that application layer projects don't reference `Hrot.ClusterRunner`; run isolated builds.
- **DEBT-006**: Remove dead `_replicationConfigured`/`_replicationRole` from `HrotNodeBuilder`.
- All existing tests remain green.

---

## ✅ Tasks

### Task 1: DEBT-004 — Migrate EyesAndMuscleSubsystem (P1)

**File:** `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`

**Current state (lines 69–89):**
```csharp
_context = new HrotNodeBuilder(nodeCfg)
    .WithRole("EyesAndMuscle", NodeRole.AllInOne)
    .Build();

// ... later:
_nedReplicationModule = new NedReplicationModule(
    participant:  _context.Participant,
    role:         NodeRole.AllInOne,
    entityMap:    _context.EntityMap,
    geoTransform: HrotEnvironment.CreateGeoTransform(),
    eventBus:     _context.EventBus,    ← NOTE: uses EventBus, not World.Bus
    localNodeId:  config.NodeId,
    domainId:     config.DomainId);
_context.Kernel.RegisterModule(_nedReplicationModule);
```

**Target state:**
```csharp
using Hrot.Network.Infrastructure;   // ADD THIS using directive

_context = new HrotNodeBuilder(nodeCfg)
    .WithRole("EyesAndMuscle", NodeRole.AllInOne)
    .WithReplication(NodeRole.AllInOne)            // NEW
    .Build();                                       // calls HrotNodeBuilderWithReplication.Build()

// ... later:
_context.Kernel.RegisterModule(_context.NedReplication!);   // replaces manual NedReplicationModule
```

**Delete:** The `private IEcsModule? _nedReplicationModule;` field (or whatever type it is declared as).
**Delete from Shutdown():** `_nedReplicationModule = null;` (if present).
**Delete:** The manual `NedReplicationModule` constructor call (lines 81–89).

**Note on World.Bus vs EventBus:** The `HrotNodeBuilderWithReplication.Build()` internally uses `context.World.Bus` for the eventBus parameter. The previous manual code used `context.EventBus`. The `World.Bus` is correct (CgfSubsystem had a documented comment explaining why: events published during the Input kernel phase must be visible to PostSimulation systems via `World.Bus`). **Do not change `HrotNodeBuilderWithReplication.Build()` to match the old `EventBus` usage.**

---

### Task 2: MODINIT-S301 — Refactor SimHostApp

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s301--refactor-simhostapp-to-use-nedreplicationmodule`

**File:** `Hrot.SimHost/SimHostApp.cs`

**Changes:**

1. **Add using directive** at the top of the file: `using Hrot.Network.Infrastructure;`

2. **Delete lines 104–106** (P2 debt comment + dead field):
   ```csharp
   // TODO (P2 debt): wire NedReplicationModule once it moves to Hrot.Common so SimHostApp can reference it.
   private ModuleHost.Core.Abstractions.IEcsModule? _nedReplicationModule;
   ```

3. **Update the builder chain** around line 261–263:
   ```csharp
   // BEFORE:
   _context = new HrotNodeBuilder(hrotConfig)
       .WithRole("SimHost", _role)
       .Build();

   // AFTER:
   _context = new HrotNodeBuilder(hrotConfig)
       .WithRole("SimHost", _role)
       .WithReplication(_role)
       .Build();
   ```

4. **Register the module**: After the builder call and `_world = _context.World;` assignments, find where other modules like `SimulationLogicModule` are registered on the kernel. Add:
   ```csharp
   _context.Kernel.RegisterModule(_context.NedReplication!);
   ```
   Place this BEFORE `_context.Kernel.Initialize()` (the kernel must be initialized after all modules are registered).

5. **NodeBootstrapper.cs** — verify the namespace updates from BATCH-01 are present:
   ```powershell
   Select-String "Hrot.SimHost.Network" Hrot.SimHost/NodeBootstrapper.cs   # should return 0 results
   ```
   If any remain, apply the fix: update to `Hrot.Map.Common.Translators` / `Hrot.Network.Translators` as appropriate.

**Tests required:**
- See TASK-DETAIL.md MODINIT-S301 success conditions 7 and 8 — spawn integration test and AllInOne role guard
- Use existing `Hrot.SimHost.Integration.Tests/` infrastructure

**Verify:**
```powershell
Select-String "P2 debt|_nedReplicationModule" Hrot.SimHost/SimHostApp.cs   # → 0 results
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj --no-build
```

---

### Task 3: MODINIT-S302 — Refactor IgApplication

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s302--refactor-igapplication-to-use-nedreplicationmodule`

**File:** `Hrot.IG/IgApplication.cs`

**Architecture note:** `IgApplication` calls `InitializeEcs()` (creates ECS world using HrotNodeBuilder with `Headless = true`) separately from `InitializeNetwork()` (creates DDS manually, builds customTranslators). For MODINIT-S302, the migration is:

1. **Change HrotNodeBuilder config in `InitializeEcs()`** (around line 619):
   - Change `Headless = true` → `Headless = _headless` (so in non-headless production mode, HrotNodeBuilder creates the DDS participant)
   - Add `.WithReplication(NodeRole.ImageGenerator)` to the chain
   - The builder's `Build()` will now construct `NedReplicationModule` with a live DDS participant when not headless

2. **Add using directive**: `using Hrot.Network.Infrastructure;`

3. **In `InitializeNetwork()` — REMOVE the following:**
   - The `customTranslators` list construction (lines ~787–905): the `List<IDescriptorTranslator>` that manually adds `EntityMasterIngressTranslator`, `GeoSpatialIngressTranslator`, `EntityInfoIngressTranslator`, `EntityDamageIngressTranslator`, `MapEntitySymbolIngressTranslator`, etc.
   - `_kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem())` at line ~1202
   - Any direct `NedReplicationModule` instantiation (if present)

4. **In `InitializeNetwork()` — ADD:**
   ```csharp
   // Register NedReplicationModule (bundles EntityStatesIngressPack + DeadReckoningSyncSystem(driveFromNetwork:true))
   if (_context?.NedReplication != null)
       _context.Kernel.RegisterModule(_context.NedReplication);
   ```

**IMPORTANT:** Preserve ALL other initialization in `InitializeNetwork()` that is NOT related to the translator packs or DeadReckoningSyncSystem:
- `_clusterSlave`, `_slaveTranslator`, DDS readers/writers for command/interaction channels
- Visualization, camera, canvas initialization  
- Any other systems explicitly registered

**Tests required:** See TASK-DETAIL condition 6 — drive-all dead reckoning test for ghost entities.

**Verify:**
```powershell
Select-String "new EntityMasterIngressTranslator|new GeoSpatialIngressTranslator|new EntityInfoIngressTranslator|RegisterGlobalSystem.*DeadReckoningSyncSystem" Hrot.IG/IgApplication.cs  # → 0 results
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj --no-build
```

---

### Task 4: MODINIT-S402 — Sever Upward Project References

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s402--sever-upward-project-references`

Run these validation queries and confirm zero results:
```powershell
Select-String "<ProjectReference.*ClusterRunner" Hrot.SimHost/Hrot.SimHost.csproj   # → 0
Select-String "<ProjectReference.*ClusterRunner" Hrot.IG/Hrot.IG.csproj             # → 0
Select-String "<ProjectReference.*ClusterRunner" Hrot.CGF/Hrot.CGF.csproj           # → 0
```

(Based on prior exploration, SimHost and IG have no ClusterRunner reference. CGF likely doesn't either, but verify explicitly.)

Run isolated builds:
```powershell
dotnet build Hrot.SimHost/Hrot.SimHost.csproj --no-restore
dotnet build Hrot.IG/Hrot.IG.csproj --no-restore
dotnet build Hrot.CGF/Hrot.CGF.csproj --no-restore
```

If any `<ProjectReference>` to `Hrot.ClusterRunner` is found in those `.csproj` files: identify the type causing the dependency, either move it or find an alternative, and remove the reference.

This task is validation-only if no upward references are found. Document the result in the report.

---

### Task 5: DEBT-006 — Clean Up Dead Internal Fields on HrotNodeBuilder

**File:** `Hrot.Common/Infrastructure/HrotNodeBuilder.cs`

Remove the two internal fields that were added in BATCH-02 but are unused after the wrapper-type pattern adoption:
```csharp
// DELETE THESE:
internal bool     _replicationConfigured;
internal NodeRole _replicationRole;
```

Also remove the corresponding comment block. The `InternalsVisibleTo("Hrot.Network")` in `Hrot.Common.csproj` may remain (it's still valid for future extensions).

**Verify:** `dotnet build IOS-IG-SimHost.sln` still succeeds after removal.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests at each step:**

1. **Task 1 (DEBT-004):** Migrate EyesAndMuscleSubsystem → build passes, ClusterRunner tests green ✅
2. **Task 2 (S301):** Refactor SimHostApp → build passes, SimHost integration tests green ✅
3. **Task 3 (S302):** Refactor IgApplication → build passes, IG tests green ✅
4. **Task 4 (S402):** Boundary validation + isolated builds → document results ✅
5. **Task 5 (DEBT-006):** Remove dead fields → build passes ✅

Do NOT stop to ask for permission or confirmation for routine operations. If a test fails, fix the root cause immediately. Work autonomously until all tasks are complete.

---

## 🧪 Testing Requirements

**Zero new failures** allowed in:
- `Hrot.ClusterRunner.Tests/` (152 tests)
- `Hrot.SimHost.Tests/` (444 pass currently)
- `Hrot.SimHost.Integration.Tests/`
- `Hrot.IG.Tests/` (412 pass currently)
- `Hrot.ClusterRunner.Integration.Tests/` (CgfComponentRegistryTests — 4 tests)
- `Hrot.Map.Common.Tests/` (116 tests)

**New tests required:**
- S301 success condition 7: spawn via SimHostApp → entity appears on network (EntityMasterEgressTranslator fires)
- S301 success condition 8: AllInOne role → `DeadReckoningSyncSystem.DriveFromNetwork == false`
- S302 success condition 6: ghost entity in IG-backed world → both local and ghost `SimTransform` updated after tick

The driver commands:
```powershell
dotnet test IOS-IG-SimHost.sln  # full suite
# Or per project:
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj
dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj
```

---

## 📊 Report Requirements

Submit to `.dev/mod-init/reports/BATCH-03-REPORT.md`.

**Required sections:**

### 1. Status Summary
For each task (DEBT-004, S301, S302, S402, DEBT-006): ✅ Done / ⚠️ Partial / ❌ Failed.

### 2. Validation Outputs
Paste:
- Final `dotnet build IOS-IG-SimHost.sln` output (last 5 lines)
- Full test results summary for each project tested
- Output of the MODINIT-S402 boundary queries

### 3. Developer Insights

**Q1:** Was `IgApplication.InitializeEcs()` calling HrotNodeBuilder with `Headless = true` a problem for NedReplicationModule initialization? How did you handle it?

**Q2:** What did you find when searching `IgApplication.InitializeNetwork()` for translators to remove? Were there any edge cases — translators that are NOT inside EntityStatesIngressPack that needed to remain?

**Q3:** Were there any hidden callers of `NedReplicationModule` or the manual translator packs that weren't listed in the instructions?

**Q4:** What was the final result of MODINIT-S402 — did any application project (.csproj) reference `Hrot.ClusterRunner`?

**Q5:** Are all three application classes (`SimHostApp`, `IgApplication`, `CgfApplication`) now structurally capable of running standalone without `Hrot.ClusterRunner` in the build graph?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `EyesAndMuscleSubsystem` uses `.WithReplication(NodeRole.AllInOne).Build()` (DEBT-004)
- [ ] `SimHostApp._nedReplicationModule` field and `// TODO (P2 debt)` deleted (MODINIT-S301)
- [ ] `SimHostApp` uses `.WithReplication(_role).Build()` in builder chain (MODINIT-S301)
- [ ] `IgApplication` has no manual `new EntityMasterIngressTranslator` / `new GeoSpatialIngressTranslator` etc. (MODINIT-S302)
- [ ] `IgApplication` has no `RegisterGlobalSystem(new DeadReckoningSyncSystem())` (MODINIT-S302)
- [ ] `IgApplication` uses `.WithReplication(NodeRole.ImageGenerator).Build()` (MODINIT-S302)
- [ ] `Select-String "<ProjectReference.*ClusterRunner"` returns 0 for SimHost, IG, CGF `.csproj` (MODINIT-S402)
- [ ] Isolated builds of SimHost, IG, CGF succeed (`dotnet build ... --no-restore`) (MODINIT-S402)
- [ ] `HrotNodeBuilder._replicationConfigured` and `_replicationRole` fields removed (DEBT-006)
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds — 0 errors
- [ ] All pre-existing test results unchanged (no new failures)
- [ ] Report submitted to `.dev/mod-init/reports/BATCH-03-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

1. **`Using Hrot.Network.Infrastructure;` is required** in every file that calls `.WithReplication()`. Without it, the compiler resolves `.WithReplication()` as an extension method but can't find it.

2. **`IgApplication.InitializeEcs()` uses `Headless = true`** in HrotNodeBuilder. If you don't change this, `.WithReplication()` will create `NedReplicationModule` with `participant = null` even in production mode. The fix is to change `Headless = true` → `Headless = _headless`.

3. **Do NOT remove non-replication DDS infrastructure from `InitializeNetwork()`** — writers for `MapClickEvent`, `SelectionChangedEvent`, `ContextMenuRequest`, etc. are NOT part of NedReplicationModule and must remain.

4. **NedReplicationModule must be registered BEFORE `_context.Kernel.Initialize()`**. Check the initialization order carefully.

5. **Don't confuse `_context.EventBus` and `_context.World.Bus`**: `HrotNodeBuilderWithReplication.Build()` uses `context.World.Bus`. If the old code used `context.EventBus` in the manual module construction, refer to the CgfSubsystem comment which explains WHY `World.Bus` is correct.

6. **MODINIT-S402 is primarily a validation task** — if SimHost, IG, and CGF already have no project references to ClusterRunner, just document this and move on.

---

## 📚 Reference Materials

- **Design:** `.dev/mod-init/DESIGN.md` — §3 Stage 3, §4 Stage 4, "Key Decisions", "Success Criteria"
- **Task Definitions:** `.dev/mod-init/TASK-DETAIL.md` — MODINIT-S301, S302, S402
- **Previous Review:** `.dev/mod-init/reviews/BATCH-02-REVIEW.md`
- **Wrapper type source:** `Hrot.Network/Infrastructure/HrotNodeBuilderReplicationExtensions.cs`
- **CgfSubsystem reference implementation:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs` — use as pattern for how `.WithReplication().Build()` is used
- **EyesAndMuscleSubsystem current source:** `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`
