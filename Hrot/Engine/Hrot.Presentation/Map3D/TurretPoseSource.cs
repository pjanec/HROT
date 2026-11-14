using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ WL-1 (docs/DESIGN_Weapon_Layers.md W-9, DESIGN_Map_3D_Mode §3.11 M23) — what the 3-D map's articulated kits are posed by: the
/// <see cref="TurretPose"/> on turret 0's part child (<see cref="TurretParts"/>). Same convention on both sides — radians, traverse
/// counter-clockwise from the hull's forward, elevation up — so it passes straight through.
/// <para>The parent → child map is rebuilt only when the world changes shape (another world, a new version or entity count); the
/// pose itself is read live, so a turning turret draws every frame without a rebuild.</para>
/// </summary>
public sealed class TurretPoseSource
{
    private readonly Dictionary<Entity, Entity> _turretOf = new();
    private EntityRepository? _world;
    private uint _version;
    private int _entityCount = -1;

    /// <summary>The pose of <paramref name="entity"/>'s main turret; forward and level when it has none.</summary>
    public ArticulationPose PoseOf(EntityRepository world, Entity entity)
    {
        if (!world.IsComponentTypeRegistered<TurretPose>() || !world.IsComponentTypeRegistered<PartMetadata>())
            return ArticulationPose.Neutral;
        Refresh(world);
        if (!_turretOf.TryGetValue(entity, out var child) || !world.IsAlive(child) || !world.HasComponent<TurretPose>(child))
            return ArticulationPose.Neutral;
        ref readonly var pose = ref world.GetComponentRO<TurretPose>(child);
        return new ArticulationPose(pose.Azimuth, pose.Elevation, Published: true);   // ⭐ CE-1114 — azimuth AND elevation, as replicated
    }

    private void Refresh(EntityRepository world)
    {
        if (ReferenceEquals(world, _world) && world.GlobalVersion == _version && world.EntityCount == _entityCount) return;
        _world = world;
        _version = world.GlobalVersion;
        _entityCount = world.EntityCount;
        _turretOf.Clear();
        foreach (var e in world.Query().With<PartMetadata>().With<TurretPose>().Build())
        {
            ref readonly var meta = ref world.GetComponentRO<PartMetadata>(e);
            if (meta.InstanceId == TurretParts.FirstPartId) _turretOf[meta.ParentEntity] = e;
        }
    }
}
