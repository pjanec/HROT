using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>⭐ S1b-2 / SD-S5v (D-138) — the device's MERGED input — what its logic reads (its OWN entity — the parallel rule). Written ONLY by the device
    /// tick's serial pre-pass (<see cref="DeviceInputMerge"/>). ⚠ R-4301: SD-S5v grew it (held bits, switches) and dropped the frame <c>Seq</c>; the merge's
    /// bookkeeping (per-field writer + wall time, per-source sequence) is NOT here — a wall time in recorded state would make every recording depend on the
    /// wall clock (the S1e lesson) — it lives in the merge itself.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DeviceInput)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DeviceInput
    {
        public DeviceControl Control;
    }

    /// <summary>⭐ SD-S5v (D-143) — what one change says.</summary>
    public enum InputChangeKind : byte
    {
        None = 0,
        /// <summary>A press of button <c>Element</c>: the device bumps its own counter by <c>Count</c> and sets the held bit.</summary>
        ButtonDown = 1,
        /// <summary>Button <c>Element</c> released: the held bit clears.</summary>
        ButtonUp = 2,
        /// <summary>Switch <c>Element</c> is at position <c>Value</c>.</summary>
        Switch = 3,
        /// <summary>Axis <c>Element</c> is at <c>Value</c> (absolute, −1…1).</summary>
        Axis = 4,
        /// <summary>The held bits of the buttons this source holds (<c>Value</c> = their levels, <c>Count</c> = WHICH bits it speaks for) — the heal (D-140): a
        /// lost <c>ButtonUp</c> clears here; ⭐ scoped to the source's own buttons, so a source's heal never clears a button another source holds.</summary>
        HeldSnapshot = 5,
    }

    /// <summary>⭐⭐ SD-S5v (D-143) — ONE change of one element, 8 B: (kind, the element's index within its kind in the type's descriptor — D-142, a count, a value).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InputChange
    {
        public InputChangeKind Kind;
        /// <summary>The element's index WITHIN its kind (<c>DeviceDefDto.Elements</c>).</summary>
        public byte Element;
        /// <summary><see cref="InputChangeKind.ButtonDown"/>: the presses coalesced in this frame · <see cref="InputChangeKind.HeldSnapshot"/>: the bits it speaks for · else 0.</summary>
        public ushort Count;
        /// <summary>Switch: the position · Axis: −1…1 · HeldSnapshot: the held mask.</summary>
        public float Value;
    }

    /// <summary>⭐ SD-S5v (D-144) — at most eight changes per event (more ⇒ a second event, the next <c>SourceSeq</c>).</summary>
    [InlineArray(Count)]
    public struct InputChanges
    {
        public const int Count = 8;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private InputChange _element;
#pragma warning restore IDE0044
    }

    /// <summary>⭐ SD-S5v (D-145) — the KIND of source an input event comes from; the merge's priority IS the kind (<see cref="DeviceInputSources.Priority"/>), so
    /// no separate field can disagree with it.</summary>
    public enum DeviceInputSource : byte
    {
        /// <summary>The station's virtual panel: the HUD's clickable chips, the mouse, the keys, the pad (priority 1).</summary>
        Virtual = 0,
        /// <summary>A scenario's scripted trainee (priority 1).</summary>
        Script = 1,
        /// <summary>An instructor's console pressing a trainee's device (priority 1).</summary>
        Instructor = 2,
        /// <summary>A physical replica of the device (priority 2).</summary>
        Replica = 3,
        /// <summary>A physical tracker (priority 3).</summary>
        Tracker = 4,
    }

    /// <summary>⭐ SD-S5v (D-139 / D-145) — the source kinds' priorities.</summary>
    public static class DeviceInputSources
    {
        /// <summary>Tracker 3 &gt; Replica 2 &gt; Virtual = Script = Instructor 1.</summary>
        public static byte Priority(DeviceInputSource s) => s switch
        {
            DeviceInputSource.Tracker => 3,
            DeviceInputSource.Replica => 2,
            _ => 1,
        };

        /// <summary>The kinds (the merge's per-source slots).</summary>
        public const int Kinds = 5;
    }

    /// <summary>
    /// ⭐⭐⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-137 / D-143 / D-144 / D-145) — ONE SPARSE input event for ONE device: what one
    /// SOURCE (a station's virtual panel, a scripted trainee, a replica, a tracker) CHANGED since its own last event — a fixed pose block (its
    /// <see cref="PoseMask"/> says which parts are present) and up to eight element changes. Every source sends only its own changes, so any number of
    /// sources can drive one device without overwriting each other; the DEVICE merges them in arrival order (<see cref="DeviceInputMerge"/>).
    /// ⛔ SUPERSEDES <c>DeviceFrameEvent</c> (the whole-state frame — its event id is reused, R-4301). Unmanaged, fixed size (~112 B), 0 B to send.
    /// ⚠ The device is a local <see cref="Entity"/> handle — the event is published on the local bus until the interaction transport carries it (SD-C, which
    /// re-keys it by network id).
    /// </summary>
    [EventId(Fdp.Toolkit.Behavior.Possession.PossessionEventIds.DeviceInput)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInputEvent
    {
        public Entity Device;
        /// <summary>The station the source belongs to (node + seat) — the device takes events only from the station operating it.</summary>
        public StationId Station;
        public DeviceInputSource Source;
        /// <summary>Changes used, ≤ <see cref="InputChanges.Count"/>.</summary>
        public byte Count;
        /// <summary>Which pose parts are present — <see cref="OrientationPresent"/> · <see cref="PositionPresent"/> · <see cref="ViewedPresent"/> (+ <see cref="ViewedOn"/>).</summary>
        public byte PoseMask;
        /// <summary>Per (Station, Source), monotonic: the device drops a duplicate (≤ the last it took).</summary>
        public uint SourceSeq;
        public Quaternion Orientation;
        public Vector3 Position;
        public InputChanges Changes;

        public const byte OrientationPresent = 1, PositionPresent = 2, ViewedPresent = 4, ViewedOn = 8;
    }

    /// <summary>
    /// ⭐⭐⭐ SD-S5v (§3.13e D-138 / D-139 / D-140) — THE device-side MERGE, in the device tick's serial pre-pass (one place, the device's own node): every
    /// <see cref="DeviceInputEvent"/> of the frame, in arrival order, into the device's <see cref="DeviceInput"/>. A press bumps the DEVICE's own counter (so
    /// presses from several sources add up and <see cref="DevicePressReader"/> is unchanged); a FIELD (an axis, a switch, the orientation, the position)
    /// is last-writer-wins UNLESS a higher-priority source (<see cref="DeviceInputSources.Priority"/>) wrote it within
    /// <see cref="HoldSeconds"/> of WALL time — a live tracker keeps the look, the mouse takes it the moment the tracker goes quiet. A duplicate (a
    /// <c>SourceSeq</c> not above that source's last) is dropped. ⭐ The bookkeeping (per field: the writer's priority and when; per source: its last seq) is
    /// this object's own, never recorded (a wall time in recorded state would make the recording depend on the wall clock). 0 B per steady frame.
    /// </summary>
    public sealed class DeviceInputMerge
    {
        /// <summary>How long a higher-priority writer holds a field (s, wall) — D-139.</summary>
        public const double HoldSeconds = 0.5;

        // the fields: axes 0–3, switches 4–11, orientation 12, position 13
        private const int FAxis = 0, FSwitch = DeviceAxes.Count, FOrientation = FSwitch + DeviceSwitches.Count, FPosition = FOrientation + 1;
        private const int Fields = FPosition + 1;

        [InlineArray(Fields)] private struct Bytes { private byte _e; }
        [InlineArray(Fields)] private struct Times { private double _e; }
        [InlineArray(DeviceInputSources.Kinds)] private struct Seqs { private uint _e; }

        private struct State
        {
            public Bytes Writer;     // the priority that last wrote each field
            public Times WrittenAt;  // when (wall s)
            public StationId Station;   // the station the sequences belong to (a new operator re-bases)
            public Seqs LastSeq;
            public byte SeqKnown;    // bit k: LastSeq[k] is valid
        }

        private readonly System.Collections.Generic.Dictionary<long, State> _states = new();
        private static readonly double TicksToSeconds = 1.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>The wall clock (s) — a rail sets its own.</summary>
        public Func<double> Clock { get; set; } = () => System.Diagnostics.Stopwatch.GetTimestamp() * TicksToSeconds;

        /// <summary>Events taken / dropped as duplicates / refused by the hold (rails).</summary>
        public long Taken { get; private set; }
        /// <inheritdoc cref="Taken"/>
        public long Duplicates { get; private set; }
        /// <inheritdoc cref="Taken"/>
        public long Held { get; private set; }

        /// <summary>Forgets a device's bookkeeping (a new operator — its input was cleared, so the next event of every source is taken).</summary>
        public void Forget(Entity device) => _states.Remove(Key(device));

        private static long Key(Entity e) => ((long)e.Generation << 32) | (uint)e.Index;

        /// <summary>Merges <paramref name="e"/> into <paramref name="input"/> (the device's current merged input; default when it has none). False ⇒ a duplicate, nothing changed.</summary>
        public bool Apply(in DeviceInputEvent e, ref DeviceInput input)
        {
            ref var st = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_states, Key(e.Device), out _);
            if (!st.Station.Equals(e.Station)) { st = default; st.Station = e.Station; }
            int k = (int)e.Source;
            if (k < 0 || k >= DeviceInputSources.Kinds) return false;
            if ((st.SeqKnown & (1 << k)) != 0 && e.SourceSeq <= st.LastSeq[k]) { Duplicates++; return false; }
            st.LastSeq[k] = e.SourceSeq;
            st.SeqKnown |= (byte)(1 << k);
            Taken++;

            byte prio = DeviceInputSources.Priority(e.Source);
            double now = Clock();
            ref var c = ref input.Control;
            if ((e.PoseMask & DeviceInputEvent.OrientationPresent) != 0 && May(ref st, FOrientation, prio, now))
            { c.Orientation = e.Orientation; c.Trackers |= DeviceControl.OrientationPresent; }
            if ((e.PoseMask & DeviceInputEvent.PositionPresent) != 0 && May(ref st, FPosition, prio, now))
            { c.Position = e.Position; c.Trackers |= DeviceControl.PositionPresent; }
            if ((e.PoseMask & DeviceInputEvent.ViewedPresent) != 0) c.Viewed = (e.PoseMask & DeviceInputEvent.ViewedOn) != 0 ? (byte)1 : (byte)0;   // the station's (the view)

            int n = Math.Min(e.Count, (byte)InputChanges.Count);
            for (int i = 0; i < n; i++)
            {
                var ch = e.Changes[i];
                int el = ch.Element;
                switch (ch.Kind)
                {
                    case InputChangeKind.ButtonDown when el < DevicePresses.Count:
                        unchecked { c.Presses[el] += (byte)Math.Max((ushort)1, ch.Count); }   // ⭐ ADDITIVE: the device's own counter, whoever pressed
                        c.Held |= (ushort)(1 << el);
                        break;
                    case InputChangeKind.ButtonUp when el < DevicePresses.Count:
                        c.Held &= (ushort)~(1 << el);
                        break;
                    case InputChangeKind.Switch when el < DeviceSwitches.Count:
                        if (May(ref st, FSwitch + el, prio, now)) c.Switches[el] = (byte)Math.Clamp((int)MathF.Round(ch.Value), 0, 255);
                        break;
                    case InputChangeKind.Axis when el < DeviceAxes.Count:
                        if (May(ref st, FAxis + el, prio, now)) c.Axes[el] = Math.Clamp(ch.Value, -1f, 1f);
                        break;
                    case InputChangeKind.HeldSnapshot:
                        c.Held = (ushort)(((uint)c.Held & ~(uint)ch.Count) | ((uint)ch.Value & ch.Count));   // only the bits this source holds
                        break;
                }
            }
            return true;
        }

        /// <summary>D-139 — may a source of <paramref name="prio"/> write field <paramref name="f"/> now? Last writer wins unless a HIGHER priority wrote it
        /// within <see cref="HoldSeconds"/>; a write records the writer and the time.</summary>
        private bool May(ref State st, int f, byte prio, double now)
        {
            if (prio < st.Writer[f] && now - st.WrittenAt[f] < HoldSeconds) { Held++; return false; }
            st.Writer[f] = prio;
            st.WrittenAt[f] = now;
            return true;
        }
    }
}
