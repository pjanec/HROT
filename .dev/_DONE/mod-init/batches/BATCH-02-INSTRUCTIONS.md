# BATCH-02: Stage 2 — Relocate NedReplicationModule + Wire into Context + Update CgfSubsystem

**Batch Number:** BATCH-02
**Tasks:** MODINIT-S201, MODINIT-S202, MODINIT-S401
**Phase:** Stage 2 — Relocate NedReplicationModule + Stage 4 partial (CgfSubsystem)
**Estimated Effort:** 8–10 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 must be committed and green (✅ confirmed — Stage 1 all done)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes Stage 2 of the `mod-init` workstream by:
1. Moving `NedReplicationModule` from `Hrot.ClusterRunner.Replication` → `Hrot.Network.Replication`
2. Introducing `INedReplicationModule` interface in `Hrot.Common` and wiring it into `HrotNodeContext`
3. Creating `HrotNodeBuilderReplicationExtensions` in `Hrot.Network` — the OCP-compliant extension that makes `.WithReplication()` available without `Hrot.Common` referencing `Hrot.Network`
4. Updating `CgfSubsystem` to use the new module from its correct home (MODINIT-S401)

**Why MODINIT-S401 is included:** The extension `Build()` method introduced in S202 requires `.WithReplication()` to have been called (it raises `InvalidOperationException` at runtime otherwise). `CgfSubsystem` must call `.WithReplication(NodeRole.Brain)` to avoid test failures once S202 is live.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Design Document:** `.dev/mod-init/DESIGN.md` — Sections: "Architectural Constraint: Clean Architecture", §2.1 Physical Relocation, §2.2 Purge Application-Layer Directives, §2.3 Preserve Anti-Corruption Contract, §2.4 Wire NedReplicationModule into HrotNodeContext, §4.1 Update CgfSubsystem, "Key Decisions"
3. **Task Definitions:** `.dev/mod-init/TASK-DETAIL.md` — See MODINIT-S201, MODINIT-S202, MODINIT-S401
4. **Previous Review:** `.dev/mod-init/reviews/BATCH-01-REVIEW.md`
5. **DEBT-TRACKER:** `.dev/mod-init/DEBT-TRACKER.md` — See DEBT-002 (stale using directives in NedReplicationModule)

### Source Code Locations

- **File to move:** `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` → `Hrot.Network/Replication/NedReplicationModule.cs`
- **New interface:** `Hrot.Common/Abstractions/INedReplicationModule.cs` (new directory)
- **Modified:** `Hrot.Common/Infrastructure/HrotNodeContext.cs` — add `NedReplication` property
- **Modified:** `Hrot.Common/Infrastructure/HrotNodeBuilder.cs` — add internal state for extension + `InternalsVisibleTo`
- **New extension file:** `Hrot.Network/Infrastructure/HrotNodeBuilderReplicationExtensions.cs`
- **Modified:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs` — update namespace + use extension
- **Possibly modified:** `Hrot.CGF/CgfApplication.cs` — check for NedReplicationModule reference
- **Test project:** `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs` (namespace update)
- **Integration tests:** `Hrot.ClusterRunner.Integration.Tests/CgfComponentRegistryTests.cs` (may need update)

### Report Submission

**When done, submit your report to:**
`.dev/mod-init/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/mod-init/questions/BATCH-02-QUESTIONS.md`

---

## Context

After BATCH-01, `NedReplicationModule` still lives in `Hrot.ClusterRunner.Replication`. Its dependencies:
- `DeadReckoningSyncSystem` → ✅ now in `Hrot.Common.Systems`
- `SharedTranslatorPack`, `KinematicTranslatorPack` → ✅ now in `Hrot.Map.Common.Translators`
- `CognitiveTranslatorPack` → ✅ now in `Hrot.Network.Translators`
- `using Hrot.SimHost;` (line 15) and `using Hrot.SimHost.Network;` (line 16) → ✅ **STALE** — only appear in a code comment on line 216. They must be removed.

The module can now be moved cleanly to `Hrot.Network.Replication`.

---

## 🎯 Batch Objectives

- Move `NedReplicationModule` to `Hrot.Network.Replication` and remove stale using directives (resolves DEBT-002)
- Define `INedReplicationModule` interface in `Hrot.Common`; add nullable `NedReplication` property to `HrotNodeContext`
- Create `HrotNodeBuilderReplicationExtensions` with `WithReplication()` and an extension `Build()` that enforces the guard and populates `NedReplication`
- Update `CgfSubsystem` to use `.WithReplication(NodeRole.Brain)` via the extension; remove the manual `NedReplicationModule` field
- All existing tests remain green

---

## ✅ Tasks

### Task 1: MODINIT-S201 — Move NedReplicationModule to Hrot.Network

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s201--move-nedreplicationmodule-to-hrotmapcommon`

