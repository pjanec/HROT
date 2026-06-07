# BATCH-14 Instructions — JM-P2-009: Bootstrap Wiring (GATE)

**Batch:** BATCH-14
**Task:** JM-P2-009 — Bootstrap wiring (role-driven NodeBootstrapper + editor + CLI)
**Priority:** HIGH — this is a GATE task. No further tasks begin until this is done.

---

## Context

Read before starting:
- `.dev/json-migration/TASK-DETAILS.md` section `JM-P2-009`
- `AGENTS.md` editing invariants (mandatory)
- The design role-matrix at `.dev/json-migration/Migration-system.md` §8.3

**Goal:** Wire `MigrationServices` into every host composition root so each process registers only the document types it actually loads. This is the M-2 (per-host scoped registration) enforcement gate.

---

## Architecture Decisions

**Key constraint:** `Hrot.IG` does NOT reference `Hrot.SimHost`. Therefore `IgNodeBootstrapper`
cannot call `NodeBootstrapper.RegisterMigrationServices`. Instead:

1. Create a new `HrotMigrationBootstrap` static class in `Hrot.Common.Scenario.Migrations`
   (same namespace/location as `PassthroughFormatsModule`). This provides profile-specific
   factory methods for each host role.

2. `NodeBootstrapper.RegisterMigrationServices(NodeRole role)` is a thin wrapper around
   `HrotMigrationBootstrap` for the SimHost/CGF roles.

3. `IgNodeBootstrapper` calls `HrotMigrationBootstrap.BuildIg()` directly in its `BuildOrchestration`.

4. `EditorBootstrap.CreateFileService()` calls `HrotMigrationBootstrap.BuildEditor()` and
   passes the result to `ScenarioFileService(serializer, migrationServices: migrations)`.

5. `ClusterRunner Program.cs` gets `--mode migrate` support.

---

## Files to Create

### 1. `BehaviorTreeMigrationModule.cs` — SKELETON (NEW)

**Path:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/BehaviorTreeMigrationModule.cs`

Follow the exact pattern of `BlueprintMigrationModule.cs` (passthrough at v1). Doc type:
`HrotDocumentTypes.BehaviorTree`. Note: `HrotDocumentTypes.BehaviorTree` constant must exist
in `Hrot.Common.Scenario.HrotDocumentTypes`; if it does not, add it as `"Hrot.BehaviorTree"`.

```csharp
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Skeleton migration module for the HROT BehaviorTree format.
    /// Currently at version 1 with no migration chain. A migration chain will be
    /// added in a later phase when the BehaviorTree format is bumped.
    /// <para>Registered doc type: <see cref="HrotDocumentTypes.BehaviorTree"/> — version 1.</para>
    /// </summary>
    public static class BehaviorTreeMigrationModule
    {
        public const int CurrentVersion = 1;

        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null) throw new System.ArgumentNullException(nameof(registry));
            registry.RegisterPassthroughDocType(HrotDocumentTypes.BehaviorTree, CurrentVersion);
        }
    }
}
```

### 2. `HrotMigrationBootstrap.cs` — NEW

**Path:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/HrotMigrationBootstrap.cs`

```csharp
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Role-specific factory for <see cref="MigrationServices"/>.
    /// Each host process (SimHost, CGF, IG, Editor, ClusterRunner) calls the
    /// appropriate method once during startup.
    /// <para>Enforces M-2: each host registers only the formats it actually loads.</para>
    /// </summary>
    public static class HrotMigrationBootstrap
    {
        /// <summary>
        /// Creates <see cref="MigrationServices"/> for a SimHost or CGF node.
        /// Registers: Scenario, TKB, RoadNetwork (all read-only) + OrchestratorContext passthrough.
        /// </summary>
        public static MigrationServices BuildSimHostCgf(string writerIdentifier = "Hrot.SimHost")
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2);
            }, writerIdentifier);
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for an IG (Image Generator) node.
        /// Registers: Scenario, TKB (read-only) + OrchestratorContext + MapInteractionConfig passthroughs.
        /// Blueprint and BehaviorTree are intentionally NOT registered (M-2).
        /// </summary>
        public static MigrationServices BuildIg()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.MapInteractionConfig, 1);
            }, "Hrot.IG");
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for the Editor host.
        /// Registers all customer-facing formats (both adapters) plus all HROT passthrough formats.
        /// </summary>
        public static MigrationServices BuildEditor()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                BlueprintMigrationModule.RegisterAll(reg);
                BehaviorTreeMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                PassthroughFormatsModule.RegisterAll(reg);
            }, "Hrot.Editor");
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for <c>Hrot.ClusterRunner --mode migrate</c>.
        /// Same profile as Editor (persistent adapter), different writer identifier.
        /// </summary>
        public static MigrationServices BuildClusterRunnerMigrate()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                BlueprintMigrationModule.RegisterAll(reg);
                BehaviorTreeMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                PassthroughFormatsModule.RegisterAll(reg);
            }, "Hrot.ClusterRunner --mode migrate");
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for <c>Hrot.ClusterRunner --mode ci</c>.
        /// Same as SimHost plus TestScript and NodeConfiguration passthroughs.
        /// </summary>
        public static MigrationServices BuildClusterRunnerCi()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.TestScript, 1);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.NodeConfiguration, 1);
            }, "Hrot.ClusterRunner --mode ci");
        }
    }
}
```

