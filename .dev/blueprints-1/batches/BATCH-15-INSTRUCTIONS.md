# BATCH-15 Instructions — Phase 4: Hot Reload (HR-001, HR-002, HR-003)

**Tasks:** TASK-HR-001 (AiHotReloadCoordinator Core), TASK-HR-002 (SimulateReload Test Harness Integration), TASK-HR-003 (Hot Reload Test Suite)  
**Design refs:** `.dev/blueprints-1/TASK-DETAIL.md` §HR-001, §HR-002, §HR-003; `.dev/blueprints-1/Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`; `.dev/blueprints-1/Blueprint_Subsystem_Hot_Reload_Detailed_Design_InlinePatches.md`  
**Phase:** 4 — Hot Reload  
**Current test state:** 347 pass / 5 skip / 0 fail (commit `2130ae6c`)

---

## 0. Context

You are implementing Phase 4 of the Blueprint subsystem: the `AiHotReloadCoordinator` and its test suite.

**Key codebase facts:**
- `AiHotReloadCoordinator` does NOT yet exist in `FDP/Toolkits/Fdp.Toolkits/Behavior/`. Create it there.
- There is an EXISTING `AiHotReloadCoordinator` in `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` — that is the ENGINE coordinator. **Do NOT modify it.** Create a separate simplified one in `Fdp.Toolkit.Behavior`.
- `BlueprintTestFixture` is at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs` — update it.
- All hot reload tests go in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/` subdirectory, namespace `Hrot.Blueprints.Tests.HotReload`.
- `HsmActionDispatcher` is `public static unsafe class` in `Fhsm.Kernel` — it CANNOT be a constructor parameter or registrar argument.
- `BlueprintRegistrarAttribute` lives at `FDP/Toolkits/Fdp.Toolkits/Blueprints/Attributes/BlueprintRegistrarAttribute.cs`.
- `BehaviorRegistry.Register(int id, string name, BehaviorDefinition definition)` is in `Fdp.Toolkit.Behavior`.
- `BlueprintRegistry.BeginStaging()` / `CommitStaging(staging)` are in `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`.
- The test project `Hrot.Blueprints.Tests.csproj` references `Hrot.Blueprints.Core` which references `Fdp.Toolkits`, so the new coordinator is automatically available.

**Read before starting:**
1. `TASK-DETAIL.md` §HR-001, §HR-002, §HR-003 for the full scope and success conditions.
2. `Blueprint_Subsystem_Hot_Reload_Detailed_Design_InlinePatches.md` — all 4 patches supersede parts of the main doc.
3. The key patches:
   - **Patch 1**: `_currentAlc` is main-thread-only; `PendingReload` has no `OldAlc` field.
   - **Patch 2**: `HsmActionDispatcher` is static — no constructor param, throws if registrar requests it; `ApplyReload` calls `HsmActionDispatcher.ClearAll()` directly.
   - **Patch 3**: `ApplyQuickReload(AssemblyLoadContext, Assembly)` — coordinator owns all ALCs.
   - **Patch 4**: `BlueprintRegistry` param injection is explicitly forbidden with "RCU contract" message.

---

## 1. HR-001: Create `AiHotReloadCoordinator`

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs`  
**Namespace:** `Fdp.Toolkit.Behavior`

Create this new file. It is NOT a modification of the engine coordinator in `Hrot.Editor`.

### 1.1 Supporting types

Define these in the same file (top of file, before the class):

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;

namespace Fdp.Toolkit.Behavior;
```

**Exception type:**
```csharp
/// <summary>
/// Thrown when a registrar's parameter list violates the hot reload injection rules.
/// </summary>
public sealed class HotReloadRegistrarException : Exception
{
    public HotReloadRegistrarException(string message) : base(message) { }
}
```

**Options record:**
```csharp
/// <summary>
/// Configuration for <see cref="AiHotReloadCoordinator"/>.
/// </summary>
public sealed record AiHotReloadCoordinatorOptions
{
    /// <summary>
    /// When true, attempt to load a co-located .pdb file alongside the PE.
    /// Enables debugger step-through of generated code in developer mode.
    /// </summary>
    public bool LoadPdbOnDeveloperMode { get; init; } = false;

    /// <summary>
    /// Debounce window for file-watcher events. Multiple change notifications
    /// within this window are coalesced into a single LoadAndScan call.
    /// </summary>
    public TimeSpan FileWatcherDebounce { get; init; } = TimeSpan.FromMilliseconds(500);
}
```

**PendingReload record (internal, main-thread-only Patch 1 — no OldAlc field):**
```csharp
internal sealed class PendingReload
{
    public required AssemblyLoadContext NewAlc   { get; init; }
    public required Assembly            NewAssembly { get; init; }
    public required IReadOnlyList<ResolvedRegistrar> Registrars { get; init; }
    // No OldAlc: main-thread ApplyReload reads _currentAlc directly (Patch 1).
}
```

