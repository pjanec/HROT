using System;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// Per-frame recording system that alternates between keyframes (every
    /// <see cref="KeyframeInterval"/> ticks) and delta frames.
    /// Registered into the scheduler by <see cref="RecordingModule"/> and
    /// <see cref="StoryRecorderModule"/> via <see cref="ISystemRegistry"/>.
    /// Runs in the <see cref="SystemPhase.PostSimulation"/> phase so entity state
    /// is fully settled before snapshot capture.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class RecorderTickSystem : IEcsModuleSystem
    {
        /// <summary>Number of delta frames between keyframes.</summary>
        public const int KeyframeInterval = 60;

        private readonly AsyncRecorder _recorder;
        private readonly bool _blocking;
        private int _framesSinceKeyframe;
        private uint _prevTick;

        /// <param name="recorder">The <see cref="AsyncRecorder"/> that owns the output file.</param>
        /// <param name="blocking">
        /// When <c>true</c>, each <c>CaptureFrame</c> / <c>CaptureKeyframe</c> call blocks
        /// until the front-buffer swap completes, preventing delta drops in tight loops.
        /// Mirrors <see cref="RecordingConfiguration.Blocking"/>.
        /// </param>
        public RecorderTickSystem(AsyncRecorder recorder, bool blocking = false)
        {
            _recorder = recorder;
            _blocking = blocking;
            // Start at KeyframeInterval - 1 so the very first Execute call issues a keyframe.
            _framesSinceKeyframe = KeyframeInterval - 1;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // For synchronous (main-thread) modules, view IS the live EntityRepository.
            var repo = (EntityRepository)view;

            // Read the atomic, frame-locked wall-clock timestamp from the GlobalTime singleton.
            // This is the single source of truth populated by the time controller at the start
            // of every frame, guaranteeing all PostSimulation systems see identical ticks.
            // Falls back to DateTime.UtcNow.Ticks until Phase 3 time controllers populate
            // GlobalTime.TotalWallTicks (WCR-P3-T002/T003).
            var globalTime = repo.GetSingletonUnmanaged<GlobalTime>();
            long wallClockTicks = globalTime.TotalWallTicks != 0
                ? globalTime.TotalWallTicks
                : DateTime.UtcNow.Ticks;

            if (++_framesSinceKeyframe >= KeyframeInterval)
            {
                _recorder.CaptureKeyframe(repo, wallClockTicks, blocking: _blocking);
                _framesSinceKeyframe = 0;
            }
            else
            {
                _recorder.CaptureFrame(repo, _prevTick, wallClockTicks, blocking: _blocking);
            }

            _prevTick = repo.GlobalVersion;
        }
    }
}