**Note:** Check whether `HrotDocumentTypes.TestScript` and `HrotDocumentTypes.BehaviorTree`
constants exist. If missing, add them to `HrotDocumentTypes` as `"Hrot.TestScript"` and
`"Hrot.BehaviorTree"` respectively. `HrotDocumentTypes` lives in
`Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs`.

---

## Files to Modify

### 3. `NodeBootstrapper.cs` — ADD METHOD + PROPERTY

**Path:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

Add `using Hrot.Common.Scenario.Migrations;` and
`using Fdp.Core.Serialization.Migrations;` to usings (if not already present).

Add a public property and method to `NodeBootstrapper`:

```csharp
/// <summary>
/// After <see cref="RegisterMigrationServices"/> is called, exposes the
/// constructed <see cref="MigrationServices"/> bundle.
/// </summary>
public MigrationServices? MigrationServices { get; private set; }

/// <summary>
/// Constructs and stores <see cref="MigrationServices"/> for the given node role.
/// <para>
/// Roles:
/// <list type="bullet">
///   <item>Brain / MuscleGround → SimHost/CGF profile</item>
///   <item>ImageGenerator → IG profile</item>
/// </list>
/// </para>
/// </summary>
public MigrationServices RegisterMigrationServices(NodeRole role,
    string? writerIdentifier = null)
{
    MigrationServices ms;

    if (role.HasFlag(NodeRole.ImageGenerator))
        ms = HrotMigrationBootstrap.BuildIg();
    else
        ms = HrotMigrationBootstrap.BuildSimHostCgf(
            writerIdentifier ?? "Hrot.SimHost");

    MigrationServices = ms;
    return ms;
}
```

### 4. `SimHostNodeBootstrapper.cs` — WIRE MIGRATION SERVICES

**Path:** `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs`

Add to the class:
```csharp
/// <summary>Migration services bundle. Valid after BootstrapNode() returns.</summary>
public MigrationServices? MigrationServices { get; private set; }
```

In `BuildOrchestration(...)`, after `_nodeBootstrapper = new NodeBootstrapper(_networkFactory);`
and before the `slave = _nodeBootstrapper.BuildOrchestration(...)` call, add:
```csharp
MigrationServices = _nodeBootstrapper.RegisterMigrationServices(
    _role,
    writerIdentifier: _role.HasFlag(NodeRole.Brain) ? "Hrot.CGF" : "Hrot.SimHost");
```

### 5. `IgNodeBootstrapper.cs` — WIRE MIGRATION SERVICES

**Path:** `Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs`

Add `using Hrot.Common.Scenario.Migrations;` and `using Fdp.Core.Serialization.Migrations;`.

Add to the class:
```csharp
/// <summary>Migration services bundle. Valid after BootstrapNode() returns.</summary>
public MigrationServices? MigrationServices { get; private set; }
```

In `BuildOrchestration(...)`, near the top of the method body (before/after `OrchestrationBus = orchestrationBus`), add:
```csharp
MigrationServices = HrotMigrationBootstrap.BuildIg();
```

### 6. `EditorBootstrap.cs` — WIRE MIGRATION SERVICES

**Path:** `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs`

Add `using Hrot.Common.Scenario.Migrations;` and `using Fdp.Core.Serialization.Migrations;`.

Update `CreateFileService()`:
```csharp
public static ScenarioFileService CreateFileService()
{
    var behaviorRegistry = new BehaviorRegistry();
    var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);
    var migrations = HrotMigrationBootstrap.BuildEditor();
    return new ScenarioFileService(serializer, migrationServices: migrations);
}
```

**Also** expose a `CreateMigrationServices()` static method for convenience:
```csharp
/// <summary>Creates the full Editor MigrationServices bundle.</summary>
public static MigrationServices CreateMigrationServices() =>
    HrotMigrationBootstrap.BuildEditor();
```

