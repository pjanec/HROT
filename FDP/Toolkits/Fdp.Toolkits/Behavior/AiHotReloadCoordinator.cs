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
using Fdp.Toolkit.Blueprints.Attributes;
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

// Per Patch 1 — no OldAlc field; _currentAlc is main-thread-only.
internal sealed class PendingReload
{
    public required AssemblyLoadContext NewAlc   { get; init; }
    public required Assembly            NewAssembly { get; init; }
    public required IReadOnlyList<ResolvedRegistrar> Registrars { get; init; }
    // No OldAlc: main-thread ApplyReload reads _currentAlc directly (Patch 1).
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
    /// The caller (<c>QuickReloadService</c>) handles <see cref="HsmActionDispatcher.ClearAll"/>
    /// and registrar invocation into staging buffers; this method performs only the staging
    /// commits, ALC swap, and event notification.
    /// Throws on failure; coordinator unloads <paramref name="newAlc"/> on failure.
    /// </summary>
    public void ApplyQuickReload(
        AssemblyLoadContext newAlc,
        BehaviorRegistry behaviorStaging,
        BlueprintRegistryStaging blueprintStaging)
    {
        try
        {
            // Step 1: atomic commit of BlueprintRegistry.
            _blueprintRegistry.CommitStaging(blueprintStaging);

            // Step 2: apply staging behavior registry -> live registry.
            _behaviorRegistry.MergeFrom(behaviorStaging);

            // Step 3: swap _currentAlc - ONLY after successful commits (Patch 1).
            var oldAlc = _currentAlc;
            _currentAlc = newAlc;
            oldAlc?.Unload();

            // Step 4: fire completion event.
            OnReloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            // Unload the new patch ALC on failure; old _currentAlc stays live.
            try { newAlc.Unload(); } catch { /* best-effort */ }
            OnReloadFailed?.Invoke(ex);
            throw;
        }
    }

    // ---- Internal: test access to current ALC ------------------------------

    /// <summary>Internal for test access; verifies ALC identity after reload.</summary>
    internal AssemblyLoadContext? GetCurrentAlc() => _currentAlc;

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