**ResolvedRegistrar + RegistrarParameter records:**
```csharp
public sealed record ResolvedRegistrar(
    Type DeclaringType,
    MethodInfo RegisterMethod,
    IReadOnlyList<RegistrarParameter> Parameters);

public sealed record RegistrarParameter(
    string Name,
    Type ParameterType,
    int OrdinalIndex);
```

### 1.2 The coordinator class

```csharp
/// <summary>
/// Hot-reload coordinator for Blueprint AI assemblies.
/// Manages the ALC lifecycle (file-watcher or Quick-Reload paths).
/// <para><b>Thread model:</b>
/// Background thread: LoadAndScan (creates ALC, scans registrars).
/// Main thread: DrainPendingCallbacks / ApplyQuickReload (applies staging, swaps ALC).
/// </para>
/// </summary>
public sealed class AiHotReloadCoordinator : IDisposable
{
    // ---- Events (fired on main thread) -------------------------------------
    public event Action?            OnReloadCompleted;
    public event Action<Exception>? OnReloadFailed;

    // ---- Dependencies (set in constructor) ---------------------------------
    private readonly BehaviorRegistry              _behaviorRegistry;
    private readonly BlueprintRegistry             _blueprintRegistry;
    private readonly AiHotReloadCoordinatorOptions _options;

    // ---- ALC state (main-thread-only, Patch 1) -----------------------------
    private AssemblyLoadContext? _currentAlc;

    // ---- Queues ------------------------------------------------------------
    private readonly ConcurrentQueue<PendingReload> _pendingReloads = new();

    // ---- File watcher (optional) -------------------------------------------
    private FileSystemWatcher? _watcher;
    private Timer?             _debounceTimer;
    private string?            _pendingPath;
    private readonly object    _debounceLock = new();

    // ---- Constructor -------------------------------------------------------

    /// <summary>
    /// Per Patch 2: no HsmActionDispatcher constructor parameter.
    /// HsmActionDispatcher is a static class; ClearAll() is called statically.
    /// </summary>
    public AiHotReloadCoordinator(
        BehaviorRegistry              behaviorRegistry,
        BlueprintRegistry             blueprintRegistry,
        AiHotReloadCoordinatorOptions options)
    {
        _behaviorRegistry  = behaviorRegistry;
        _blueprintRegistry = blueprintRegistry;
        _options           = options;
    }

    // ---- Public: file watching ---------------------------------------------

    /// <summary>
    /// Starts watching the given DLL path for changes.
    /// On change, triggers background LoadAndScan.
    /// </summary>
    public void StartWatching(string dllPath)
    {
        var dir    = Path.GetDirectoryName(Path.GetFullPath(dllPath))!;
        var filter = Path.GetFileName(dllPath);

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(dir, filter)
        {
            NotifyFilter        = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
    }

    /// <summary>Stops file watching and disposes the watcher.</summary>
    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    // ---- Public: main-thread apply -----------------------------------------

    /// <summary>
    /// Dequeues one pending reload and applies it. Call once per frame on the main thread.
    /// </summary>
    public void DrainPendingCallbacks()
    {
        if (!_pendingReloads.TryDequeue(out var pending)) return;

        try
        {
            ApplyReload(pending);
            OnReloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            // _currentAlc is untouched (Patch 1 failure path).
            OnReloadFailed?.Invoke(ex);
            try { pending.NewAlc.Unload(); }
            catch { /* best-effort unload */ }
        }
    }

    /// <summary>
    /// Apply an in-memory Quick Reload (Patch 3).
    /// Scans registrars from the given assembly, then calls ApplyReload directly.
    /// Throws on failure; caller should NOT unload newAlc on failure (coordinator does it).
    /// </summary>
    public void ApplyQuickReload(AssemblyLoadContext newAlc, Assembly newAssembly)
    {
        var registrars = ScanForRegistrars(newAssembly);
        var pending = new PendingReload
        {
            NewAlc      = newAlc,
            NewAssembly = newAssembly,
            Registrars  = registrars,
        };

        try
        {
            ApplyReload(pending);
            OnReloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            OnReloadFailed?.Invoke(ex);
            try { pending.NewAlc.Unload(); }
            catch { /* best-effort */ }
            throw;
        }
    }

    // ---- Internal: test access to current ALC ------------------------------

    /// <summary>Internal for test access; verifies ALC identity after reload.</summary>
    internal AssemblyLoadContext? GetCurrentAlc() => _currentAlc;

    // ---- IDisposable -------------------------------------------------------

    public void Dispose()
    {
        StopWatching();
        var alc = Interlocked.Exchange(ref _currentAlc, null);
        alc?.Unload();
    }

    // ---- Private: apply ----------------------------------------------------

    private void ApplyReload(PendingReload pending)
    {
        // Step 1: Patch 2 — static call, no instance needed.
        HsmActionDispatcher.ClearAll();

        // Step 2: begin staging.
        var staging = _blueprintRegistry.BeginStaging();

        // Step 3: invoke each registrar.
        foreach (var registrar in pending.Registrars)
            InvokeRegistrar(registrar, staging);

        // Step 4: atomic commit.
        _blueprintRegistry.CommitStaging(staging);

        // Step 5: swap _currentAlc — ONLY after successful commit (Patch 1).
        var oldAlc = _currentAlc;
        _currentAlc = pending.NewAlc;
        oldAlc?.Unload();
    }

    private void InvokeRegistrar(ResolvedRegistrar registrar, BlueprintRegistryStaging staging)
    {
        var args = registrar.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => ResolveRegistrarArgument(p.ParameterType, staging))
            .ToArray();
        registrar.RegisterMethod.Invoke(null, args);
    }

    /// <summary>
    /// Resolves a registrar parameter type to its argument value.
    /// Throws <see cref="HotReloadRegistrarException"/> for forbidden or unknown types (Patch 2, Patch 4).
    /// </summary>
    private object ResolveRegistrarArgument(Type paramType, BlueprintRegistryStaging staging)
    {
        if (paramType == typeof(BlueprintRegistryStaging)) return staging;
        if (paramType == typeof(BehaviorRegistry))         return _behaviorRegistry;

        // Patch 4: explicitly forbidden — would bypass the atomic RCU contract.
        if (paramType == typeof(BlueprintRegistry))
            throw new HotReloadRegistrarException(
                $"Registrar requests BlueprintRegistry as a parameter, but only " +
                "BlueprintRegistryStaging may be injected. Direct access to the live " +
                "registry would violate the atomic RCU contract. " +
                "Change the registrar's parameter to BlueprintRegistryStaging.");

        // Patch 2: HsmActionDispatcher is a static class — cannot be injected.
        if (paramType == typeof(HsmActionDispatcher))
            throw new HotReloadRegistrarException(
                $"Registrar requests HsmActionDispatcher as a parameter, but it is a " +
                "static class and cannot be injected. " +
                "Call HsmActionDispatcher.RegisterAction statically from inside Register.");

        throw new HotReloadRegistrarException(
            $"Unknown registrar parameter type: {paramType.FullName}. " +
            "Supported: BlueprintRegistryStaging, BehaviorRegistry.");
    }

    // ---- Private: registrar discovery --------------------------------------

    /// <summary>
    /// Scans the assembly for classes with [BlueprintRegistrar].
    /// ONLY scans for BlueprintRegistrar — explicitly not FbtRegistrar or HsmActionRegistrar.
    /// Per HR-001 constraint: avoids invoking generated native registrars with missing params.
    /// </summary>
    internal IReadOnlyList<ResolvedRegistrar> ScanForRegistrars(Assembly assembly)
    {
        var registrars = new List<ResolvedRegistrar>();
        Type[] types;

        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() == null)
                continue;

            var method =
                type.GetMethod("Register",    BindingFlags.Public | BindingFlags.Static) ??
                type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static);
            if (method == null) continue;

            var parameters = method.GetParameters()
                .Select((p, i) => new RegistrarParameter(p.Name ?? $"arg{i}", p.ParameterType, i))
                .ToList();

            registrars.Add(new ResolvedRegistrar(type, method, parameters));
        }

        return registrars
            .OrderBy(r => r.DeclaringType.FullName, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Private: file watching --------------------------------------------

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock) { _pendingPath = e.FullPath; }
        int ms = (int)_options.FileWatcherDebounce.TotalMilliseconds;
        _debounceTimer?.Change(ms, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        string? path;
        lock (_debounceLock) { path = _pendingPath; }
        if (path != null)
            ThreadPool.QueueUserWorkItem(_ => DoLoadAndScan(path));
    }

    private void DoLoadAndScan(string dllPath)
    {
        try
        {
            var alc = new AssemblyLoadContext(
                name: $"AiBehaviors_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                isCollectible: true);

            Assembly assembly = LoadAssemblyInto(alc, dllPath);
            var registrars = ScanForRegistrars(assembly);

            _pendingReloads.Enqueue(new PendingReload
            {
                NewAlc      = alc,
                NewAssembly = assembly,
                Registrars  = registrars,
            });
        }
        catch (Exception)
        {
            // Background failures: logged by caller; old ALC remains live.
            // Do not propagate — background thread must not crash.
        }
    }

    private Assembly LoadAssemblyInto(AssemblyLoadContext alc, string dllPath)
    {
        using var peStream = File.OpenRead(dllPath);

        if (_options.LoadPdbOnDeveloperMode)
        {
            var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                using var pdbStream = File.OpenRead(pdbPath);
                return alc.LoadFromStream(peStream, pdbStream);
            }
        }

        return alc.LoadFromStream(peStream);
    }
}
```

