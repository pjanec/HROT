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
    public ActionRegistry<byte, BTreeContext> ActionRegistry { get; } = new();

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

        RegisterWorldComponents(_repo);

        DebugProbe.Sink = DebugSession;   // route generated probe calls to the capturing session
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE component registration set for a fixture world — and for any SCRATCH world that
    /// has to receive its recordings.</b>
    ///
    /// <para>🔴 <b>Why this is a method and not eight lines in the constructor.</b> The sub-tick
    /// recorder replays a recorded frame into a scratch <c>EntityRepository</c>, and
    /// <c>PlaybackSystem</c> throws <i>"Component type ID N not found in repository"</i> for anything
    /// the source world had and the scratch world does not. ⇒ the two registration lists MUST match,
    /// and until now they were hand-mirrored in three places — ⛔ already imperfectly: the scratch
    /// builders never registered the three channel components, which only worked because no recorded
    /// entity happened to carry one.</para>
    ///
    /// <para>📌 <b>Measured <c>2026-09-21</c>:</b> adding <see cref="BehaviorState"/> here for
    /// <c>P3-C</c> reddened <c>SubTickRecorderIntegrationTests</c> and <c>VirtualPointerTests</c> with
    /// exactly that message — the fixture gained a component the scratch world did not know. ⭐ One
    /// list makes the class of failure impossible rather than fixing this instance of it.</para>
    /// </summary>
    public static void RegisterWorldComponents(EntityRepository repo)
    {
        MockTestComponents.Register(repo);

        // ⭐ B4: register from the LADDER, not a hand-list. ⛔ A hand-list silently leaves a
        //   newly-appended tier unregistered — O3b's 256 tier reddened 192 tests this way.
        //   The bound keeps this world's deliberate exclusion of the larger tiers (their
        //   virtual-address reservation exceeds the allocator's paranoid-mode cap).
        // ⚠ BlueprintBlackboard16384 (16 384 bytes) would require ~16 GB of virtual-address
        //   reservation for MAX_ENTITIES = 1 000 000, which exceeds the paranoid-mode cap in
        //   NativeMemoryAllocator. Tests that need BB16384 must use a standalone fixture.
        BlueprintTierTable.RegisterUpTo(repo, maxTotalSize: 4096);

        // Behavior channel components needed for end-to-end compiled blueprint tests.
        repo.RegisterComponent<LocomotionChannel>();
        repo.RegisterComponent<WeaponChannel>();
        repo.RegisterComponent<InteractionChannel>();
        repo.RegisterComponent<BrainBlackboard>();
        // ⛔⛔ P4-① (2026-09-22): the Blackboard1024 registration is GONE. ⚠ This line is why
        //    CE-311 looked green — the fixture was the ONE world where the inline AiPrimitive host's
        //    emitted GetComponentRW<Blackboard1024> could run. Working state lives in the tier ladder.

        // 🔴 P3-C (2026-09-21): an emitted thunk's SEED reads the entity's ROOT PARAMS SLOT, whose key
        //   comes from BehaviorState.ActiveBehaviorHash ⇒ the component is no longer optional here.
        repo.RegisterComponent<global::Fdp.Toolkit.Behavior.Components.BehaviorState>();
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

    /// <summary>
    /// Compiles multiple Blueprint assets with custom CompileOptions (e.g. cross-asset
    /// CallPeerBlueprint siblings that must be resolvable within the SAME merged compilation)
    /// and loads them into a new collectible ALC.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Assembly CompileAndLoadMany(
        IReadOnlyList<BlueprintAsset> assets,
        CompileOptions options)
        => CompileAndLoadCore(assets, options);

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
                    else if (paramInfos[i].ParameterType == typeof(ActionRegistry<byte, BTreeContext>))
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

    /// <summary>
    /// ⭐⭐⭐ <c>O7b</c> — invokes the asset's HSM thunk <b>through a real kernel dispatch</b>.
    ///
    /// <para>🔴 <b>Why it can no longer be called directly.</b> The thunk now keys its working state by
    /// the <c>(region, state)</c> pair the kernel stamps (<c>O6</c>) and by the hosting machine id in
    /// the instance header. ⛔ <c>HsmCommandWriter.StampOccurrence</c> is <b>internal to the kernel on
    /// purpose</b> — a harness that could forge a stamp could forge a cross-occurrence alias — so the
    /// only honest way to invoke one is to let the kernel dispatch it.</para>
    ///
    /// <para>⭐ <b>This makes the fixture more truthful, not less convenient:</b> it now exercises the
    /// production path (kernel → dispatcher → thunk → occurrence store) instead of a hand-rolled
    /// approximation of it. ⚠ The old shape passed a pinned <c>Params</c> box as <c>instance</c>, which
    /// is exactly the convention <c>CE-297</c> records as wrong.</para>
    /// </summary>
    public unsafe bool InvokeHsmAction(BlueprintAsset asset, Entity entity)
    {
        DispatchThroughKernel(asset, entity, asGuard: false, eventId: 0);
        return true;
    }

    /// <summary>The guard twin of <see cref="InvokeHsmAction"/> — same kernel dispatch, same reasons.</summary>
    public unsafe bool InvokeHsmGuard(BlueprintAsset asset, Entity entity, ushort eventId = 0)
        => DispatchThroughKernel(asset, entity, asGuard: true, eventId);

    /// <summary>
    /// Builds a one-state HSM whose entry action (or whose single guarded transition) is the asset's
    /// thunk, and ticks it once. ⭐ The kernel stamps <c>(region 0, state 0)</c> before the dispatch,
    /// which is the identity the thunk keys on.
    /// </summary>
    private unsafe bool DispatchThroughKernel(BlueprintAsset asset, Entity entity, bool asGuard, ushort eventId)
    {
        _ = FindGeneratedType(asset);   // keeps the "asset was compiled" precondition explicit

        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        ushort id = unchecked((ushort)blueprintId);

        // The thunk projects its params from BrainBlackboard (CE-297) and its working state from the
        // entity's occurrence store, so both must exist before the dispatch.
        if (!_repo.HasComponent<global::Fdp.Toolkit.Behavior.Components.BrainBlackboard>(entity))
            _repo.AddComponent(entity, default(global::Fdp.Toolkit.Behavior.Components.BrainBlackboard));
        EnsureOccurrenceStore(entity);

        const uint MachineId = 0x07B0A5E1;
        var states = new global::Fhsm.Kernel.Data.StateDef[1];
        var transitions = Array.Empty<global::Fhsm.Kernel.Data.TransitionDef>();

        if (asGuard)
        {
            // A guard only runs while a transition is being SELECTED, so give it one to guard — and
            // ⛔ NOT a self-transition: 0 -> 0 leaves the leaf unchanged whether the guard passed or
            //    not, so it cannot be the observable. 0 -> 1 can.
            states = new[]
            {
                new global::Fhsm.Kernel.Data.StateDef
                {
                    ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1,
                },
                new global::Fhsm.Kernel.Data.StateDef
                {
                    ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF,
                },
            };
            transitions = new[]
            {
                new global::Fhsm.Kernel.Data.TransitionDef
                {
                    SourceStateIndex = 0, TargetStateIndex = 1, EventId = eventId, GuardId = id,
                },
            };
        }
        else
        {
            states[0] = new global::Fhsm.Kernel.Data.StateDef
            {
                ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, OnEntryActionId = id,
            };
        }

        var header = new global::Fhsm.Kernel.Data.HsmDefinitionHeader
        {
            StructureHash = MachineId,
            StateCount = (ushort)states.Length,
            TransitionCount = (ushort)transitions.Length,
        };
        var blob = new global::Fhsm.Kernel.Data.HsmDefinitionBlob(
            header, states, transitions,
            Array.Empty<global::Fhsm.Kernel.Data.RegionDef>(),
            Array.Empty<global::Fhsm.Kernel.Data.GlobalTransitionDef>(),
            Array.Empty<ushort>(), Array.Empty<ushort>());

        // ⭐ The instance lives in the entity's BrainHsm128 component, as in production — the debug
        //   session recovers the hosting machine's id from there to LABEL each occurrence (§24.11).
        if (!_repo.IsComponentTypeRegistered<global::Fdp.Toolkit.Behavior.Components.BrainHsm128>())
            _repo.RegisterComponent<global::Fdp.Toolkit.Behavior.Components.BrainHsm128>();
        if (!_repo.HasComponent<global::Fdp.Toolkit.Behavior.Components.BrainHsm128>(entity))
            _repo.AddComponent(entity, default(global::Fdp.Toolkit.Behavior.Components.BrainHsm128));

        var inst = new global::Fhsm.Kernel.Data.HsmInstance128();
        inst.Header.MachineId = MachineId;
        // ⚠ 0 is a VALID state index, so every unused region slot must read 0xFFFF or the kernel
        //   treats all four as sitting in state 0 (measured — design §23.5).
        for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;

        if (asGuard)
        {
            inst.ActiveLeafIds[0] = 0;
            inst.Header.Phase = global::Fhsm.Kernel.Data.InstancePhase.RTC;
            inst.Reserved1 = eventId;
        }
        else
        {
            inst.Header.Phase = global::Fhsm.Kernel.Data.InstancePhase.Entry;
        }

        var bridge = new HsmKernelBridge { Self = entity, WorldHandle = _repo.UnmanagedHandle };
        var page = default(global::Fhsm.Kernel.Data.CommandPage);
        global::Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in bridge, 0.016f, ref page);

        _repo.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.BrainHsm128>(entity).State = inst;

        // For a guard, "did it pass?" is observable as the transition having been TAKEN.
        return !asGuard || inst.ActiveLeafIds[0] == 1;
    }

    /// <summary>The entity's occurrence store — where an HSM-hosted occurrence's working state lives.</summary>
    private unsafe void EnsureOccurrenceStore(Entity entity)
    {
        if (_repo.HasComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(entity))
            return;

        _repo.AddComponent(entity, default(global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024));
        ref var tier = ref _repo.GetComponentRW<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
            global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.Initialize(
                mem,
                global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024.TotalSize,
                (byte)global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024.MaxSlots);
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

        // ⭐ B4: walk the LADDER, not a hand-list — this was three copied arms that did not
        //   know about the 256 tier. ⚠ Largest-first, the same probe order the seam documents.
        var descending = BlueprintTierTable.Descending;
        for (int i = 0; i < descending.Count; i++)
        {
            var spec = descending[i];
            if (!spec.Has(_repo, entity)) continue;

            GetTierMemoryAndMeta(entity, spec.Tier, out byte* memory, out _, out _);
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out payloadOffset))
            {
                tier = spec.Tier;
                return true;
            }
        }

        tier = BlueprintTierTable.Ascending[0].Tier;
        payloadOffset = -1;
        return false;
    }

    // ⛔⛔ O3b / B4 (2026-09-20) — THIS FIXTURE CARRIED ITS OWN COPY OF THE TIER LADDER, and it
    //   was the FIFTH. `ChooseTier` still held the pre-B3② literals `928 / 3936`;
    //   `EnsureTierComponent` and `GetTierMemoryAndMeta` were the same three-arm switches B3①
    //   collapsed in production. B3①'s census covered PRODUCTION files, so these were invisible
    //   to it.
    // 🔴 And it passed B3②'s gate because it is SELF-CONSISTENT: the test asserts the fixture's
    //   own answer, so both sides were wrong together. A fixture that duplicates production logic
    //   cannot fail when production changes — it just silently diverges, and hands entities a
    //   different tier than the code under test would.
    // ⇒ all three now delegate to BlueprintTierTable, like every production caller.

    private unsafe void GetTierMemoryAndMeta(
        Entity entity, BlackboardTier tier,
        out byte* memory, out int totalSize, out byte maxSlots)
    {
        var spec  = BlueprintTierTable.ByTier(tier);
        memory    = spec.Memory(_repo, entity);
        totalSize = spec.TotalSize;
        maxSlots  = (byte)spec.MaxSlots;
    }

    // ---- Entity convenience -------------------------------------------------

    /// <summary>
    /// ⭐⭐ <c>E-cap</c> — a fixture entity carries an occurrence store, because a PRODUCTION brained
    /// entity does: <c>BehaviorIngressSystem</c> provisions one at assign even when the behaviour
    /// declares no stateful slots (design §27).
    ///
    /// <para>⛔ Without it, every test that ticks a blueprint action would have to remember to add one
    /// — and the first one that forgot would report <i>"carries no occurrence store"</i>, which reads
    /// as a product defect rather than a fixture gap. ⚠ That is exactly how this landed the first
    /// time.</para>
    /// </summary>
    public Entity CreateEntity()
    {
        var entity = _repo.CreateEntity();
        EnsureOccurrenceStore(entity);
        EnsureRootParams(entity);
        return entity;
    }

    /// <summary>
    /// ⭐⭐ <b><c>P3-C</c> (<c>2026-09-21</c>) — a fixture entity needs a ROOT PARAMS SLOT, because an
    /// emitted thunk's SEED now reads one.</b>
    ///
    /// <para>🔴 <b>What changed.</b> A blueprint thunk seeds a freshly-attached occurrence from the
    /// hosting behaviour's params region. That region used to be <c>BrainBlackboard</c> — a component
    /// every entity in this fixture carried by construction, readable whether or not anyone had ever
    /// written it. ⇒ the seed silently read zeros and nothing noticed. ⛔ The region is now an
    /// occurrence slot, and <c>RootParamsAccess</c> THROWS rather than inventing one, so a fixture
    /// entity with no slot fails at the first dispatch.</para>
    ///
    /// <para>⚠ <b>The values are still zeros</b> — this fixture never runs ingress, so nothing parses
    /// params into the slot. ⭐ That is deliberate and is exactly the old behaviour: these rails are
    /// about the thunk's STRUCTURE (offsets, slot keys, working state), not about parameter values.
    /// ⛔ What has changed is that the absence is now explicit instead of accidental.</para>
    /// </summary>
    private unsafe void EnsureRootParams(Entity entity)
    {
        const int HarnessBehaviourHash = 0x7E5702;

        if (!_repo.HasComponent<global::Fdp.Toolkit.Behavior.Components.BehaviorState>(entity))
            _repo.AddComponent(entity, new global::Fdp.Toolkit.Behavior.Components.BehaviorState());

        ref var st = ref _repo.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.BehaviorState>(entity);
        if (st.ActiveBehaviorHash == 0) st.ActiveBehaviorHash = HarnessBehaviourHash;

        global::Fdp.Toolkit.Behavior.RootParamsAccess.ResolveOrAttachRoot(
            _repo, entity, st.ActiveBehaviorHash,
            global::Fdp.Toolkit.Behavior.BehaviorConstants.MaxBehaviorParamByteSize,
            OccurrenceKind.BTree, out _);
    }

    // ---- Attach Blueprint ---------------------------------------------------

    public unsafe void AttachBlueprint(BlueprintAsset asset, Entity entity)
    {
        if (!Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def))
            throw new InvalidOperationException(
                $"Blueprint '{asset.Name}' not loaded into registry. Call CompileAndLoad first.");

        // ⭐ B4 — design §17.7. Mirror production EXACTLY: the payload-only pick is reconciled with
        //   the tier the entity may already carry. ⛔ ChooseTier alone would add a SECOND store the
        //   moment the pick disagrees — which the 256 tier made the common case.
        var tier = BlueprintTierTable.EnsureAtLeast(
            _repo, entity, BlueprintTierTable.SelectByPayload(def!.StateSize)).Tier;
        EnsureTierComponent(entity, tier);

        GetTierMemoryAndMeta(entity, tier, out byte* memory, out int totalSize, out byte maxSlots);
        BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots);

        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        // ⭐ A3/D1' — declare the kind, exactly as the production attach paths do
        //   (BlueprintInstanceService / BlueprintTickSystem / BlueprintMaterializationSystem).
        //   ⛔ Without it the slot reads Invalid and A4's walker skips it, so nothing ticks.
        if (!BlueprintBlackboardPartitions.TryAttach(memory, blueprintId, def.StateSize, def.StructureHash,
                OccurrenceKind.Blueprint, out int payloadOffset))
            throw new InvalidOperationException(
                $"Failed to attach Blueprint '{asset.Name}' to entity {entity} (tier {tier}).");

        if (def.InitDefault != null)
        {
            ref byte payloadRef = ref Unsafe.AsRef<byte>(memory + payloadOffset);
            var initSpan = MemoryMarshal.CreateSpan(ref payloadRef, def.StateSize);
            def.InitDefault(initSpan);
        }
    }

    /// <summary>⛔ Delegates — see the note above <c>GetTierMemoryAndMeta</c>. This is the SAME
    /// payload-only selector <c>BlueprintInstanceService.ChooseTier</c> uses, so the fixture seats a
    /// blueprint exactly where production would.</summary>
    internal static BlackboardTier ChooseTier(int stateSize)
        => BlueprintTierTable.SelectByPayload(stateSize).Tier;

    private void EnsureTierComponent(Entity entity, BlackboardTier tier)
    {
        var spec = BlueprintTierTable.ByTier(tier);
        if (!spec.Has(_repo, entity))
            spec.Add(_repo, entity);
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

        // ⭐ B4: snapshot every REGISTERED tier from the ladder. ⛔ This was two copied blocks
        //   naming 1024 and 4096, so a 256-tier store was simply absent from the snapshot and a
        //   "nothing changed" assertion over it was vacuously true.
        // ⚠ Ascending, so the byte order is stable and does not depend on the probe order.
        var ascending = BlueprintTierTable.Ascending;
        for (int t = 0; t < ascending.Count; t++)
        {
            var spec = ascending[t];
            if (!spec.IsRegistered(_repo)) continue;

            foreach (var entity in spec.BuildQuery(_repo))
            {
                byte* ptr = spec.MemoryReadOnly(_repo, entity);
                writer.Write(entity.Index);
                writer.Write(entity.Generation);
                for (int i = 0; i < spec.TotalSize; i++)
                    writer.Write(ptr[i]);
            }
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
