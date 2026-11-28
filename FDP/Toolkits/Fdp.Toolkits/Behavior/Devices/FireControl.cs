using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// Simulated devices SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13 D-76 / D-87, sec 3.13b D-92 - D-96, grown by sec 3.13f D-151 / D-157 and sec 3.13g D-173 - D-181) -
    /// WHAT A FIRE-CONTROL DEVICE ASKS OF ITS WEAPON, written by the device's own logic ON ITSELF (live frames only for everything that acts on the world, D-83) and READ by
    /// the weapon's one controller (<c>OperatorControlBehavior</c>, a declared tick-start read - D-94) and by the muscle (the launch, D-95, and the guided round's steering, D-96).
    /// The device never writes the weapon, never holds a channel (D-76); a launch is STATE, not an event (D-77): <see cref="LaunchSeq"/> moves, the controller fires when it
    /// sees it move past the last it saw and <see cref="Ready"/> holds.
    /// <para>The layout is declared whole in SD-S4 so it is set once (R-4301, the S4c / S4d fields are written by their slices): S4 writes <see cref="Ready"/>,
    /// <see cref="LaunchSeq"/>, <see cref="GuideOrigin"/> / <see cref="GuideDir"/>, <see cref="Steer0"/> / <see cref="Steer1"/> (S4b) and <see cref="ViewFromRound"/>;
    /// S4c <see cref="Selected"/>, <see cref="Trigger"/>, <see cref="LayDir"/>, <see cref="Zoom"/>, <see cref="FireMode"/>; S4d <see cref="Aim"/>, <see cref="Guide"/>,
    /// <see cref="Target"/>, <see cref="TargetSeq"/>, <see cref="LockState"/>, <see cref="RangeM"/>, <see cref="AimPoint"/>.</para>
    /// Unmanaged, recorded; created WITH the device (<c>DeviceTkbTranslator</c>: a cross-entity read cannot see a component another phase added this frame); not authored.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FireControl)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct FireControl
    {
        /// <summary>1 while the unit may be fired: armed, a round that can leave, the weapon deployed, a live (not paused) frame (D-93).</summary>
        public byte Ready;
        /// <summary>D-87 - 1 while the operator asked for the round's view (toggled by VIEW; works in pause, D-91).</summary>
        public byte ViewFromRound;
        /// <summary>S4c (D-151) - the weapon index in <c>ControlledEntities</c> (0 - 3).</summary>
        public byte Selected;
        /// <summary>S4c (D-151) - 1 while FIRE is held (an automatic weapon).</summary>
        public byte Trigger;
        /// <summary>S4c (D-157) - 0 SINGLE, 1 BURST, 2 AUTO.</summary>
        public byte FireMode;
        /// <summary>S4d (D-173) - the active aim method: 0 LAY, 1 LINE, 2 STICK, 3 LOCK.</summary>
        public byte Aim;
        /// <summary>S4d (D-176) - the guidance the operator asks of a round in flight: 0 SEEKER, 1 STICK, 2 LINE.</summary>
        public byte Guide;
        /// <summary>S4d (D-174) - 0 SEARCH, 1 TRACKING, 2 LOST.</summary>
        public byte LockState;
        /// <summary>The launch request number: bumped per accepted FIRE (D-77).</summary>
        public uint LaunchSeq;
        /// <summary>S4d (D-176) - bumped per new lock, so a round in flight tells a retarget from the standing target.</summary>
        public uint TargetSeq;
        /// <summary>S4d - the locked target (network id).</summary>
        public EntityRef Target;
        /// <summary>D-96 - the device's sight line, world: where it looks from and along (a unit vector). The command round flies it (SACLOS by hand).</summary>
        public Vector3 GuideOrigin, GuideDir;
        /// <summary>S4c (D-151) / S4d (D-181) - the commanded lay direction and the aimed point (the HUD's lead).</summary>
        public Vector3 LayDir, AimPoint;
        /// <summary>D-81 - the stick, two axes -1 ... 1 (a MCLOS round turns by it).</summary>
        public float Steer0, Steer1;
        /// <summary>S4c (D-151) - the sight's zoom step; S4d (D-181) - the held range (m, 0 = measure).</summary>
        public float Zoom, RangeM;
    }

    /// <summary>SD-S4 (D-177 as built for the command round) - how the device's round flies now (the operator's one word is NO LOCK).</summary>
    public enum GuidedFlightState : byte
    {
        None = 0,
        /// <summary>The round's own seeker flies it.</summary>
        Seeker = 1,
        /// <summary>The operator's stick turns it (MCLOS, S4b).</summary>
        Stick = 2,
        /// <summary>It flies the device's sight line (SACLOS).</summary>
        Line = 3,
        /// <summary>No longer guided onto anything (D-177).</summary>
        NoLock = 4,
        Ended = 5,
    }

    /// <summary>
    /// SD-S4 (sec 3.13b D-88 / D-95) - THE FLIGHT OF THE DEVICE'S ROUND, written ON THE DEVICE by the MUSCLE only (the same shape as <c>LaserSpot</c>): <see cref="LaunchSeq"/> echoes
    /// the <see cref="FireControl.LaunchSeq"/> the round was launched for (what makes a launch "the device's" - an AI shot from the same weapon moves no seq), <see cref="Round"/>
    /// is the round, <see cref="Live"/> goes to 0 when the round is gone, <see cref="State"/> is how it flies. Created with the device (a read of it across entities needs it from
    /// the tick start). NOTE: AS BUILT: <see cref="Round"/> is this node's handle, as <see cref="DeviceView.Entity"/> - SD-C re-keys both by network id. Unmanaged, recorded; not authored.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GuidedFlight)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct GuidedFlight
    {
        public uint LaunchSeq;
        public Entity Round;
        public byte Live;
        public GuidedFlightState State;
    }

    /// <summary>
    /// SD-S4 (sec 3.13b D-85 / D-95 / D-96, sec 3.13f D-148) - THE ONE RESOLVER of a weapon's fire control and the one launch rule: the controller reads <see cref="Of"/> to fire
    /// (D-94) and the muscle reads it at launch to know which device guides the round (D-95) - two readers, one walk.
    /// </summary>
    public static class FireControls
    {
        /// <summary>The fire-control device of <paramref name="weapon"/>: a device in a station of it (its <see cref="PassengerBuffer"/> seats), else the device commanding it
        /// (<see cref="ControlledBy"/>, D-148 - one that carries a <see cref="FireControl"/>); null when none. 0 B.</summary>
        public static Entity Of(EntityRepository w, Entity weapon)
        {
            if (weapon.IsNull || !w.IsAlive(weapon) || !w.IsComponentTypeRegistered<FireControl>()) return Entity.Null;
            if (w.IsComponentTypeRegistered<PassengerBuffer>() && w.HasComponent<PassengerBuffer>(weapon))
            {
                ref readonly var seats = ref w.GetComponentRO<PassengerBuffer>(weapon);
                for (int i = 0; i < PassengerBuffer.Capacity; i++)
                {
                    var item = seats.Passengers[i];
                    if (!item.IsNull && w.IsAlive(item) && w.HasComponent<FireControl>(item)) return item;
                }
            }
            var commander = ControlledBy.Of(w, weapon);
            return !commander.IsNull && w.IsAlive(commander) && w.HasComponent<FireControl>(commander) ? commander : Entity.Null;
        }

        /// <summary>The device's sight line (D-96): false when it has none (no fire control, or no direction written yet).</summary>
        public static bool GuideLine(EntityRepository w, Entity device, out Vector3 origin, out Vector3 dir)
        {
            origin = default; dir = default;
            if (device.IsNull || !w.IsAlive(device) || !w.IsComponentTypeRegistered<FireControl>() || !w.HasComponent<FireControl>(device)) return false;
            ref readonly var fc = ref w.GetComponentRO<FireControl>(device);
            if (fc.GuideDir.LengthSquared() < 0.5f) return false;
            origin = fc.GuideOrigin; dir = fc.GuideDir;
            return true;
        }

        /// <summary>The device's <see cref="FireControl.Steer0"/> / <see cref="FireControl.Steer1"/> (D-81), or zeros.</summary>
        public static bool TrySteer(EntityRepository w, Entity device, out float steer0, out float steer1)
        {
            steer0 = steer1 = 0f;
            if (device.IsNull || !w.IsAlive(device) || !w.IsComponentTypeRegistered<FireControl>() || !w.HasComponent<FireControl>(device)) return false;
            ref readonly var fc = ref w.GetComponentRO<FireControl>(device);
            steer0 = fc.Steer0; steer1 = fc.Steer1;
            return true;
        }

        /// <summary>
        /// D-95 - the launch is the device's BY SEQ ECHO: when the weapon's fire-control device has a <see cref="FireControl.LaunchSeq"/> past its own
        /// <see cref="GuidedFlight.LaunchSeq"/>, the round just born is the device's - the flight is written on the device (the echo, the round, live, how it flies) and the
        /// device is returned (the round's <c>GuidedRound.Device</c>). Otherwise (an AI shot, a man's own trigger) null: the round flies today's line. The muscle's, serial.
        /// </summary>
        public static Entity ClaimLaunch(EntityRepository w, Entity weapon, Entity round, GuidanceKind guidance)
        {
            var device = Of(w, weapon);
            if (device.IsNull || !w.IsComponentTypeRegistered<GuidedFlight>() || !w.HasComponent<GuidedFlight>(device)) return Entity.Null;
            uint seq = w.GetComponentRO<FireControl>(device).LaunchSeq;
            ref var flight = ref w.GetComponentRW<GuidedFlight>(device);
            if ((int)(seq - flight.LaunchSeq) <= 0) return Entity.Null;
            flight = new GuidedFlight
            {
                LaunchSeq = seq, Round = round, Live = 1,
                State = guidance switch
                {
                    GuidanceKind.Command => GuidedFlightState.Line,
                    GuidanceKind.Unguided => GuidedFlightState.None,
                    _ => GuidedFlightState.Seeker,
                },
            };
            return device;
        }

        /// <summary>The muscle's one writer of <see cref="GuidedFlight.State"/> on a live flight (a change only).</summary>
        public static void MarkFlight(EntityRepository w, Entity device, GuidedFlightState state)
        {
            if (device.IsNull || !w.IsAlive(device) || !w.IsComponentTypeRegistered<GuidedFlight>() || !w.HasComponent<GuidedFlight>(device)) return;
            if (w.GetComponentRO<GuidedFlight>(device).State == state) return;
            w.GetComponentRW<GuidedFlight>(device).State = state;
        }

        /// <summary>Writes the device's own <see cref="FireControl"/> on change (the parallel rule: self only; added WITH its value when it was never created).</summary>
        public static void Write(EntityRepository w, Entity self, in FireControl next)
        {
            if (!w.IsComponentTypeRegistered<FireControl>()) return;
            if (w.HasComponent<FireControl>(self))
            {
                ref readonly var now = ref w.GetComponentRO<FireControl>(self);
                if (Same(in now, in next)) return;
                w.SetComponent(self, next);
            }
            else w.AddComponent(self, next);
        }

        private static bool Same(in FireControl a, in FireControl b)
            => a.Ready == b.Ready && a.ViewFromRound == b.ViewFromRound && a.Selected == b.Selected && a.Trigger == b.Trigger && a.FireMode == b.FireMode
               && a.Aim == b.Aim && a.Guide == b.Guide && a.LockState == b.LockState && a.LaunchSeq == b.LaunchSeq && a.TargetSeq == b.TargetSeq
               && a.Target == b.Target && a.GuideOrigin == b.GuideOrigin && a.GuideDir == b.GuideDir && a.LayDir == b.LayDir && a.AimPoint == b.AimPoint
               && a.Steer0 == b.Steer0 && a.Steer1 == b.Steer1 && a.Zoom == b.Zoom && a.RangeM == b.RangeM;
    }
}
