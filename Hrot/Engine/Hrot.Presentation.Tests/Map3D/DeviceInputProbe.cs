using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;

namespace Hrot.Presentation.Tests.Map3D
{
    /// <summary>
    /// ⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-138) — a rail's view of what a device would MERGE from a station's sparse input
    /// events: reads the frame's <see cref="DeviceInputEvent"/>s off the bus and folds them through the device's own merge (<see cref="DeviceInputMerge"/>),
    /// so a rail asserts the device's input (its counters, held bits, axes, look, Viewed) — never the encoding of one event.
    /// </summary>
    internal sealed class DeviceInputProbe
    {
        private readonly DeviceInputMerge _merge = new() { Clock = () => 0d };
        private readonly Dictionary<Entity, DeviceInput> _state = new();

        /// <summary>This frame's events (after the bus swap), merged.</summary>
        public DeviceInputEvent[] Read(EntityRepository w)
        {
            var events = w.Bus.Read<DeviceInputEvent>().ToArray();
            foreach (var e in events)
            {
                _state.TryGetValue(e.Device, out var s);
                _merge.Apply(in e, ref s);
                _state[e.Device] = s;
            }
            return events;
        }

        /// <summary>The device's merged input so far.</summary>
        public DeviceControl Of(Entity device) => _state.TryGetValue(device, out var s) ? s.Control : default;
    }
}
