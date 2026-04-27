using System;
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
    /// In normal continuous playback, <see cref="ExtraFramesThisTick"/> is 0 and
    /// Strategy A applies (advance exactly 1 frame per tick).  Fast-forward is
    /// achieved by setting <see cref="ExtraFramesThisTick"/> = N - 1 before a tick.
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

        /// <summary>
        /// Extra frames to advance in the current tick beyond the default of 1.
        /// Reset to 0 after each Execute call.  Set externally to fast-forward.
        /// </summary>
        [Obsolete("Use ITimeController-based pull model.")]
        public int ExtraFramesThisTick { get; set; } = 0;

        /// <param name="playback">The <see cref="PlaybackController"/> to drive.</param>
        /// <param name="timeController">
        /// Active time controller whose <c>TotalWallTicks</c> drives the pull-model cursor.
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

            long targetTicks  = _timeController.GetCurrentState().TotalWallTicks;
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
