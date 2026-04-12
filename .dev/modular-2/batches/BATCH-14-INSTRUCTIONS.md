# BATCH-14: Reflection Scanner + --network CLI Flag

**Batch Number:** BATCH-14
**Tasks:** TASK-P5-002, TASK-P5-003
**Phase:** Phase 5 — Composition Root Redesign
**Estimated Effort:** 3-4 hours
**Priority:** HIGH
**Dependencies:** BATCH-13 complete

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the final two Phase-5 tasks:

1. **TASK-P5-002**: Replace the hardcoded `if (Contains("simhost")) ... new SimHostSubsystem()`
   conditionals in `Program.cs` with an `AppDomain` reflection scan that automatically
   discovers all `ISubsystem` implementations.

2. **TASK-P5-003**: Add a `--network ned|bdc` CLI flag that selects which concrete
   `INetworkFactory` to instantiate, creates the `DdsParticipant` exactly once,
   and passes the factory to each subsystem.

These tasks must be done in order: P5-002 first (the reflection scanner), then P5-003
(factories injected into discovered subsystems).

### Required Reading (in order)

1. **Task Definitions:**
   - `.dev/modular-2/TASK-DETAIL.md#task-p5-002`
   - `.dev/modular-2/TASK-DETAIL.md#task-p5-003`
2. **Previous report:** `.dev/modular-2/reports/BATCH-13-REPORT.md`
3. **Current Program.cs:** `Hrot.ClusterRunner/Program.cs` — read in full
4. **ISubsystem interface:** search Fdp.Engine for `interface ISubsystem`; understand `Name` and `Initialize` signature
5. **NedNetworkFactory constructor:** `Hrot.Network.NED/Factory/NedNetworkFactory.cs` lines 1-55
6. **BdcNetworkFactory constructor:** `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` lines 1-40
7. **NodeOpSlaveTranslator:** `Hrot.Network.Orchestration/NodeOpSlaveTranslator.cs`

### Source Code Areas

- `Hrot.ClusterRunner/Program.cs` — main rewrite target
- `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` — add `--network`
- `Hrot.ClusterRunner.Tests/` — update tests
- `Hrot.ClusterRunner.Integration.Tests/` — update harness

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-14-REPORT.md`

---

## Context

After BATCH-13, `Program.cs` still has hardcoded subsystem instantiation:
```csharp
if (config.RequestedSubsystems.Contains("orchestrator")) subsystems.Add(new OrchestratorSubsystem());
if (config.RequestedSubsystems.Contains("simhost")) { ... subsystems.Add(new SimHostSubsystem(simRole)); }
// ... etc.
```

TASK-P5-002 removes all these conditionals and replaces them with a two-phase approach:
1. Scan loaded assemblies for `ISubsystem` implementations
2. Instantiate those whose `Name` matches `RequestedSubsystems`

TASK-P5-003 then adds the network factory plumbing so each subsystem that accepts
`INetworkFactory` gets the right factory based on the `--network` flag.

---

## TASK-P5-002: Reflection Scanner for ISubsystem

### Step 1: Read Program.cs fully

Read Program.cs in full before starting. The current structure is:
- DDS participant creation (somewhere in the file)
- `var subsystems = new List<ISubsystem> { perspSubsystem };`
- hardcoded `if (Contains("...")) subsystems.Add(new SomeSubsystem(...))`
- `new SubsystemOrchestrator(subsystems, options).Run()`

Understand ALL subsystem instantiation patterns — some may take constructor arguments.

### Step 2: Implement LoadReferencedAssemblies helper

Add a private static helper in `Program.cs`:
```csharp
/// <summary>
/// Eagerly loads all statically-referenced assemblies that are not yet loaded
/// in the current AppDomain, so that they are visible in the reflection scan.
/// </summary>
private static void LoadReferencedAssemblies()
{
    var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetName().Name!), StringComparer.OrdinalIgnoreCase);

    var queue = new Queue<System.Reflection.Assembly>(AppDomain.CurrentDomain.GetAssemblies());
    while (queue.Count > 0)
    {
        var asm = queue.Dequeue();
        foreach (var refName in asm.GetReferencedAssemblies())
        {
            if (loaded.Contains(refName.Name!)) continue;
            try
            {
                var loaded2 = System.Reflection.Assembly.Load(refName);
                loaded.Add(refName.Name!);
                queue.Enqueue(loaded2);
            }
            catch { /* ignore assemblies that cannot be loaded */ }
        }
    }
}
```

### Step 3: Implement ScanForSubsystems helper

Add a private static helper that returns all discovered subsystem types:
```csharp
/// <summary>
/// Scans all loaded assemblies for non-abstract ISubsystem implementations
/// (excluding PerspectiveUpdateSubsystem which is always prepended manually).
/// </summary>
private static IEnumerable<Type> ScanForSubsystems()
{
    var subsystemType = typeof(ISubsystem);  // or use fully-qualified name
    return AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
        .Where(t => t.IsClass && !t.IsAbstract
                 && subsystemType.IsAssignableFrom(t)
                 && t != typeof(PerspectiveUpdateSubsystem)
                 && t != typeof(EyesAndMuscleSubsystem));  // runner-internal, not discovered
}
```

### Step 4: Implement CreateSubsystem helper

No INetworkFactory yet — that comes in P5-003. For now, prefer parameterless constructor:
```csharp
private static ISubsystem? TryCreateSubsystem(Type type)
{
    // Prefer parameterless constructor
    var ctor = type.GetConstructor(Type.EmptyTypes);
    if (ctor != null) return (ISubsystem)ctor.Invoke(null);
    return null;
}
```

Special cases will be handled in P5-003 when the factory is available.

### Step 5: Replace hardcoded subsystem list in Program.cs

Replace:
```csharp
var subsystems = new List<ISubsystem> { perspSubsystem };
if (config.RequestedSubsystems.Contains("orchestrator")) subsystems.Add(new OrchestratorSubsystem());
if (config.RequestedSubsystems.Contains("simhost")) { ... subsystems.Add(new SimHostSubsystem(simRole)); }
// ... all the hardcoded adds ...
```

With:
```csharp
LoadReferencedAssemblies();

