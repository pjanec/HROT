using System;
using Fdp.Core.FlightRecorder;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐⭐ <c>CE-1479</c> (docs/DESIGN_Replay_Time_Axis_And_Review.md §12t) — THE one rule that maps a replay clock to recorded frames, shared by the editor's
    /// <see cref="PlaybackTickSystem"/> and the Replay Browser: <b>a recorded frame is due when its wall-clock ticks are ≤ the clock</b>; the frames due are applied
    /// in order (deltas are incremental) and never more than the clock says; a LARGE gap is a seek, not a run of steps. ⛔ The Browser used to apply one frame per
    /// 1/60 s of playback (up to 10 per render frame) whatever the recording's own timing — a recording with a frame per sim second played at ~84×.
    /// </summary>
    public static class ReplayClockRule
    {
        /// <summary>More frames due than this ⇒ a seek straight to the clock instead of stepping each (the editor's "Strategy B").</summary>
        public const int SeekThreshold = 3;

        /// <summary>
        /// How many of the frames AFTER the playback's current one are due at <paramref name="clockWallTicks"/> (consecutive, in order), counting at most
        /// <paramref name="cap"/>. 0 = nothing is due yet (or the playback is at its end).
        /// </summary>
        public static int FramesDue(PlaybackController playback, long clockWallTicks, int cap = SeekThreshold + 1)
        {
            int count = 0;
            for (int i = playback.CurrentFrame + 1; i < playback.TotalFrames && count < cap; i++)
            {
                if (playback.GetFrameMetadata(i).WallClockTicks <= clockWallTicks) count++;
                else break;
            }
            return count;
        }

        /// <summary>Is the gap large enough that the clock is reached by a seek (<see cref="FramesDue"/> counted with the default cap)?</summary>
        public static bool IsSeekGap(int due) => due > SeekThreshold;
    }
}
