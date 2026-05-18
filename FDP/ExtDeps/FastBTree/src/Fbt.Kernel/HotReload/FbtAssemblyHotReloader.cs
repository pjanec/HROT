using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace Fbt.HotReload
{
    /// <summary>
    /// Watches a directory for new DLLs and orchestrates assembly-based hot reload.
    /// All ALC operations run on a background thread; results are enqueued for
    /// application-thread consumption via <see cref="DrainPendingCallbacks"/>.
    /// Does not reference any FDP/HROT types directly.
    /// </summary>
    public sealed class FbtAssemblyHotReloader : IDisposable
    {
        // ---- Dependency types (provided by caller) ----

        /// <summary>
        /// Called when an assembly is loaded. The caller should invoke RegisterAll
        /// on the provided [FbtRegistrar] type, then return the set of trees to reload.
        /// </summary>
        public delegate IEnumerable<(string treeName, BehaviorTreeBlob blob)>
            AssemblyReloadHandler(Type registrarType, Assembly newAssembly);

        // ---- Events (fired from DrainPendingCallbacks on application thread) ----
        public event Action<string>? OnReloadCompleted;
        public event Action<string, Exception>? OnReloadFailed;

        // ---- Private fields ----
        private readonly AssemblyReloadHandler _handler;
        private readonly FileSystemWatcher _watcher;
        private readonly string _watchDirectory;
        private readonly Timer _debounceTimer;
        private string? _pendingPath;
        private readonly object _debounceLock = new object();
        private const int DebounceMs = 200;

        private AssemblyLoadContext? _currentAlc;
        private readonly ConcurrentQueue<Action> _pendingCallbacks
            = new ConcurrentQueue<Action>();

        // ---- Weak reference for GC verification ----
        /// <summary>
        /// A weak reference to the previously-unloaded ALC. Used in tests to verify
        /// the old ALC was GC'd. Null if no reload has occurred yet, or if only one
        /// reload has occurred (no previous ALC to track).
        /// </summary>
        public WeakReference<AssemblyLoadContext>? PreviousAlcRef { get; private set; }

        // ---- Constructor ----

        /// <summary>
        /// Creates a watcher that monitors <paramref name="watchDirectory"/> for any
        /// <c>*.dll</c> change.  Use the two-parameter overload to watch a specific file.
        /// </summary>
        public FbtAssemblyHotReloader(string watchDirectory, AssemblyReloadHandler handler)
            : this(watchDirectory, "*.dll", handler) { }

        /// <summary>
        /// Creates a watcher that monitors <paramref name="watchDirectory"/> for changes
        /// to files matching <paramref name="dllFilter"/> (e.g. <c>"Hrot.AI.Behaviors.dll"</c>).
        /// When <paramref name="dllFilter"/> contains no wildcards the filter acts as an
        /// exact filename match and <see cref="TriggerInitialLoad"/> can derive the path
        /// automatically.
        /// </summary>
        public FbtAssemblyHotReloader(string watchDirectory, string dllFilter, AssemblyReloadHandler handler)
        {
            _watchDirectory = watchDirectory;
            _handler = handler;
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(watchDirectory, dllFilter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
        }

        /// <summary>
        /// Immediately queues a reload of the watched DLL on a background thread without
        /// waiting for a file-system change event.  Useful for the initial load at
        /// application startup.
        ///
        /// <para>No-op when the watcher filter contains wildcards (the exact path cannot
        /// be determined) or when the file does not yet exist.</para>
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

        private void LoadAndReload(string dllPath)
        {
            try
            {
                var newAlc = new AssemblyLoadContext(
                    Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);
                Assembly newAssembly;

                // Retry loop: the file may still be locked if the copy is in progress
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

                // Find [FbtRegistrar]-annotated type
                Type? registrarType = null;
                foreach (var type in newAssembly.GetTypes())
                {
                    if (type.GetCustomAttribute(typeof(FbtRegistrarAttribute)) != null)
                    {
                        registrarType = type;
                        break;
                    }
                }

                if (registrarType == null)
                {
                    // No registrar found — fail gracefully
                    var ex = new InvalidOperationException(
                        $"No [FbtRegistrar] class found in '{dllPath}'.");
                    _pendingCallbacks.Enqueue(() => OnReloadFailed?.Invoke(dllPath, ex));
                    newAlc.Unload();
                    return;
                }

                // Call user handler to do registration + get blobs
                IEnumerable<(string, BehaviorTreeBlob)> results;
                try
                {
                    results = _handler(registrarType, newAssembly);
                }
                catch (Exception ex)
                {
                    _pendingCallbacks.Enqueue(() => OnReloadFailed?.Invoke(dllPath, ex));
                    newAlc.Unload();
                    return;
                }

                // Unload OLD ALC and track it for GC verification
                var oldAlc = Interlocked.Exchange(ref _currentAlc, newAlc);
                if (oldAlc != null)
                {
                    PreviousAlcRef = new WeakReference<AssemblyLoadContext>(oldAlc);
                    oldAlc.Unload();
                }

                // Queue success callbacks
                foreach (var (treeName, _) in results)
                {
                    var name = treeName;
                    _pendingCallbacks.Enqueue(() => OnReloadCompleted?.Invoke(name));
                }
            }
            catch (Exception ex)
            {
                _pendingCallbacks.Enqueue(() => OnReloadFailed?.Invoke(dllPath, ex));
            }
        }

        // ---- Application thread drain ----
        /// <summary>
        /// Must be called once per game update from the application thread.
        /// Fires OnReloadCompleted / OnReloadFailed for any queued reload results.
        /// </summary>
        public void DrainPendingCallbacks()
        {
            while (_pendingCallbacks.TryDequeue(out var cb))
                cb();
        }

        // ---- IDisposable ----
        public void Dispose()
        {
            _watcher.Dispose();
            _debounceTimer.Dispose();
            var alc = Interlocked.Exchange(ref _currentAlc, null);
            alc?.Unload();
        }
    }
}