// Build a map of name → type for all discovered subsystems
var discovered = ScanForSubsystems()
    .Select(t => TryCreateSubsystem(t))
    .Where(s => s != null)
    .ToDictionary(s => s!.Name, s => s!, StringComparer.OrdinalIgnoreCase);

// Select the requested subsystems; validate all names are known
var subsystems = new List<ISubsystem> { perspSubsystem };
bool addAll = config.RequestedSubsystems.Contains("all");
foreach (var name in addAll ? discovered.Keys : config.RequestedSubsystems)
{
    if (!discovered.TryGetValue(name, out var sub))
    {
        Console.Error.WriteLine($"[Runner] Unknown subsystem name: '{name}'. Available: {string.Join(", ", discovered.Keys)}");
        return 1;
    }
    if (!addAll || !config.RequestedSubsystems.Contains(name))  // avoid double-add when "all"
        subsystems.Add(sub);
}
if (addAll)
    foreach (var sub in discovered.Values)
        subsystems.Add(sub);
```

**Note:** The above logic is illustrative. Write the cleanest version. The key invariants:
- `PerspectiveUpdateSubsystem` is always prepended first
- `EyesAndMuscleSubsystem` is NOT discovered (it's runner infrastructure added separately if needed)
- Unknown names produce a clear error and exit code 1
- `--mode all` includes every discovered subsystem

**ALSO:** Remove the special CI handling block that ran before. The `CiSubsystem` 
(which is now in `Hrot.ClusterRunner/Scenarios/`) will be discovered via reflection normally.
But the legacy special CI block had scenario-name handling. Check if `CiSubsystem` now
accepts the scenario name differently, and keep any needed CI-specific logic in the
general subsystem construction path.

### Step 6: Remove all hardcoded `using` lines for concrete subsystems

After the reflection scanner handles instantiation, remove:
```csharp
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.IG;
using Hrot.ExCon;
using Hrot.CGF;
using Hrot.Editor;
```

Keep: `using Hrot.ClusterRunner.Services;` (for PerspectiveUpdateSubsystem),
`using Hrot.ClusterRunner.Scenarios;` (for CiSubsystem if it still needs special construction).

**IMPORTANT:** `Program.cs` must not contain any direct instantiation of
`SimHostSubsystem`, `IgSubsystem`, `ExConSubsystem`, `CgfSubsystem`, `OrchestratorSubsystem`,
or `EditorSubsystem` after this step.

### Step 7: Move Raylib window init to Program.cs

Per TASK-P5-002 spec: "Move Raylib window init/close into Program.cs (extracted from SubsystemOrchestrator)."

Read `Hrot.ClusterRunner/Systems/SubsystemOrchestrator.cs` to find where Raylib.InitWindow
and rlImGui.Setup are called. Move those calls to Program.cs (wrapping the orchestrator run):

```csharp
if (!config.Headless)
{
    Raylib.InitWindow(config.WindowWidth, config.WindowHeight, config.WindowTitle);
    rlImGui.Setup(true);
}

