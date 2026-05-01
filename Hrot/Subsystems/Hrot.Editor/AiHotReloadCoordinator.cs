using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Hrot.Editor
{
    /// <summary>
    /// Unified hot-reload coordinator for the AI doctrine assembly
    /// (<c>Hrot.AI.Doctrines.dll</c>).  Manages the ALC lifecycle for both BTree
    /// and HSM doctrines, replacing the older <c>FbtAssemblyHotReloader</c> pattern.
    ///
    /// <para><b>Thread model:</b>
    /// <list type="bullet">
    ///   <item>Background thread — <c>LoadAndReload</c>: loads the new DLL into a
    ///     fresh collectible ALC, reflects the factory, builds staging data, enqueues
    ///     the result.</item>
    ///   <item>Main thread — <see cref="DrainPendingCallbacks"/>: dequeues each
    ///     pending action (success or failure), and for success:
    ///     clears the HSM action table, re-registers, applies doctrines, and
    ///     hot-reloads existing HSM instances.</item>
    /// </list>
    /// The order inside <see cref="DrainPendingCallbacks"/> is mandated:
    /// <c>ClearAll</c> BEFORE <c>RegisterAll</c>.</para>
    /// </summary>
    internal sealed class AiHotReloadCoordinator : IDisposable
    {
        // ---- Payload produced on background thread, consumed on main thread ----
        private readonly struct PendingReload
        {
            public readonly DoctrineRegistry    StagingRegistry;
            public readonly AssemblyLoadContext NewAlc;
            public readonly AssemblyLoadContext? OldAlc;

            public PendingReload(
                DoctrineRegistry stagingRegistry,
                AssemblyLoadContext newAlc,
                AssemblyLoadContext? oldAlc)
            {
                StagingRegistry = stagingRegistry;
                NewAlc          = newAlc;
                OldAlc          = oldAlc;
            }
        }

        // ---- Public events (fired on main thread from DrainPendingCallbacks) ----
        public event Action<string>? OnReloadCompleted;
        public event Action<string, Exception>? OnReloadFailed;

        // ---- ALC GC verification (tests only) ----
        /// <summary>
        /// Weak reference to the ALC that was replaced by the most recent reload.
        /// Used in tests to verify the old ALC was collected after <c>GC.Collect()</c>.
        /// </summary>
        internal WeakReference<AssemblyLoadContext>? PreviousAlcRef { get; private set; }

        // ---- Dependencies ----
        private readonly EntityRepository      _world;
        private readonly DoctrineRegistry      _liveRegistry;
        private readonly IGeographicTransform? _geoTransform;
        private readonly NetworkEntityMap?     _entityMap;
        private readonly HotReloadManager      _hotReloadManager = new();

        // ---- File-system watch / debounce ----
        private readonly FileSystemWatcher _watcher;
        private readonly string            _watchDirectory;
        private readonly Timer             _debounceTimer;
        private string?                    _pendingPath;
        private readonly object            _debounceLock = new();
        private const int DebounceMs = 200;

        // ---- ALC state ----
        private AssemblyLoadContext? _currentAlc;

        // ---- Queues for inter-thread communication ----
        // Success reloads (contain staging data + ALCs).
        private readonly ConcurrentQueue<PendingReload> _pendingReloads = new();
        // Failure callbacks (pre-built for main-thread invocation).
        private readonly ConcurrentQueue<Action> _pendingFailures = new();

        // ---- Constructor ----

        /// <summary>
        /// Creates the coordinator.
        /// </summary>
        /// <param name="watchDirectory">Directory to monitor for DLL changes.</param>
        /// <param name="dllFilter">File filter, e.g. <c>"Hrot.AI.Doctrines.dll"</c>.</param>
        /// <param name="world">Live ECS world used for per-chunk HSM hot reload.</param>
        /// <param name="liveRegistry">Doctrine registry updated on main thread.</param>
        /// <param name="geoTransform">Geographic transform passed to doctrine factory.</param>
        /// <param name="entityMap">Entity map passed to doctrine factory.</param>
        public AiHotReloadCoordinator(
            string watchDirectory,
            string dllFilter,
            EntityRepository world,
            DoctrineRegistry liveRegistry,
            IGeographicTransform? geoTransform,
            NetworkEntityMap? entityMap)
        {
            _watchDirectory = watchDirectory;
            _world          = world;
            _liveRegistry   = liveRegistry;
            _geoTransform   = geoTransform;
            _entityMap      = entityMap;

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
                ThreadPool.QueueUserWorkItem(_ => LoadAndReload(path));
        }

        /// <summary>
        /// Must be called once per frame from the main thread (between kernel ticks).
        /// Applies any pending DLL reload in the mandated order:
        /// <list type="number">
        ///   <item><see cref="HsmActionDispatcher.ClearAll"/> — purge stale function pointers.</item>
        ///   <item>Reflect <c>HsmActionRegistrar.RegisterAll</c> from new ALC and invoke.</item>
        ///   <item>Apply staging registry to <see cref="_liveRegistry"/>.</item>
        ///   <item>Hot-reload live HSM instances via <see cref="HotReloadManager"/>.</item>
        ///   <item>Release old ALC; store weak reference for GC verification.</item>
        /// </list>
        /// </summary>
        public void DrainPendingCallbacks()
        {
            // Drain failure callbacks first so they are visible in the same frame.
            while (_pendingFailures.TryDequeue(out var failCb))
                failCb();

            while (_pendingReloads.TryDequeue(out var pending))
            {
                try
                {
                    // Step 1: clear stale HSM function pointers FIRST.
                    HsmActionDispatcher.ClearAll();

                    // Step 2: re-register HSM actions from the NEW assembly.
                    var newAssembly = FindAssembly(pending.NewAlc, "Hrot.AI.Doctrines");
                    if (newAssembly != null)
                    {
                        var registrarType = newAssembly.GetType(
                            "Hrot.AI.Doctrines.Generated.HsmActionRegistrar");
                        registrarType?.GetMethod("RegisterAll",
                            BindingFlags.Public | BindingFlags.Static)
                            ?.Invoke(null, null);
                    }

                    // Step 3: apply staging registry -> live registry.
                    foreach (var name in pending.StagingRegistry.GetRegisteredNames())
                    {
                        if (pending.StagingRegistry.TryGetId(name, out int id) &&
                            pending.StagingRegistry.TryGetDefinition(id, out var def))
                        {
                            _liveRegistry.Register(id, name, def);
                        }
                    }

                    // Step 4: hot-reload live HSM instances per-chunk.
                    foreach (var name in pending.StagingRegistry.GetRegisteredNames())
                    {
                        if (!pending.StagingRegistry.TryGetId(name, out int docId))
                            continue;
                        if (!pending.StagingRegistry.TryGetDefinition(docId, out var def))
                            continue;
                        if (def.BrainTier != BehaviorConstants.BrainTierHsm)
                            continue;
                        if (def.HsmDefinition == null)
                            continue;

                        var blob = def.HsmDefinition;
                        ReloadHsmChunks<BrainHsm64>(blob);
                        ReloadHsmChunks<BrainHsm128>(blob);
                    }

                    // Step 5: release old ALC; record weak reference for test verification.
                    if (pending.OldAlc != null)
                    {
                        PreviousAlcRef = new WeakReference<AssemblyLoadContext>(pending.OldAlc);
                        pending.OldAlc.Unload();
                    }

                    OnReloadCompleted?.Invoke("__ai_doctrines__");
                }
                catch (Exception ex)
                {
                    OnReloadFailed?.Invoke("__ai_doctrines__", ex);
                }
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
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }

        private void OnDebounceElapsed(object? state)
        {
            string? path;
            lock (_debounceLock) { path = _pendingPath; }
            if (path != null)
                ThreadPool.QueueUserWorkItem(_ => LoadAndReload(path));
        }

        // ---- Private: background reload ----

        private void LoadAndReload(string dllPath)
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

                // Locate AiDoctrineFactory in the newly-loaded assembly.
                var factoryType = newAssembly.GetType("Hrot.AI.Doctrines.AiDoctrineFactory");
                var buildMethod = factoryType?.GetMethod(
                    "BuildRegistrationAction",
                    BindingFlags.Public | BindingFlags.Static);

                if (buildMethod == null)
                {
                    newAlc.Unload();
                    var ex = new InvalidOperationException(
                        $"'AiDoctrineFactory.BuildRegistrationAction' not found in '{dllPath}'.");
                    EnqueueFailure(dllPath, ex);
                    return;
                }

                // Invoke on this background thread; CPU-heavy BTree/HSM compilation happens here.
                var applyAction = (Action<DoctrineRegistry>?)buildMethod.Invoke(
                    null, new object?[] { _geoTransform, _entityMap });

                if (applyAction == null)
                {
                    newAlc.Unload();
                    var ex = new InvalidOperationException(
                        $"'BuildRegistrationAction' returned null in '{dllPath}'.");
                    EnqueueFailure(dllPath, ex);
                    return;
                }

                var stagingRegistry = new DoctrineRegistry();
                applyAction(stagingRegistry);

                // Swap in new ALC; the previous one is passed to the drain for orderly release.
                var oldAlc = Interlocked.Exchange(ref _currentAlc, newAlc);
                _pendingReloads.Enqueue(new PendingReload(stagingRegistry, newAlc, oldAlc));
            }
            catch (Exception ex)
            {
                EnqueueFailure(dllPath, ex);
            }
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