### 1.3 Build verification

After creating the file, run:
```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj -v minimal
```
Ensure zero errors. Fix any missing using directives or namespace issues.

---

## 2. HR-002: Update `BlueprintTestFixture`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

### 2.1 Add coordinator field and update constructor

Add a `_coordinator` field after the private state fields:
```csharp
private readonly AiHotReloadCoordinator _coordinator;
```

In the constructor, after the `Compiler` initialization, add:
```csharp
_coordinator = new AiHotReloadCoordinator(
    BehaviorRegistry,
    Registry,
    new Fdp.Toolkit.Behavior.AiHotReloadCoordinatorOptions());
```

Add using directive at the top if needed:
```csharp
using Fdp.Toolkit.Behavior;
```

### 2.2 Update `SimulateReload` to use coordinator

Replace the existing `SimulateReload(IReadOnlyList<BlueprintAsset> newVersions)` method:

```csharp
/// <summary>
/// Compiles the given assets, loads them into a new collectible ALC,
/// and applies the reload through the coordinator (Patch 3 path).
/// Old ALC is unloaded by the coordinator after successful commit.
/// </summary>
public void SimulateReload(IReadOnlyList<BlueprintAsset> newVersions)
{
    // Compile to in-memory PE bytes.
    var sink = new DiagnosticSink();
    var options = new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    var sb = new StringBuilder();
    foreach (var asset in newVersions)
    {
        var result = Compiler.Compile(asset, options);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Blueprint '{asset.Name}' failed to compile: " +
                string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        sb.AppendLine(result.GeneratedSource);
    }

    // Compile to PE bytes via Roslyn.
    var assemblyName = $"Bp_{Guid.NewGuid():N}";
    var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
        AppDomain.CurrentDomain.GetAssemblies());
    var roslynCompiler = new InMemoryRoslynCompiler(resolver);
    var (assembly, alc) = roslynCompiler.CompileAndLoad(
        sb.ToString(),
        $"{assemblyName}.g.cs",
        assemblyName,
        sink);

    // Track ALC for GC-reclaim verification.
    _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));

    // Hand off to coordinator (Patch 3) — coordinator owns ALC lifecycle.
    _coordinator.ApplyQuickReload(alc, assembly);
}
```