try
{
    orchestrator.Initialize();
    orchestrator.Run();
}
finally
{
    orchestrator.Shutdown();
    if (!config.Headless)
    {
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
```

Remove the corresponding Raylib/ImGui calls from SubsystemOrchestrator. Adjust any tests
that mock these calls.

---

## TASK-P5-003: Add --network CLI Flag

### Step 1: Add --network option to HrotRunnerConfiguration

In `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`:

```csharp
[Option("network", Default = "ned", HelpText = "Network protocol: ned (default) or bdc")]
public string NetworkProtocol { get; set; } = "ned";
```

Validate in `Validate()`:
```csharp
if (!string.Equals(NetworkProtocol, "ned", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(NetworkProtocol, "bdc", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Unknown --network value: '{NetworkProtocol}'. Use 'ned' or 'bdc'.");
```

### Step 2: Create DdsParticipant exactly once in Program.cs

Near the top of the `Main` method (AFTER config validation, BEFORE orchestrator construction):

```csharp
// Create DDS participant (exactly one per process lifetime)
var participant = new DdsParticipant(config.DomainId);
```

### Step 3: Create NodeOpSlaveTranslator

```csharp
var nodeId = ResolveAppNodeId("Runner", config.NodeId);
var nodeOpTranslator = new NodeOpSlaveTranslator(participant, nodeId);
```

### Step 4: Instantiate the network factory

```csharp
INetworkFactory networkFactory;
if (string.Equals(config.NetworkProtocol, "ned", StringComparison.OrdinalIgnoreCase))
{
    networkFactory = new NedNetworkFactory(
        participant:      participant,
        entityMap:        /* pass required args */,
        geoTransform:     /* pass required args */,
        eventBus:         /* pass required args */,
        localNodeId:      nodeId,
        role:             NodeRole.None);  // role determined per subsystem
}
else
{
    networkFactory = new BdcNetworkFactory(
        participant:      participant,
        entityMap:        /* pass required args */,
        geoTransform:     /* pass required args */,
        eventBus:         /* pass required args */,
        localNodeId:      nodeId,
        role:             NodeRole.None);
}
```

**IMPORTANT:** Read the NedNetworkFactory and BdcNetworkFactory constructors carefully.
They take entityMap, geoTransform, eventBus etc. These were previously created deep inside
each subsystem. In the new composition root pattern, you may need to:
a) Create these shared objects in Program.cs and pass them to the factory, OR
b) Simplify: if some factories constructors can take nullable versions, pass nulls for
   params that subsystems don't share.

Check what `NetworkEntityMap`, `IGeographicTransform`, `FdpEventBus` are and whether they
can be null. If BdcNetworkFactory and NedNetworkFactory accept nulls for these (check their
constructors), pass `null` for the shared objects and let each subsystem manage its own.

If the factory constructors REQUIRE these, look at how `NodeBootstrapper.cs` creates the factory.

### Step 5: Update TryCreateSubsystem to pass factory

Update the helper from P5-002 Phase 4:
```csharp
private static ISubsystem? TryCreateSubsystem(Type type, INetworkFactory networkFactory)
{
    // Prefer constructor that accepts INetworkFactory
    var factoryCtor = type.GetConstructor(new[] { typeof(INetworkFactory) });
    if (factoryCtor != null) return (ISubsystem)factoryCtor.Invoke(new object[] { networkFactory });

    // Fall back to parameterless constructor
    var ctor = type.GetConstructor(Type.EmptyTypes);
    if (ctor != null) return (ISubsystem)ctor.Invoke(null);

    return null;
}
```

### Step 6: Update HrotRunnerConfiguration tests

Add tests for `--network` parsing:
- `"ned"` → `NetworkProtocol == "ned"`
- `"bdc"` → `NetworkProtocol == "bdc"`
- `"unknown"` → `InvalidOperationException`

---

## Build and Test Verification

```powershell
cd D:\Work\IOS-IG-SimHost-FDP-2

dotnet build IOS-IG-SimHost.sln -v quiet

dotnet test IOS-IG-SimHost.sln --filter "FullyQualifiedName!~Integration" -v quiet
```

**Verification commands (check in report):**
- `grep -r "new SimHostSubsystem\|new IgSubsystem\|new CgfSubsystem\|new ExConSubsystem\|new OrchestratorSubsystem\|new EditorSubsystem" Hrot.ClusterRunner/ --include="*.cs"` must return zero results
- Build: **0 errors**

---

## Report Requirements

Create `.dev/modular-2/reports/BATCH-14-REPORT.md` with:

1. **P5-002 status**: reflection scanner working, hardcoded instantiation removed
2. **P5-003 status**: `--network` flag added, factory passed to subsystems
3. Grep verification: no hardcoded subsystem constructors in Program.cs  
4. Test results: pass counts
5. Build result: 0 errors
6. Deferred items if any
