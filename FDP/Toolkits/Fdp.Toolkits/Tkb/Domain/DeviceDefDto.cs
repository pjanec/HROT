using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1e-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1 R7-4 / D-26 / D-29, §3.2 D-45) — the TKB descriptor
    /// <c>Gen.Device</c>: a type whose entities are DEVICES — an item a trainee operates whose logic is a behaviour on its own brain (a laser
    /// rangefinder, binoculars). Creation (<c>DeviceTkbTranslator</c>) writes the ITEM facts (<c>Mountable</c>, kind
    /// <see cref="MountKinds.Device"/>) and the device marker (<c>DeviceInfo</c>: this type + <see cref="Behavior"/>); the type's
    /// <see cref="BehaviorProfileDto"/> carries a <c>BrainTier</c> and NO default behaviour (the device tick starts <see cref="Behavior"/> — a
    /// default would queue a second start), and <c>BehaviorTkbTranslator</c> stamps no tactical block for it (it is not a unit).
    /// <para>⭐ D-26: the MEANING of the generic wire is data here — <see cref="Channels"/> names each press counter (index = the channel), so an
    /// adapter (the keyboard, a replica, a scenario's <c>do: control</c>) presses a button by NAME; the numbers stay the wire.</para>
    /// </summary>
    [TkbDescriptor("Gen.Device")]
    public record DeviceDefDto
    {
        /// <summary>The behaviour the device runs (its logic) — a registered behaviour name the device tick starts and restarts.</summary>
        public string Behavior { get; init; } = "";

        /// <summary>The press channels by NAME, in channel order (index i = <c>DevicePresses[i]</c>): "RANGE", "MODE" … At most 16.</summary>
        public List<string> Channels { get; init; } = new();

        /// <summary>The message kinds the device sends and accepts (SD-S5b — <c>DeviceMessageEvent.Kind</c>); empty in SD-S1.</summary>
        public List<string> Messages { get; init; } = new();

        /// <summary>The optic's magnification steps (×), the default first in <see cref="DefaultStep"/>; empty = no optic. A view bound to the device
        /// shows <c>DeviceOptics.FovDegrees(step)</c>.</summary>
        public List<float> Magnifications { get; init; } = new();

        /// <summary>The step a fresh device starts at (index into <see cref="Magnifications"/>).</summary>
        public int DefaultStep { get; init; }

        /// <summary>D-61 — where a device STACKED on this one (mounted in its station) sits, metres in this device's frame (x forward, y left, z up).</summary>
        public float StackOffsetX { get; init; }
        /// <inheritdoc cref="StackOffsetX"/>
        public float StackOffsetY { get; init; }
        /// <inheritdoc cref="StackOffsetX"/>
        public float StackOffsetZ { get; init; }

        /// <summary>Where it can be mounted — a kit station by default (<see cref="MountKinds.Device"/>).</summary>
        public MountKinds MountKinds { get; init; } = MountKinds.Device;

        /// <summary>Who carries it — one man by default (a hand-held device).</summary>
        public WeaponPortability Portable { get; init; } = WeaponPortability.OneMan;

        /// <summary>The carrier's speed factor while he carries it (a hand-held device: none).</summary>
        public float CarrySpeedFactor { get; init; } = 1f;

        /// <summary>The channel named <paramref name="name"/> (case-insensitive), or −1.</summary>
        public int ChannelOf(string name)
        {
            for (int i = 0; i < Channels.Count; i++)
                if (string.Equals(Channels[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
