using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-137 / D-140 / D-144 / D-146) — THE ONE ENCODER every input adapter writes through:
    /// the keyboard / mouse station and its virtual panel (the HUD's clickable chips, the hotkeys, the wheel, the pad), a scenario's scripted trainee, later a
    /// replica or a tracker. ⛔ SUPERSEDES <c>DeviceFrameSender</c> (S1e-2's whole-state frame). An adapter STAGES what its source wants this frame — presses,
    /// held levels, switch positions, axes, the look, <c>Viewed</c> — per device; <see cref="Flush"/> sends each device ONLY what changed since this source's own
    /// last event (<see cref="DeviceInputEvent"/>, ≤ 8 changes each — more ⇒ another event, the next <c>SourceSeq</c>, D-144), and re-sends the source's owned
    /// fields once a second and at once on a new device or operator epoch (the heal, D-140) so a lost <c>ButtonUp</c> or a late device never keeps a stale value.
    /// <para>⭐ Names: <see cref="Encode"/> is the one name → (kind, index) lookup (the type's descriptor, D-142) — the HUD's <c>PanelButton</c>, the replica's
    /// pin map and a scenario's <c>press</c> name all reach the SAME element through it. 0 B per steady frame (fixed slots, a stack span).</para>
    /// <para>⚠ Published on the local bus — the interaction transport carries no device input yet (SD-C).</para>
    /// </summary>
    public sealed class DeviceInputEncoder
    {
        /// <summary>The heal period (s, wall) — D-140.</summary>
        public const double HealSeconds = 1.0;

        [InlineArray(DevicePresses.Count)] private struct Downs { private ushort _e; }

        private struct Slot
        {
            public Entity Device;
            public uint Epoch;
            public bool Known, HealNow;
            public double LastHeal;
            // what the source wants
            public Downs Downs;
            public ushort HeldWant, HeldOwned;   // HeldOwned: the buttons this source ever held — the bits its heal speaks for
            public DeviceAxes Axes; public ushort AxesOwned;
            public DeviceSwitches Switches; public byte SwitchesOwned;
            public Quaternion Look; public bool HasLook;
            public Vector3 Position; public bool HasPosition;
            public bool Viewed, HasViewed;
            // what it last sent
            public ushort SentHeld;
            public DeviceAxes SentAxes; public ushort SentAxesOwned;
            public DeviceSwitches SentSwitches; public byte SentSwitchesOwned;
            public Quaternion SentLook; public bool SentHasLook;
            public Vector3 SentPosition; public bool SentHasPosition;
            public bool SentViewed, SentHasViewed;
        }

        private readonly Slot[] _slots;
        private static readonly double TicksToSeconds = 1.0 / System.Diagnostics.Stopwatch.Frequency;
        // ⭐ process-wide, so a NEW encoder of the same (station, source) — a re-possess builds a new driver — never restarts below the device's last seq (D-145)
        private static uint s_seq;

        public DeviceInputEncoder(DeviceInputSource source, int maxDevices = 8)
        {
            Source = source;
            _slots = new Slot[Math.Max(1, maxDevices)];
        }

        /// <summary>The source kind every event of this encoder carries (its priority, D-145).</summary>
        public DeviceInputSource Source { get; }

        /// <summary>The wall clock (s) of the heal — a rail sets its own; <see cref="double.PositiveInfinity"/> never heals by time.</summary>
        public Func<double> Clock { get; set; } = () => System.Diagnostics.Stopwatch.GetTimestamp() * TicksToSeconds;

        /// <summary>Events sent (rails).</summary>
        public long Sent { get; private set; }

        /// <summary>Heals sent (rails).</summary>
        public long Heals { get; private set; }

        /// <summary>The look as a station holds it: yaw about +Z, then pitch (up +) — forward is +X.</summary>
        public static Quaternion Look(float yaw, float pitch)
            => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pitch);

        /// <summary>⭐ D-146 — THE name → (kind, index) lookup of every adapter: the element named <paramref name="name"/> in <paramref name="type"/>'s descriptor.</summary>
        public static bool Encode(DeviceDefDto? type, string name, out DeviceElementKind kind, out int index)
        {
            if (type is null) { kind = default; index = -1; return false; }
            return type.TryElement(name, out kind, out index);
        }

        // ── staging (this frame) ────────────────────────────────────────────────────────────────────────────

        /// <summary>A momentary press of button <paramref name="button"/>: a <c>ButtonDown</c> (×<paramref name="count"/>, coalesced) and its release, unless the
        /// source holds the button (<see cref="Hold"/>).</summary>
        public void Press(Entity device, int button, int count = 1)
        {
            if ((uint)button >= DevicePresses.Count || count <= 0) return;
            ref var s = ref SlotOf(device);
            s.Downs[button] = (ushort)Math.Min(ushort.MaxValue, s.Downs[button] + count);
        }

        /// <summary>A press of the element named <paramref name="name"/> — a button pressed, a switch stepped to its next position (D-142). Null when accepted,
        /// else why not.</summary>
        public string? Press(Entity device, DeviceDefDto? type, string name, int switchPosition = -1)
        {
            if (!Encode(type, name, out var kind, out int index)) return type is null ? "the device has no TKB type (Gen.Device) naming its elements"
                : $"the device has no element '{name}' (its elements: {type.Names()})";
            switch (kind)
            {
                case DeviceElementKind.Button: Press(device, index); return null;
                case DeviceElementKind.Switch:
                    int positions = Math.Max(1, type!.ElementAt(kind, index)!.Positions);
                    SetSwitch(device, index, switchPosition >= 0 ? switchPosition : (CurrentSwitch(device, index) + 1) % positions);
                    return null;
                default: return $"'{name}' is an axis — it takes a value, not a press";
            }
        }

        /// <summary>The held LEVEL of button <paramref name="button"/> (a replica's or a pad's button held down): down ⇒ one press as it goes down, up ⇒ its release.</summary>
        public void Hold(Entity device, int button, bool down)
        {
            if ((uint)button >= DevicePresses.Count) return;
            ref var s = ref SlotOf(device);
            if (down) { s.HeldWant |= (ushort)(1 << button); s.HeldOwned |= (ushort)(1 << button); } else s.HeldWant &= (ushort)~(1 << button);
        }

        /// <summary>Switch <paramref name="index"/> at <paramref name="position"/>.</summary>
        public void SetSwitch(Entity device, int index, int position)
        {
            if ((uint)index >= DeviceSwitches.Count) return;
            ref var s = ref SlotOf(device);
            s.Switches[index] = (byte)Math.Clamp(position, 0, 255);
            s.SwitchesOwned |= (byte)(1 << index);
        }

        /// <summary>The switch position this source last set (0 when it never did).</summary>
        public int CurrentSwitch(Entity device, int index)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].Device == device && (uint)index < DeviceSwitches.Count) return _slots[i].Switches[index];
            return 0;
        }

        /// <summary>Axis <paramref name="index"/> at <paramref name="value"/> (−1…1).</summary>
        public void SetAxis(Entity device, int index, float value)
        {
            if ((uint)index >= DeviceAxes.Count) return;
            ref var s = ref SlotOf(device);
            s.Axes[index] = Math.Clamp(value, -1f, 1f);
            s.AxesOwned |= (ushort)(1 << index);
        }

        /// <summary>The device's orientation as this source sees it (relative to its mount).</summary>
        public void SetOrientation(Entity device, Quaternion orientation)
        {
            ref var s = ref SlotOf(device);
            s.Look = orientation;
            s.HasLook = true;
        }

        /// <summary>The station's LOOK (world) as the device's orientation RELATIVE TO ITS MOUNT (R5-13 — the mouse mode: the device writes its own pose from it).</summary>
        public void SetWorldLook(EntityRepository w, Entity device, Quaternion look)
        {
            Quaternion mount = w.HasComponent<SimTransform>(device) ? (Quaternion)w.GetComponentRO<SimTransform>(device).Rotation : Quaternion.Identity;
            SetOrientation(device, Quaternion.Normalize(Quaternion.Inverse(mount == default ? Quaternion.Identity : mount) * look));
        }

        /// <summary>The device's position (a tracker).</summary>
        public void SetPosition(Entity device, Vector3 position)
        {
            ref var s = ref SlotOf(device);
            s.Position = position;
            s.HasPosition = true;
        }

        /// <summary>Whether a view of this station shows the device (D-63).</summary>
        public void SetViewed(Entity device, bool viewed)
        {
            ref var s = ref SlotOf(device);
            s.Viewed = viewed;
            s.HasViewed = true;
        }

        // ── sending ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends each of <paramref name="devices"/> what this source changed (and the heal when due), from <paramref name="station"/>; a slot of a device no longer
        /// listed is freed. Nothing is published when the event is not registered (the staged presses are dropped).
        /// </summary>
        public void Flush(EntityRepository w, StationId station, ReadOnlySpan<Entity> devices)
        {
            bool registered = w.Bus.IsRegistered<DeviceInputEvent>();
            double now = Clock();
            Span<InputChange> changes = stackalloc InputChange[DevicePresses.Count * 2 + DeviceAxes.Count + DeviceSwitches.Count + 1];
            for (int si = 0; si < _slots.Length; si++)
            {
                ref var s = ref _slots[si];
                if (s.Device.IsNull) continue;
                if (devices.IndexOf(s.Device) < 0) { s = default; continue; }
                if (!registered) { s.Downs = default; continue; }
                uint epoch = EpochOf(w, s.Device);
                if (!s.Known || epoch != s.Epoch) { ResetSent(ref s); s.Known = true; s.Epoch = epoch; s.HealNow = true; }
                bool heal = s.HealNow || now - s.LastHeal >= HealSeconds;
                int n = 0;
                for (int b = 0; b < DevicePresses.Count; b++)
                {
                    ushort bit = (ushort)(1 << b);
                    int downs = s.Downs[b];
                    bool want = (s.HeldWant & bit) != 0, was = (s.SentHeld & bit) != 0;
                    if (want && !was && downs == 0) downs = 1;   // a held level going down is a press
                    if (downs > 0) changes[n++] = new InputChange { Kind = InputChangeKind.ButtonDown, Element = (byte)b, Count = (ushort)downs };
                    if (!want && (was || downs > 0)) changes[n++] = new InputChange { Kind = InputChangeKind.ButtonUp, Element = (byte)b };
                }
                for (int k = 0; k < DeviceSwitches.Count; k++)
                {
                    if ((s.SwitchesOwned & (1 << k)) == 0) continue;
                    if (heal || (s.SentSwitchesOwned & (1 << k)) == 0 || s.SentSwitches[k] != s.Switches[k])
                        changes[n++] = new InputChange { Kind = InputChangeKind.Switch, Element = (byte)k, Value = s.Switches[k] };
                }
                for (int k = 0; k < DeviceAxes.Count; k++)
                {
                    if ((s.AxesOwned & (1 << k)) == 0) continue;
                    if (heal || (s.SentAxesOwned & (1 << k)) == 0 || s.SentAxes[k] != s.Axes[k])
                        changes[n++] = new InputChange { Kind = InputChangeKind.Axis, Element = (byte)k, Value = s.Axes[k] };
                }
                if (heal && s.HeldOwned != 0) changes[n++] = new InputChange { Kind = InputChangeKind.HeldSnapshot, Count = s.HeldOwned, Value = s.HeldWant };
                byte pose = 0;
                if (s.HasLook && (heal || !s.SentHasLook || s.SentLook != s.Look)) pose |= DeviceInputEvent.OrientationPresent;
                if (s.HasPosition && (heal || !s.SentHasPosition || s.SentPosition != s.Position)) pose |= DeviceInputEvent.PositionPresent;
                if (s.HasViewed && (heal || !s.SentHasViewed || s.SentViewed != s.Viewed))
                    pose |= (byte)(DeviceInputEvent.ViewedPresent | (s.Viewed ? DeviceInputEvent.ViewedOn : 0));
                if (n > 0 || pose != 0)
                {
                    int at = 0;
                    do
                    {
                        var e = new DeviceInputEvent
                        {
                            Device = s.Device, Station = station, Source = Source, SourceSeq = System.Threading.Interlocked.Increment(ref s_seq),
                            PoseMask = at == 0 ? pose : (byte)0, Orientation = s.Look, Position = s.Position,
                        };
                        int take = Math.Min(InputChanges.Count, n - at);
                        for (int i = 0; i < take; i++) e.Changes[i] = changes[at + i];
                        e.Count = (byte)take;
                        at += take;
                        w.Bus.Publish(e);
                        Sent++;
                    } while (at < n);
                }
                if (heal) { s.LastHeal = now; s.HealNow = false; Heals++; }
                s.SentHeld = s.HeldWant;
                s.SentAxes = s.Axes; s.SentAxesOwned = s.AxesOwned;
                s.SentSwitches = s.Switches; s.SentSwitchesOwned = s.SwitchesOwned;
                s.SentLook = s.Look; s.SentHasLook = s.HasLook;
                s.SentPosition = s.Position; s.SentHasPosition = s.HasPosition;
                s.SentViewed = s.Viewed; s.SentHasViewed = s.HasViewed;
                s.Downs = default;
            }
        }

        private static void ResetSent(ref Slot s)
        {
            s.SentHeld = 0; s.SentAxesOwned = 0; s.SentSwitchesOwned = 0; s.SentHasLook = false; s.SentHasPosition = false; s.SentHasViewed = false;
        }

        private static uint EpochOf(EntityRepository w, Entity device)
            => w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>() && w.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(device)
                ? w.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(device).Epoch : 0u;

        private ref Slot SlotOf(Entity device)
        {
            int free = -1;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Device == device) return ref _slots[i];
                if (free < 0 && _slots[i].Device.IsNull) free = i;
            }
            if (free < 0) free = 0;
            _slots[free] = new Slot { Device = device };
            return ref _slots[free];
        }
    }
}
