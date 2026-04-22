# BATCH-13: OfflineNetworkFactory + CLI RunMode Refactoring

**Batch Number:** BATCH-13
**Tasks:** TASK-P4-005, TASK-P5-001
**Phase:** Phase 4 & 5 — Subsystem Decoupling + Composition Root
**Estimated Effort:** 2-3 hours
**Priority:** HIGH
**Dependencies:** BATCH-12 complete

---

## Onboarding & Workflow

### Developer Instructions

This batch covers two independent tasks:

1. **TASK-P4-005**: Create `OfflineNetworkFactory` in `Hrot.Editor` so the editor
   boots without any DDS participant, and wire it into `EditorSubsystem`.

2. **TASK-P5-001**: Delete the `RunMode` flags enum from `Hrot.ClusterRunner` and
   replace mode handling with a simple `HashSet<string>` of requested subsystem names.

Both tasks can be worked in any order.

### Required Reading (in order)

1. **Task Definitions:**
   - `.dev/modular-2/TASK-DETAIL.md#task-p4-005`
   - `.dev/modular-2/TASK-DETAIL.md#task-p5-001`
2. **Previous report:** `.dev/modular-2/reports/BATCH-12-REPORT.md`
3. **Pattern reference:** `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` — all null stubs exist here; reuse or adapt
4. **Current INetworkFactory:** `Hrot.Core/Network/INetworkFactory.cs` — 9 methods to implement
5. **Current CLI wiring:** `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` and `RunMode.cs`
6. **Program.cs:** `Hrot.ClusterRunner/Program.cs` — understand all RunMode.HasFlag usages

### Source Code Areas

- **P4-005:** `Hrot.Editor/`, `Hrot.Editor/EditorSubsystem.cs`, `Hrot.Core/Network/`
- **P5-001:** `Hrot.ClusterRunner/Configuration/`, `Hrot.ClusterRunner/Program.cs`,
  `Hrot.ClusterRunner.Tests/`

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-13-REPORT.md`

---

## TASK-P4-005: OfflineNetworkFactory for Hrot.Editor

### Context

`EditorSubsystem.Initialize()` boots an editor session that has no network connectivity.
Currently it references NED indirectly (through the subsystem chain). The goal is:
- Create `OfflineNetworkFactory` in `Hrot.Editor` that returns no-op stubs for all
  `INetworkFactory` methods
- Inject it into the `EditorSubsystem` init chain
- Confirm `Hrot.Editor.csproj` has zero DIRECT references to `Hrot.Network.NED` / `Hrot.Network.BDC`

**Constraint reminder:** Transitive references (through Hrot.SimHost, Hrot.CGF, etc.) are acceptable;
only direct project references are disallowed.

---

#### Step 1: Check EditorSubsystem.Initialize()

Read `Hrot.Editor/EditorSubsystem.cs` (moved here in BATCH-12).
Identify where to inject the factory. Look for how `SimHostSubsystem` or `IgSubsystem`
accept an `INetworkFactory` as an indication of the expected pattern.

---

#### Step 2: Create OfflineNetworkFactory

**File:** `Hrot.Editor/OfflineNetworkFactory.cs` (NEW FILE)

```csharp
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using ModuleHost.Core;

namespace Hrot.Editor;

/// <summary>
/// No-op INetworkFactory for the offline editor mode.
/// Returns null-stub implementations for all network services; no DDS is allocated.
/// </summary>
public sealed class OfflineNetworkFactory : INetworkFactory
{
    /// <inheritdoc/>
    public IReplicationModule CreateReplicationModule() => new NullReplicationModule();

    /// <inheritdoc/>
    public ICommandGateway CreateCommandGateway() => new NullCommandGateway();

    /// <inheritdoc/>
    public IExConEgressWriters CreateExConEgressWriters() => new NullExConEgressWriters();

    /// <inheritdoc/>
    public ITimeControlGateway CreateTimeControlGateway() => new NullTimeControlGateway();

    /// <inheritdoc/>
    public ISimHostMissionSender CreateSimHostMissionSender() => new NullSimHostMissionSender();

    /// <inheritdoc/>
    public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators() => new NullSimHostAuxiliaryTranslators();

    /// <inheritdoc/>
    public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators() => new NullSimHostPathfindingTranslators();

    /// <inheritdoc/>
    public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators() => new NullSimHostPerceptionTranslators();

    /// <inheritdoc/>
    public IIgTranslators CreateIgTranslators() => new NullIgTranslators();

    // ---- null stubs -------------------------------------------------------

    private sealed class NullReplicationModule : IReplicationModule
    {
        public void Dispose() { }
    }

