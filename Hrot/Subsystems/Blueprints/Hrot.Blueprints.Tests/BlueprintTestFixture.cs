using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fhsm.Kernel;
using Fdp.Toolkit.Blueprints.Attributes;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Debug;
using Hrot.Blueprints.Tests.Mocks;

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

    // ---- Private state ------------------------------------------------------

    private readonly BlueprintTestFixtureOptions _options;
    private readonly EntityRepository _repo;
    private readonly List<WeakReference<AssemblyLoadContext>> _alcWeakRefs = new();
    private readonly List<AssemblyLoadContext> _activeAlcs = new();
    private readonly List<IEcsModuleSystem> _auxSimulationSystems = new();
    private Action<ISimulationView, IEntityCommandBuffer>? _tickActions;

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

        MockTestComponents.Register(_repo);

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
        TickSystem.Execute(View);
        foreach (var sys in _auxSimulationSystems)
            sys.Execute(_repo, deltaTime);  // pass EntityRepository so MockDispatcherSystem can cast for write access

        // 4. BeforeSync phase
        MaintenanceSystem.Execute(View);

        // 5. Sync phase: ECB playback (structural mutations + queued events apply)
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
        => CompileAndLoadMany(new[] { asset }, mode);

    /// <summary>
    /// Compiles multiple Blueprint assets and loads them into a new collectible ALC.
    /// Requires Phase 3 compiler -- throws NotImplementedException in Phase 1.
    /// </summary>
    public Assembly CompileAndLoadMany(
        IReadOnlyList<BlueprintAsset> assets,
        CompilerMode mode = CompilerMode.Debug)
    {
        // Compile each asset to C# source (will throw NotImplementedException in Phase 1)
        var sb = new StringBuilder();
        foreach (var asset in assets)
        {
            var src = Compiler.Compile(asset, mode);
            sb.AppendLine(src);
        }

        // Roslyn in-memory compile (also stub in Phase 1)
        var assemblyName = $"Bp_{Guid.NewGuid():N}";
        var assembly = new InMemoryRoslynCompiler()
            .CompileAndLoad(sb.ToString(), CreateCollectibleAlc(assemblyName));

        DiscoverAndInvokeRegistrars(assembly);
        return assembly;
    }

    // ---- Test-only ALC bypass -----------------------------------------------

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

    public void SimulateReload(IReadOnlyList<BlueprintAsset> newVersions)
    {
        var oldAlcs = new List<AssemblyLoadContext>(_activeAlcs);
        // Remove old ALCs from active list (they stay in _alcWeakRefs for GC tracking)
        _activeAlcs.Clear();

        CompileAndLoadMany(newVersions);   // populates _activeAlcs with new ALC(s)

        foreach (var alc in oldAlcs)
            alc.Unload();
    }

    // ---- Invoke helpers (Phase 1 stubs) ------------------------------------
    // Phase 1 stubs -- throw NotImplementedException until Phase 3 compiler is in place.

    public NodeStatus InvokeBTreeAction(BlueprintAsset asset, Entity entity, int paramIndex = 0)
        => throw new NotImplementedException("Requires compiled blueprint assembly (Phase 3).");

    public unsafe bool InvokeHsmAction(BlueprintAsset asset, Entity entity)
        => throw new NotImplementedException("Requires compiled blueprint assembly (Phase 3).");

    public unsafe bool InvokeHsmGuard(BlueprintAsset asset, Entity entity, ushort eventId = 0)
        => throw new NotImplementedException("Requires compiled blueprint assembly (Phase 3).");

    // ---- Slot inspection helpers --------------------------------------------

    public bool HasSlot(BlueprintAsset asset, Entity entity)
    {
        return TryGetSlotAcrossTiers(asset.AssetId, entity, out _, out _, out _);
    }

    public unsafe BlueprintStateView? GetBlueprintState(BlueprintAsset asset, Entity entity)
    {
        if (!Registry.TryGetById(asset.AssetId, out var def))
            return null;
        if (!TryGetSlotAcrossTiers(asset.AssetId, entity, out var tier, out _, out var offset))
            return null;

        // In Phase 1, BlueprintBlackboardPartitions.TryGetSlotOffset always returns false,
        // so this returns null. Full implementation in Phase 2.
        return null;
    }

    private bool TryGetSlotAcrossTiers(
        Guid assetId, Entity entity,
        out BlackboardTier tier, out int slotIndex, out int payloadOffset)
    {
        // Check each tier component
        if (_repo.HasComponent<BlueprintBlackboard1024>(entity) &&
            BlueprintBlackboardPartitions.TryGetSlotOffset(
                _repo, entity, assetId, out tier, out slotIndex, out payloadOffset))
            return true;
        if (_repo.HasComponent<BlueprintBlackboard4096>(entity) &&
            BlueprintBlackboardPartitions.TryGetSlotOffset(
                _repo, entity, assetId, out tier, out slotIndex, out payloadOffset))
            return true;
        if (_repo.HasComponent<BlueprintBlackboard16384>(entity) &&
            BlueprintBlackboardPartitions.TryGetSlotOffset(
                _repo, entity, assetId, out tier, out slotIndex, out payloadOffset))
            return true;

        tier = BlackboardTier.B1024;
        slotIndex = -1;
        payloadOffset = -1;
        return false;
    }

    // ---- Attach Blueprint ---------------------------------------------------

    public unsafe void AttachBlueprint(BlueprintAsset asset, Entity entity)
    {
        if (!Registry.TryGetById(asset.AssetId, out var def))
            throw new InvalidOperationException(
                $"Blueprint '{asset.Name}' not loaded into registry. Call CompileAndLoad first.");

        var tier = ChooseTier(def!.StateSize);
        EnsureTierComponent(entity, tier);

        if (!BlueprintBlackboardPartitions.TryAttach(_repo, entity, def, tier, out _))
            throw new InvalidOperationException(
                $"Failed to attach Blueprint '{asset.Name}' to entity {entity} (tier {tier}).");

        // Initialize default state in the slot (no-op in Phase 1 stub)
        // def.InitDefault(...);  -- leave this for Phase 2 when BlueprintBlackboardPartitions is real
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
            if (i < maxRetries - 1) Thread.Sleep(delayMs);
        }
        return AllAlcsReclaimed();
    }

    // ---- Registrar discovery ------------------------------------------------

    private void DiscoverAndInvokeRegistrars(Assembly assembly)
    {
        var staging = Registry.BeginStaging();
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() == null) continue;
            var method = type.GetMethod("Register",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null) continue;
            var prms = method.GetParameters();
            var args = prms.Select(p => ResolveRegistrarParam(p.ParameterType, staging)).ToArray();
            method.Invoke(null, args);
        }
        Registry.CommitStaging(staging);
    }

    private object? ResolveRegistrarParam(Type t, BlueprintRegistryStaging staging)
    {
        if (t == typeof(BlueprintRegistryStaging)) return staging;
        if (t == typeof(BlueprintRegistry))        return Registry;
        if (t == typeof(BehaviorRegistry))         return BehaviorRegistry;
        throw new InvalidOperationException(
            $"Unknown registrar parameter type: {t.FullName}");
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
        HsmActionDispatcher.ClearAll();  // clear stale function pointers before ALC unload
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
