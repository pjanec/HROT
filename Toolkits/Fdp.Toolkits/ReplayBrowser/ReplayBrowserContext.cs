using System;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.FlightRecorder;
using Fdp.Core.Logging;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser
{
    /// <summary>
    /// Headless sandbox context for replay: owns a dedicated EntityRepository, FdpEventBus,
    /// DiagnosticEventHistoryService, and PlaybackController.
    /// No reference to Fdp.Presentation or any UI assembly is allowed in this class.
    /// A richer context that includes RepositoryAdapter and InspectorState is deferred to
    /// Stage 2 (Hrot.ReplayBrowser assembly) which may depend on Fdp.Presentation.
    /// </summary>
    public sealed class ReplayBrowserContext : IDisposable
    {
        public EntityRepository SandboxRepo { get; }
        public FdpEventBus SandboxBus { get; }
        public IDiagnosticEventHistoryService HistoryService { get; }
        public PlaybackController? Playback { get; private set; }
        public string? CurrentFdpPath { get; private set; }
        public int CurrentFrame => Playback?.CurrentFrame ?? -1;

        private bool _disposed;

        public ReplayBrowserContext()
        {
            SandboxRepo = new EntityRepository();
            SandboxBus = new FdpEventBus();
            HistoryService = new DiagnosticEventHistoryService();
            PrimeAppDomainAndSandbox(SandboxRepo, SandboxBus);
        }

        /// <summary>
        /// Constructor for testing: allows injection of custom collaborators.
        /// </summary>
        internal ReplayBrowserContext(
            EntityRepository sandboxRepo,
            FdpEventBus sandboxBus,
            IDiagnosticEventHistoryService historyService)
        {
            SandboxRepo = sandboxRepo;
            SandboxBus = sandboxBus;
            HistoryService = historyService;
        }

        /// <summary>
        /// Loads an .fdp recording file and prepares playback.
        /// The previous PlaybackController (if any) is disposed.
        /// </summary>
        public void LoadRecording(string fdpPath)
        {
            ThrowIfDisposed();
            Playback?.Dispose();
            CurrentFdpPath = fdpPath;
            // Purge old sandbox state so entities and events do not bleed across recordings.
            SandboxRepo.SoftClear();
            SandboxBus.ClearAll();
            HistoryService.ClearHistory();
            try
            {
                Playback = new PlaybackController(fdpPath);
                Playback.EventBus = SandboxBus;
                // Register all component types found in the recording's schema manifest so that
                // ApplyChunkData can restore component data into SandboxRepo.
                RecordingSearchService.RegisterAllComponents(SandboxRepo, Playback);
            }
            catch (Exception ex)
            {
                FdpLog<ReplayBrowserContext>.Error(
                    "FATAL: Failed to load recording '{0}'. Reason: {1}",
                    fdpPath,
                    ex.Message);
                Playback = null;
            }
        }

        /// <summary>
        /// Seeks to a specific frame index.
        /// Order: ClearCurrentBuffers -> SeekToFrame -> HistoryService.Capture.
        /// </summary>
        public void SeekToFrame(int frameIndex, bool suppressHistory = false)
        {
            ThrowIfDisposed();
            if (Playback == null) return;
            if (!suppressHistory)
                HistoryService.ClearHistory();
            SandboxBus.ClearCurrentBuffers();
            Playback.SeekToFrame(SandboxRepo, frameIndex);
            if (!suppressHistory)
                HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame);
        }

        /// <summary>
        /// Steps one frame forward.
        /// Order: ClearCurrentBuffers -> StepForward -> HistoryService.Capture.
        /// Returns false if already at end of recording.
        /// </summary>
        public bool StepForward(bool suppressHistory = false)
        {
            ThrowIfDisposed();
            if (Playback == null) return false;
            SandboxBus.ClearCurrentBuffers();
            bool stepped = Playback.StepForward(SandboxRepo);
            if (stepped && !suppressHistory)
                HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame);
            return stepped;
        }

        /// <summary>
        /// Steps one frame backward.
        /// Order: ClearCurrentBuffers -> StepBackward -> HistoryService.Capture.
        /// Returns false if already at start of recording.
        /// </summary>
        public bool StepBackward(bool suppressHistory = false)
        {
            ThrowIfDisposed();
            if (Playback == null) return false;
            if (!suppressHistory)
                HistoryService.ClearHistory();
            SandboxBus.ClearCurrentBuffers();
            bool stepped = Playback.StepBackward(SandboxRepo);
            if (stepped && !suppressHistory)
                HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame);
            return stepped;
        }

        /// <summary>
        /// Disposes the PlaybackController and EntityRepository.
        /// Double-dispose is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Playback?.Dispose();
            Playback = null;
            SandboxRepo.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ReplayBrowserContext));
        }

        private static void PrimeAppDomainAndSandbox(EntityRepository repo, FdpEventBus bus)
        {
            MethodInfo? registerMethod = null;
            foreach (var m in typeof(EntityRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "RegisterComponent") continue;
                if (!m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length == 1) { registerMethod = m; break; }
            }

            MethodInfo? ensureStreamMethod = typeof(FdpEventBus).GetMethod(
                nameof(FdpEventBus.PrepareForNativeEventReplay),
                BindingFlags.Public | BindingFlags.Instance);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string? fullName = assembly.FullName;
                if (!string.IsNullOrEmpty(fullName) &&
                    (fullName.StartsWith("System", StringComparison.Ordinal) ||
                     fullName.StartsWith("Microsoft", StringComparison.Ordinal)))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = System.Array.FindAll(ex.Types, t => t != null)!;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type.GetCustomAttributes(typeof(ComponentIdAttribute), false).Length > 0)
                    {
                        try
                        {
                            ComponentTypeRegistry.GetOrRegisterManaged(type);
                            registerMethod?.MakeGenericMethod(type).Invoke(repo, new object?[] { null });
                        }
                        catch
                        {
                        }
                    }

                    if (type.IsValueType && type.GetCustomAttributes(typeof(EventIdAttribute), false).Length > 0)
                    {
                        try
                        {
                            ensureStreamMethod?.MakeGenericMethod(type).Invoke(bus, null);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
    }
}