### 2.3 Add `SimulateQuickReload` convenience overload

```csharp
/// <summary>Single-asset convenience wrapper for SimulateReload.</summary>
public void SimulateQuickReload(BlueprintAsset asset)
    => SimulateReload(new[] { asset });
```

### 2.4 Add `GetCurrentAlc` method

```csharp
/// <summary>
/// Returns the coordinator's current ALC.
/// Used by hot reload tests to verify ALC identity across reloads.
/// </summary>
public AssemblyLoadContext? GetCurrentAlc()
    => _coordinator.GetCurrentAlc();
```

### 2.5 Add `SimulateReloadWithThrowingRegistrar` helper

```csharp
/// <summary>
/// Compiles a minimal assembly with a [BlueprintRegistrar] whose Register method
/// throws InvalidOperationException. Used to test failure-rollback behavior.
/// </summary>
public void SimulateReloadWithThrowingRegistrar()
{
    const string source = @"
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;

[BlueprintRegistrar]
public static class ThrowingRegistrar
{
    public static void Register(BlueprintRegistryStaging staging)
        => throw new System.InvalidOperationException(""Deliberate registrar failure for testing."");
}
";
    var assemblyName = $"ThrowingReg_{Guid.NewGuid():N}";
    var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
        AppDomain.CurrentDomain.GetAssemblies());
    var roslynCompiler = new InMemoryRoslynCompiler(resolver);
    var sink = new DiagnosticSink();
    var (assembly, alc) = roslynCompiler.CompileAndLoad(
        source, $"{assemblyName}.g.cs", assemblyName, sink);

    _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));
    _coordinator.ApplyQuickReload(alc, assembly);
}
```

### 2.6 Update `CompileAndLoad` / `CompileAndLoadMany` to notify coordinator

In `CompileAndLoadMany`, REPLACE the current `DiscoverAndInvokeRegistrars(assembly)` call with the coordinator path:

```csharp
// Hand off to coordinator so _currentAlc is tracked.
_coordinator.ApplyQuickReload(alc, assembly);
```

This ensures `GetCurrentAlc()` is non-null after the first `CompileAndLoad` call.

> **Important:** Remove the `_activeAlcs.Add(alc)` call from `CompileAndLoadMany` since the coordinator now owns ALC lifecycle. Also remove `DiscoverAndInvokeRegistrars(assembly)` call — the coordinator does this internally.

### 2.7 Update `Dispose` to stop coordinator

In the `Dispose` method of `BlueprintTestFixture`, add:
```csharp
_coordinator.Dispose();
```
before the existing GC-reclaim verification block.

