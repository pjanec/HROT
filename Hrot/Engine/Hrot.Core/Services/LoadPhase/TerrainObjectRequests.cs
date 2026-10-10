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
///   <item><description><b>K5 / 5e</b> — a door starts in the state the scenario's <c>TerrainObjects</c> section saved for its key,
///   else as the terrain authored it.</description></item>
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
    /// <param name="saved">⭐ 5e — the scenario's <c>TerrainObjects</c> door states (key → state); a door it names starts in that
    /// state, every other door as the terrain authored it. A saved key this terrain does not define is reported and ignored.</param>
    public static List<EntityCreationRequest> ForDoors(TerrainWorld world, ISet<string> existingKeys, INetworkIdAllocator idAllocator,
        IReadOnlyDictionary<string, TerrainDoorState>? saved = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (idAllocator == null) throw new ArgumentNullException(nameof(idAllocator));
        if (saved != null)
            foreach (var key in saved.Keys)
                if (world.DoorIndexOf(key) < 0)
                    Fdp.Core.Logging.FdpLog<TerrainWorld>.Warn(
                        "[LoadPhase/TerrainObjects] the scenario saves door '{0}', which terrain '{1}' does not define — ignored.", key, world.Name ?? "(unnamed)");

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
                    // ⭐ 5e — the scenario's saved state, else the state the terrain authored
                    new DoorState { State = saved != null && saved.TryGetValue(d.Key, out var st) ? st : d.Initial },
                    new TerrainObjectKey { Key = d.Key },
                    new SimTransform { Position = new Vector3(d.Center, d.SillZ), Rotation = Quaternion.Identity },
                },
            });
        }
        return requests;
    }
}
