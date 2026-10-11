using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ WL-1 (docs/DESIGN_Weapon_Layers.md W-9, M23) — <b>the ONE builder and finder of a turret part child.</b> Every node that
    /// spawns the unit builds the same children from the TKB (<c>TurretTkbTranslator</c>), so the part id —
    /// <see cref="FirstPartId"/> + the turret index — is the same everywhere and keys the replicated pose with no config on the wire
    /// (the sensor-child pattern, <c>SensorChildFactory</c>).
    /// <para>Part id ranges on one parent: weapon mounts <c>1 … N</c>, TKB sensors <c>1000 +</c>, turrets <c>2000 +</c>.</para>
    /// </summary>
    public static class TurretParts
    {
        /// <summary>Part ids at and above this are turrets (<c>2000 + turret index</c>).</summary>
        public const int FirstPartId = 2000;

        /// <summary>The highest turret index a part id can carry — a platform with more turrets than this is malformed.</summary>
        public const int MaxTurrets = 16;

        /// <summary>True when this node registers the turret part's component types.</summary>
        public static bool HostsTurrets(EntityRepository repo)
            => repo.IsComponentTypeRegistered<PartMetadata>() && repo.IsComponentTypeRegistered<TurretPose>();

        /// <summary>The turret index a part id stands for, or −1 when it is not a turret part id.</summary>
        public static int IndexOf(int partId)
            => partId >= FirstPartId && partId < FirstPartId + MaxTurrets ? partId - FirstPartId : -1;

        /// <summary>
        /// Builds turret <paramref name="index"/> of <paramref name="parent"/> unless it exists (a translator can run twice for one
        /// entity — spawn and ghost promotion). The pose starts forward and level. Returns the child, or <see cref="Entity.Null"/>
        /// when this node does not host turrets or the index is out of range.
        /// </summary>
        public static Entity Ensure(EntityRepository repo, Entity parent, int index)
        {
            if (!HostsTurrets(repo) || index < 0 || index >= MaxTurrets) return Entity.Null;
            var existing = Find(repo, parent, index);
            if (!existing.IsNull) return existing;

            var child = repo.CreateEntity();
            repo.AddComponent(child, new PartMetadata { ParentEntity = parent, InstanceId = FirstPartId + index });
            repo.AddComponent(child, default(TurretPose));
            DerivedParts.MarkNotSaved(repo, child);   // CE-3045 — rebuilt from the TKB on load
            return child;
        }

        /// <summary>The turret part <paramref name="index"/> of <paramref name="parent"/>, or <see cref="Entity.Null"/>.</summary>
        public static Entity Find(ISimulationView view, Entity parent, int index)
        {
            int partId = FirstPartId + index;
            foreach (var e in view.Query().With<PartMetadata>().With<TurretPose>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(e);
                if (meta.InstanceId == partId && meta.ParentEntity.Equals(parent)) return e;
            }
            return Entity.Null;
        }

        /// <summary>The pose of turret <paramref name="index"/> of <paramref name="parent"/>; forward and level when it has none.</summary>
        public static TurretPose PoseOf(ISimulationView view, Entity parent, int index)
        {
            var child = Find(view, parent, index);
            return child.IsNull ? default : view.GetComponentRO<TurretPose>(child);
        }
    }
}
