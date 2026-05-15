using System;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.FlightRecorder;
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
            Playback = new PlaybackController(fdpPath);
            Playback.EventBus = SandboxBus;
            CurrentFdpPath = fdpPath;
            // Register all component types found in the recording's schema manifest so that
            // ApplyChunkData can restore component data into SandboxRepo.
            RecordingSearchService.RegisterAllComponents(SandboxRepo, Playback);
        }

        /// <summary>
        /// Seeks to a specific frame index.
        /// Order: ClearCurrentBuffers -> SeekToFrame -> HistoryService.Capture.
        /// </summary>
        public void SeekToFrame(int frameIndex)
        {
            ThrowIfDisposed();
            if (Playback == null) return;
            SandboxBus.ClearCurrentBuffers();
            Playback.SeekToFrame(SandboxRepo, frameIndex);
            HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame);
        }

        /// <summary>
        /// Steps one frame forward.
        /// Order: ClearCurrentBuffers -> StepForward -> HistoryService.Capture.
        /// Returns false if already at end of recording.
        /// </summary>
        public bool StepForward()
        {
            ThrowIfDisposed();
            if (Playback == null) return false;
            SandboxBus.ClearCurrentBuffers();
            bool stepped = Playback.StepForward(SandboxRepo);
            if (stepped)
                HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame);
            return stepped;
        }

        /// <summary>
        /// Steps one frame backward.
        /// Order: ClearCurrentBuffers -> StepBackward -> HistoryService.Capture.
        /// Returns false if already at start of recording.
        /// </summary>
        public bool StepBackward()
        {
            ThrowIfDisposed();
            if (Playback == null) return false;
            SandboxBus.ClearCurrentBuffers();
            bool stepped = Playback.StepBackward(SandboxRepo);
            if (stepped)
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
    }
}
