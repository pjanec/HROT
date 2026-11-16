using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-5008 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-5, R-5041) — the group a placed ROW of obstacle entities belongs to: every member of one row carries the same <see cref="Id"/>; selecting one member selects them all (the editor's
    /// click), and the group moves / turns with the built group transform. ⛔ <c>MacroInstance</c> does not fit: its members are transient (re-expanded from a file at load), a drawn row's blocks are saved one by one. Unmanaged,
    /// authored, recorded and saved. 0 = no group.
    /// </summary>
    [ComponentId(GlobalComponentIds.PlacementGroup)]
    [DataPolicy(DataPolicy.Default)]
    public struct PlacementGroup
    {
        /// <summary>The group's id (non-zero; a row takes a fresh one, see <see cref="NewId"/>).</summary>
        public uint Id;

        private static long _last;

        /// <summary>A fresh non-zero group id for a row being placed now (a user action: the wall clock's milliseconds, never the simulation's; always above the last one handed out).</summary>
        public static uint NewId()
        {
            long now = System.DateTime.UtcNow.Ticks / 10_000 & 0x7FFFFFFF;
            while (true)
            {
                long last = System.Threading.Interlocked.Read(ref _last), next = System.Math.Max(now, last + 1);
                if (System.Threading.Interlocked.CompareExchange(ref _last, next, last) == last) return (uint)next;
            }
        }
    }
}
