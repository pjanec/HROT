using System;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// Data-plane module that strictly owns one <see cref="AsyncRecorder"/> and the
    /// <see cref="RecorderTickSystem"/> that drives it at the scheduler's frame rate.
    /// <para>
    /// Lifecycle: <see cref="RegisterSystems"/> opens the <c>.fdp</c> file stream;
    /// <see cref="Dispose"/> blocks until all LZ4 buffers are flushed and the
    /// <c>.meta.json</c> manifest is written.
    /// </para>
    /// <para>
    /// Zero-cost idle path: when the module is not installed, no
    /// <see cref="RecorderTickSystem"/> exists in the scheduler — no
    /// <c>if (isRecording)</c> guards on the hot path.
    /// </para>
    /// </summary>
    public sealed class RecordingModule : IModule, IDisposable
    {
        private readonly RecordingConfiguration _config;
        private AsyncRecorder? _recorder;

        /// <inheritdoc/>
        public string Name => $"Recording_{_config.DrillId:N}";

        /// <inheritdoc/>
        /// <remarks>
        /// Synchronous policy so <see cref="RecorderTickSystem"/> runs on the main thread
        /// and receives the live <see cref="EntityRepository"/> as its
        /// <see cref="ISimulationView"/>.
        /// </remarks>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <summary>
        /// Creates a recording module with the given configuration.
        /// The output file is not opened until <see cref="RegisterSystems"/> is called.
        /// </summary>
        public RecordingModule(RecordingConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            _recorder = new AsyncRecorder(_config.FilePath);

            if (_config.EntityFilter != null)
                _recorder.EntityFilter = _config.EntityFilter;

            registry.RegisterSystem(new RecorderTickSystem(_recorder));
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { /* driven by RecorderTickSystem */ }

        /// <summary>
        /// Blocking dispose: drains the LZ4 front-buffer, writes
        /// <c>MaxNetworkId</c> and <c>.meta.json</c> manifest before returning.
        /// <c>NodeOpStatus(Success)</c> must not be sent before this completes.
        /// </summary>
        public void Dispose()
        {
            _recorder?.Dispose();
            _recorder = null;
        }
    }
}
