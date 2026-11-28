using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
// the gizmo primitive's string (GizmoMap.Contracts), not Fdp.Core's
using HudText = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.AI.Behaviors.Devices
{
    /// <summary>
    /// ⭐ Simulated devices SD-S5b / SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119, mockups <c>devices-hud-link.svg</c> 25–26 and
    /// <c>devices-hud-designator.svg</c> 16) — the received-target cue's SENDER line, <c>from LASER RANGEFINDER (linked)</c>: the sender's TKB name, and
    /// "(linked)" when the RECEIVER's own link names the sender (a pairing — S5b AS BUILT ③). ONE helper for every receiving device HUD (the binoculars, the
    /// designator — ruling 9); each projector holds one instance. Built once per (type, linked) — it allocates then, never per steady frame.
    /// </summary>
    public sealed class HudCueSender
    {
        private long _type = long.MinValue;
        private bool _linked;
        private HudText _text;

        /// <summary>The sender line for a target <paramref name="receiver"/> received from <paramref name="from"/> (a device of TKB type <paramref name="fromType"/>).</summary>
        public HudText Of(EntityRepository w, Entity receiver, long fromType, Fdp.Toolkit.Replication.EntityRef from)
        {
            bool linked = DeviceMessages.IsLinked(w, receiver, from);
            if (_type == fromType && _linked == linked) return _text;
            string name = "DEVICE";
            if (w.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db
                && db.TryGetByType(fromType, out var t) && t != null && !string.IsNullOrEmpty(t.Name)) name = t.Name.ToUpperInvariant();
            _text = new HudText("from " + name + (linked ? " (linked)" : ""));
            _type = fromType; _linked = linked;
            return _text;
        }
    }
}