### 7. `HrotRunnerConfiguration.cs` — ADD "migrate" MODE

**Path:** `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`

In `Validate()`, add `"migrate"` to the `validNames` set:
```csharp
var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "simhost", "ig", "excon", "orchestrator", "cgf", "ci", "editor",
      "stridemock", "replaybrowser", "migrate" };
```

Update the `HelpText` on the `--mode` option to include `migrate`:
```csharp
[Option('m', "mode", Required = true,
    HelpText = "all|simhost|ig|ios|orchestrator|cgf|ci|editor|migrate|stridemock|replaybrowser or comma-separated combination")]
```

### 8. `Program.cs` — ADD --mode migrate HANDLER

**Path:** `Hrot/Runner/Hrot.ClusterRunner/Program.cs`

After the existing `// ── CI mode: ...` block (around line 130), add a `--mode migrate` handler:

```csharp
// ── Migrate mode: run $meta envelope migration on all known JSON files ──────────
if (config.RequestedSubsystems.Contains("migrate"))
{
    Console.WriteLine("[Runner] Migrate mode – constructing migration services...");
    var migrationServices = HrotMigrationBootstrap.BuildClusterRunnerMigrate();
    Console.WriteLine("[Runner] Migrate mode – MigrationServices constructed. " +
        $"Registered types: {string.Join(", ", migrationServices.Registry.RegisteredDocTypes)}");
    // TODO(JM-P4): enumerate input directory and run PersistentMigrationAdapter.LoadAndMigrateAsync on each file.
    Console.WriteLine("[Runner] Migrate mode – stub complete (full file migration wired in Phase 4).");
    return 0;
}
```

Add `using Hrot.Common.Scenario.Migrations;` to `Program.cs` usings if not already present.

---

## Tests

### 9. `NodeBootstrapperMigrationTests.cs` — CREATE

**Path:** `Hrot/Subsystems/Hrot.SimHost.Tests/NodeBootstrapperMigrationTests.cs`

```csharp
using System.Linq;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization;
using Hrot.Common;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Hrot.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>Tests for <see cref="NodeBootstrapper.RegisterMigrationServices"/>
/// and <see cref="HrotMigrationBootstrap"/> role profiles (M-2 enforcement).</summary>
public class NodeBootstrapperMigrationTests
{
    // ── T01: SimHost (MuscleGround) profile ────────────────────────────────

    [Fact]
    public void RegisterMigrationServices_MuscleGroundRole_RegistersScenarioTkbRoadNetworkOrchestratorContext()
    {
        var sut = new NodeBootstrapper();
        var ms = sut.RegisterMigrationServices(NodeRole.MuscleGround);

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,          types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,     types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,         types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext, types);
    }

    [Fact]
    public void RegisterMigrationServices_MuscleGroundRole_DoesNotRegisterBlueprintOrMapInteractionConfig()
    {
        var sut = new NodeBootstrapper();
        var ms = sut.RegisterMigrationServices(NodeRole.MuscleGround);

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint,          types);
        Assert.DoesNotContain(HrotDocumentTypes.MapInteractionConfig, types);
    }

    // ── T02: CGF (Brain) profile ────────────────────────────────────────────

    [Fact]
    public void RegisterMigrationServices_BrainRole_RegistersSameAsSimHost()
    {
        var sut = new NodeBootstrapper();
        var ms = sut.RegisterMigrationServices(NodeRole.Brain);

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,          types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,     types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,         types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext, types);
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint, types);
    }

    // ── T03: IG profile ─────────────────────────────────────────────────────

    [Fact]
    public void BuildIg_RegistersScenarioTkbOrchestratorContextMapInteractionConfig()
    {
        var ms = HrotMigrationBootstrap.BuildIg();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,            types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,       types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext,  types);
        Assert.Contains(HrotDocumentTypes.MapInteractionConfig, types);
    }

    [Fact]
    public void BuildIg_DoesNotRegisterBlueprintOrRoadNetwork()
    {
        var ms = HrotMigrationBootstrap.BuildIg();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint,  types);
        Assert.DoesNotContain(FdpDocumentTypes.RoadNetwork, types);
    }

    // ── T04: M-2 fail-loud — IG pipeline rejects Blueprint docType ──────────

    [Fact]
    public void BuildIg_Pipeline_ThrowsMigrationException_ForBlueprintDocType()
    {
        var ms = HrotMigrationBootstrap.BuildIg();

        var dom = System.Text.Json.Nodes.JsonNode.Parse(
            @"{""$meta"":{""docType"":""Hrot.Blueprints"",""schemaVersion"":1},""data"":""""}")!
            .AsObject();

        var ex = Assert.Throws<MigrationException>(
            () => ms.Pipeline.MigrateToCurrent(dom));

        Assert.Contains("Hrot.Blueprints", ex.Message);
    }

    // ── T05: Editor profile ─────────────────────────────────────────────────

    [Fact]
    public void BuildEditor_RegistersAllCustomerFacingAndPassthroughFormats()
    {
        var ms = HrotMigrationBootstrap.BuildEditor();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,      types);
        Assert.Contains(HrotDocumentTypes.Blueprint,     types);
        Assert.Contains(HrotDocumentTypes.BehaviorTree,  types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition, types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,     types);
        // All passthrough formats
        Assert.Contains(HrotDocumentTypes.OrchestratorContext,   types);
        Assert.Contains(HrotDocumentTypes.MapInteractionConfig,  types);
        Assert.Contains(HrotDocumentTypes.StructEdit,            types);
        Assert.Contains(HrotDocumentTypes.TestScript,            types);
        Assert.Contains(HrotDocumentTypes.NodeConfiguration,     types);
    }

    // ── T06: ClusterRunner CI profile ───────────────────────────────────────

    [Fact]
    public void BuildClusterRunnerCi_RegistersSimHostPlusTestScriptAndNodeConfig()
    {
        var ms = HrotMigrationBootstrap.BuildClusterRunnerCi();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,          types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,     types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,         types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext, types);
        Assert.Contains(HrotDocumentTypes.TestScript,         types);
        Assert.Contains(HrotDocumentTypes.NodeConfiguration,  types);
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint, types);
    }

    // ── T07: MigrationServices property set on NodeBootstrapper ────────────

    [Fact]
    public void RegisterMigrationServices_SetsPropertyOnBootstrapper()
    {
        var sut = new NodeBootstrapper();
        Assert.Null(sut.MigrationServices);

        var ms = sut.RegisterMigrationServices(NodeRole.MuscleGround);

        Assert.NotNull(sut.MigrationServices);
        Assert.Same(ms, sut.MigrationServices);
    }
}
```

