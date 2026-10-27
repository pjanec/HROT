using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐⭐ <c>CE-3318</c> / <c>CE-3337</c> (restore design §1m) — <b>the ONE protocol that keeps a per-process id counter in step with the world
    /// across a restore.</b> The counter itself stays a process static (minted off the frame thread, or by many systems); the world carries a
    /// COPY (a recorded singleton, written whenever the counter moved), and after a restore — a checkpoint, a dry-run exit, a branch from a
    /// replay (all counted by <c>WorldEpoch.Restored</c>) — the counter resumes from the copy on the restore's FIRST STEP: on the paused
    /// frames before it the clock has not reached the restored time yet, so the copy is decided then; a copy NEWER than the clock is the
    /// abandoned future's and is not used. ⭐ Adopted by the navigation handles (<c>TrajectoryRestoreSystem</c>) and the behaviour run tokens
    /// (<c>BehaviorIngressSystem</c>) — ⛔ never re-implemented per counter.
    /// </summary>
    /// <typeparam name="TMark">The world's copy (a recorded unmanaged singleton).</typeparam>
    public sealed class ResumableCounter<TMark> where TMark : unmanaged
    {
        private readonly Func<int> _last;
        private readonly Action<int> _resumeAt;
        private readonly Func<TMark, (int Last, double At)> _read;
        private readonly Func<int, double, TMark> _make;
        private int _restoresSeen;   // 0: a world restored before this owner's first frame still resumes (CE-3318's order)
        private bool _resumePending;

        public ResumableCounter(Func<int> last, Action<int> resumeAt, Func<TMark, (int Last, double At)> read, Func<int, double, TMark> make)
        {
            _last = last ?? throw new ArgumentNullException(nameof(last));
            _resumeAt = resumeAt ?? throw new ArgumentNullException(nameof(resumeAt));
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _make = make ?? throw new ArgumentNullException(nameof(make));
        }

        /// <summary>How many times the counter was resumed from the world's copy (diagnostics, rails).</summary>
        public int Resumes { get; private set; }

        /// <summary>
        /// Once per frame, before anything mints from the counter. Returns <see langword="false"/> on a paused frame of a pending restore
        /// (the caller must not mint then — nothing does before a step). <paramref name="floor"/>: a value the resumed counter may not go
        /// below (an id a restored entity already holds).
        /// </summary>
        public bool Step(EntityRepository repo, float deltaTime, int floor = int.MinValue)
        {
            int restores = Fdp.Toolkit.Replication.Services.WorldEpoch.RestoresOf(repo);
            if (restores != _restoresSeen) _resumePending = true;
            _restoresSeen = restores;

            if (_resumePending)
            {
                if (deltaTime <= 0f) return false;   // ⛔ and the mirror waits too: it would overwrite the carried copy
                _resumePending = false;
                if (repo.HasSingletonUnmanaged<TMark>() && _read(repo.GetSingletonUnmanaged<TMark>()) is var mark && mark.At <= SimTime(repo) + 1e-6)
                {
                    _resumeAt(Math.Max(mark.Last, floor));
                    Resumes++;
                }
            }
            // the world's copy of the counter, so a checkpoint / a recording carries it
            int last = _last();
            if (!repo.HasSingletonUnmanaged<TMark>() || _read(repo.GetSingletonUnmanaged<TMark>()).Last != last)
                repo.SetSingletonUnmanaged(_make(last, SimTime(repo)));
            return true;
        }

        private static double SimTime(EntityRepository repo) => repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanaged<GlobalTime>().TotalTime : 0d;
    }
}
