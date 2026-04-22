# BATCH-02: Phase 2 Foundation — Interface Definitions and Translator Move

**Batch Number:** BATCH-02
**Tasks:** HEXAG2-S003, HEXAG2-S004, HEXAG2-S005
**Phase:** Phase 2 — Hexagonal Architecture Compliance (foundation)
**Estimated Effort:** 8-12 hours

---

## Onboarding

You are implementing the foundation of Phase 2 of the hexag-2 design. BATCH-01 (Phase 1) is
already complete and committed. Read these documents before starting:
- `.dev/hexag-2/DESIGN.md` — Sections 2.1, 4.2.1, 4.2.2, 4.2.3
- `.dev/hexag-2/TASK-DETAIL.md` — tasks HEXAG2-S003, HEXAG2-S004, HEXAG2-S005
- `.dev/hexag-2/reviews/BATCH-01-REVIEW.md` — feedback from previous batch
- `.dev/hexag-2/ONBOARDING.md`

**Goal of this batch:** Add the interface contracts that will allow OrchestratorSubsystem to be
decoupled from DDS in later batches (HEXAG2-S006, S007, S008). This batch is primarily
additive: new interfaces, new stub implementations in all existing factory classes, and
physically moving two translator files. No subsystem behaviour changes yet.

**Development branch:** All changes go on the current working branch (hexag).

**Build command:** `dotnet build IOS-IG-SimHost.sln -v q`  
**Test command:** `dotnet test IOS-IG-SimHost.sln --no-build -v q`

**Project paths:**
- `Hrot.Core`: `Hrot/Engine/Hrot.Core/Hrot.Core.csproj`
- `Hrot.Network.Orchestration`: `Hrot/Network/Hrot.Network.Orchestration/Hrot.Network.Orchestration.csproj`
- `Hrot.Network.NED`: `Hrot/Network/Hrot.Network.NED/Hrot.Network.NED.csproj`
- `Hrot.Orchestrator`: `Hrot/Subsystems/Hrot.Orchestrator/Hrot.Orchestrator.csproj`

---

## Developer Insights Section

When writing your report, answer these questions explicitly:
1. **What issues were encountered?** (csproj conflicts, circular references, namespace mismatches)
2. **What weak points did you spot?** (missing factory stub coverage, missing interface tests)
3. **What design decisions did you make beyond the spec?**

---

## Test-Driven Task Progression (MANDATORY)

For every task:
1. Read the success conditions in TASK-DETAIL.md before touching any code.
2. Write or verify tests first where applicable.
3. Implement until all tests pass.
4. Run the full test suite after each task.
5. Do not move to the next task until current task's tests pass.

---

## Tasks

### HEXAG2-S003 — Define `IOrchestrationTranslator` Interface

**Files to create:**
- `Hrot/Engine/Hrot.Core/Network/IOrchestrationTranslator.cs`

**What to do:**
Create a new file with exactly this content (adjust namespace if `Hrot.Core.Network` is not 
the correct namespace — check what namespace `INetworkFactory.cs` uses):

```csharp
namespace Hrot.Core.Network;

/// <summary>
/// Ticks all DDS ingress/egress for the orchestrator master transport (one call per frame).
/// Called inside OrchestratorSubsystem.Update() during Phase 1, before SwapBuffers.
/// </summary>
public interface IOrchestrationTranslator : IDisposable
{
    void Tick();
}
```

Also create a `NullOrchestrationTranslator` no-op class somewhere it can be shared with tests.
Options:
- Put it in `Hrot.Core` alongside the interface (if the project allows internal test doubles).
- Put it in a `Hrot.Core.Tests` test-helper.
- Put it in a new `Hrot.Core/Network/Null/NullOrchestrationTranslator.cs`.

The no-op must satisfy the interface without any DDS assembly reference:
```csharp
internal sealed class NullOrchestrationTranslator : IOrchestrationTranslator
{
    public void Tick() { }
    public void Dispose() { }
}
```

**Success conditions:**
1. `IOrchestrationTranslator.cs` compiles with no warnings in `Hrot.Core`.
2. A test double implementing the interface compiles in a test project WITHOUT any
   CycloneDDS assembly references.

---

### HEXAG2-S004 — Extend INetworkFactory with Orchestrator Ports

**Files to change:**
- `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs`
- `Hrot/Engine/Hrot.Core/Network/IMasterTimeTranslators.cs` (NEW)
- All classes that implement `INetworkFactory` — add stub implementations for the three
  new methods.

