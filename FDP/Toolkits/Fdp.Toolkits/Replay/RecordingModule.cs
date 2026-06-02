using System;
using System.IO;
using System.Threading;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replay
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
    public sealed class RecordingModule : IEcsModule, IDisposable
    {
        private readonly RecordingConfiguration _config;
        private AsyncRecorder? _recorder;

        /// <inheritdoc/>
        public string Name => $"Recording_{_config.ExerciseId:N}";

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
            var metadata = new RecordingMetadata { ExerciseId = _config.ExerciseId, NodeId = _config.NodeId };
            _recorder = new AsyncRecorder(_config.FilePath, metadata);

            if (_config.EntityFilter != null)
                _recorder.EntityFilter = _config.EntityFilter;

            registry.RegisterSystem(new RecorderTickSystem(_recorder, _config.Blocking));
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { /* driven by RecorderTickSystem */ }

        /// <summary>
        /// Sets the maximum network entity ID for the recording session; written to
        /// <c>.meta.json</c> when <see cref="Dispose"/> is called.
        /// Callers supply the value from the network entity map just before requesting uninstall.
        /// </summary>
        public void SetMaxNetworkId(long maxNetworkId)
        {
            if (_recorder != null)
                _recorder.MaxNetworkId = maxNetworkId;
        }

        /// <summary>
        /// Blocking dispose: waits for in-flight LZ4 writes, closes the <c>.fdp</c> writer
        /// (releasing its <c>FileShare.None</c> handle), and writes the <c>.meta.json</c> manifest.
        /// <c>NodeOpStatus(Success)</c> must not be sent before this completes.
        /// <para>
        /// <b>BATCH-16 Fix B:</b> after closing the writer, this additionally blocks until the OS
        /// has verifiably released the file handle (<see cref="WaitForWriterRelease"/>). The kernel
        /// invokes <see cref="Dispose"/> only after the <c>RecorderTickSystem</c> has been removed
        /// from the topology and drained, so this is the correct (race-free) place to close — but
        /// the handle-release latency between <c>FileStream.Dispose</c> and the OS dropping the lock
        /// could still let a <c>ReplayModule</c> opening the same file for read fail with
        /// <c>IOException("…node_0.fdp… used by another process")</c> (the D9 crash). The barrier
        /// makes the "<c>FinalizeRecordingAsync</c> completed ⟹ file is openable" contract hold.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            if (_recorder == null)
                return;

            // Capture the path BEFORE disposing so we can probe handle release afterwards.
            string filePath = _config.FilePath;

            // AsyncRecorder.Dispose: waits for the in-flight worker, closes the FileStream, writes
            // the .meta.json manifest. The FileShare.None write handle is released here.
            _recorder.Dispose();
            _recorder = null;

            // Block until the just-closed writer handle is verifiably gone, so the next opener
            // (the ReplayModule's PlaybackController, FileShare.Read) cannot lose the race.
            WaitForWriterRelease(filePath);
        }

        /// <summary>
        /// Blocks (bounded) until <paramref name="filePath"/> can be opened for read without any
        /// other process/handle holding a conflicting lock — i.e. the recording writer's handle has
        /// been fully released by the OS. Returns immediately on the first successful probe; gives
        /// up after a short budget (the file is then almost certainly openable and a hard failure
        /// would surface at the real open site anyway). Never throws.
        /// </summary>
        private static void WaitForWriterRelease(string filePath)
        {
            const int maxAttempts = 50;       // ~ up to 500 ms worst case
            const int delayMs = 10;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    // Open with the SAME share mode PlaybackController uses (FileShare.Read). If the
                    // writer's exclusive (FileShare.None) handle is still open this throws IOException;
                    // once released this succeeds and we are done.
                    using var probe = new FileStream(
                        filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return;
                }
                catch (IOException)
                {
                    // Handle still releasing — back off briefly and retry.
                    Thread.Sleep(delayMs);
                }
                catch (Exception)
                {
                    // Any non-sharing error (e.g. file genuinely missing) is not ours to mask here;
                    // let the real open site report it.
                    return;
                }
            }
        }
    }
}
