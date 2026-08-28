using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Core
{
    /// <summary>
    /// Per-vehicle configuration (flyweight pattern).
    /// Referenced by index from VehicleState.
    /// Stored in global NativeArray<VehicleParams> table.
    /// </summary>
    /// <remarks>
    /// <b>Data policy:</b> marked <see cref="DataPolicy.NoSave"/> -- this component is
    /// entirely <i>derived</i>, built by <c>VehicleKinematicsTkbTranslator</c> from the
    /// TKB's <c>Gen.VehicleParameters</c> descriptor at entity creation.  The TKB is the
    /// single source of vehicle parameters and is present on every node offline, so
    /// persisting a copy into a scenario adds no information and creates a second source
    /// that silently goes stale when the TKB is edited.
    /// <para>
    /// Saved copies are also not overrides: nothing on the load path treats a scenario's
    /// <c>VehicleParams</c> as authoritative over the TKB, so a stale copy round-trips
    /// through save/load looking meaningful while changing nothing.
    /// </para>
    /// <para>
    /// Same reasoning as <c>UnitRoster</c>, which is <c>NoSave</c> because it is derived
    /// from <c>UnitSubordinate</c> records.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.VehicleParams)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct VehicleParams
    {
        public VehicleClass Class;   // Vehicle classification
        public float Length;         // Vehicle length (meters)
        public float Width;          // Vehicle width (meters)
        public float WheelBase;      // Distance between axles (meters)
        
        public float MaxSpeedFwd;    // Max forward speed (m/s)
        public float MaxSpeedRev;    // Reserved for future (currently unused)
        
        public float MaxAccel;       // Max acceleration (m/s²)
        public float MaxDecel;       // Max braking deceleration (m/s²)
        
        public float MaxSteerAngle;  // Max steering angle (radians)
        public float MaxSteerRate;   // Max steering rate (rad/s)
        
        public float MaxLatAccel;    // Max lateral acceleration for curvature limits
        public float AvoidanceRadius; // Collision radius for RVO (meters)
        
        // Control tuning
        public float LookaheadTimeMin; // Pure Pursuit lookahead min (seconds)
        public float LookaheadTimeMax; // Pure Pursuit lookahead max (seconds)
        public float AccelGain;        // Speed controller proportional gain
    }
}