**Step 1: Add `IMasterTimeTranslators` interface (new file)**

```csharp
namespace Hrot.Core.Network;

/// <summary>
/// Groups the three master-side time-sync translators behind a single per-frame call surface.
/// </summary>
public interface IMasterTimeTranslators : IDisposable
{
    /// <summary>Read managed write-buffer -> DDS egress (time-mode + lockstep).</summary>
    void ScanAndPublish();
    /// <summary>DDS ingress -> write buffer (time-mode + lockstep).</summary>
    void PollIngress();
    /// <summary>Late NTP ingress poll (Phase 5, after SwapBuffers).</summary>
    void PollNtpIngress();
}
```

**Step 2: Add three methods to `INetworkFactory`**

```csharp
/// <summary>
/// Creates the orchestrator master-side DDS translators (ClusterOp, NodeOp, heartbeat).
/// All created DDS resources are owned by the returned translator and released on Dispose().
/// Returns a no-op translator when there is no DDS participant (headless / test mode).
/// No domain types (ClusterMaster, etc.) are accepted; integration is via bus events only.
/// </summary>
IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus bus, int nodeId);

/// <summary>
/// Creates and starts the hosted DDS ID allocator server background thread.
/// The caller owns the returned handle; Dispose() blocks via Thread.Join to guarantee
/// clean teardown before the shared DdsParticipant is destroyed.
/// Returns a no-op IDisposable when there is no DDS participant.
/// </summary>
IDisposable CreateIdAllocatorServer();

/// <summary>
/// Creates the master-side time-sync DDS translators (time-mode broadcast,
/// lockstep barrier, master NTP sync). Absorbs _timeModeTranslator, _lockstepTranslator,
/// and _masterTimeSyncTranslator. Returns a no-op implementation when there is no
/// DDS participant.
/// </summary>
IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId);
```

**Step 3: Add stub implementations to ALL INetworkFactory implementors**

Find all classes that implement `INetworkFactory` in the codebase:
- Search for `INetworkFactory` to find them.
- Common ones: `NedNetworkFactory`, any offline/null factory, any test mock factories.
- For each one, add stub implementations that return no-op values:

```csharp
// Example stubs for non-NED factories:
public IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus bus, int nodeId)
    => new NullOrchestrationTranslator();

public IDisposable CreateIdAllocatorServer()
    => new NullDisposable();  // or: System.Reactive.Disposables.Disposable.Empty or similar

public IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId)
    => new NullMasterTimeTranslators();
```

For `NullDisposable`, use the simplest approach: `NullDisposable` inner class or a simple lambda wrapper. 

For `NedNetworkFactory`: add stub implementations that return the same no-ops for now
(the real implementations come in HEXAG2-S006 and HEXAG2-S007 in a later batch).

**Do NOT implement the real DDS logic in NedNetworkFactory yet** — that is HEXAG2-S006/S007 scope.

Also add `ISlaveOrchestrationTranslator` and `IOrchestrationObserver` interfaces (for HEXAG2-S012):

```csharp
// Hrot/Engine/Hrot.Core/Network/ISlaveOrchestrationTranslator.cs
namespace Hrot.Core.Network;

/// <summary>
/// Ticks the slave-side orchestration transport (NodeOpCommand ingress,
/// NodeOpStatus + NodeHeartbeat egress). Called in Phase 1 of slave Update().
/// </summary>
public interface ISlaveOrchestrationTranslator : IDisposable
{
    void Tick();
}

// Hrot/Engine/Hrot.Core/Network/IOrchestrationObserver.cs
namespace Hrot.Core.Network;

/// <summary>
/// Ticks the cluster observer translator (SystemState, AssetInventory ingress).
/// Called in Phase 1 of slash Update() for observer nodes.
/// </summary>
public interface IOrchestrationObserver : IDisposable
{
    void Tick();
}
```

Add the slave-side factory methods to `INetworkFactory` as well:
```csharp
/// <summary>
/// Creates the slave-side orchestration translator (NodeOpCommand ingress,
/// NodeOpStatus + NodeHeartbeat egress).
/// Returns a no-op translator when there is no DDS participant.
/// </summary>
ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(FdpEventBus bus, int nodeId);

/// <summary>
/// Creates the cluster observer translator (SystemStateTopic, AssetInventoryTopic ingress).
/// Returns a no-op translator when there is no DDS participant.
/// </summary>
IOrchestrationObserver CreateOrchestrationObserver(FdpEventBus bus);
```

