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
    public sealed class RecorderTickSystem : IModuleSystem
    {
        /// <summary>Number of delta frames between keyframes.</summary>
        public const int KeyframeInterval = 60;

        private readonly AsyncRecorder _recorder;
        private int _framesSinceKeyframe;
        private uint _prevTick;

        /// <param name="recorder">The <see cref="AsyncRecorder"/> that owns the output file.</param>
        public RecorderTickSystem(AsyncRecorder recorder)
        {
            _recorder = recorder;
            // Start at KeyframeInterval - 1 so the very first Execute call issues a keyframe.
            _framesSinceKeyframe = KeyframeInterval - 1;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // For synchronous (main-thread) modules, view IS the live EntityRepository.
            var repo = (EntityRepository)view;

            if (++_framesSinceKeyframe >= KeyframeInterval)
            {
                _recorder.CaptureKeyframe(repo);
                _framesSinceKeyframe = 0;
            }
            else
            {
                _recorder.CaptureFrame(repo, _prevTick);
            }

            _prevTick = repo.GlobalVersion;
        }
    }
}