---

## Build and Test Commands

```powershell
# Build
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5

# Run new migration tests
dotnet test "Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj" -c Debug --no-build --filter "NodeBootstrapperMigration" 2>&1 | Select-Object -Last 5

# Run full SimHost.Tests suite (no regressions)
dotnet test "Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 5

# Run IG.Tests (no regressions from IgNodeBootstrapper change)
dotnet test "Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj" -c Debug --no-build --filter "IgNodeBootstrapper" 2>&1 | Select-Object -Last 5
```

---

## CRITICAL Notes

1. **`HrotDocumentTypes` check**: Verify `BehaviorTree` and `TestScript` constants exist in
   `Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs`. If missing, add them.
   Check `OrchestratorContext` is version 2 in `PassthroughFormatsModule` (it should be — C-4).

2. **`FdpDocumentTypes.RoadNetwork`**: This is `"Fdp.RoadNetwork"` in `Fdp.Core.Serialization.FdpDocumentTypes`.
   It's registered via `RoadNetworkMigrationModule.RegisterAll(reg)`, not directly as a passthrough.

3. **Project references**: `Hrot.Common` references `Fdp.Core` — so `MigrationBootstrap.BuildForProduction`
   is accessible from `HrotMigrationBootstrap`. Verify the Hrot.Common.csproj has the reference.

4. **Do NOT break existing tests**: SimHostNodeBootstrapper changes must not change constructor
   signatures. `RegisterMigrationServices` is called inside `BuildOrchestration` with no external
   caller change needed.

5. **IgNodeBootstrapper is `internal sealed`**: The `MigrationServices` property should be
   `public` so `IgApplication.cs` can access it if needed. If `IgNodeBootstrapper` is `internal`,
   the property can be `internal` too — but `public` is safer.

6. **ClusterRunner `--mode migrate` stub**: The TODO comment is intentional. The actual file
   migration logic is out of scope for JM-P2-009. The key deliverable is the `MigrationServices`
   creation path being wired.

7. **`HrotDocumentTypes.OrchestratorContext` version 2**: When registering inline
   (`reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2)`) use version **2**,
   not 1. C-4 established this. `PassthroughFormatsModule` also uses version 2.

---

## Report

Write the report to `.dev/json-migration/reports/BATCH-14-REPORT.md`.
Include: all files changed, test results (pass/fail counts), any deviations.
