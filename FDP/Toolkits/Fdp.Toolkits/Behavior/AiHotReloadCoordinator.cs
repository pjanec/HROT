using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using Fdp.Toolkit.Blueprints;
using Fhsm.Kernel;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// Thrown when a registrar's parameter list violates the hot reload injection rules.
/// </summary>
public sealed class HotReloadRegistrarException : Exception
{
    public HotReloadRegistrarException(string message) : base(message) { }
}

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

// File-watcher full-rebuild path. No OldAlc field; ApplyReload manages _alcByBlueprintId.
internal sealed class PendingReload
{
    public required AssemblyLoadContext NewAlc   { get; init; }
    public required Assembly            NewAssembly { get; init; }
    public required IReadOnlyList<ResolvedRegistrar> Registrars { get; init; }
}

public sealed record ResolvedRegistrar(
    Type DeclaringType,
    MethodInfo RegisterMethod,
    IReadOnlyList<RegistrarParameter> Parameters);

public sealed record RegistrarParameter(
    string Name,
    Type ParameterType,
    int OrdinalIndex);

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

    // ---- ALC state (main-thread-only). One collectible ALC retained per blueprint id so that
    //      quick-reloading one blueprint never unloads a sibling's still-live Tick/InitDefault. ----
    private readonly Dictionary<int, AssemblyLoadContext> _alcByBlueprintId = new();

    // ---- Queues ------------------------------------------------------------
    private readonly ConcurrentQueue<PendingReload> _pendingReloads  = new();
    // BPF-044: background failures are enqueued here and drained on the main thread.
    private readonly ConcurrentQueue<Exception>     _pendingFailures  = new();

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
        // BPF-044: drain background scan failures before success reloads.
        while (_pendingFailures.TryDequeue(out var failEx))
            OnReloadFailed?.Invoke(failEx);

        if (!_pendingReloads.TryDequeue(out var pending)) return;

        try
        {
            ApplyReload(pending);
            OnReloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            // Per-blueprint ALC map is untouched on failure path.
            OnReloadFailed?.Invoke(ex);
            try { pending.NewAlc.Unload(); }
            catch { /* best-effort unload */ }
        }
    }

    /// <summary>
    /// Apply an in-memory Quick Reload (DEBT-MVE-003 fix).
    /// The caller (<c>QuickReloadService</c>) handles <see cref="HsmActionDispatcher.ClearAll"/>
    /// and registrar invocation into staging buffers; this method performs only the staging
    /// commits, per-blueprint ALC retention, and event notification.
    /// Uses <see cref="BlueprintRegistry.CommitStagingMerge"/> so sibling blueprints and
    /// code-defined definitions survive the reload of a single blueprint.
    /// Throws on failure; coordinator unloads <paramref name="newAlc"/> on failure.
    /// </summary>
    public void ApplyQuickReload(
        AssemblyLoadContext newAlc,
        BehaviorRegistry behaviorStaging,
        BlueprintRegistryStaging blueprintStaging)
    {
        try
        {
            // Step 1: MERGE-commit so sibling + code-defined definitions survive (DEBT-MVE-003).
            _blueprintRegistry.CommitStagingMerge(blueprintStaging);

            // Step 2: apply staging behavior registry -> live registry.
            _behaviorRegistry.MergeFrom(behaviorStaging);

            // Step 3: retain newAlc per recompiled id; unload only ALCs no longer referenced.
            var supersededAlcs = new List<AssemblyLoadContext>();
            foreach (var id in blueprintStaging.StagedBlueprintIds)
            {
                if (_alcByBlueprintId.TryGetValue(id, out var prevAlc) &&
                    !ReferenceEquals(prevAlc, newAlc))
                {
                    supersededAlcs.Add(prevAlc);
                }
                _alcByBlueprintId[id] = newAlc;
            }
            foreach (var old in supersededAlcs.Distinct())
            {
                // Only unload if no retained id still references this ALC.
                bool stillReferenced = false;
                foreach (var a in _alcByBlueprintId.Values)
                    if (ReferenceEquals(a, old)) { stillReferenced = true; break; }
                if (!stillReferenced)
                    old.Unload();
            }

            // Step 4: fire completion event.
            OnReloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            // newAlc was never retained on the failure paths above commit; unload it.
            try { newAlc.Unload(); } catch { /* best-effort */ }
            OnReloadFailed?.Invoke(ex);
            throw;
        }
    }

    // ---- Internal: test seams for ALC retention ----------------------------

    /// <summary>Test seam: number of distinct ALCs currently retained.</summary>
    internal int RetainedAlcCountForTest => _alcByBlueprintId.Values.Distinct().Count();

    /// <summary>Test seam: the ALC currently retained for a blueprint id, or null.</summary>
    internal AssemblyLoadContext? GetRetainedAlcForTest(int blueprintId)
        => _alcByBlueprintId.TryGetValue(blueprintId, out var alc) ? alc : null;

    /// <summary>
    /// Test seam: returns all distinct ALCs currently retained, for type-lookup
    /// helpers that must search all loaded assemblies.
    /// </summary>
    internal IEnumerable<AssemblyLoadContext> GetAllRetainedAlcsForTest()
        => _alcByBlueprintId.Values.Distinct();

    /// <summary>
    /// Test seam: enqueues a set of registrars as a pending reload so unit tests can
    /// exercise <see cref="DrainPendingCallbacks"/> without loading a physical DLL.
    /// The <paramref name="alc"/> is used as the new ALC (pass a dummy collectible context).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal void EnqueueReloadForTest(IReadOnlyList<ResolvedRegistrar> registrars, AssemblyLoadContext alc)
    {
        // Use the alc itself as the "assembly" placeholder; ScanForRegistrars is not called.
        _pendingReloads.Enqueue(new PendingReload
        {
            NewAlc      = alc,
            NewAssembly = alc.Assemblies.FirstOrDefault() ?? typeof(object).Assembly,
            Registrars  = registrars,
        });
    }

    /// <summary>
    /// Test seam: enqueues a pre-built exception so unit tests can verify
    /// <see cref="OnReloadFailed"/> is fired from <see cref="DrainPendingCallbacks"/>
    /// without needing a real background scan.
    /// </summary>
    internal void EnqueueFailureForTest(Exception ex) => _pendingFailures.Enqueue(ex);

    // ---- IDisposable -------------------------------------------------------

    // NoInlining: prevents the JIT from inlining this into the caller's frame,
    // which would keep the 'alc' local alive as a GC root during TryReclaimAllAlcs
    // (see DEBT-009 in BlueprintTestFixture for the full explanation).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Dispose()
    {
        StopWatching();
        // Release BehaviorDefinition delegate references (ParseParams, ParamsDtoType, etc.)
        // from collectible assemblies so they can be GC-reclaimed.
        _behaviorRegistry.Clear();
        foreach (var alc in _alcByBlueprintId.Values.Distinct())
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
        _alcByBlueprintId.Clear();
    }

    // ---- Private: apply ----------------------------------------------------

    private void ApplyReload(PendingReload pending)
    {
        // Step 1: Patch 2 — static call, no instance needed.
        HsmActionDispatcher.ClearAll();

        // Step 2: begin staging for both blueprint and behavior registries.
        // BPF-042: use a fresh staging BehaviorRegistry so that a throwing registrar
        // cannot partially mutate the live _behaviorRegistry.
        var blueprintStaging  = _blueprintRegistry.BeginStaging();
        var behaviorStaging   = new BehaviorRegistry();

        // Step 3: invoke registrars into the staging registries.
        // Production path (file watcher): use the shared scanner to discover and invoke
        // all [BlueprintRegistrar]-decorated classes in the newly loaded assembly.
        // Test-seam path (EnqueueReloadForTest): pre-built ResolvedRegistrar list is used
        // directly so unit tests can exercise ApplyReload without loading a physical DLL.
        if (pending.Registrars.Count > 0)
        {
            foreach (var registrar in pending.Registrars)
                InvokeRegistrar(registrar, blueprintStaging, behaviorStaging);
        }
        else
        {
            BlueprintRegistrarScanner.Scan(pending.NewAssembly, blueprintStaging, behaviorStaging);
        }

        // Step 4: atomic full-replace commit (file-watcher full-rebuild path).
        _blueprintRegistry.CommitStaging(blueprintStaging);

        // Step 5: merge staging BehaviorRegistry -> live registry (only on full success).
        _behaviorRegistry.MergeFrom(behaviorStaging);

        // Step 6: replace per-blueprint ALCs. Full rebuild means all old ALCs for
        // superseded ids are replaced; unload those no longer referenced.
        var oldAlcs = _alcByBlueprintId.Values.Distinct().ToList();
        _alcByBlueprintId.Clear();
        foreach (var id in blueprintStaging.StagedBlueprintIds)
            _alcByBlueprintId[id] = pending.NewAlc;
        foreach (var old in oldAlcs)
        {
            bool stillReferenced = false;
            foreach (var a in _alcByBlueprintId.Values)
                if (ReferenceEquals(a, old)) { stillReferenced = true; break; }
            if (!stillReferenced)
                old.Unload();
        }
    }

    private void InvokeRegistrar(ResolvedRegistrar registrar, BlueprintRegistryStaging blueprintStaging, BehaviorRegistry behaviorStaging)
    {
        var args = registrar.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => ResolveRegistrarArgument(p.ParameterType, blueprintStaging, behaviorStaging))
            .ToArray();
        registrar.RegisterMethod.Invoke(null, args);
    }

    /// <summary>
    /// Resolves a registrar parameter type to its argument value.
    /// BPF-042: returns the staging BehaviorRegistry (not the live one) so that a
    /// throwing registrar cannot partially corrupt the live registry.
    /// Throws <see cref="HotReloadRegistrarException"/> for forbidden or unknown types (Patch 2, Patch 4).
    /// </summary>
    private object ResolveRegistrarArgument(Type paramType, BlueprintRegistryStaging blueprintStaging, BehaviorRegistry behaviorStaging)
    {
        if (paramType == typeof(BlueprintRegistryStaging)) return blueprintStaging;
        // BPF-042: inject the staging registry, not the live one.
        if (paramType == typeof(BehaviorRegistry))         return behaviorStaging;

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

            // Registrar discovery + invocation is deferred to the main thread (ApplyReload)
            // so that BlueprintRegistrarScanner.Scan runs on the main thread and the staging
            // buffers are never touched from the background thread.
            _pendingReloads.Enqueue(new PendingReload
            {
                NewAlc      = alc,
                NewAssembly = assembly,
                Registrars  = Array.Empty<ResolvedRegistrar>(),
            });
        }
        catch (Exception ex)
        {
            // BPF-044: enqueue failure for main-thread reporting via OnReloadFailed.
            // Do not propagate — background thread must not crash.
            _pendingFailures.Enqueue(ex);
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
