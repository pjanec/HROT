using System;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐⭐ <b>The ONE acquired / lost rule</b> (docs/DESIGN_Sensors_And_Doctrine.md §5.4). A contact seen THIS tick becomes
    /// <see cref="SensorContactState.Acquired"/>; an acquired contact unseen for more than
    /// <see cref="TrackLostThresholdTicks"/> becomes <see cref="SensorContactState.Lost"/>.
    /// <para>⭐ Called by <c>SensorMemoryStage</c> (every perception sensor the EQS solver runs — vision included since
    /// <c>CE-3038</c>) and by <see cref="SensorTrackDebounceSystem"/> (the toolkit's own chain, kept only for the FDP
    /// examples) — one rule, two callers, no copy.</para>
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
        public static unsafe int Apply(ref SensorContactList list, uint tick,
                                       Span<(long TargetId, SensorTrackStatus State)> transitions, out bool changed)
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
