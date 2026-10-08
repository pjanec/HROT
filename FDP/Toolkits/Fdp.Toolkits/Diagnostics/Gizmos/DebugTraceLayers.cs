namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-3117</c> — the map layer bits of the debug-trace gizmos, toggled by <c>LayerControlDto</c> (bits 0–3 are Entities,
    /// Perception, AiHelpers, FireTraces). 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// </summary>
    public static class DebugTraceLayers
    {
        public const byte FireTraces = 3;
        public const byte Doors      = 4;
        public const byte Paths      = 5;
        public const byte Blast      = 6;
        public const byte Hearing    = 7;
    }
}

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>⭐ <c>CE-3117</c> — the sim time a debug trace is aged against (R-143: sim time only). Restored with the world on a
    /// replay seek, so a trace's age is the same live and replayed.</summary>
    public static class DebugTraceClock
    {
        public static double Now(Fdp.Core.EntityRepository repo) =>
            repo.HasSingleton<Fdp.Core.GlobalTime>() ? repo.GetSingleton<Fdp.Core.GlobalTime>().TotalTime : 0d;

        /// <summary>1 at age 0, falling to 0 at <paramref name="shown"/>; negative when the trace is not shown.</summary>
        public static float Fade(double age, double shown) => age < 0d || age > shown ? -1f : (float)(1d - age / shown);
    }
}