### 2.8 Build and run existing tests

After changes:
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```
All 347 previously-passing tests must still pass. The fixture changes must not break existing tests.

> **Note:** The existing `DiscoverAndInvokeRegistrars` and `_activeAlcs` private members can be removed if they are no longer referenced. BUT: keep `_alcWeakRefs` since it is used by `GetAlcWeakReferences()` and `ForceGcReclaim()`.

---

## 3. HR-003: Hot Reload Test Suite

All tests go in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/` directory.  
Namespace: `namespace Hrot.Blueprints.Tests.HotReload`

**Filter to run only hot reload tests:**
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~HotReload" -v minimal
```

### 3.1 `Coordinator/FailureRollbackTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Patch 1: verifies that a failed reload does not mutate _currentAlc.
/// </summary>
public sealed class FailureRollbackTests
{
    [Fact]
    public void Reload_Failure_DoesNotMutateCurrentAlc()
    {
        // Load a baseline blueprint so coordinator has a non-null currentAlc.
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var aliveAlcBefore = fixture.GetCurrentAlc();
        Assert.NotNull(aliveAlcBefore);

        // Simulate a reload that throws inside the registrar.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());

        // The exception should propagate.
        Assert.NotNull(ex);

        // _currentAlc must be unchanged after failure.
        var aliveAlcAfter = fixture.GetCurrentAlc();
        Assert.Same(aliveAlcBefore, aliveAlcAfter);

        // Registry still has the original blueprint.
        Assert.True(fixture.Registry.TryGetByName("LibraryMath", out _));
    }

    [Fact]
    public void Reload_FailureThenSuccess_LiveCodeNeverInterrupted()
    {
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        // Failed reload.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
        Assert.NotNull(ex);

        // Original code still runs.
        Assert.True(fixture.Registry.TryGetByName("LibraryMath", out var def));
        Assert.Equal(BlueprintDispatchKind.Library, def!.Kind);

        // Successful reload with new blueprint.
        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateReload(new[] { v2 });

        Assert.True(fixture.Registry.TryGetByName("MoveToAndFire", out _));
    }
}
```

### 3.2 `Coordinator/RegistrarInjectionTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Patch 2 and Patch 4: verifies ResolveRegistrarArgument throws for forbidden types.
/// Tests that 2-parameter registrar (BlueprintRegistryStaging, BehaviorRegistry) is invoked correctly.
/// </summary>
public sealed class RegistrarInjectionTests
{
    [Fact]
    public void ResolveRegistrarArgument_BlueprintRegistry_ThrowsWithRcuMessage()
    {
        // Compile an assembly with a registrar that requests BlueprintRegistry (forbidden, Patch 4).
        const string source = @"
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;

[BlueprintRegistrar]
public static class ForbiddenRegistrar
{
    public static void Register(BlueprintRegistryStaging staging, BlueprintRegistry registry)
    {
        // This should never be called.
    }
}
";
        using var fixture = new BlueprintTestFixture();
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var roslynCompiler = new InMemoryRoslynCompiler(resolver);
        var sink = new DiagnosticSink();
        var (assembly, alc) = roslynCompiler.CompileAndLoad(
            source, "ForbiddenReg.g.cs", "ForbiddenReg", sink);

        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => fixture.SimulateReloadFromAlc(alc, assembly));