**Steps:**

1. **Remove stale using directives** (DEBT-002): Delete lines 15–16 from `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`:
   ```csharp
   using Hrot.SimHost;              // ← delete (stale)
   using Hrot.SimHost.Network;      // ← delete (stale)
   ```
   These only appear in one code comment (`// Brain role receives entities from Muscle/SimHost as ghosts`). Remove or rephrase the comment to not reference the stale namespace.

2. **Move the file:** `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` → `Hrot.Network/Replication/NedReplicationModule.cs`

3. **Update namespace:** `Hrot.ClusterRunner.Replication` → `Hrot.Network.Replication`

4. **Implement `INedReplicationModule`:** After the interface is created in Task 2, add `: INedReplicationModule` to the class declaration. (You may need to do Task 2 first, or add a forward-declaration stub.)

5. **Update all callers of the old namespace.** Find them:
   ```powershell
   Get-ChildItem -Recurse -Include "*.cs" | Select-String "Hrot.ClusterRunner.Replication.NedReplicationModule|using Hrot.ClusterRunner.Replication"
   ```
   Expected callers: `CgfSubsystem.cs`, `NedReplicationModuleTests.cs`, and possibly `CgfApplication.cs`.

   For each:
   - Change `using Hrot.ClusterRunner.Replication;` → `using Hrot.Network.Replication;`

   **Note:** `CgfSubsystem.cs` will undergo deeper rework in Task 3 (MODINIT-S401). For now, just update the `using` directive.

6. **Verify `Hrot.Network.csproj`** still has no reference to `Hrot.SimHost` or `Hrot.IG`.

**Verify:**
```powershell
dotnet build IOS-IG-SimHost.sln
Get-ChildItem -Recurse -Include "*.cs" | Select-String "Hrot.ClusterRunner.Replication"  # → 0 matches
Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Network/Hrot.Network.csproj    # → 0 matches
```

---

### Task 2: MODINIT-S202 — Wire NedReplicationModule into HrotNodeContext

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s202--wire-nedreplicationmodule-into-hrotnodecontext-mandatory`

This task has 4 sub-steps:

#### 2a. Define INedReplicationModule interface

**File:** `Hrot.Common/Abstractions/INedReplicationModule.cs` (NEW — create the `Abstractions/` subdirectory)

```csharp
using ModuleHost.Core.Abstractions;

namespace Hrot.Common.Abstractions;

/// <summary>
/// Minimal abstraction over the NED replication module.
/// Defined in Hrot.Common so that HrotNodeContext can hold a typed reference without
/// Hrot.Common needing to reference Hrot.Network (which would create a cycle).
/// </summary>
public interface INedReplicationModule : IEcsModule
{
    // IEcsModule provides: string Name, ExecutionPolicy Policy,
    //                      RegisterSystems(ISystemRegistry), Tick(ISimulationView, float)
    // Extend this interface when SubsystemOrchestrator hot-swap logic demands more surface.
}
```

**Constraint:** This file MUST NOT reference any `Hrot.Network` types (must compile within `Hrot.Common` in isolation).

#### 2b. Update HrotNodeContext

**File:** `Hrot.Common/Infrastructure/HrotNodeContext.cs`

Add the `NedReplication` property (nullable — made non-null by the extension Build()):

```csharp
/// <summary>
/// The NED replication module bundling translator packs and their lifecycle systems.
/// Set by <c>HrotNodeBuilderReplicationExtensions.Build()</c>.
/// <c>null</c> only in legacy call sites that have not yet migrated to the extension Build().
/// </summary>
public INedReplicationModule? NedReplication { get; init; }
```

Add `using Hrot.Common.Abstractions;` to the file's using directives.

Also: Change the existing `GhostCreationSystem?` property type from `GhostCreationSystem?` to remain as-is (no change needed — the extension `Build()` will overwrite it using `with`).

#### 2c. Update HrotNodeBuilder (internal state for extension)

**File:** `Hrot.Common/Infrastructure/HrotNodeBuilder.cs`

Add internal fields and a setter method so the extension class can store replication config:

```csharp
// ── Replication extension state (set by HrotNodeBuilderReplicationExtensions.WithReplication) ──
internal bool     _replicationConfigured;
internal NodeRole _replicationRole;
```

Also add `InternalsVisibleTo("Hrot.Network")` to `Hrot.Common/Hrot.Common.csproj` so the extension class in `Hrot.Network` can access these internal fields:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
    <_Parameter1>Hrot.Network</_Parameter1>
</AssemblyAttribute>
```

