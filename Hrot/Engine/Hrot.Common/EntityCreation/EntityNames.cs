using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// ⭐⭐ <c>CE-4312</c> — scenario lifecycle LC-2 (📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md §2.2, R-4166): a placed unit is NUMBERED —
    /// <c>"&lt;its type's name&gt; &lt;n&gt;"</c>, <c>n</c> the lowest number no entity in the scenario uses for that name (<i>Rifleman 1</i>,
    /// <i>Rifleman 2</i> …; a gap is reused) — so every new unit is told apart in the ORBAT, the forms and the rules. ⛔ It placed every unit under
    /// the bare type name. Names handed out but not yet in the world (several placed in one frame) count as used (<paramref name="pending"/>).
    /// A user action, never per frame: it reads every name once.
    /// </summary>
    public static class EntityNames
    {
        /// <summary>The next free numbered name for <paramref name="baseName"/> in <paramref name="world"/>.</summary>
        public static string Next(EntityRepository? world, string baseName, ICollection<string>? pending = null)
        {
            baseName = string.IsNullOrWhiteSpace(baseName) ? "Unit" : baseName.Trim();
            var used = new HashSet<int>();
            if (world is not null && world.IsComponentTypeRegistered<EntityInfo>())
                foreach (var e in world.Query().With<EntityInfo>().Build())
                    if (NumberOf(world.GetComponentRO<EntityInfo>(e).Name.ToString(), baseName) is int n) used.Add(n);
            if (pending is not null)
                foreach (var p in pending)
                    if (NumberOf(p, baseName) is int n) used.Add(n);
            int next = 1;
            while (used.Contains(next)) next++;
            return baseName + " " + next.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary><c>n</c> when <paramref name="name"/> is <c>"&lt;baseName&gt; &lt;n&gt;"</c> (n ≥ 1), else null.</summary>
        public static int? NumberOf(string? name, string baseName)
        {
            if (name is null || name.Length <= baseName.Length + 1 || !name.StartsWith(baseName, StringComparison.Ordinal) || name[baseName.Length] != ' ')
                return null;
            return int.TryParse(name.AsSpan(baseName.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 ? n : null;
        }
    }
}
