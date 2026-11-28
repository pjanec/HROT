using System;
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
    /// <para>⭐ D-26 / ⭐⭐ SD-S5v D-142 (§3.13e): the MEANING of the generic wire is data here — <see cref="Elements"/> is the type's DESCRIPTOR (a HID
    /// descriptor, a MIDI map): each input ELEMENT by name, kind (<see cref="DeviceElementKind"/>), positions / range, hotkey and wheel role. An element
    /// crosses the wire as (KIND, INDEX within its kind) — never its name; every adapter (the keyboard, the HUD's clickable chips, a replica, a scenario's
    /// <c>do: control</c>) names it and the one encoder (<c>DeviceInputEncoder</c>) looks the index up here. ⛔ SUPERSEDES the bare <c>Channels</c> list.</para>
    /// </summary>
    [TkbDescriptor("Gen.Device")]
    public record DeviceDefDto
    {
        /// <summary>The behaviour the device runs (its logic) — a registered behaviour name the device tick starts and restarts.</summary>
        public string Behavior { get; init; } = "";

        /// <summary>⭐⭐ SD-S5v (D-142) — the input elements, in descriptor order: a <see cref="DeviceElementKind.Button"/>'s index among the buttons is its
        /// press counter (<c>DevicePresses[i]</c>, at most 16), a switch's among the switches its position slot (at most 8), an axis's among the axes its
        /// value (at most 4). A type that changes its elements re-indexes (R-4301).</summary>
        public List<DeviceElementDto> Elements { get; init; } = new();

        /// <summary>The message kinds the device sends and accepts (SD-S5b — <c>DeviceMessageEvent.Kind</c>); empty in SD-S1.</summary>
        public List<string> Messages { get; init; } = new();

        /// <summary>The optic's magnification steps (×), the default first in <see cref="DefaultStep"/>; empty = no optic. A view bound to the device
        /// shows <c>DeviceOptics.FovDegrees(step)</c>.</summary>
        public List<float> Magnifications { get; init; } = new();

        /// <summary>The step a fresh device starts at (index into <see cref="Magnifications"/>).</summary>
        public int DefaultStep { get; init; }

        // ⭐ SD-S5b (D-120 / D-61) — ⛔ StackOffsetX/Y/Z RETIRED (R-4301; no reader): where a device STACKED on this one sits is the place of the type's STACKING
        //   STATION — a Gen.Carrier seat (Role Station, Accepts Device, X/Y/Z), the kit stations' one mechanism (boarding, the carry, the operator walk), read by
        //   DevicePoses through StationSeat.Place. Two fields for one place would drift (ruling 9).

        /// <summary>⭐ D-171 — where the device's OPTIC (its lens, its laser's exit) sits above its own base, metres in the device's frame (x forward, y left,
        /// z up): a tripod rangefinder ~1.3 m above its feet, a console's sight on top of the console. 0 = at its base (a hand-held device — carried, it is used
        /// at the man's eye, D-170). Copied once into <c>DeviceInfo.OpticOffset</c> at creation.</summary>
        public float OpticOffsetX { get; init; }
        /// <inheritdoc cref="OpticOffsetX"/>
        public float OpticOffsetY { get; init; }
        /// <inheritdoc cref="OpticOffsetX"/>
        public float OpticOffsetZ { get; init; }

        /// <summary>⭐ SD-S2c (§3.13c D-104) — a GROUND STATION: its command (<c>RemoteControl</c>) is created WITH the device, so the paired drone's brain finds
        /// it in the tick-start view from its first tick (a cross-entity read cannot see a component added in the same frame).</summary>
        public bool RemoteStation { get; init; }

        /// <summary>Where it can be mounted — a kit station by default (<see cref="MountKinds.Device"/>).</summary>
        public MountKinds MountKinds { get; init; } = MountKinds.Device;

        /// <summary>Who carries it — one man by default (a hand-held device).</summary>
        public WeaponPortability Portable { get; init; } = WeaponPortability.OneMan;

        /// <summary>The carrier's speed factor while he carries it (a hand-held device: none).</summary>
        public float CarrySpeedFactor { get; init; } = 1f;

        /// <summary>⭐ D-142 — the element named <paramref name="name"/> (case-insensitive): its kind and its index WITHIN that kind; false when none.</summary>
        public bool TryElement(string name, out DeviceElementKind kind, out int index)
        {
            Span<int> counts = stackalloc int[3];
            for (int i = 0; i < Elements.Count; i++)
            {
                var e = Elements[i];
                int k = (int)e.Kind;
                if (string.Equals(e.Name, name, System.StringComparison.OrdinalIgnoreCase)) { kind = e.Kind; index = counts[k]; return true; }
                counts[k]++;
            }
            kind = default; index = -1;
            return false;
        }

        /// <summary>The BUTTON named <paramref name="name"/> (case-insensitive) — its press counter — or −1 (no such element, or not a button).</summary>
        public int ButtonOf(string name) => TryElement(name, out var k, out int i) && k == DeviceElementKind.Button ? i : -1;

        /// <summary>The element of <paramref name="kind"/> at <paramref name="index"/> within its kind, or null.</summary>
        public DeviceElementDto? ElementAt(DeviceElementKind kind, int index)
        {
            for (int i = 0, n = 0; i < Elements.Count; i++)
                if (Elements[i].Kind == kind && n++ == index) return Elements[i];
            return null;
        }

        /// <summary>How many elements of <paramref name="kind"/> the type declares.</summary>
        public int CountOf(DeviceElementKind kind)
        {
            int n = 0;
            for (int i = 0; i < Elements.Count; i++) if (Elements[i].Kind == kind) n++;
            return n;
        }

        /// <summary>The element names, in descriptor order (a refusal's text — allocates).</summary>
        public string Names() => string.Join(", ", Elements.ConvertAll(e => e.Name));
    }

    /// <summary>⭐ SD-S5v (D-142 / D-143) — an input element's kind: a momentary BUTTON (presses + a held level), a latching SWITCH (a position — a
    /// selector, a toggle; D-135), an AXIS (an absolute value in its range).</summary>
    public enum DeviceElementKind : byte { Button = 0, Switch = 1, Axis = 2 }

    /// <summary>⭐ SD-S5v (D-124) — what the mouse wheel drives on the virtual panel: a wheel notch up / down presses the button that names it.</summary>
    public enum DeviceWheelRole : byte { None = 0, Up = 1, Down = 2 }

    /// <summary>
    /// ⭐⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-142) — one input element of a device type's descriptor (<see cref="DeviceDefDto.Elements"/>).
    /// </summary>
    public record DeviceElementDto
    {
        /// <summary>The element's name — what the HUD chip shows, the replica's pin map and a scenario's <c>press</c> name.</summary>
        public string Name { get; init; } = "";
        public DeviceElementKind Kind { get; init; }
        /// <summary>A switch's positions (2 = OFF / ON).</summary>
        public int Positions { get; init; } = 2;
        /// <summary>An axis's range.</summary>
        public float Min { get; init; } = -1f;
        /// <inheritdoc cref="Min"/>
        public float Max { get; init; } = 1f;
        /// <summary>⭐ D-124 — the key of the virtual panel (a letter, <c>Enter</c>, …; a <c>NodeEditor.Primitives.EditorKey</c> name); empty = none.</summary>
        public string Hotkey { get; init; } = "";
        /// <summary>⭐ D-124 — the wheel notch that presses this button.</summary>
        public DeviceWheelRole Wheel { get; init; }

        public static DeviceElementDto Button(string name, string hotkey = "", DeviceWheelRole wheel = DeviceWheelRole.None)
            => new() { Name = name, Kind = DeviceElementKind.Button, Hotkey = hotkey, Wheel = wheel };
        public static DeviceElementDto Switch(string name, int positions = 2, string hotkey = "")
            => new() { Name = name, Kind = DeviceElementKind.Switch, Positions = positions, Hotkey = hotkey };
        public static DeviceElementDto Axis(string name, float min = -1f, float max = 1f)
            => new() { Name = name, Kind = DeviceElementKind.Axis, Min = min, Max = max };
    }
}
