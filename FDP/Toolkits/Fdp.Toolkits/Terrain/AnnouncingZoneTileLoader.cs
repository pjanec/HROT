using System.Numerics;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// The slice-1 <see cref="IZoneTileLoader"/> — a fake that <b>announces itself on every round</b>.
    ///
    /// <para><b>⭐ The announcement IS the deliverable, not a caveat.</b> 🔒 Tiles are ruled postponed
    /// ("tiles are implementation detail, used as example of possible implementation, not a concept to
    /// implement now, fake is ok"), and <c>R-133</c> says a capability reported present that silently
    /// no-ops is worse than an absent one. ⇒ this logs its stub-ness EVERY time it is asked to build,
    /// never once at startup and never behind a "first time only" flag — a one-shot notice is exactly the
    /// silence the ruling forbids.</para>
    ///
    /// <para>⛔ <b>And nothing advertises a terrain capability.</b> The capability manifest is measured
    /// from what is actually wired, and no terrain cell is declared at all — by ruling, the cleanest way
    /// to satisfy <c>R-133</c> is to declare none (design §8.3: "no capability is announced").</para>
    ///
    /// <para>⭐ It returns <c>true</c>. That is honest rather than optimistic: with static terrain the
    /// zone-load postcondition — "the terrain data covering this zone is resident" — <b>is genuinely
    /// met</b>, because all of it already is. Static is a trivially complete mode, not a degraded one.
    /// ⛔ Returning <c>false</c> would mark every zone <c>Failed</c> on every node and make the map lie
    /// in the opposite direction.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.6, §6, §8.3; §7 (what filling this in will touch).
    /// </summary>
    public sealed class AnnouncingZoneTileLoader : IZoneTileLoader
    {
        /// <summary>Counts the rounds, so the log line shows this is not a one-shot notice.</summary>
        private int _rounds;

        /// <inheritdoc/>
        public bool Build(Vector2 boundsMin, Vector2 boundsMax, ulong footprintHash)
        {
            _rounds++;

            // ⚠ FdpLog.Info takes at most 4 format arguments, so the coverage is pre-formatted.
            FdpLog<AnnouncingZoneTileLoader>.Info(
                "[TerrainTiles] STUB — no tiles were built (round {0}). Requested coverage {1} for "
              + "footprint {2:X16}. Terrain tiling is postponed by ruling; the zone-load postcondition "
              + "is satisfied immediately because the terrain is static. ⛔ Do not read a Loaded marker "
              + "as evidence that tiles exist.",
                _rounds,
                $"[{boundsMin.X},{boundsMin.Y}]..[{boundsMax.X},{boundsMax.Y}]",
                footprintHash);

            return true;
        }

        /// <summary>How many times this stub has been asked to build — asserted by the announcing rail.</summary>
        public int Rounds => _rounds;
    }
}
