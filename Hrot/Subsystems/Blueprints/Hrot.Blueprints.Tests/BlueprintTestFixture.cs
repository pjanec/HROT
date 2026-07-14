using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Debug;
using Hrot.Blueprints.Tests.Mocks;
using BlueprintCompiler = Hrot.Blueprints.Core.Compiler.BlueprintCompiler;
using InMemoryRoslynCompiler = Hrot.Blueprints.Core.Compiler.Roslyn.InMemoryRoslynCompiler;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Central per-test fixture that wires all Blueprint infrastructure for integration tests.
/// Manages entity repositories, mock views, tick systems, debug sessions, and collectible
/// ALCs. Dispose triggers ALC unload and optional GC-reclaim verification.
/// </summary>
public sealed class BlueprintTestFixture : IDisposable
{
    // ---- Public properties --------------------------------------------------

    public EntityRepository World { get; }
    public MockSimulationView View { get; }
    public MockEntityCommandBuffer Ecb { get; }
    public BlueprintRegistry Registry { get; }
    public BehaviorRegistry BehaviorRegistry { get; }
    public BlueprintTickSystem TickSystem { get; }
    public BlueprintMaintenanceSystem MaintenanceSystem { get; }
    public BlueprintCompiler Compiler { get; }
    public CapturingDebugSession DebugSession { get; }

    /// <summary>
    /// I1: the FastBTree action registry that BTree-hosted AiPrimitive registrars (and the JSON
    /// bridge registrars) populate. Exposed so a test can build an <see cref="Interpreter{TBlackboard,TContext}"/>
    /// that binds a blueprint-authored action by its registered key and tick it for real.
    /// </summary>
    public ActionRegistry<BrainBlackboard, BTreeContext> ActionRegistry { get; } = new();

    /// <summary>
    /// When set, passed to generated registrars that declare an IPredicateCompiler parameter.
    /// Null (default) means predicates compile in degraded mode (delegate fields stay null).
    /// </summary>
    public Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler? PredicateCompiler { get; set; }

    /// <summary>
    /// When set, passed to generated registrars that declare an ISearchPredicateRegistry parameter.
    /// </summary>
    public Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry? PredicateRegistry { get; set; }

    // ---- Private state ------------------------------------------------------

    private readonly BlueprintTestFixtureOptions _options;
    private readonly EntityRepository _repo;
    private readonly List<WeakReference<AssemblyLoadContext>> _alcWeakRefs = new();
    private readonly List<AssemblyLoadContext> _activeAlcs = new();
    private readonly List<IEcsModuleSystem> _auxSimulationSystems = new();
    private readonly AiHotReloadCoordinator _coordinator;
    private Action<ISimulationView, IEntityCommandBuffer>? _tickActions;

    // Persistent working-state per (assetId, entity) for TickCore reflection invocation.
    private readonly Dictionary<(Guid assetId, Entity entity), object> _persistedWorkingState = new();

    // Tracks the most recently applied blueprint id so GetCurrentAlc() can return
    // the ALC for that id from the coordinator's per-blueprint map.
    private int _lastAppliedBlueprintId;

    // ---- Constructor --------------------------------------------------------

    public BlueprintTestFixture(BlueprintTestFixtureOptions? options = null)
    {
        _options = options ?? BlueprintTestFixtureOptions.Default;
        _repo = new EntityRepository();
        World = _repo;
        Ecb = new MockEntityCommandBuffer(_repo);
        View = new MockSimulationView(_repo, Ecb);
        Registry = new BlueprintRegistry();
        BehaviorRegistry = new BehaviorRegistry();
        DebugSession = new CapturingDebugSession();
        TickSystem = new BlueprintTickSystem(Registry);
        MaintenanceSystem = new BlueprintMaintenanceSystem();
        Compiler = new BlueprintCompiler();

        _coordinator = new AiHotReloadCoordinator(
            BehaviorRegistry,
            Registry,
            new AiHotReloadCoordinatorOptions());

        MockTestComponents.Register(_repo);
        _repo.RegisterComponent<BlueprintBlackboard1024>();
        _repo.RegisterComponent<BlueprintBlackboard4096>();
        // BlueprintBlackboard16384 (16 384 bytes) would require ~16 GB of virtual-address
        // reservation for MAX_ENTITIES = 1 000 000, which exceeds the paranoid-mode cap in
        // NativeMemoryAllocator.  Tests that need BB16384 must use a standalone fixture.

        // Register behavior channel components needed for end-to-end compiled blueprint tests.
        _repo.RegisterComponent<LocomotionChannel>();
        _repo.RegisterComponent<WeaponChannel>();
        _repo.RegisterComponent<InteractionChannel>();
        _repo.RegisterComponent<BrainBlackboard>();
        _repo.RegisterComponent<Blackboard1024>();   // FBT behavior blackboard (AiPrimitive working state)

        DebugProbe.Sink = DebugSession;   // route generated probe calls to the capturing session
    }