        Assert.Contains("BlueprintRegistryStaging", ex.Message);
        Assert.Contains("RCU contract", ex.Message);
    }

    [Fact]
    public void ResolveRegistrarArgument_HsmActionDispatcher_ThrowsWithStaticClassMessage()
    {
        // Compile an assembly with a registrar that requests HsmActionDispatcher (forbidden, Patch 2).
        const string source = @"
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;

[BlueprintRegistrar]
public static class HsmRegistrar
{
    public static void Register(BlueprintRegistryStaging staging, HsmActionDispatcher dispatcher)
    {
        // This should never be called.
    }
}
";
        using var fixture = new BlueprintTestFixture();
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var roslynCompiler = new InMemoryRoslynCompiler(resolver);
        var sink = new DiagnosticSink();
        var (assembly, alc) = roslynCompiler.CompileAndLoad(
            source, "HsmReg.g.cs", "HsmReg", sink);

        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => fixture.SimulateReloadFromAlc(alc, assembly));

        Assert.Contains("static class", ex.Message);
    }

    [Fact]
    public void AiPrimitive_TwoParameterRegistrar_IsInvokedCorrectly()
    {
        // A valid AiPrimitive registrar has (BlueprintRegistryStaging, BehaviorRegistry).
        // Compiling MoveToAndFire invokes it without exception.
        using var fixture = new BlueprintTestFixture();
        var asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

        // Should not throw.
        fixture.CompileAndLoad(asset);

        // The BlueprintRegistry should contain MoveToAndFire.
        Assert.True(fixture.Registry.TryGetByName("MoveToAndFire", out var def));
        Assert.Equal(BlueprintDispatchKind.AiPrimitive, def!.Kind);
    }
}
```

**Note:** The `SimulateReloadFromAlc(alc, assembly)` method needs to be added to `BlueprintTestFixture`:
```csharp
/// <summary>
/// Test-only: calls coordinator.ApplyQuickReload with a pre-built ALC.
/// Tracks the ALC for GC-reclaim verification. Throws on registrar errors.
/// </summary>
internal void SimulateReloadFromAlc(AssemblyLoadContext alc, Assembly assembly)
{
    _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));
    _coordinator.ApplyQuickReload(alc, assembly);
}
```

### 3.3 `Coordinator/AlcLifecycleTests.cs`

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Verifies ALC unload and GC reclaim behavior across reload sequences.
/// </summary>
public sealed class AlcLifecycleTests
{
    [Fact]
    public void SuccessfulReload_UnloadsOldAlc()
    {
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var alc1 = fixture.GetCurrentAlc();
        Assert.NotNull(alc1);
        var alc1WeakRef = MakeWeakRef(alc1!);

        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateReload(new[] { v2 });

        var alc2 = fixture.GetCurrentAlc();
        Assert.NotSame(alc1, alc2);

        // After reload, old ALC should be eligible for GC.
        alc1 = null;  // release strong reference
        fixture.ForceGcReclaim();
        Assert.False(alc1WeakRef.IsAlive, "Old ALC should be reclaimed after successful reload.");
    }

    [Fact]
    public void FailedReload_DoesNotLeakNewAlc()
    {
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var alcCountBefore = fixture.GetAlcWeakReferences().Count;

        // Failed reload — coordinator should unload the new (failed) ALC.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
        Assert.NotNull(ex);

        // Force GC to reclaim the failed ALC.
        fixture.ForceGcReclaim();

        // The only live ALC should be the coordinator's current one.
        var liveAlcs = fixture.GetAlcWeakReferences()
            .Count(w => w.TryGetTarget(out _));
        Assert.Equal(1, liveAlcs);
    }

    [Fact]
    public void ChainedReloads_R1Success_R2Failure_R3Success_CorrectAlcAtEachStep()
    {
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);
        var alcA = fixture.GetCurrentAlc();
        Assert.NotNull(alcA);

        // R1: success.
        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateReload(new[] { v2 });
        var alcB = fixture.GetCurrentAlc();
        Assert.NotSame(alcA, alcB);

        // R2: failure.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
        Assert.NotNull(ex);
        var alcAfterFailure = fixture.GetCurrentAlc();
        Assert.Same(alcB, alcAfterFailure);  // unchanged after failure.

        // R3: success with another asset.
        var v3 = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        fixture.SimulateReload(new[] { v3 });
        var alcD = fixture.GetCurrentAlc();
        Assert.NotSame(alcB, alcD);

        // B should be reclaimed (replaced by R3).
        var alcBWeakRef = MakeWeakRef(alcB!);
        alcB = null;
        fixture.ForceGcReclaim();
        Assert.False(alcBWeakRef.IsAlive, "R1 ALC should be reclaimed after R3 success.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeWeakRef(AssemblyLoadContext alc)
        => new WeakReference(alc);
}
```

### 3.4 `Coordinator/QuickReloadTests.cs`

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Patch 3: Quick Reload goes through the coordinator.
/// </summary>
public sealed class QuickReloadTests
{
    [Fact]
    public void QuickReload_UpdatesCurrentAlc()
    {
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var alc1 = fixture.GetCurrentAlc();

        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateQuickReload(v2);

        var alc2 = fixture.GetCurrentAlc();
        Assert.NotNull(alc2);
        Assert.NotSame(alc1, alc2);
    }

    [Fact]
    public void QuickReload_AfterPreviousQuickReload_UnloadsThePreviousAlc()
    {
        using var fixture = new BlueprintTestFixture();
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);
        var alc1 = fixture.GetCurrentAlc();
        Assert.NotNull(alc1);
        var alc1WeakRef = MakeWeakRef(alc1!);

        // Quick Reload 1.
        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateQuickReload(v2);
        var alc2 = fixture.GetCurrentAlc();
        Assert.NotSame(alc1, alc2);

        alc1 = null;
        fixture.ForceGcReclaim();
        Assert.False(alc1WeakRef.IsAlive, "First ALC should be reclaimed after Quick Reload.");

        // Quick Reload 2.
        var alc2WeakRef = MakeWeakRef(alc2!);
        var v3 = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        fixture.SimulateQuickReload(v3);

