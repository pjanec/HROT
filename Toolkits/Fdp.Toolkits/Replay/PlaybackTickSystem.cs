using System;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Replication.Utilities;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// Per-frame playback tick system that advances the <see cref="PlaybackController"/>
    /// one frame per Execute call (Strategy A) or jumps directly to a target frame
    /// when the gap exceeds <see cref="StrategyBThreshold"/> (Strategy B).
    /// <para>
    /// Strategy A (small gap, ≤ <see cref="StrategyBThreshold"/> frames):
    /// sequential <see cref="PlaybackController.StepForward"/> calls — all delta
    /// frames are applied in-memory; no intermediate frames are rendered.
    /// </para>
    /// <para>
    /// Strategy B (large gap): direct <see cref="PlaybackController.SeekToFrame"/>
    /// which binary-searches the keyframe index, blasts the keyframe chunks into
    /// <c>NativeChunkTable</c>, then applies ≤ 59 delta frames.
    /// </para>
    /// <para>
    /// The indexing cursor is derived from the time controller's
    /// <c>TotalTime</c> (seconds), converted to wall-clock ticks via
    /// <c>Stopwatch.Frequency</c>.  This makes replay natively speed-controllable
    /// and pauseable: fast-forward, slow-motion, and pausing are driven purely by
    /// the time controller's <c>TimeScale</c> — no manual frame-injection is needed.
    /// During a seek the time controller must be seeded with the target time
    /// (see <see cref="ReplayModule.SeekToWallClockTicksAsync"/>).
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class PlaybackTickSystem : IEcsModuleSystem
    {
        /// <summary>
        /// Frame-gap threshold above which Strategy B (SeekToFrame) is used
        /// instead of iterative StepForward calls.
        /// </summary>
        public const int StrategyBThreshold = 3;

        private readonly PlaybackController _playback;
        private readonly ITimeController _timeController;
        private readonly Action? _afterSeek;

        /// <param name="playback">The <see cref="PlaybackController"/> to drive.</param>
        /// <param name="timeController">
        /// Active time controller whose <c>TotalTime</c> (seconds) drives the pull-model
        /// cursor.  The value is converted to wall-clock ticks via
        /// <c>Stopwatch.Frequency</c> before comparison against the recording index.
        /// </param>
        public PlaybackTickSystem(PlaybackController playback, ITimeController timeController, Action? afterSeek = null)
        {
            _playback       = playback;
            _timeController = timeController;
            _afterSeek      = afterSeek;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_playback.IsAtEnd) return;

            long targetTicks  = (long)(_timeController.GetCurrentState().TotalTime * Stopwatch.Frequency);
            long currentTicks = _playback.IsAtStart
                ? long.MinValue
                : _playback.GetFrameMetadata(_playback.CurrentFrame).WallClockTicks;

            if (targetTicks <= currentTicks) return;

            var repo = (EntityRepository)view;

            // Count consecutive upcoming frames with WallClockTicks <= targetTicks.
            // Stop as soon as count exceeds StrategyBThreshold (O(threshold) check).
            int count     = 0;
            int nextFrame = _playback.CurrentFrame + 1;
            for (int i = nextFrame; i < _playback.TotalFrames && count <= StrategyBThreshold; i++)
            {
                if (_playback.GetFrameMetadata(i).WallClockTicks <= targetTicks)
                    count++;
                else
                    break;
            }

            if (count > StrategyBThreshold)
            {
                // Strategy B: large gap — seek directly to target wall ticks.
                _playback.SeekToWallClockTicks(repo, targetTicks);
                SmartEgressUtil.ForceMarkAllDirty(repo);
                _afterSeek?.Invoke();
            }
            else
            {
                // Strategy A: small gap — step forward frame by frame.
                for (int i = 0; i < count && !_playback.IsAtEnd; i++)
                    _playback.StepForward(repo);
            }
        }
    }
}