    // ---- Tick ---------------------------------------------------------------

    /// <summary>
    /// Advances the simulation by one frame: SwapBuffers, sim systems, maintenance,
    /// ECB playback, then mid-tick inspection hook.
    /// Order follows Patch 1 + Patch 2 from Test Harness DD inline patches.
    /// </summary>
    public void TickFrame(float deltaTime)
    {
        // 1. Advance event bus so events published last frame become readable this frame
        _repo.Bus.SwapBuffers();

        // 2. Advance simulation time
        View.AdvanceTime(deltaTime);

        // 3. Simulation phase
        // Inject the fixture's MockEntityCommandBuffer so blueprints get EAGER entity
        // creation semantics (CreateEntity returns a real entity, not a deferred placeholder).
        // Pass _repo (EntityRepository) so BlueprintTickSystem can cast for write access.
        // Also sync repo simulation time so view.Time is accurate for tick delegates.
        _repo.SetSimulationTime(View.Time);
        _repo.SetCommandBufferOverride(Ecb);
        TickSystem.Execute(_repo, deltaTime);
        foreach (var sys in _auxSimulationSystems)
            sys.Execute(_repo, deltaTime);  // pass EntityRepository so MockDispatcherSystem can cast for write access
        _repo.SetCommandBufferOverride(null);

        // 4. BeforeSync phase
        MaintenanceSystem.Execute(_repo, deltaTime);

        // 5. Sync phase: flush any deferred ops from production-path ECBs (safety), then
        //    play back the fixture mock ECB (test-injected ops and ops from simulation systems).
        _repo.FlushCommandBuffers();
        Ecb.Playback(_repo);

        // 6. Mid-tick inspection hook (after everything settled)
        _tickActions?.Invoke(View, Ecb);
    }

    // ---- System registration helpers ----------------------------------------

    public void RegisterTickAction(Action<ISimulationView, IEntityCommandBuffer> action)
        => _tickActions += action;

    public void AddSimulationSystem(IEcsModuleSystem system)
        => _auxSimulationSystems.Add(system);

    // ---- Compile and load ---------------------------------------------------

    /// <summary>
    /// Compiles one Blueprint asset and loads it into a new collectible ALC.
    /// Requires Phase 3 compiler -- throws NotImplementedException in Phase 1.
    /// </summary>
    public Assembly CompileAndLoad(BlueprintAsset asset, CompilerMode mode = CompilerMode.Debug)
        => CompileAndLoadCore(new[] { asset }, MakeDefaultOptions(mode));

    /// <summary>
    /// Compiles one Blueprint asset with custom CompileOptions.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Assembly CompileAndLoad(BlueprintAsset asset, CompileOptions options)
        => CompileAndLoadCore(new[] { asset }, options);

