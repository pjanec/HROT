using System;
using System.IO;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Toolkit.ReplayBrowser.Support
{
    // Component IDs 296-299 reserved for this file (Fdp.Toolkits.Tests/ReplayBrowser/Support)
    // (Moved from 202-205 which collide with production AreaQueryBatchData/EqsTargetPool/BlueprintBlackboard1024/4096.
    //  291-295 also taken by other test files.)
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(296)]
    public struct HarnessPosition { public float X, Y, Z; }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(299)]
    public struct HarnessEntityInfo { public FixedString32 Name; }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(297)]
    public struct HarnessVelocity { public float Vx, Vy; }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(298)]
    public struct HarnessTransform
    {
        public System.Numerics.Vector3 Position;
    }

    /// <summary>
    /// Test helper that builds deterministic .fdp recordings in temp files.
    /// Callers must call ComponentTypeRegistry.Clear() before construction.
    /// Call BuildToTempFile() to finalize the recording, then Dispose() to clean up temp files.
    /// </summary>
    public sealed class FdpRecordingHarness : IDisposable
    {
        private readonly EntityRepository _repo;
        private AsyncRecorder? _recorder;
        private readonly string _tempFilePath;
        private uint _prevTick;
        private bool _disposed;
        private Entity _lastSpawned;

        public FdpRecordingHarness()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<HarnessPosition>();
            _repo.RegisterComponent<HarnessVelocity>();
            _repo.RegisterComponent<HarnessTransform>();
            _repo.RegisterComponent<HarnessEntityInfo>();

            _tempFilePath = Path.GetTempFileName() + ".fdp";
            _recorder = new AsyncRecorder(_tempFilePath);
            _prevTick = 0;
        }

        /// <summary>The EntityRepository used by this harness.</summary>
        public EntityRepository Repository => _repo;

        /// <summary>The entity most recently created by SpawnEntity().</summary>
        public Entity LastSpawned => _lastSpawned;

        // ── Entity lifecycle ──────────────────────────────────────────────────

        public FdpRecordingHarness SpawnEntity()
        {
            _lastSpawned = _repo.CreateEntity();
            return this;
        }

        /// <summary>Adds a component to the most recently spawned entity.</summary>
        public FdpRecordingHarness WithComponent<T>(T component)
        {
            _repo.AddComponent(_lastSpawned, component);
            return this;
        }

        public FdpRecordingHarness AddComponent<T>(Entity entity, T component)
        {
            _repo.AddComponent(entity, component);
            return this;
        }

        public FdpRecordingHarness RemoveComponent<T>(Entity entity)
        {
            _repo.RemoveComponent<T>(entity);
            return this;
        }

        public FdpRecordingHarness DestroyEntity(Entity entity)
        {
            _repo.DestroyEntity(entity);
            return this;
        }

        /// <summary>
        /// Reads the current component value, applies the mutator, and writes back the result.
        /// </summary>
        public FdpRecordingHarness MutateComponent<T>(Entity entity, Func<T, T> mutator)
        {
            T old = _repo.GetComponent<T>(entity);
            _repo.SetComponent(entity, mutator(old));
            return this;
        }

        // ── Event publishing ──────────────────────────────────────────────────

        public FdpRecordingHarness FireUnmanagedEvent<T>(T evt) where T : unmanaged
        {
            _repo.Bus.Publish(evt);
            return this;
        }

        public FdpRecordingHarness FireManagedEvent<T>(T evt)
        {
            _repo.Bus.PublishManaged(evt);
            return this;
        }

        // ── Simulation tick ───────────────────────────────────────────────────

        public FdpRecordingHarness Tick()
        {
            _repo.Tick();
            return this;
        }

        // ── Recording ────────────────────────────────────────────────────────

        /// <summary>Captures a full keyframe.</summary>
        public FdpRecordingHarness RecordKeyframe(long wallClockTicks = 0)
        {
            if (_recorder == null) throw new InvalidOperationException("Recording already finalized.");
            if (wallClockTicks == 0) wallClockTicks = DateTime.UtcNow.Ticks;

            _recorder.CaptureKeyframe(_repo, wallClockTicks, blocking: true, eventBus: _repo.Bus);
            // Use GlobalVersion - 1 so mutations that happened at the CURRENT version
            // (before the next Tick) are detected as changes in the next delta frame.
            _prevTick = _repo.GlobalVersion > 0 ? _repo.GlobalVersion - 1 : 0;
            _repo.Bus.SwapBuffers();
            return this;
        }

        /// <summary>Captures a delta frame relative to the previous recording point.</summary>
        public FdpRecordingHarness RecordDelta(long wallClockTicks = 0)
        {
            if (_recorder == null) throw new InvalidOperationException("Recording already finalized.");
            if (wallClockTicks == 0) wallClockTicks = DateTime.UtcNow.Ticks;

            _recorder.CaptureFrame(_repo, _prevTick, wallClockTicks, blocking: true, eventBus: _repo.Bus);
            // Use GlobalVersion - 1 so mutations that happened at the CURRENT version
            // (before the next Tick) are detected as changes in the next delta frame.
            _prevTick = _repo.GlobalVersion > 0 ? _repo.GlobalVersion - 1 : 0;
            _repo.Bus.SwapBuffers();
            return this;
        }

        // ── Finalization ──────────────────────────────────────────────────────

        /// <summary>
        /// Finalizes the recording and returns the path to the .fdp temp file.
        /// The .fdp.meta.json companion file is also written at this point.
        /// The caller is responsible for opening any PlaybackController BEFORE calling Dispose().
        /// </summary>
        public string BuildToTempFile()
        {
            if (_recorder == null) throw new InvalidOperationException("Recording already finalized.");
            _recorder.Dispose();
            _recorder = null;
            return _tempFilePath;
        }

        /// <summary>
        /// Finalizes the recording and returns the path via an out parameter.
        /// </summary>
        public void BuildToTempFile(out string path)
        {
            path = BuildToTempFile();
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _recorder?.Dispose(); } catch { /* best-effort flush */ }
            _recorder = null;

            _repo.Dispose();

            TryDelete(_tempFilePath);
            TryDelete(_tempFilePath + ".meta.json");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