#### 2d. Create HrotNodeBuilderReplicationExtensions

**File:** `Hrot.Network/Infrastructure/HrotNodeBuilderReplicationExtensions.cs` (NEW)

```csharp
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Network.Replication;
using Fdp.Modules.Geographic;

namespace Hrot.Network.Infrastructure;

/// <summary>
/// OCP-compliant extension that adds .WithReplication() and the replication-aware Build()
/// to HrotNodeBuilder without requiring Hrot.Common to reference Hrot.Network.
/// </summary>
public static class HrotNodeBuilderReplicationExtensions
{
    /// <summary>
    /// Configures NedReplicationModule for the given node role.
    /// Must be called before <see cref="Build"/> when using this extension.
    /// </summary>
    public static HrotNodeBuilder WithReplication(this HrotNodeBuilder builder, NodeRole role)
    {
        builder._replicationConfigured = true;
        builder._replicationRole       = role;
        return builder;
    }

    /// <summary>
    /// Builds the node context AND constructs NedReplicationModule, returning an HrotNodeContext
    /// where <see cref="HrotNodeContext.NedReplication"/> is non-null.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="WithReplication"/> was not called before this method.
    /// </exception>
    public static HrotNodeContext Build(this HrotNodeBuilder builder)
    {
        if (!builder._replicationConfigured)
            throw new InvalidOperationException(
                "HrotNodeBuilderReplicationExtensions.Build() requires .WithReplication(role) " +
                "to have been called first. Add .WithReplication(NodeRole.X) to your builder chain " +
                "and add 'using Hrot.Network.Infrastructure;' to the calling file.");

        // Call the NATIVE HrotNodeBuilder.Build() — instance method takes precedence,
        // so this resolves to HrotNodeBuilder.Build(), not this extension.
        var context = builder.Build();

        var ned = new NedReplicationModule(
            participant:  context.Participant,
            role:         builder._replicationRole,
            entityMap:    context.EntityMap,
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     context.World.Bus,
            localNodeId:  context.ClusterSlave is { } cs ? GetNodeId(cs) : 0,
            domainId:     0);  // domainId not stored on context; pass 0 (unused in current impl)

        return context with
        {
            NedReplication      = ned,
            GhostCreationSystem = ned.GhostCreationSystem,
        };
    }

    private static int GetNodeId(Hrot.Common.Orchestration.ClusterSlave cs)
    {
        // ClusterSlave.NodeId is accessible; if not exposed, use 0 fallback.
        // Check the actual ClusterSlave API before writing this.
        return cs.NodeId;
    }
}
```

**CRITICAL DESIGN NOTES for the developer:**
- `builder.Build()` inside the extension body calls the **NATIVE** `HrotNodeBuilder.Build()` instance method, NOT this extension again. In C#, when an extension method exists alongside an instance method with the same name, the instance method wins when calling on an object instance from within any scope.
- If `ClusterSlave.NodeId` is not a public property, check `Hrot.Common/Orchestration/ClusterSlave.cs` and expose it as `internal int NodeId` (add `InternalsVisibleTo("Hrot.Network")` already done in 2c).
- `HrotEnvironment.CreateGeoTransform()` is already used in `CgfSubsystem.cs` and returns the correct geographic transform.
- The `World.Bus` pattern for `eventBus` matches what `CgfSubsystem.cs` uses: "Use world.Bus so that events published by EntityMasterIngressTranslator.ProcessDispose() during the Input kernel phase are made visible to GhostDestructionSystem".
- `domainId` is passed as `0` because it's not stored in `HrotNodeContext`. Verify in `NedReplicationModule.cs` that `domainId` is listed as "reserved for future use" and `0` is safe.

**Tests required for S202:**