Add no-op stubs for all these in every INetworkFactory implementor as well.

**Success conditions:**
1. `INetworkFactory` compiles with all five new methods.
2. `IMasterTimeTranslators.cs`, `ISlaveOrchestrationTranslator.cs`, `IOrchestrationObserver.cs`
   compile with no warnings in `Hrot.Core`.
3. All implementing classes compile (no missing member errors).
4. Full solution builds: `dotnet build IOS-IG-SimHost.sln -v q` → 0 errors.

---

### HEXAG2-S005 — Move Master Translators to Hrot.Network.Orchestration

**Files to move:**
- `Hrot/Subsystems/Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs`
  → `Hrot/Network/Hrot.Network.Orchestration/ClusterOpMasterTranslator.cs`
- `Hrot/Subsystems/Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`
  → `Hrot/Network/Hrot.Network.Orchestration/NodeOpMasterTranslator.cs`
- Any master-specific Payload types in `Hrot.Orchestrator/Translators/Payloads/`
  → `Hrot/Network/Hrot.Network.Orchestration/Payloads/`

**Steps:**
1. Read the current file content carefully before moving.
2. Copy each file to the new location.
3. Update the `namespace` declaration in each file:
   - FROM: `namespace Hrot.Orchestrator.Translators` (or similar)
   - TO: `namespace Hrot.Network.Orchestration` (or an appropriate sub-namespace)
4. Update any `using` directives as needed.
5. Remove the old files from `Hrot.Orchestrator`.
6. Update `Hrot.Orchestrator.csproj`: remove the `<Compile>` entries for moved files (if
   explicit), remove any CycloneDDS NuGet reference IF it was only needed by the moved files.
7. Update `Hrot.Network.Orchestration.csproj`: add the new files (if explicit includes used).
8. Update any `using Hrot.Orchestrator.Translators` references in other files — search the
   entire solution for the old namespace.

**Important:** Do NOT modify the translator behaviour in this task. The
`_unhandledRequestCallback` in `ClusterOpMasterTranslator` stays as-is — that is removed in
HEXAG2-S010 (a later batch). Just move the files and fix namespaces.

**Also:** Check if `Hrot.Network.Orchestration.csproj` already has a reference to `Fdp.Toolkits`
(needed later for intent publishing). If not, add it.

**Success conditions:**
1. `Hrot.Network.Orchestration` assembly contains `ClusterOpMasterTranslator` and
   `NodeOpMasterTranslator` with updated namespace.
2. `Hrot.Orchestrator` assembly no longer contains those files.
3. `dotnet build IOS-IG-SimHost.sln -v q` → 0 errors.
4. If `Hrot.Orchestrator.csproj` no longer directly uses CycloneDDS after the move, remove
   that reference. Verify with grep that no `using CycloneDDS` or `DdsReader<T>` / 
   `DdsWriter<T>` or `DdsParticipant` remain directly in `Hrot.Orchestrator/*.cs` files
   (excluding test files).

---

## Report Format

Submit your report to `.dev/hexag-2/reports/BATCH-02-REPORT.md`:

```markdown
# BATCH-02 Report

## Tasks Completed
- [ ] HEXAG2-S003
- [ ] HEXAG2-S004
- [ ] HEXAG2-S005

## Tests Written
(list test names and locations)

## Test Results
(paste relevant dotnet test output)

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Made Beyond Spec

## Files Changed
(list every file changed/created/moved)
```

---

## Verification Checklist

- [ ] `IOrchestrationTranslator` interface exists and compiles in `Hrot.Core`
- [ ] `IMasterTimeTranslators` interface exists and compiles in `Hrot.Core`
- [ ] `ISlaveOrchestrationTranslator` interface exists and compiles in `Hrot.Core`
- [ ] `IOrchestrationObserver` interface exists and compiles in `Hrot.Core`
- [ ] `INetworkFactory` has 5 new methods (3 master + 2 slave)
- [ ] All INetworkFactory implementors compile with new stubs
- [ ] `NullOrchestrationTranslator` compiles without DDS references
- [ ] `ClusterOpMasterTranslator` is in `Hrot.Network.Orchestration` (not `Hrot.Orchestrator`)
- [ ] `NodeOpMasterTranslator` is in `Hrot.Network.Orchestration` (not `Hrot.Orchestrator`)
- [ ] Build: `dotnet build IOS-IG-SimHost.sln -v q` → 0 errors
- [ ] All previously passing tests still pass
