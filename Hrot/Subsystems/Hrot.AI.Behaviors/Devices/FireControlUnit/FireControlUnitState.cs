using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Devices.FireControlUnit
{
    /// <summary>
    /// SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13b D-93) - the procedure phase of a fire-control unit, one colour each on the HUD (OFF grey - TEST amber -
    /// STANDBY blue - ARMED green - GUIDING red): <c>OFF ->(POWER) TEST ->(self-test over) STANDBY ->(ARM, a round in the weapon) ARMED ->(FIRE) GUIDING ->(the flight ends) ARMED,
    /// or STANDBY "NO MISSILE"</c>; POWER from any phase is OFF.
    /// </summary>
    public enum FireControlPhase : byte
    {
        Off = 0,
        Test = 1,
        Standby = 2,
        Armed = 3,
        Guiding = 4,
    }

    /// <summary>
    /// SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13b D-93 / D-112, sec 3.13g D-172) - the FIRE-CONTROL UNIT's state, a <c>[UnitMemory]</c> struct on the DEVICE:
    /// recorded, read by the HUD projector and by a scenario's <c>memory: FireControlUnitState</c> cue. Written ONLY by <see cref="FireControlUnitBehavior.Tick"/>.
    /// </summary>
    [UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public struct FireControlUnitState
    {
        public FireControlPhase Phase;
        /// <summary>1 when the unit may be fired now (the <c>FireControl.Ready</c> it publishes: armed, a round that can leave, deployed, a live frame).</summary>
        public byte Ready;
        /// <summary>1 while the operator asked for the round's view (<c>FireControl.ViewFromRound</c>).</summary>
        public byte ViewFromRound;
        /// <summary>1 while the round it launched flies (the flight's echo seen live) - the unit leaves GUIDING when it ends.</summary>
        public byte Flying;
        /// <summary>The rounds its weapon holds (the HUD's MISSILES n).</summary>
        public int Missiles;
        /// <summary>The weapon it is mounted in (this node's handle).</summary>
        public Entity Weapon;
        /// <summary>Sim seconds of the self-test left (frozen in pause: the unit ticks on SIM time).</summary>
        public float TestLeft;
        /// <summary>The launch request number it published (<c>FireControl.LaunchSeq</c>).</summary>
        public uint LaunchSeq;
        /// <summary>Sim time (s) of the FIRE that started the flight in progress - the HUD's "T+" and the grace for a shot the weapon never took.</summary>
        public double LaunchedAt;
        /// <summary>Sim time (s) of the last tick (a self-test counts SIM time, so a pause stands still).</summary>
        public double LastNow;
        /// <summary>The press counters last seen and the operator epoch they are based on (the ONE tracker every device keeps).</summary>
        public Fdp.Toolkit.Behavior.Devices.DevicePressTracker Presses;
    }

    /// <summary>
    /// The unit's press channels (the <c>DeviceInput</c> press counter each one reads) - the BUTTON indexes of TKB type 643's elements (D-142: POWER, ARM, FIRE, VIEW; pinned by
    /// <c>FireControlUnitTests</c>); the numbers are the wire, the names are the type's data.
    /// </summary>
    public static class FireControlUnitChannels
    {
        public const int Count = 4;
        public const int Power = 0, Arm = 1, Fire = 2, View = 3;
    }
}
