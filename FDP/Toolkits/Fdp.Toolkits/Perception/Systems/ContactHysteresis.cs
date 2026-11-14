using System;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐⭐ <b>The ONE acquired / lost rule</b> (docs/DESIGN_Sensors_And_Doctrine.md §5.4). A contact seen THIS tick becomes
    /// <see cref="SensorContactState.Acquired"/>; an acquired contact unseen for more than
    /// <see cref="TrackLostThresholdTicks"/> becomes <see cref="SensorContactState.Lost"/>.
    /// <para>⭐ Called by <c>SensorMemoryStage</c> for every perception sensor the EQS solver runs — vision included since
    /// <c>CE-3038</c>. Its second caller, the toolkit's own <c>SensorTrackDebounceSystem</c>, is retired (<c>CE-3052</c>).</para>
    /// </summary>
    public static class ContactHysteresis
    {
        /// <summary>Ticks of silence after which an acquired contact is lost.</summary>
        public const uint TrackLostThresholdTicks = 20;

        /// <summary>
        /// Applies the rule to every slot of <paramref name="list"/> at <paramref name="tick"/> and writes each transition
        /// (target id, new status) into <paramref name="transitions"/>. Returns the number written; <paramref name="changed"/>
        /// is true when any slot changed.
        /// </summary>
        public static int Apply(ref SensorContactList list, uint tick,
                                Span<(long TargetId, SensorTrackStatus State)> transitions, out bool changed)
            => Apply(ref list, tick, transitions, out changed, double.PositiveInfinity);

        /// <summary>
        /// ⭐ <c>CE-3260</c> (BR-7, DESIGN_Sensors_And_Doctrine §5.7 AQ-1) — the rule with the DWELL: a Pending / Lost contact seen now is
        /// acquired only once it has been seen without a break for its <see cref="SensorContactList.AcquireAfter"/> (from
        /// <see cref="SensorContactList.VisibleSince"/> to <paramref name="now"/>, sim seconds). 0 ⇒ the tick he is seen, as before.
        /// </summary>
        public static unsafe int Apply(ref SensorContactList list, uint tick,
                                       Span<(long TargetId, SensorTrackStatus State)> transitions, out bool changed, double now)
        {
            changed = false;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var state = (SensorContactState)list.State[i];
                uint age = tick - list.LastSeenTick[i];

                if (state is SensorContactState.Pending or SensorContactState.Lost)
                {
                    if (age != 0) continue;
                    // ⭐ CE-3260 — not seen long enough yet. ⚠ VisibleSince is a FLOAT and now a double: 0.1 − 0.1f is a hair below 0, so a zero
                    //   dwell must pass outright and the rest compare to the millisecond (found by the rail: no contact was acquired at all)
                    if (list.AcquireAfter[i] > 0f && now - list.VisibleSince[i] < list.AcquireAfter[i] - 1e-3) continue;
                    list.State[i] = (byte)SensorContactState.Acquired;
                    changed = true;
                    if (n < transitions.Length) transitions[n++] = (list.EntityIds[i], SensorTrackStatus.Acquired);
                }
                else if (state == SensorContactState.Acquired && age > TrackLostThresholdTicks)
                {
                    list.State[i] = (byte)SensorContactState.Lost;
                    changed = true;
                    if (n < transitions.Length) transitions[n++] = (list.EntityIds[i], SensorTrackStatus.Lost);
                }
            }
            return n;
        }
    }
}