    /// <summary>
    /// Compiles multiple Blueprint assets and loads them into a new collectible ALC.
    /// Requires Phase 3 compiler -- throws NotImplementedException in Phase 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Assembly CompileAndLoadMany(
        IReadOnlyList<BlueprintAsset> assets,
        CompilerMode mode = CompilerMode.Debug)
        => CompileAndLoadCore(assets, MakeDefaultOptions(mode));

    private static CompileOptions MakeDefaultOptions(CompilerMode mode) => new CompileOptions(
        Mode:              mode,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Core implementation shared by all CompileAndLoad overloads.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Assembly CompileAndLoadCore(
        IReadOnlyList<BlueprintAsset> assets,
        CompileOptions options)
    {
        var sink = new DiagnosticSink();

        var generatedSources = new List<string>(assets.Count);
        foreach (var asset in assets)
        {
            var result = Compiler.Compile(asset, options);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Blueprint '{asset.Name}' failed to compile: " +
                    string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            generatedSources.Add(result.GeneratedSource!);
        }

        // Merge all generated sources into a single valid C# compilation unit.
        // Each source uses a file-scoped namespace; concatenating them raw would
        // produce CS1529/CS8954. MergeGeneratedSources combines usings and wraps
        // all type declarations under a single block-scoped namespace.
        var mergedSource = generatedSources.Count == 1
            ? generatedSources[0]
            : MergeGeneratedSources(generatedSources);

        var assemblyName = $"Bp_{Guid.NewGuid():N}";
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var roslynCompiler = new InMemoryRoslynCompiler(resolver);
        var (assembly, alc) = roslynCompiler.CompileAndLoad(
            mergedSource,
            $"{assemblyName}.g.cs",
            assemblyName,
            sink);

        _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));

        // Hand off to coordinator so _currentAlc is tracked.
        ApplyQuickReloadFromAssembly(alc, assembly);
        return assembly;
    }

    // ---- Test-only ALC bypass -----------------------------------------------

    /// <summary>
    /// Merges multiple generated C# source files (each with file-scoped namespace) into a
    /// single valid compilation unit. The generated sources each contain using directives,
    /// a file-scoped namespace declaration, and type declarations. Concatenating them raw
    /// would produce CS1529/CS8954. This method collects all unique usings, extracts the
    /// common namespace, and wraps all type declarations in a single block-scoped namespace.
    /// </summary>
    private static string MergeGeneratedSources(IReadOnlyList<string> sources)
    {
        var allUsings = new SortedSet<string>(StringComparer.Ordinal);
        string? namespaceName = null;
        var typeCode = new StringBuilder();

        foreach (var source in sources)
        {
            bool pastNamespace = false;
            foreach (var rawLine in source.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (!pastNamespace)
                {
                    if (line.StartsWith("namespace ", StringComparison.Ordinal) &&
                        line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                    {
                        // File-scoped namespace declaration.
                        namespaceName ??= line.Trim().TrimEnd(';').Substring("namespace ".Length);
                        pastNamespace = true;
                    }
                    else if (line.StartsWith("using ", StringComparison.Ordinal))
                    {
                        allUsings.Add(line.Trim());
                    }
                    // Skip comment lines and blank lines before namespace.
                }
                else
                {
                    typeCode.AppendLine(line);
                }
            }
        }

        var sb = new StringBuilder();
        foreach (var u in allUsings)
        {
            sb.AppendLine(u);
        }
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName ?? "Hrot.AI.Behaviors.Generated"}");
        sb.AppendLine("{");
        sb.Append(typeCode);
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Test-only ALC bypass: loads raw PE bytes into a new collectible ALC and
    /// registers it for GC-reclaim tracking. Used by ALC lifecycle tests when the
    /// Blueprint compiler is not yet available.
    /// </summary>
    internal Assembly LoadTestAssemblyFromBytes(byte[] peBytes)
    {
        var assemblyName = $"TestAlc_{Guid.NewGuid():N}";
        var alc = CreateCollectibleAlc(assemblyName);
        using var ms = new MemoryStream(peBytes);
        return alc.LoadFromStream(ms);
    }

    private AssemblyLoadContext CreateCollectibleAlc(string name)
    {
        var alc = new AssemblyLoadContext(name, isCollectible: true);
        _activeAlcs.Add(alc);
        _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));
        return alc;
    }

    // Test-only: removes a specific ALC from active tracking and initiates unload.
    // Mirrors what SimulateReload does for old-generation ALCs (Phase 3+).
    // Use inside a [NoInlining] helper to avoid Debug-JIT pinning (see DEBT-009).
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void UnloadAndReleaseAlc(AssemblyLoadContext alc)
    {
        _activeAlcs.Remove(alc);
        alc.Unload();
    }

    // ---- Simulate reload ----------------------------------------------------

    /// <summary>
    /// Compiles the given assets, loads them into a new collectible ALC,
    /// and applies the reload through the coordinator (Patch 3 path).
    /// Old ALC is unloaded by the coordinator after successful commit.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
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

        var reloadSources = new List<string>(newVersions.Count);
        foreach (var asset in newVersions)
        {
            var result = Compiler.Compile(asset, options);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Blueprint '{asset.Name}' failed to compile: " +
                    string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            reloadSources.Add(result.GeneratedSource!);
        }

        var reloadMergedSource = reloadSources.Count == 1
            ? reloadSources[0]
            : MergeGeneratedSources(reloadSources);

        // Compile to PE bytes via Roslyn.
        var assemblyName = $"Bp_{Guid.NewGuid():N}";
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var roslynCompiler = new InMemoryRoslynCompiler(resolver);
        var (assembly, alc) = roslynCompiler.CompileAndLoad(
            reloadMergedSource,
            $"{assemblyName}.g.cs",
            assemblyName,
            sink);

        // Track ALC for GC-reclaim verification.
        _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));

        // Hand off to coordinator (Patch 3) — coordinator owns ALC lifecycle.
        ApplyQuickReloadFromAssembly(alc, assembly);
    }

    /// <summary>Single-asset convenience wrapper for SimulateReload.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SimulateQuickReload(BlueprintAsset asset)
        => SimulateReload(new[] { asset });

    /// <summary>
    /// Returns the ALC for the most recently applied blueprint reload.
    /// Used by hot reload tests to verify ALC identity across reloads.
    /// </summary>
    public AssemblyLoadContext? GetCurrentAlc()
        => _lastAppliedBlueprintId != 0
            ? _coordinator.GetRetainedAlcForTest(_lastAppliedBlueprintId)
            : null;

    /// <summary>
    /// Compiles a minimal assembly with a [BlueprintRegistrar] whose Register method
    /// throws InvalidOperationException. Used to test failure-rollback behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
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
        ApplyQuickReloadFromAssembly(alc, assembly);
    }

    /// <summary>
    /// Test-only: calls coordinator.ApplyQuickReload with a pre-built ALC.
    /// Tracks the ALC for GC-reclaim verification. Throws on registrar errors.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void SimulateReloadFromAlc(AssemblyLoadContext alc, Assembly assembly)
    {
        _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));
        ApplyQuickReloadFromAssembly(alc, assembly);
    }

    // Scans registrars from the assembly, invokes them into staging buffers, then
    // calls coordinator.ApplyQuickReload with the populated staging.  Mirrors the
    // pipeline in QuickReloadService so test helpers stay consistent with production.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ApplyQuickReloadFromAssembly(AssemblyLoadContext alc, Assembly assembly)
    {
        HsmActionDispatcher.ClearAll();
        var behaviorStaging  = new BehaviorRegistry();
        var blueprintStaging = new BlueprintRegistryStaging();

        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() == null)
                    continue;

                var method = type.GetMethod("Register",    BindingFlags.Public | BindingFlags.Static)
                          ?? type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static);
                if (method == null) continue;

                var paramInfos = method.GetParameters();
                var args = new object[paramInfos.Length];
                for (int i = 0; i < paramInfos.Length; i++)
                {
                    if (paramInfos[i].ParameterType == typeof(BlueprintRegistryStaging))
                        args[i] = blueprintStaging;
                    else if (paramInfos[i].ParameterType == typeof(BehaviorRegistry))
                        args[i] = behaviorStaging;
                    // I1: BTree-hosted AiPrimitive registrars register their thunks here.
                    else if (paramInfos[i].ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>))
                        args[i] = ActionRegistry;
                    // Patch 4: BlueprintRegistry is forbidden — violates the RCU contract.
                    else if (paramInfos[i].ParameterType == typeof(BlueprintRegistry))
                        throw new HotReloadRegistrarException(
                            "Registrar requests BlueprintRegistry as a parameter, but only " +
                            "BlueprintRegistryStaging may be injected. Direct access to the live " +
                            "registry would violate the atomic RCU contract. " +
                            "Change the registrar's parameter to BlueprintRegistryStaging.");
                    // Patch 2: HsmActionDispatcher is a static class — cannot be injected.
                    else if (paramInfos[i].ParameterType == typeof(HsmActionDispatcher))
                        throw new HotReloadRegistrarException(
                            "Registrar requests HsmActionDispatcher as a parameter, but it is a " +
                            "static class and cannot be injected. " +
                            "Call HsmActionDispatcher.RegisterAction statically from inside Register.");
                    else if (paramInfos[i].ParameterType == typeof(Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler))
                        args[i] = PredicateCompiler;
                    else if (paramInfos[i].ParameterType == typeof(Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry))
                        args[i] = PredicateRegistry;
                    else
                        throw new HotReloadRegistrarException(
                            $"Unknown registrar parameter type: {paramInfos[i].ParameterType.FullName}. " +
                            "Supported: BlueprintRegistryStaging, BehaviorRegistry.");
                }
                method.Invoke(null, args);
            }

            _coordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging);
            // Track the last reloaded blueprint id for GetCurrentAlc().
            foreach (var id in blueprintStaging.StagedBlueprintIds)
                _lastAppliedBlueprintId = id;
        }
        catch
        {
            // Coordinator takes ownership on success; on failure we must unload here.
            try { alc.Unload(); } catch { /* best-effort */ }
            throw;
        }
    }

    // ---- Invoke helpers ----------------------------------------------------

    public NodeStatus InvokeBTreeAction(BlueprintAsset asset, Entity entity, int paramIndex = 0)
    {
        var genType = FindGeneratedType(asset);
        var paramsType = genType.GetNestedType("Params")
            ?? throw new InvalidOperationException($"No Params nested type in {genType.Name}");
        var wsType = genType.GetNestedType("WorkingState")
            ?? throw new InvalidOperationException($"No WorkingState nested type in {genType.Name}");
        var tickCore = genType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"No TickCore method in {genType.Name}");

        var stateKey = (asset.AssetId, entity);
        if (!_persistedWorkingState.TryGetValue(stateKey, out var wsBoxed))
            wsBoxed = Activator.CreateInstance(wsType)!;

        var paramsBoxed = Activator.CreateInstance(paramsType)!;
        var args = new object?[] { paramsBoxed, wsBoxed, entity, World, View.Time };
        var rawStatus = tickCore.Invoke(null, args)!;
        // TickCore now returns global::Fbt.NodeStatus; convert by name so tests keep using
        // the compiler's NodeStatus enum without caring about differing ordinals.
        var status = (NodeStatus)Enum.Parse(typeof(NodeStatus), rawStatus.ToString()!);

        // args[1] contains the updated WorkingState after invocation (ref param updated in-place).
        _persistedWorkingState[stateKey] = args[1]!;

        return status;
    }

    public unsafe bool InvokeHsmAction(BlueprintAsset asset, Entity entity)
    {
        var genType = FindGeneratedType(asset);

        // The generated registrar calls:
        // HsmActionDispatcher.RegisterAction(unchecked((ushort)ClassName.BlueprintId), ...)
        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        ushort actionId = unchecked((ushort)blueprintId);

        // Generated HsmActivity reads Blackboard1024 from the entity; ensure it exists.
        if (!_repo.HasComponent<Blackboard1024>(entity))
            _repo.AddComponent(entity, default(Blackboard1024));

        var bridge = new HsmKernelBridge { Self = entity, WorldHandle = _repo.UnmanagedHandle };

        var paramsType = genType.GetNestedType("Params");
        if (paramsType != null && paramsType.IsValueType)
        {
            var paramsBoxed = Activator.CreateInstance(paramsType)!;
            var paramsHandle = GCHandle.Alloc(paramsBoxed, GCHandleType.Pinned);
            try
            {
                void* paramsPtr = (void*)paramsHandle.AddrOfPinnedObject();
                HsmActionDispatcher.ExecuteAction(actionId, paramsPtr, &bridge, null);
            }
            finally
            {
                paramsHandle.Free();
            }
        }
        else
        {
            HsmActionDispatcher.ExecuteAction(actionId, null, &bridge, null);
        }
        return true;
    }

    public unsafe bool InvokeHsmGuard(BlueprintAsset asset, Entity entity, ushort eventId = 0)
    {
        var genType = FindGeneratedType(asset);

        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        ushort guardId  = unchecked((ushort)blueprintId);

        // Generated HsmGuard reads Blackboard1024 from the entity; ensure it exists.
        if (!_repo.HasComponent<Blackboard1024>(entity))
            _repo.AddComponent(entity, default(Blackboard1024));

        var bridge = new HsmKernelBridge { Self = entity, WorldHandle = _repo.UnmanagedHandle };

        var paramsType = genType.GetNestedType("Params");
        if (paramsType != null && paramsType.IsValueType)
        {
            var paramsBoxed = Activator.CreateInstance(paramsType)!;
            var paramsHandle = GCHandle.Alloc(paramsBoxed, GCHandleType.Pinned);
            try
            {
                void* paramsPtr = (void*)paramsHandle.AddrOfPinnedObject();
                return HsmActionDispatcher.EvaluateGuard(guardId, paramsPtr, &bridge, eventId);
            }
            finally
            {
                paramsHandle.Free();
            }
        }
        else
        {
            return HsmActionDispatcher.EvaluateGuard(guardId, null, &bridge, eventId);
        }
    }

    private Type FindGeneratedType(BlueprintAsset asset)
    {
        var prefix = SanitizeNameForClass(asset.Name) + "_";

        // Search all coordinator-retained ALCs (normal CompileAndLoad path).
        // The per-blueprint map may hold ALCs for several blueprints simultaneously.
        foreach (var retainedAlc in _coordinator.GetAllRetainedAlcsForTest())
        {
            foreach (var asm in retainedAlc.Assemblies)
            {
                var t = asm.GetTypes().FirstOrDefault(
                    t => t.Name.StartsWith(prefix, StringComparison.Ordinal)
                      && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
                if (t != null) return t;
            }
        }

        // Fallback to _activeAlcs (for LoadTestAssemblyFromBytes path).
        foreach (var alc in _activeAlcs)
            foreach (var asm in alc.Assemblies)
            {
                var t = asm.GetTypes().FirstOrDefault(
                    t => t.Name.StartsWith(prefix, StringComparison.Ordinal)
                      && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
                if (t != null) return t;
            }
        throw new InvalidOperationException($"No generated blueprint type found for '{asset.Name}'.");
    }

    private static string SanitizeNameForClass(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.ToString();
    }

    // ---- Slot inspection helpers --------------------------------------------

    public bool HasSlot(BlueprintAsset asset, Entity entity)
    {
        return TryGetSlotAcrossTiers(asset.AssetId, entity, out _, out _);
    }

    public unsafe BlueprintStateView? GetBlueprintState(BlueprintAsset asset, Entity entity)
    {
        if (!Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def))
            return null;
        if (!TryGetSlotAcrossTiers(asset.AssetId, entity, out var tier, out var payloadOffset))
            return null;

        GetTierMemoryAndMeta(entity, tier, out byte* memory, out _, out _);
        return new BlueprintStateView(memory + payloadOffset, def!.StateSize, def!);
    }

    private unsafe bool TryGetSlotAcrossTiers(
        Guid assetId, Entity entity,
        out BlackboardTier tier, out int payloadOffset)
    {
        int blueprintId = BlueprintIdHash.Compute(assetId);

        if (_repo.HasComponent<BlueprintBlackboard1024>(entity))
        {
            GetTierMemoryAndMeta(entity, BlackboardTier.B1024, out byte* memory, out _, out _);
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out payloadOffset))
            {
                tier = BlackboardTier.B1024;
                return true;
            }
        }
        if (_repo.HasComponent<BlueprintBlackboard4096>(entity))
        {
            GetTierMemoryAndMeta(entity, BlackboardTier.B4096, out byte* memory, out _, out _);
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out payloadOffset))
            {
                tier = BlackboardTier.B4096;
                return true;
            }
        }
        if (_repo.HasComponent<BlueprintBlackboard16384>(entity))
        {
            GetTierMemoryAndMeta(entity, BlackboardTier.B16384, out byte* memory, out _, out _);
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out payloadOffset))
            {
                tier = BlackboardTier.B16384;
                return true;
            }
        }

        tier = BlackboardTier.B1024;
        payloadOffset = -1;
        return false;
    }

    private unsafe void GetTierMemoryAndMeta(
        Entity entity, BlackboardTier tier,
        out byte* memory, out int totalSize, out byte maxSlots)
    {
        switch (tier)
        {
            case BlackboardTier.B1024:
            {
                ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                ref byte memRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
                memory    = (byte*)Unsafe.AsPointer(ref memRef);
                totalSize = BlueprintBlackboard1024.TotalSize;
                maxSlots  = BlueprintBlackboard1024.MaxSlots;
                return;
            }
            case BlackboardTier.B4096:
            {
                ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                ref byte memRef = ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb);
                memory    = (byte*)Unsafe.AsPointer(ref memRef);
                totalSize = BlueprintBlackboard4096.TotalSize;
                maxSlots  = BlueprintBlackboard4096.MaxSlots;
                return;
            }
            default:
            {
                ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                ref byte memRef = ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb);
                memory    = (byte*)Unsafe.AsPointer(ref memRef);
                totalSize = BlueprintBlackboard16384.TotalSize;
                maxSlots  = BlueprintBlackboard16384.MaxSlots;
                return;
            }
        }
    }

    // ---- Entity convenience -------------------------------------------------

    public Entity CreateEntity() => _repo.CreateEntity();

    // ---- Attach Blueprint ---------------------------------------------------

    public unsafe void AttachBlueprint(BlueprintAsset asset, Entity entity)
    {
        if (!Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def))
            throw new InvalidOperationException(
                $"Blueprint '{asset.Name}' not loaded into registry. Call CompileAndLoad first.");

        var tier = ChooseTier(def!.StateSize);
        EnsureTierComponent(entity, tier);

        GetTierMemoryAndMeta(entity, tier, out byte* memory, out int totalSize, out byte maxSlots);
        BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots);

        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        if (!BlueprintBlackboardPartitions.TryAttach(memory, blueprintId, def.StateSize, def.StructureHash, out int payloadOffset))
            throw new InvalidOperationException(
                $"Failed to attach Blueprint '{asset.Name}' to entity {entity} (tier {tier}).");

        if (def.InitDefault != null)
        {
            ref byte payloadRef = ref Unsafe.AsRef<byte>(memory + payloadOffset);
            var initSpan = MemoryMarshal.CreateSpan(ref payloadRef, def.StateSize);
            def.InitDefault(initSpan);
        }
    }

    internal static BlackboardTier ChooseTier(int stateSize)
    {
        if (stateSize <= 928)  return BlackboardTier.B1024;
        if (stateSize <= 3936) return BlackboardTier.B4096;
        return BlackboardTier.B16384;
    }

    private void EnsureTierComponent(Entity entity, BlackboardTier tier)
    {
        switch (tier)
        {
            case BlackboardTier.B1024:
                if (!_repo.HasComponent<BlueprintBlackboard1024>(entity))
                    _repo.AddComponent(entity, default(BlueprintBlackboard1024));
                break;
            case BlackboardTier.B4096:
                if (!_repo.HasComponent<BlueprintBlackboard4096>(entity))
                    _repo.AddComponent(entity, default(BlueprintBlackboard4096));
                break;
            case BlackboardTier.B16384:
                if (!_repo.HasComponent<BlueprintBlackboard16384>(entity))
                    _repo.AddComponent(entity, default(BlueprintBlackboard16384));
                break;
        }
    }

    // ---- BPF-008: Fixture helpers ------------------------------------------

    /// <summary>
    /// Returns a copy of the <see cref="BlueprintSlotEntry"/> for the given blueprint
    /// on the specified entity. Throws if no slot exists.
    /// </summary>
    public unsafe BlueprintSlotEntry GetSlotEntry(BlueprintAsset asset, Entity entity)
    {
        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        if (!TryGetSlotAcrossTiers(asset.AssetId, entity, out var tier, out _))
            throw new InvalidOperationException(
                $"No slot for blueprint '{asset.Name}' on entity {entity}. " +
                "Call AttachBlueprint first.");

        GetTierMemoryAndMeta(entity, tier, out byte* memory, out _, out _);

        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        int slotCount  = header.SlotCount;
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        for (int i = 0; i < slotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
            if (slot.BlueprintId == blueprintId)
                return slot;  // return copy
        }
        throw new InvalidOperationException(
            $"Slot table scan failed for blueprint '{asset.Name}' on entity {entity}.");
    }

    /// <summary>
    /// Writes <paramref name="status"/> into the <c>Status</c> field of channel component
    /// <typeparamref name="T"/> on <paramref name="entity"/>.
    /// </summary>
    public unsafe void SetChannelStatus<T>(Entity entity, Fbt.NodeStatus status) where T : unmanaged
    {
        int offset = (int)Marshal.OffsetOf<T>("Status");
        ref var component = ref _repo.GetComponentRW<T>(entity);
        byte* ptr = (byte*)Unsafe.AsPointer(ref component);
        Unsafe.Write(ptr + offset, status);
    }

    /// <summary>
    /// Returns a snapshot of all blackboard component bytes for all entities that have
    /// BB1024 or BB4096 components. Useful for before/after state comparison.
    /// Format per entity: [Index:int][Generation:int][bytes...]
    /// </summary>
    public unsafe ImmutableArray<byte> SnapshotAllBlackboards()
    {
        var ms     = new MemoryStream();
        var writer = new BinaryWriter(ms);

        var query1024 = _repo.Query().With<BlueprintBlackboard1024>().Build();
        foreach (var entity in query1024)
        {
            ref readonly var bb  = ref _repo.GetComponentRO<BlueprintBlackboard1024>(entity);
            byte* ptr            = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
            int   size           = Unsafe.SizeOf<BlueprintBlackboard1024>();
            writer.Write(entity.Index);
            writer.Write(entity.Generation);
            for (int i = 0; i < size; i++)
                writer.Write(ptr[i]);
        }

        var query4096 = _repo.Query().With<BlueprintBlackboard4096>().Build();
        foreach (var entity in query4096)
        {
            ref readonly var bb  = ref _repo.GetComponentRO<BlueprintBlackboard4096>(entity);
            byte* ptr            = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
            int   size           = Unsafe.SizeOf<BlueprintBlackboard4096>();
            writer.Write(entity.Index);
            writer.Write(entity.Generation);
            for (int i = 0; i < size; i++)
                writer.Write(ptr[i]);
        }

        writer.Flush();
        return ms.ToArray().ToImmutableArray();
    }

    // ---- GC helpers and weak reference inspection ---------------------------

    public IReadOnlyList<WeakReference<AssemblyLoadContext>> GetAlcWeakReferences()
        => _alcWeakRefs;

    public void ForceGcReclaim()
    {
        for (int i = 0; i < _options.GcReclaimRetries; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (AllAlcsReclaimed()) return;
            Thread.Sleep(_options.GcReclaimDelayMs);
        }
    }

    private bool AllAlcsReclaimed()
        => _alcWeakRefs.All(w => !w.TryGetTarget(out _));

    private bool TryReclaimAllAlcs(int maxRetries, int delayMs)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (AllAlcsReclaimed()) return true;
            if (_options.VerboseLeakDiagnostics)
            {
                int alive = _alcWeakRefs.Count(w => w.TryGetTarget(out _));
                Console.Error.WriteLine(
                    $"[ALC GC] retry {i + 1}/{maxRetries}: {alive} ALC(s) still alive");
            }
            if (i < maxRetries - 1) Thread.Sleep(delayMs);
        }
        return AllAlcsReclaimed();
    }

    // ---- IDisposable --------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnloadAndClearAlcs()
    {
        foreach (var alc in _activeAlcs)
            alc.Unload();
        _activeAlcs.Clear();
    }

    public void Dispose()
    {
        DebugProbe.Sink = NullProbeSink.Instance;   // release reference to this session
        HsmActionDispatcher.ClearAll();  // clear stale function pointers before ALC unload
        _coordinator.Dispose();          // unloads coordinator's current ALC + clears BehaviorRegistry
        _persistedWorkingState.Clear();  // release boxed working-state objects from collectible assemblies
        ActionRegistry.Clear();          // release AiPrimitive/bridge BTree thunk delegates (I1) from collectible assemblies
        Registry.CommitStaging(Registry.BeginStaging()); // release Tick/InitDefault delegates from collectible assemblies
        UnloadAndClearAlcs();
        // ALCs are unloaded and _activeAlcs is cleared; the foreach variable inside
        // UnloadAndClearAlcs is now off-stack, allowing the GC to reclaim them.

        if (_options.VerifyAlcUnloadOnDispose)
        {
            if (!TryReclaimAllAlcs(_options.GcReclaimRetries, _options.GcReclaimDelayMs))
            {
                int leaked = _alcWeakRefs.Count(w => w.TryGetTarget(out _));
                if (_options.VerboseLeakDiagnostics)
                {
                    // Best-effort diagnostic (stub ok for Phase 1)
                }
                throw new InvalidOperationException(
                    $"{leaked} ALC(s) not GC-reclaimed after {_options.GcReclaimRetries} retries. " +
                    $"Common causes: static fields, event subscriptions, or cached delegate " +
                    $"references pointing into the collectible assembly.");
            }
        }
    }
}