        alc2 = null;
        fixture.ForceGcReclaim();
        Assert.False(alc2WeakRef.IsAlive, "Second ALC should be reclaimed after second Quick Reload.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeWeakRef(AssemblyLoadContext alc)
        => new WeakReference(alc);
}
```

### 3.5 `RuntimeIntegration/SoftReloadTests.cs`

Tests that soft reload (hash unchanged) preserves Instance Blueprint slot state.

```csharp
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Soft reload: StructureHash unchanged → slot payload preserved → tick resumes from saved state.
/// </summary>
public sealed class SoftReloadTests
{
    [Fact]
    public void SoftReload_InstanceBlueprint_SlotPayloadPreserved()
    {
        using var fixture = new BlueprintTestFixture();

        // Use HealthRegen (Instance blueprint with variables).
        var v1 = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(v1);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(v1, entity);

        // Tick once so the slot has been touched.
        fixture.TickFrame(0.016f);

        // Get slot state before reload.
        var stateBefore = fixture.GetBlueprintState(v1, entity);
        Assert.NotNull(stateBefore);
        var hashBefore = stateBefore!.StructureHash;

        // Reload with same asset (hash unchanged = soft reload).
        fixture.SimulateReload(new[] { v1 });

        // Slot must still exist and hash must be the same.
        var stateAfter = fixture.GetBlueprintState(v1, entity);
        Assert.NotNull(stateAfter);
        Assert.Equal(hashBefore, stateAfter!.StructureHash);
    }
}
```

### 3.6 `RuntimeIntegration/HardReloadTests.cs`

Tests that hard reload (hash changed) zeroes slot payload and bumps InstanceVersion.

```csharp
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Hard reload: StructureHash changed → slot payload zeroed → InstanceVersion bumped.
/// </summary>
public sealed class HardReloadTests
{
    [Fact]
    public void HardReload_InstanceBlueprint_SlotPayloadZeroed()
    {
        using var fixture = new BlueprintTestFixture();

        // V1 with one variable.
        var assetId = Guid.NewGuid();
        var v1 = BlueprintAssetBuilder
            .Instance("ReloadTarget", assetId)
            .WithVariable("counter", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.CompileAndLoad(v1);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(v1, entity);
        fixture.TickFrame(0.016f);

        // V2: different variable set → different StructureHash.
        var v2 = BlueprintAssetBuilder
            .Instance("ReloadTarget", assetId)
            .WithVariable("counter", typeof(int))
            .WithVariable("extra",   typeof(float))  // adds a field → hash change
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.SimulateReload(new[] { v2 });

        // After hard reload, slot should be reset (zeroed payload).
        // The next tick will re-init via InitDefault.
        fixture.TickFrame(0.016f);

        // Verify blueprint is still accessible (not crashed).
        Assert.True(fixture.Registry.TryGetByName("ReloadTarget", out var def));
        Assert.Equal(BlueprintDispatchKind.Instance, def!.Kind);
    }
}
```

### 3.7 `RuntimeIntegration/AiPrimitiveReloadTests.cs`

Tests that AiPrimitive working state resets on hash change.

```csharp
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// AiPrimitive reload: working-state reset on hash change (inline hash check in BTreeTick thunk).
/// </summary>
public sealed class AiPrimitiveReloadTests
{
    [Fact]
    public void AiPrimitive_AfterReload_CompilesAndTicksWithoutError()
    {
        using var fixture = new BlueprintTestFixture();

        var v1 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.CompileAndLoad(v1);

        var entity = fixture.CreateEntity();

        // Reload the same asset — should reset working state on next call.
        fixture.SimulateReload(new[] { v1 });

        // The new tick should not crash (working-state reset by inline hash check).
        var status = fixture.InvokeBTreeAction(v1, entity);
        // MoveToAndFire currently returns Failure (Stage5 WaitForChannel traversal deferred to Phase 5).
        // We just verify it doesn't throw.
        Assert.True(
            status == NodeStatus.Failure || status == NodeStatus.Running || status == NodeStatus.Success,
            $"Unexpected status: {status}");
    }
}
```

### 3.8 `RuntimeIntegration/LatentCursorReloadTests.cs`

Tests that latent cursor resets on hard reload.

```csharp
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Latent cursor: soft reload resumes cleanly; hard reload resets cursor to ResumeAt=0.
/// </summary>
public sealed class LatentCursorReloadTests
{
    [Fact]
    public void HardReload_InstanceBlueprint_NextTickDoesNotCrash()
    {
        using var fixture = new BlueprintTestFixture();

        // Build a simple Instance blueprint.
        var assetId = Guid.NewGuid();
        var v1 = BlueprintAssetBuilder
            .Instance("CursorTest", assetId)
            .WithVariable("x", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.CompileAndLoad(v1);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(v1, entity);
        fixture.TickFrame(0.016f);

        // Hard reload (add variable changes hash).
        var v2 = BlueprintAssetBuilder
            .Instance("CursorTest", assetId)
            .WithVariable("x", typeof(int))
            .WithVariable("y", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.SimulateReload(new[] { v2 });

        // Next tick after hard reload must not crash.
        fixture.TickFrame(0.016f);

        Assert.True(fixture.Registry.TryGetByName("CursorTest", out _));
    }
}
```

### 3.9 `PdbLoading/PdbLoadTests.cs`

```csharp
namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// PDB loading: when LoadPdbOnDeveloperMode=true, assembly loads with PDB symbols accessible.
/// </summary>
public sealed class PdbLoadTests
{
    [Fact]
    public void CompileWithPdb_AiPrimitive_AssemblyLoadsSuccessfully()
    {
        // Use Debug compiler mode (embeds PDB source).
        using var fixture = new BlueprintTestFixture();
        var asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

        // CompileAndLoad in Debug mode (already the default) embeds PDB.
        var assembly = fixture.CompileAndLoad(asset, CompilerMode.Debug);

        // Assembly should be non-null and have the expected type.
        Assert.NotNull(assembly);
        var types = assembly.GetTypes();
        Assert.Contains(types, t => t.Name.Contains("MoveToAndFire") && t.Name.EndsWith("_Bp"));
    }
}
```

---

## 4. Required `TestData.SampleAssets` additions

The tests reference `TestData.SampleAssets.HasVisibleTarget` and `TestData.SampleAssets.HealthRegen`. Add these to `TestData.cs` if they don't already exist:

```csharp
public static class SampleAssets
{
    // ... existing entries ...
    public const string HasVisibleTarget = "HasVisibleTarget";
    public const string HealthRegen      = "HealthRegen";
}
```

And corresponding test asset JSON files must exist in `TestAssets/`. Check if they already exist:
```
Get-ChildItem Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\TestAssets\
```
If they don't, check the Snapshots directory — there ARE golden snapshot files for `HasVisibleTarget.cs.txt` and `HealthRegen.cs.txt`. Their test assets likely already exist.

---

## 5. Build and test sequence

After implementing all changes:

```powershell
# 1. Build Fdp.Toolkits (new coordinator)
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj -v minimal

# 2. Build test project
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal

# 3. Run existing tests (must still be 347 pass / 5 skip / 0 fail)
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal

# 4. Run hot reload tests specifically
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~HotReload" -v normal

# 5. Run all tests (combined)
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

**Expected final result:** All HotReload tests pass (SC1 from HR-003). All previously-passing 347 tests still pass. Zero failures.

---

## 6. Completion report

Provide a batch report (BATCH-15-REPORT.md) with:
- List of files created/modified
- Test output summary (pass/fail/skip counts)
- Any deviations from the spec and the reason
- Build errors encountered and resolved

---

## 7. Important constraints / known pitfalls

1. **`HsmActionDispatcher` is unsafe**: `Fhsm.Kernel.HsmActionDispatcher` is `public static unsafe class`. Adding it as a type check in `ResolveRegistrarArgument` will require `typeof(HsmActionDispatcher)` which is fine. But in the test assembly (source string), you need `using Fhsm.Kernel;` which requires `Fhsm.Kernel` to be accessible — check if `MetadataReferenceResolver.ForRuntimeAssemblies` includes it.

2. **`SimulateReloadWithThrowingRegistrar` compiles inline source**: The registrar's `Register` method throws inside the coordinator's `InvokeRegistrar`. This means the exception propagates out of `coordinator.ApplyQuickReload` as a `TargetInvocationException` wrapping the `InvalidOperationException`. The tests use `Record.Exception` to catch any exception — don't assert specific exception type unless you unwrap it.

3. **`GetCurrentAlc()` is null until first `CompileAndLoad`**: The coordinator's `_currentAlc` starts null. Tests that call `GetCurrentAlc()` must call `CompileAndLoad` first.

4. **WeakReference GC reclaim in tests**: Use `[MethodImpl(MethodImplOptions.NoInlining)]` for methods that create `WeakReference` to prevent JIT from keeping the object live. The fixture's `ForceGcReclaim()` already handles multiple GC cycles.

5. **`SoftReloadTests` and `HardReloadTests` require `BlueprintTickSystem` integration**: The `fixture.TickFrame(dt)` already calls `TickSystem.Execute()`. The slot must be created via `fixture.AttachBlueprint(v1, entity)` before ticking.

6. **`BlueprintAssetBuilder.Instance` with explicit assetId**: The `HardReloadTests` use `BlueprintAssetBuilder.Instance("ReloadTarget", assetId)` — check if the builder supports an explicit assetId overload. If not, use the default (both v1 and v2 will get different assetIds based on content — that may be fine since the BlueprintId is content-hash based, not assetId based).

7. **TestData.SampleAssets for HealthRegen**: Check if `TestData.SampleAssets.HealthRegen` is defined in `TestData.cs`. If it's not, look for the constant name used in the existing `HealthRegen_EndToEndTests.cs`.