    private sealed class NullCommandGateway : ICommandGateway
    {
        public void Dispose() { }
    }

    private sealed class NullExConEgressWriters : IExConEgressWriters
    {
        public void Dispose() { }
    }

    private sealed class NullTimeControlGateway : ITimeControlGateway
    {
        public void Dispose() { }
    }

    private sealed class NullSimHostMissionSender : ISimHostMissionSender
    {
        public void SendNavigateToPoint(long id, System.Numerics.Vector2 dest, float speed, float radius) { }
        public void Dispose() { }
    }

    private sealed class NullSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
    {
        public void RegisterOn(ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    private sealed class NullSimHostPathfindingTranslators : ISimHostPathfindingTranslators
    {
        public void RegisterOn(ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    private sealed class NullSimHostPerceptionTranslators : ISimHostPerceptionTranslators
    {
        public void RegisterOn(ModuleHostKernel kernel) { }
        public void Dispose() { }
    }

    private sealed class NullIgTranslators : IIgTranslators
    {
        public IReadOnlyList<IDescriptorTranslator> GetTranslators(
            DdsParticipant participant, NetworkEntityMap entityMap, FdpEventBus bus,
            GhostCreationSystem? ghostCreationSystem, long localNodeId, bool headless)
            => System.Array.Empty<IDescriptorTranslator>();
    }
}
```

**IMPORTANT:** The null stubs above are ILLUSTRATIVE. Read the actual interface definitions
(especially `IReplicationModule`, `ICommandGateway`, `IExConEgressWriters`, `ITimeControlGateway`)
to find all method signatures before implementing. Check the BDC null stubs in
`Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` as the authoritative reference — each
`BdcNull*` class shows exactly what methods need implementations. Adapt all signatures from there.

For `NullIgTranslators`: check if `Hrot.Core/Network/IIgTranslators.cs` declares it as public,
and reuse it if it is (rather than creating a duplicate in Hrot.Editor).

---

#### Step 3: Wire OfflineNetworkFactory into EditorSubsystem

After reading `EditorSubsystem.Initialize()`, inject `new OfflineNetworkFactory()` at the
appropriate spot. The pattern should mirror how `SimHostSubsystem` accepts an
`INetworkFactory` parameter.

If `EditorSubsystem.Initialize()` builds subsystem context without any network, add
an internal field `private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory()`
and use it where appropriate in initialization.

---

#### Step 4: Verify Hrot.Editor has no direct NED/BDC references

Run:
```powershell
dotnet list Hrot.Editor/Hrot.Editor.csproj reference
```
Must NOT show `Hrot.Network.NED` or `Hrot.Network.BDC`.

If the editor project doesn't reference them directly, no csproj changes are needed.

---

## TASK-P5-001: Delete RunMode Enum and Refactor CLI Parsing

### Context

`RunMode.cs` is a flags enum with values like `RunMode.SimHost`, `RunMode.IG`, etc.
`HrotRunnerConfiguration` parses `--mode simhost,ig` into a `ParsedMode: RunMode` flags
value. `Program.cs` checks `config.ParsedMode.HasFlag(RunMode.SimHost)` etc.

The replacement approach: replace the enum with a case-insensitive `HashSet<string>`.
`ModeString` splits on commas, trims, and each name goes directly into the set.

---

#### Step 1: Read current files

Read in full:
- `Hrot.ClusterRunner/Configuration/RunMode.cs`
- `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`
- `Hrot.ClusterRunner/Program.cs` (lines 1-210, already shown above — look at all `HasFlag` usages)
- Any configuration tests: search for `RunMode` or `ParseModeString` in `Hrot.ClusterRunner.Tests/`

---

#### Step 2: Delete RunMode.cs

Delete `Hrot.ClusterRunner/Configuration/RunMode.cs` entirely.

---

#### Step 3: Update HrotRunnerConfiguration.cs

Make these changes (no other changes):

**Remove:**
- `public RunMode ParsedMode { get; set; }` property
- `ParseModeString()` method
- Any `using` directives for RunMode

**Add:**
- `public HashSet<string> RequestedSubsystems { get; private set; } = new(StringComparer.OrdinalIgnoreCase);`

**Update `Validate()` method:**
- Replace the `ParsedMode = ParseModeString(ModeString)` call with:
  ```csharp
  RequestedSubsystems.Clear();
  foreach (var name in ModeString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      RequestedSubsystems.Add(name);
  if (RequestedSubsystems.Count == 0)
      throw new InvalidOperationException("--mode must specify at least one subsystem name.");
  ```
- Remove the `if (ParsedMode == RunMode.None)` check (replaced by above)
- Replace `if (ParsedMode == RunMode.CI)` with `if (RequestedSubsystems.Contains("ci"))`
- Replace `if (ParsedMode == RunMode.Editor)` with `if (RequestedSubsystems.Contains("editor"))`
- Replace `if (ParsedMode.HasFlag(RunMode.Editor) && (ParsedMode & (RunMode.IG | ...)) != 0)` with:
  ```csharp
  if (RequestedSubsystems.Contains("editor") &&
      (RequestedSubsystems.Contains("ig") || RequestedSubsystems.Contains("excon") ||
       RequestedSubsystems.Contains("orchestrator") || RequestedSubsystems.Contains("cgf")))
      throw new InvalidOperationException("editor must not be combined with distributed flags...");
  ```

**Remove `--wait-for` and `--no-wait` CLI options** if they exist as `Option` attributes on
properties in `HrotRunnerConfiguration`. Find them by searching for `wait` in the class.
Remove: the properties, their Option attributes, and any validation logic referencing them.
Also remove any `WaitingRoomCoordinator` usage from Program.cs if present.

---

#### Step 4: Update Program.cs

Replace all `config.ParsedMode.HasFlag(RunMode.X)` calls with `config.RequestedSubsystems.Contains("x")`.

**Current pattern:**
```csharp
if (config.ParsedMode == RunMode.CI) { ... }
if (config.ParsedMode.HasFlag(RunMode.Orchestrator)) subsystems.Add(new OrchestratorSubsystem());
if (config.ParsedMode.HasFlag(RunMode.SimHost)) { ... }
if (config.ParsedMode.HasFlag(RunMode.IG)) subsystems.Add(new IgSubsystem());
if (config.ParsedMode.HasFlag(RunMode.ExCon)) subsystems.Add(new ExConSubsystem());
if (config.ParsedMode.HasFlag(RunMode.CGF)) subsystems.Add(new CgfSubsystem());
if (config.ParsedMode.HasFlag(RunMode.Editor)) subsystems.Add(new EditorSubsystem());
```

**Replacement pattern:**
```csharp
if (config.RequestedSubsystems.Contains("ci")) { ... }
if (config.RequestedSubsystems.Contains("orchestrator")) subsystems.Add(new OrchestratorSubsystem());
if (config.RequestedSubsystems.Contains("simhost")) { ... }
if (config.RequestedSubsystems.Contains("ig")) subsystems.Add(new IgSubsystem());
if (config.RequestedSubsystems.Contains("excon")) subsystems.Add(new ExConSubsystem());
if (config.RequestedSubsystems.Contains("cgf")) subsystems.Add(new CgfSubsystem());
if (config.RequestedSubsystems.Contains("editor")) subsystems.Add(new EditorSubsystem());
```

Also handle the `"all"` case if it exists in Program.cs — check for it explicitly and add all known subsystems.

Update the log message:
```csharp
// OLD:
Console.WriteLine($"[Runner] Starting – mode={config.ParsedMode}, ...");
// NEW:
Console.WriteLine($"[Runner] Starting – mode={string.Join(",", config.RequestedSubsystems)}, ...");
```

---

#### Step 5: Update tests

Search for all test files in `Hrot.ClusterRunner.Tests/` and
`Hrot.ClusterRunner.Integration.Tests/` that reference `RunMode`, `ParsedMode`, or
`ParseModeString`. Update them to use `RequestedSubsystems.Contains(...)` or to set
`ModeString` and call `Validate()` to populate `RequestedSubsystems`.

---

## Build and Test Verification

```powershell
cd D:\Work\IOS-IG-SimHost-FDP-2

dotnet build IOS-IG-SimHost.sln -v quiet

dotnet test IOS-IG-SimHost.sln --filter "FullyQualifiedName!~Integration" -v quiet
```

**Success conditions:**
- **0 build errors**
- `RunMode.cs` does not exist in the repository
- `dotnet list Hrot.Editor/Hrot.Editor.csproj reference` shows no NED/BDC refs
- All `Hrot.ClusterRunner.Tests` pass
- All other unit tests pass

---

## Report Requirements

Create `.dev/modular-2/reports/BATCH-13-REPORT.md` with:

1. **P4-005 status**: OfflineNetworkFactory created, EditorSubsystem wired, direct NED/BDC refs confirmed absent
2. **P5-001 status**: RunMode.cs deleted, HrotRunnerConfiguration updated, Program.cs updated
3. **Test results**: pass counts for ClusterRunner.Tests and others
4. **Build result**: 0 errors
5. **Deferred items**: anything skipped with debt proposals
