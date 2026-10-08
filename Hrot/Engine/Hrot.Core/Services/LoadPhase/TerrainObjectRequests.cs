using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Terrain;
using Hrot.Core.Network;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐ Buildings Stage 5b — the creation requests for the entities a TERRAIN provides (today: its doors). 📄
/// docs/DESIGN_Building_Interiors.md §3b K1–K5, §3j.
/// <list type="bullet">
///   <item><description><b>K1</b> — the runtime id comes from the ONE allocator (the cluster's authority, the same instance the
///   scenario's own entities use), so it can never collide with a scenario id.</description></item>
///   <item><description><b>K2</b> — the door is identified by its <see cref="TerrainObjectKey"/> string, never a number.</description></item>
///   <item><description><b>K3</b> — created in KEY ORDER, right after the scenario's own requests, so the ids are deterministic for a
///   given scenario + terrain. A key that already has an entity is skipped (a re-commit never doubles a door).</description></item>
///   <item><description><b>K5</b> — <see cref="EntityCreationRequest.IsTransient"/>: every node that spawns it stamps
///   <c>ScenarioIgnoreTag</c>, so a door entity is never written into a scenario (the terrain recreates it).</description></item>
/// </list>
/// </summary>
public static class TerrainObjectRequests
{
    /// <summary>One request per door of <paramref name="world"/> that has no entity yet, in key order.</summary>
    public static List<EntityCreationRequest> ForDoors(TerrainWorld world, ISet<string> existingKeys, INetworkIdAllocator idAllocator)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (idAllocator == null) throw new ArgumentNullException(nameof(idAllocator));

        var doors = new List<TerrainDoorDef>(world.Doors);
        doors.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        var requests = new List<EntityCreationRequest>(doors.Count);
        foreach (var d in doors)
        {
            if (string.IsNullOrEmpty(d.Key) || existingKeys.Contains(d.Key)) continue;
            requests.Add(new EntityCreationRequest
            {
                RequestId             = Guid.NewGuid(),
                TkbType               = TkbEntityTypes.Door,
                PreAllocatedNetworkId = idAllocator.AllocateId(),
                IsTransient           = true,
                InitialComponents     = new List<object>
                {
                    // the state the terrain authored (5e will apply a scenario's saved state here)
                    new DoorState { State = d.Initial },
                    new TerrainObjectKey { Key = d.Key },
                    new SimTransform { Position = new Vector3(d.Center, d.SillZ), Rotation = Quaternion.Identity },
                },
            });
        }
        return requests;
    }
}