1. **API surface test:** `new HrotNodeBuilder(config).WithRole(...).WithReplication(NodeRole.AllInOne).Build()` (using `using Hrot.Network.Infrastructure;`) compiles and returns a non-null `NedReplication`.
2. **Guard test:** `new HrotNodeBuilder(config).WithRole(...).Build()` (extension `Build()`, without `.WithReplication()`) throws `InvalidOperationException`.
3. **Role contract test:** `.WithReplication(NodeRole.AllInOne)` → `context.NedReplication.DriveFromNetwork == false` (ghost-only).

Place these tests in `Hrot.Network.Tests/` if it exists, or create that project as a minimal xUnit test assembly under the solution. If creating a new test project is too much overhead, add to `Hrot.ClusterRunner.Tests/` (which already has access to `Hrot.Network` via `Hrot.ClusterRunner`).

---

### Task 3: MODINIT-S401 — Update CgfSubsystem to Use .WithReplication()

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s401--update-cgfsubsystem-to-reference-hrotcommon`

**File:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs`

Changes:
1. Remove `using Hrot.ClusterRunner.Replication;` (now done as part of S201 — just the namespace change)
2. Add `using Hrot.Network.Infrastructure;` (to access the extension `Build()` and `WithReplication()`)
3. Update the builder chain:
   ```csharp
   // BEFORE:
   _context = new HrotNodeBuilder(nodeConfig)
       .WithRole("CgfNode", NodeRole.Brain)
       .Build();
   // ... later:
   _nedReplicationModule = new NedReplicationModule(participant: _context.Participant, role: NodeRole.Brain, ...);
   _context.Kernel.RegisterModule(_nedReplicationModule);

   // AFTER:
   _context = new HrotNodeBuilder(nodeConfig)
       .WithRole("CgfNode", NodeRole.Brain)
       .WithReplication(NodeRole.Brain)    // NEW
       .Build();                            // calls extension Build()
   // ... later:
   _context.Kernel.RegisterModule(_context.NedReplication!);
   // No manual NedReplicationModule instantiation
   ```
4. **Delete** the `private IEcsModule? _nedReplicationModule;` field entirely.
5. In `Shutdown()`: Remove `_nedReplicationModule = null;` (field is gone).
6. Verify that `CgfSubsystem` no longer has any direct `NedReplicationModule` type reference.

**Also check `Hrot.CGF/CgfApplication.cs`:** Search for `NedReplicationModule` or `Hrot.ClusterRunner.Replication`. If found, update to `Hrot.Network.Replication`. (Based on the design, CgfApplication may or may not directly reference it — verify.)

```powershell
Select-String "NedReplicationModule|ClusterRunner.Replication" Hrot.CGF/CgfApplication.cs
```

**Verify:**
```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build
# CgfComponentRegistryTests (4 tests) must remain green
Select-String "_nedReplicationModule" Hrot.ClusterRunner/Services/CgfSubsystem.cs   # → 0 matches
Select-String "NedReplicationModule" Hrot.ClusterRunner/Services/CgfSubsystem.cs    # references context only
```

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests at each step:**

1. **Task 1 (S201):** Move module + remove stale usings → `dotnet build IOS-IG-SimHost.sln` passes ✅
2. **Task 2 (S202):** Interface + context + builder extension → new tests pass ✅
3. **Task 3 (S401):** Update CgfSubsystem → all ClusterRunner tests pass ✅

For Task 1, commit/stage temporarily broken state is acceptable between steps, but before moving to Task 2 the build must be green.

Do NOT stop to ask for permission for obvious operations (fixing compilation errors, running tests, adding missing using directives). Work autonomously until all tasks are done.

---

## 🧪 Testing Requirements

- **Zero new failures** in pre-existing test projects (`Hrot.ClusterRunner.Tests`, `Hrot.Map.Common.Tests`, `Hrot.SimHost.Tests`, `Hrot.IG.Tests`)
- **New tests for S202:** (minimum 3)
  - Builder with replication returns non-null `NedReplication`
  - Builder without `.WithReplication()` throws `InvalidOperationException` (extension `Build()`)
  - `AllInOne` role → `DriveFromNetwork == false`
- **Existing tests for S201:** `NedReplicationModuleTests.cs` — update namespace; all existing tests continue to pass
- **S401 verification:** `CgfComponentRegistryTests.cs` (4 tests in integration suite) must remain green

```powershell
dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --filter "CgfComponent"
```

---

## 📊 Report Requirements

