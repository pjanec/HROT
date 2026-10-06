using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>⭐ <c>CE-3072</c> B0 — which route a danger-area sensor watches. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.2.</summary>
    public enum DangerRouteSource : byte
    {
        /// <summary>The unit's own current move (its <c>NavigationStatus.RouteHandle</c>, B4′) — for a unit that only WATCHES.</summary>
        OwnMove = 0,
        /// <summary>A given, already planned route (<see cref="DangerAreaSettings.RouteHandle"/>).</summary>
        Handle = 1,
        /// <summary>
        /// From the unit's position at each refresh to <see cref="DangerAreaSettings.RoutePoint"/>. ⚠ The one to use when the
        /// behaviour MOVES the unit in reaction to the sensor: with <see cref="OwnMove"/> the reaction's own move (to a near
        /// handle) becomes the watched route and hides the area it reacts to (§10.4).
        /// </summary>
        ToPoint = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3072</c> B0 (R-213) — the per-kind SETTINGS of a <c>SensorModality.DangerArea</c> sensor: what a behaviour
    /// (<c>DangerAreaChildSensor.Ensure</c>), a TKB entry or a blueprint <c>SpawnSensor(DangerArea)</c> configures. The
    /// <c>SensorKindRegistry</c> names it as the kind's settings type. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.6.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DangerAreaSettings
    {
        /// <summary>Half the width of the corridor along the route that is searched (m).</summary>
        public float CorridorHalfWidth;
        /// <summary>How many areas to report, 1..8 (0 = 8).</summary>
        public byte MaxAreas;
        /// <summary>Which route is watched.</summary>
        public DangerRouteSource RouteSource;
        /// <summary>Seconds between answers (0 = the solver's default).</summary>
        public float RefreshSeconds;
        /// <summary>The route's end for <see cref="DangerRouteSource.ToPoint"/>.</summary>
        public Vector3 RoutePoint;
        /// <summary>The planned route for <see cref="DangerRouteSource.Handle"/>.</summary>
        public uint RouteHandle;

        /// <summary>The defaults: a 10 m corridor, 8 areas, an answer every 2 s, the unit's own move.</summary>
        public static DangerAreaSettings Default => new()
        {
            CorridorHalfWidth = 10f, MaxAreas = 8, RefreshSeconds = 2f, RouteSource = DangerRouteSource.OwnMove,
        };

        /// <summary>Watch the route from the unit to <paramref name="point"/> (see <see cref="DangerRouteSource.ToPoint"/>).</summary>
        public static DangerAreaSettings ToPoint(Vector3 point)
        {
            var s = Default;
            s.RouteSource = DangerRouteSource.ToPoint;
            s.RoutePoint = point;
            return s;
        }
    }
}
