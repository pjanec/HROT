using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Line-of-sight service interface. Phase 3 uses a stub (always blocked).
    /// Phase 5 will replace with raycast against the occluder grid.
    /// </summary>
    public interface ILosService
    {
        /// <summary>
        /// Returns true if there is a clear line of sight from <paramref name="observer"/>
        /// to <paramref name="target"/> (no occluders between them).
        /// Returns false if the line is blocked (occluded = cover is valid).
        /// </summary>
        bool HasCheapLineOfSight(Vector2 observer, Vector2 target);
    }

    /// <summary>
    /// Phase 3 stub: always reports LOS as blocked (cover always valid).
    /// </summary>
    public sealed class BlockedLosService : ILosService
    {
        public bool HasCheapLineOfSight(Vector2 observer, Vector2 target) => false;
    }
}