Submit to `.dev/mod-init/reports/BATCH-02-REPORT.md`.

**Required sections:**

### 1. Status Summary
For each task (S201, S202, S401): ✅ Done / ⚠️ Partial / ❌ Failed with notes.

### 2. Validation Outputs
Paste:
- Final `dotnet build IOS-IG-SimHost.sln` output (last 10 lines)
- `dotnet test Hrot.ClusterRunner.Tests/` result
- `dotnet test Hrot.ClusterRunner.Integration.Tests/` result

### 3. Developer Insights

**Q1:** What issues did you encounter when calling the native `Build()` from inside the extension? Did you hit any recursive call issues? How was it resolved?

**Q2:** Did you need to expose any additional `internal` members on `ClusterSlave` or `HrotNodeBuilder` for the extension class? What were they?

**Q3:** Did `Hrot.CGF/CgfApplication.cs` need any changes? What did you find?

**Q4:** What risks or complications do you foresee for Stage 3 (SimHostApp and IgApplication migration)?

**Q5:** Any weak points spotted in the existing codebase that this batch's changes exposed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `Hrot.Network/Replication/NedReplicationModule.cs` exists; `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` deleted (S201)
- [ ] No occurrences of `using Hrot.ClusterRunner.Replication;` in any .cs file (S201)
- [ ] No occurrences of `using Hrot.SimHost;` or `using Hrot.SimHost.Network;` in `NedReplicationModule.cs` (DEBT-002)
- [ ] `Hrot.Common/Abstractions/INedReplicationModule.cs` exists with `: IEcsModule` (S202)
- [ ] `HrotNodeContext.NedReplication` property exists (nullable `INedReplicationModule?`) (S202)
- [ ] `HrotNodeBuilderReplicationExtensions.cs` exists in `Hrot.Network/Infrastructure/` (S202)
- [ ] Guard test: calling extension `Build()` without `.WithReplication()` throws `InvalidOperationException` (S202)
- [ ] `CgfSubsystem._nedReplicationModule` field deleted; builder uses `.WithReplication(NodeRole.Brain)` (S401)
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds — 0 errors
- [ ] All pre-existing test suite results unchanged (no new failures)
- [ ] Report submitted to `.dev/mod-init/reports/BATCH-02-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

1. **Recursive extension `Build()` call:** The extension `Build(this HrotNodeBuilder builder)` calls `builder.Build()`. In C#, this resolves to the **native instance method** `HrotNodeBuilder.Build()` because instance methods take precedence over extension methods. If you call `builder.Build()` and it appears to loop, you likely have a scope issue — check your `using` imports at the top of the extension file.

2. **Do NOT make `NedReplication` a `required` property on `HrotNodeContext`** — this would break every place that creates `HrotNodeContext` via `new` (the native `Build()` would fail to compile). Use a nullable property (`INedReplicationModule?`) for now.

3. **InternalsVisibleTo must be in the `.csproj` `<AssemblyAttribute>` format**, not C# attribute syntax. See the existing `InternalsVisibleTo` entries in `Hrot.Common/Hrot.Common.csproj` for the correct format.

4. **CgfSubsystem's `Shutdown()` still needs to null `_context`** — just remove only the `_nedReplicationModule = null;` line, keep `_context = null;`.

5. **Do not change SimHostApp or IgApplication** — those are Stage 3 (BATCH-03). The extension `Build()` guard only fires when the caller uses `using Hrot.Network.Infrastructure;`. SimHostApp and IgApplication still call the NATIVE `Build()` (no `using Hrot.Network.Infrastructure;` in scope), so they are not affected.

---

## 📚 Reference Materials

- **Design:** `.dev/mod-init/DESIGN.md` — §2 Stage 2, §2.4, §4.1
- **Task Definitions:** `.dev/mod-init/TASK-DETAIL.md` — MODINIT-S201, S202, S401
- **Previous Review:** `.dev/mod-init/reviews/BATCH-01-REVIEW.md`
- **Debt Tracker:** `.dev/mod-init/DEBT-TRACKER.md` — DEBT-002 resolved by S201
- **HrotNodeBuilder current source:** `Hrot.Common/Infrastructure/HrotNodeBuilder.cs`
- **HrotNodeContext current source:** `Hrot.Common/Infrastructure/HrotNodeContext.cs`
- **CgfSubsystem current source:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs`
- **NedReplicationModuleTests current source:** `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs`
