using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Replication.Services;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Hrot.Editor
{
    // ---- Supporting types (Options, Events) ---------------------------------

    /// <summary>
    /// Configuration for <see cref="AiHotReloadCoordinator"/>.
    /// </summary>
    public record AiHotReloadCoordinatorOptions(
        bool LoadPdbOnDeveloperMode = false,
        TimeSpan? FileWatcherDebounce = null
    );

    /// <summary>
    /// Discriminates the origin of a completed hot reload.
    /// Subscribers use this to avoid disk-read race conditions on the
    /// <c>.dbgmap.json</c> file.
    /// </summary>
    public enum ReloadSource
    {
        /// <summary>Reload was driven by the file-system watcher (MSBuild full rebuild).</summary>
        FullRebuildViaFileWatcher,
        /// <summary>Reload was injected in-memory by the Editor QuickReloadService.</summary>
        QuickReloadViaApi,
    }

    /// <summary>
    /// Payload delivered to <see cref="AiHotReloadCoordinator.OnReloadCompleted"/>
    /// subscribers.
    /// </summary>
    public record ReloadCompletedInfo(
        ReloadSource Source,
        AssemblyLoadContext NewAlc,
        string? DllPath);

    // ---- Registrar discovery types ------------------------------------------

    /// <summary>One parameter of a discovered registrar entry-point method.</summary>
    public record RegistrarParameter(string Name, Type ParameterType, int OrdinalIndex);

    /// <summary>
    /// Metadata for a registrar class discovered by
    /// <see cref="AiHotReloadCoordinator.ScanForRegistrars"/>.
    /// </summary>
    public record ResolvedRegistrar(
        Type DeclaringType,
        MethodInfo RegisterMethod,
        IReadOnlyList<RegistrarParameter> Parameters);

    // =========================================================================

    /// <summary>
    /// Unified hot-reload coordinator for the AI behavior assembly
    /// (<c>Hrot.AI.Behaviors.dll</c>).  Manages the ALC lifecycle for both BTree
    /// and HSM behaviors.
    ///
    /// <para><b>Thread model:</b>
    /// <list type="bullet">
    ///   <item>Background thread — <c>LoadAndScan</c>: loads the new DLL into a
    ///     fresh collectible ALC, discovers registrar classes via attributes, enqueues
    ///     the result.</item>
    ///   <item>Main thread — <see cref="DrainPendingCallbacks"/>: dequeues each
    ///     pending result, and for success: clears the HSM action table, invokes
    ///     each registrar, applies staging, hot-reloads HSM instances, and swaps
    ///     the ALC.</item>
    /// </list>
    /// The order inside <see cref="DrainPendingCallbacks"/> is mandated:
    /// <c>ClearAll</c> BEFORE registrar invocation.</para>
    /// </summary>
    internal sealed class AiHotReloadCoordinator : IDisposable
    {
        // ---- Payload produced on background thread, consumed on main thread ----
        private readonly struct PendingReload
        {
            public readonly IReadOnlyList<ResolvedRegistrar> Registrars;
            public readonly AssemblyLoadContext              NewAlc;
            public readonly string                           DllPath;

            public PendingReload(
                IReadOnlyList<ResolvedRegistrar> registrars,
                AssemblyLoadContext newAlc,
                string dllPath)
            {
                Registrars = registrars;
                NewAlc     = newAlc;
                DllPath    = dllPath;
            }
        }

        // ---- Public events (fired on main thread from DrainPendingCallbacks) ----
        /// <summary>Fired just before the new assembly is swapped into _currentAlc.</summary>
        public event Action? OnReloadBegin;
        public event Action<ReloadCompletedInfo>? OnReloadCompleted;
        public event Action<string, Exception>?   OnReloadFailed;

        // ---- ALC GC verification (tests only) ----
        /// <summary>
        /// Weak reference to the ALC that was replaced by the most recent reload.
        /// Used in tests to verify the old ALC was collected after <c>GC.Collect()</c>.
        /// </summary>
        internal WeakReference<AssemblyLoadContext>? PreviousAlcRef { get; private set; }

        /// <summary>Test seam: fires <see cref="OnReloadBegin"/> without performing an actual reload.</summary>
        internal void RaiseReloadBeginForTest() => OnReloadBegin?.Invoke();

        /// <summary>
        /// Test seam: enqueues a minimal pending reload so unit tests can verify
        /// the one-reload-per-frame guarantee without loading a physical DLL.
        /// </summary>
        internal void EnqueueReloadForTest(
            IReadOnlyList<ResolvedRegistrar> registrars,
            AssemblyLoadContext alc,
            string dllPath = "test.dll")
            => _pendingReloads.Enqueue(new PendingReload(registrars, alc, dllPath));

        // ---- Dependencies ----
        private readonly EntityRepository              _world;
        private readonly BehaviorRegistry              _liveRegistry;
        private readonly BlueprintRegistry             _blueprintRegistry;
        private readonly AiHotReloadCoordinatorOptions _options;
        private readonly IGeographicTransform?         _geoTransform;
        private readonly NetworkEntityMap?             _entityMap;
        private readonly global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler? _predicateCompiler;
        private readonly global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry? _dtoRegistry;
        private readonly HotReloadManager              _hotReloadManager = new();

        // ---- File-system watch / debounce ----
        private readonly FileSystemWatcher _watcher;
        private readonly string            _watchDirectory;
        private readonly Timer             _debounceTimer;
        private string?                    _pendingPath;
        private readonly object            _debounceLock = new();

        // ---- ALC state (main-thread-only after construction) ----
        private AssemblyLoadContext? _currentAlc;

        // ---- Queues for inter-thread communication ----
        // Success reloads (contain metadata + ALCs).
        private readonly ConcurrentQueue<PendingReload> _pendingReloads = new();
        // Failure callbacks (pre-built for main-thread invocation).
        private readonly ConcurrentQueue<Action> _pendingFailures = new();

        // ---- Constructor ----

        /// <summary>
        /// Creates the coordinator.
        /// </summary>
        /// <param name="watchDirectory">Directory to monitor for DLL changes.</param>
        /// <param name="dllFilter">File filter, e.g. <c>"Hrot.AI.Behaviors.dll"</c>.</param>
        /// <param name="world">Live ECS world used for per-chunk HSM hot reload.</param>
        /// <param name="liveRegistry">Behavior registry updated on main thread.</param>
        /// <param name="blueprintRegistry">Blueprint registry for atomic staging commits.</param>
        /// <param name="options">Coordinator configuration options.</param>
        /// <param name="geoTransform">Geographic transform passed to behavior registrars.</param>
        /// <param name="entityMap">Entity map passed to behavior registrars.</param>
        public AiHotReloadCoordinator(
            string watchDirectory,
            string dllFilter,
            EntityRepository world,
            BehaviorRegistry liveRegistry,
            BlueprintRegistry blueprintRegistry,
            AiHotReloadCoordinatorOptions options,
            IGeographicTransform? geoTransform = null,
            NetworkEntityMap? entityMap = null,
            global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler? predicateCompiler = null,
            global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry? dtoRegistry = null)
        {
            _watchDirectory    = watchDirectory;
            _world             = world;
            _liveRegistry      = liveRegistry;
            _blueprintRegistry = blueprintRegistry;
            _options           = options;
            _geoTransform      = geoTransform;
            _entityMap         = entityMap;
            _predicateCompiler = predicateCompiler;
            _dtoRegistry       = dtoRegistry;

            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(watchDirectory, dllFilter)
            {
                NotifyFilter        = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
        }

        // ---- Public API ----

        /// <summary>
        /// Queues an immediate reload of the watched DLL on a background thread.
        /// A no-op when the filter contains wildcards or the file does not exist.
        /// </summary>
        public void TriggerInitialLoad()
        {
            string filter = _watcher.Filter;
            if (filter.IndexOf('*') >= 0 || filter.IndexOf('?') >= 0)
                return;
            string path = Path.Combine(_watchDirectory, filter);
            if (File.Exists(path))
                ThreadPool.QueueUserWorkItem(_ => LoadAndScan(path));
        }

        /// <summary>
        /// Must be called once per frame from the main thread (between kernel ticks).
        /// Applies any pending DLL reload in the mandated order:
        /// <list type="number">
        ///   <item><see cref="HsmActionDispatcher.ClearAll"/> — purge stale function pointers.</item>
        ///   <item>Invoke all discovered registrars with resolved staging parameters.</item>
        ///   <item>Commit <see cref="BlueprintRegistry"/> staging atomically.</item>
        ///   <item>Apply staging behavior registry to <see cref="_liveRegistry"/>.</item>
        ///   <item>Hot-reload live HSM instances via <see cref="HotReloadManager"/>.</item>
        ///   <item>Swap <c>_currentAlc</c> and release old ALC (success path only).</item>
        ///   <item>Fire <see cref="OnReloadCompleted"/> with <see cref="ReloadSource.FullRebuildViaFileWatcher"/>.</item>
        /// </list>
        /// </summary>
        public void DrainPendingCallbacks()
        {
            // Drain failure callbacks first so they are visible in the same frame.
            while (_pendingFailures.TryDequeue(out var failCb))
                failCb();

            // BPF-043: apply at most one reload per frame (Hot Reload DD §4.2 one-reload-per-frame bound).
            if (!_pendingReloads.TryDequeue(out var pending))
                return;

            try
            {
                // Step 1: clear stale HSM function pointers FIRST.
                HsmActionDispatcher.ClearAll();

                // Step 2: invoke all discovered registrars with resolved staging params.
                var behaviorStaging  = new BehaviorRegistry();
                var blueprintStaging = _blueprintRegistry.BeginStaging();
                // BTree bridges construct `new Interpreter(blob, registry)` and require a NON-null,
                // populated ActionRegistry (the baked-offset thunks live here). Build it from the
                // same loaded assembly the registrars came from (mirrors Fdp.Toolkit coordinator).
                var btreeActionRegistry = BuildBTreeActionRegistry(pending.Registrars);

                foreach (var registrar in pending.Registrars)
                {
                    // PU-402 resilience: a single broken editor-owned asset (e.g. an HSM whose saved
                    // .hsm.json has an invalid structure — an orphaned transition target) must NOT abort
                    // the whole reload batch or crash the editor. Invoke each registrar in isolation;
                    // skip + report the failing one so the rest still register and the editor stays usable.
                    try
                    {
                        var args = registrar.Parameters
                            .OrderBy(p => p.OrdinalIndex)
                            .Select(p => ResolveRegistrarParam(p.ParameterType, behaviorStaging, blueprintStaging, btreeActionRegistry))
                            .ToArray();
                        registrar.RegisterMethod.Invoke(null, args);
                    }
                    catch (Exception regEx)
                    {
                        // Unwrap reflection's TargetInvocationException for a useful message.
                        var inner = (regEx as TargetInvocationException)?.InnerException ?? regEx;
                        var assetName = registrar.RegisterMethod.DeclaringType?.Name ?? "<unknown>";
                        Console.WriteLine(
                            $"[AiHotReload] WARNING: registrar '{assetName}' failed to register and was skipped: {inner.Message}");
                        OnReloadFailed?.Invoke(pending.DllPath, inner);
                        // continue: register the remaining assets
                    }
                }

                // Step 3: atomic commit of BlueprintRegistry.
                _blueprintRegistry.CommitStaging(blueprintStaging);

                // Step 4: apply staging behavior registry -> live registry.
                foreach (var name in behaviorStaging.GetRegisteredNames())
                {
                    if (behaviorStaging.TryGetId(name, out int id) &&
                        behaviorStaging.TryGetDefinition(id, out var def))
                    {
                        _liveRegistry.Register(id, name, def);
                    }
                }

                // Step 5: hot-reload live HSM instances per-chunk.
                foreach (var name in behaviorStaging.GetRegisteredNames())
                {
                    if (!behaviorStaging.TryGetId(name, out int docId))
                        continue;
                    if (!behaviorStaging.TryGetDefinition(docId, out var def))
                        continue;
                    if (def.BrainTier != BehaviorConstants.BrainTierHsm)
                        continue;
                    if (def.HsmDefinition == null)
                        continue;

                    var blob = def.HsmDefinition;
                    ReloadHsmChunks<BrainHsm64>(blob);
                    ReloadHsmChunks<BrainHsm128>(blob);
                }

                // Step 5.5: notify before the swap so pending mutations are flushed.
                OnReloadBegin?.Invoke();

                // Step 6: swap _currentAlc and release the old ALC.
                // This happens ONLY after all staging commits succeed,
                // so a failure above leaves _currentAlc (and running code) untouched.
                var oldAlc = _currentAlc;
                _currentAlc = pending.NewAlc;

                if (oldAlc != null)
                {
                    PreviousAlcRef = new WeakReference<AssemblyLoadContext>(oldAlc);
                    oldAlc.Unload();
                }

                // Step 7: fire completion event.
                OnReloadCompleted?.Invoke(new ReloadCompletedInfo(
                    ReloadSource.FullRebuildViaFileWatcher,
                    pending.NewAlc,
                    pending.DllPath));
            }
            catch (Exception ex)
            {
                // _currentAlc is NOT updated; simulation keeps running with previous assembly.
                OnReloadFailed?.Invoke(pending.DllPath, ex);
            }
        }

        /// <summary>
        /// Applies an in-memory Quick Reload.  Bypasses the file-watcher and background
        /// reflection scan.  The Editor's <c>QuickReloadService</c> handles the reflection,
        /// HSM clearing, and registrar invocation, then hands the populated staging
        /// buffers here for the atomic swap.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> The caller (<c>QuickReloadService</c>) must call
        /// <see cref="HsmActionDispatcher.ClearAll"/> and invoke registrars <em>before</em>
        /// calling this method.  This method only performs the staging commits, ALC swap,
        /// and event notification.
        /// </remarks>
        public void ApplyQuickReload(
            AssemblyLoadContext newAlc,
            BehaviorRegistry behaviorStaging,
            BlueprintRegistryStaging blueprintStaging)
        {
            try
            {
                // Step 1: atomic commit of BlueprintRegistry.
                _blueprintRegistry.CommitStaging(blueprintStaging);

                // Step 2: apply staging behavior registry into the live BehaviorRegistry.
                foreach (var name in behaviorStaging.GetRegisteredNames())
                {
                    if (behaviorStaging.TryGetId(name, out int id) &&
                        behaviorStaging.TryGetDefinition(id, out var def))
                    {
                        _liveRegistry.Register(id, name, def);
                    }
                }

                // Step 3: hot-reload live HSM instances per-chunk (same as file-watcher path).
                foreach (var name in behaviorStaging.GetRegisteredNames())
                {
                    if (!behaviorStaging.TryGetId(name, out int docId)) continue;
                    if (!behaviorStaging.TryGetDefinition(docId, out var def)) continue;
                    if (def.BrainTier != BehaviorConstants.BrainTierHsm) continue;
                    if (def.HsmDefinition == null) continue;

                    var blob = def.HsmDefinition;
                    ReloadHsmChunks<BrainHsm64>(blob);
                    ReloadHsmChunks<BrainHsm128>(blob);
                }

                // Step 3.5: notify before the swap so pending mutations are flushed.
                OnReloadBegin?.Invoke();

                // Step 4: swap ALC and release the old ALC (strictly main thread).
                var oldAlc = _currentAlc;
                _currentAlc = newAlc;

                if (oldAlc != null)
                {
                    PreviousAlcRef = new WeakReference<AssemblyLoadContext>(oldAlc);
                    oldAlc.Unload();
                }

                // Step 5: fire completion event tagged as a Quick Reload.
                OnReloadCompleted?.Invoke(new ReloadCompletedInfo(
                    ReloadSource.QuickReloadViaApi,
                    newAlc,
                    null));
            }
            catch (Exception ex)
            {
                // If the apply fails, unload the new patch ALC to prevent leaks,
                // leave the previous _currentAlc intact, and propagate the failure.
                newAlc.Unload();
                OnReloadFailed?.Invoke("QuickReload", ex);
                throw; // Re-throw so the Editor's QuickReloadService can show the error.
            }
        }

        // ---- IDisposable ----
        public void Dispose()
        {
            _watcher.Dispose();
            _debounceTimer.Dispose();
            var alc = Interlocked.Exchange(ref _currentAlc, null);
            alc?.Unload();
        }

        // ---- Private: file-system watcher ----

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_debounceLock) { _pendingPath = e.FullPath; }
            int debounceMs = (int)(_options.FileWatcherDebounce?.TotalMilliseconds ?? 200);
            _debounceTimer.Change(debounceMs, Timeout.Infinite);
        }

        private void OnDebounceElapsed(object? state)
        {
            string? path;
            lock (_debounceLock) { path = _pendingPath; }
            if (path != null)
                ThreadPool.QueueUserWorkItem(_ => LoadAndScan(path));
        }

        // ---- Private: background scan ----

        private void LoadAndScan(string dllPath)
        {
            try
            {
                var newAlc = new AssemblyLoadContext(
                    Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);
                Assembly newAssembly;

                // Retry: the file may still be locked if the copy is in progress.
                const int maxRetries = 5;
                int attempt = 0;
                while (true)
                {
                    try
                    {
                        using var fs = File.OpenRead(dllPath);
                        newAssembly = newAlc.LoadFromStream(fs);
                        break;
                    }
                    catch (IOException) when (attempt++ < maxRetries)
                    {
                        Thread.Sleep(50);
                    }
                }

                // SCAN: find all registrars in the new assembly via attributes.
                var registrars = ScanForRegistrars(newAssembly);

                if (registrars.Count == 0)
                {
                    newAlc.Unload();
                    var ex = new InvalidOperationException(
                        $"No registrars found in '{dllPath}'. " +
                        "Expected at least one class decorated with [BlueprintRegistrar].");
                    EnqueueFailure(dllPath, ex);
                    return;
                }

                // Enqueue the new ALC and the discovered registrars for the main thread.
                // DO NOT touch _currentAlc here.
                _pendingReloads.Enqueue(new PendingReload(registrars, newAlc, dllPath));
            }
            catch (Exception ex)
            {
                EnqueueFailure(dllPath, ex);
            }
        }

        // ---- Private: registrar discovery ----

        /// <summary>
        /// Scans <paramref name="assembly"/> for classes decorated with any of the
        /// recognized registrar attributes and returns their resolved metadata,
        /// sorted deterministically by declaring type full name.
        /// </summary>
        internal IReadOnlyList<ResolvedRegistrar> ScanForRegistrars(Assembly assembly)
        {
            var validAttributeNames = new[]
            {
                "BlueprintRegistrarAttribute",
            };

            var registrars = new List<ResolvedRegistrar>();
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Gracefully handle partial assembly loads.
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types)
            {
                bool isRegistrar = type.GetCustomAttributes().Any(attr =>
                    Array.IndexOf(validAttributeNames, attr.GetType().Name) >= 0);

                if (!isRegistrar) continue;

                // Find the public static entry point (prefer "Register", fall back to "RegisterAll").
                var method =
                    type.GetMethod("Register",    BindingFlags.Public | BindingFlags.Static) ??
                    type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static);

                if (method == null) continue;

                var parameters = method.GetParameters()
                    .Select((p, i) => new RegistrarParameter(p.Name ?? string.Empty, p.ParameterType, i))
                    .ToArray();

                registrars.Add(new ResolvedRegistrar(type, method, parameters));
            }

            // Sort deterministically to ensure reproducible registration order.
            return registrars.OrderBy(r => r.DeclaringType.FullName).ToList();
        }

        // ---- Private: parameter resolution ----

        /// <summary>
        /// Builds a populated BTree <see cref="ActionRegistry{TBB,TCtx}"/> from the assembly the
        /// discovered registrars belong to. BTree bridge <c>Register(...)</c> methods construct
        /// <c>new Interpreter(blob, registry)</c> and require a non-null registry; without this the
        /// editor crashes at startup with ArgumentNullException ('registry').
        /// </summary>
        private static ActionRegistry<BrainBlackboard, BTreeContext> BuildBTreeActionRegistry(
            IReadOnlyList<ResolvedRegistrar> registrars)
        {
            var asm = registrars.Count > 0 ? registrars[0].DeclaringType.Assembly : null;
            return asm != null
                ? BTreeActionRegistryFactory.BuildFromAssembly(asm)
                : new ActionRegistry<BrainBlackboard, BTreeContext>();
        }

        private object? ResolveRegistrarParam(
            Type paramType,
            BehaviorRegistry behaviorStaging,
            BlueprintRegistryStaging blueprintStaging,
            ActionRegistry<BrainBlackboard, BTreeContext> btreeActionRegistry)
        {
            if (paramType == typeof(BehaviorRegistry))         return behaviorStaging;
            if (paramType == typeof(BlueprintRegistryStaging)) return blueprintStaging;
            if (paramType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>)) return btreeActionRegistry;
            if (paramType == typeof(IGeographicTransform))     return _geoTransform;
            if (typeof(IGeographicTransform).IsAssignableFrom(paramType)) return _geoTransform;
            if (paramType == typeof(NetworkEntityMap))         return _entityMap;
            if (paramType == typeof(global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler))
                return _predicateCompiler;
            if (paramType == typeof(global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry))
                return _dtoRegistry;
            return null;
        }

        // ---- Private: helpers ----

        private void EnqueueFailure(string path, Exception ex)
        {
            // Capture path/ex so the closure is safe to invoke later on main thread.
            string capturedPath = path;
            Exception capturedEx = ex;
            _pendingFailures.Enqueue(() => OnReloadFailed?.Invoke(capturedPath, capturedEx));
        }

        private void ReloadHsmChunks<T>(HsmDefinitionBlob blob)
            where T : unmanaged
        {
            // Guard: the component may not be registered in this world (e.g. in tests).
            if (!_world.TryGetTable(typeof(T), out _))
                return;
            var table      = _world.GetComponentTable<T>();
            var chunkTable = table.GetChunkTable();
            for (int c = 0; c < chunkTable.TotalChunks; c++)
            {
                if (chunkTable.GetPopulationCount(c) == 0)
                    continue;
                var span = table.GetSpan(c);
                _hotReloadManager.TryReload(blob.Header.StructureHash, blob, span);
            }
        }

        private static Assembly? FindAssembly(AssemblyLoadContext alc, string simpleAssemblyName)
        {
            foreach (var asm in alc.Assemblies)
            {
                if (asm.GetName().Name == simpleAssemblyName)
                    return asm;
            }
            return null;
        }
    }
}
