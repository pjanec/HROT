using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4045</c> H1a (docs/DESIGN_Scenario_Regression.md §10-1) — how a verify world names its units, for ANY world: the editor's
    /// (<see cref="EditorVerifyWorld"/>) and a recording's playback (<see cref="RecordedRunWorld"/>). ⛔ One implementation, so a live and a
    /// recorded snapshot name the same unit the same way by construction.
    /// </summary>
    internal static class VerifyWorldNames
    {
        /// <summary>Every unit with a name and a network id (<see cref="IVerifyWorld.Named"/>).</summary>
        public static IReadOnlyList<(long Id, string Name)> Named(EntityRepository? w)
        {
            var list = new List<(long, string)>();
            if (w is null || !w.IsComponentTypeRegistered<NetworkIdentity>()) return list;
            foreach (var e in w.Query().With<EntityInfo>().With<NetworkIdentity>().Build())
            {
                var name = w.GetComponentRO<EntityInfo>(e).Name.ToString();
                if (name.Length > 0) list.Add((w.GetComponentRO<NetworkIdentity>(e).Value, name));
            }
            return list;
        }

        /// <summary>The name of the unit at entity index <paramref name="index"/> (<see cref="IVerifyWorld.NameOfEntityIndex"/>).</summary>
        public static string? NameOfEntityIndex(EntityRepository? w, int index)
        {
            if (w is null || index < 0 || index > w.MaxEntityIndex) return null;
            var e = w.GetEntityByIndex(index);
            if (!w.IsAlive(e) || !w.HasComponent<EntityInfo>(e)) return null;
            var name = w.GetComponentRO<EntityInfo>(e).Name.ToString();
            return name.Length > 0 ? name : null;
        }
    }
}
