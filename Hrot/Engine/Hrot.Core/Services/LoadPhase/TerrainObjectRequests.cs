using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Terrain;
using Hrot.Core.Network;
using Fdp.Toolkit.NetworkSpawning.Events;

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
    /// <param name="idAllocator">The load's id allocator; ⭐ CE-5028 — null for a door a live commit adds (<see cref="TerrainObjectReconcile"/>): the creation system allocates.</param>
    public static List<EntityCreationRequest> ForDoors(TerrainWorld world, ISet<string> existingKeys, INetworkIdAllocator? idAllocator,
        IReadOnlyDictionary<string, TerrainDoorState>? saved = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
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
                TkbType               = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Door),
                PreAllocatedNetworkId = idAllocator?.AllocateId() ?? 0,
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

    /// <summary>
    /// ⭐ <c>CE-4391</c> S-4 (UTM §8.2 B-7, Q-D3) — one request per saved building that has damage and no entity yet, in key order: a runtime <see cref="BuildingDamage"/> entity
    /// carrying the saved breaches and collapsed state (a building with more than 16 breaches gets several). A saved building or panel this terrain does not have is warned and ignored.
    /// <para>⚠ R-4196 — who OWNS it: the request is untargeted (<c>OwnerAppInstanceId</c> = 0), so the DEFAULT PROCESSOR makes it — the node that loads the scenario (the Brain / the editor), the
    /// same as a door. In a cluster that is NOT a muscle, and <c>dtBuildingDamage</c> has no ownership group (nothing hands it on). That does not break the decide path: <c>BuildingDamageSystem</c>
    /// writes only entities its node owns, so the muscle's first new breach of that building opens ITS OWN entity beside it (B-2) and every node reads the union; the loaded damage stays the loader's.</para>
    /// </summary>
    public static List<EntityCreationRequest> ForBuildingDamage(TerrainWorld world, IReadOnlyDictionary<string, SavedBuildingDamage>? saved, ISet<string> existingKeys,
        INetworkIdAllocator idAllocator, Action<string>? warn = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (idAllocator == null) throw new ArgumentNullException(nameof(idAllocator));
        var requests = new List<EntityCreationRequest>();
        if (saved == null || saved.Count == 0) return requests;
        warn ??= m => Fdp.Core.Logging.FdpLog<TerrainWorld>.Warn("[LoadPhase/TerrainObjects] {0}", m);

        foreach (var (key, building, entities) in BuildingDamageLoad.Resolve(world, saved, warn))
        {
            if (existingKeys.Contains(key)) continue;   // a re-commit never doubles a building's damage
            var anchor = WallGroups.Anchor(world, building);
            for (int i = 0; i < entities.Count; i++)
            {
                var initial = new List<object> { new SimTransform { Position = anchor, Rotation = Quaternion.Identity }, entities[i], new TerrainObjectKey { Key = key } };
                // ⭐ CE-5033 (glass windows GW-2) — the saved windows ride the building's first entity; the others carry none
                initial.Add(i == 0 ? BuildingDamageLoad.WindowsOf(world, building, saved[key], warn) : new WindowDamage { Building = building });
                requests.Add(new EntityCreationRequest
                {
                    RequestId             = Guid.NewGuid(),
                    TkbType               = (Fdp.Core.TkbTypeId)BuildingDamages.TkbType,
                    PreAllocatedNetworkId = idAllocator.AllocateId(),
                    IsTransient           = true,
                    InitialComponents     = initial,
                });
            }
        }
        return requests;
    }

    /// <summary>
    /// ⭐ <c>CE-4591</c> (P-4 part 3, G-7) — the saved craters as creation requests: one <see cref="GroundDamage"/> entity per ground cell (16 craters each, more ⇒ several), in
    /// cell order, untargeted (the loading node makes and owns them — the breaches' rule, R-4196: a muscle's NEW crater opens its own entity beside it). ⛔ None when the world
    /// already holds a ground-damage entity (a re-commit never doubles the craters).
    /// </summary>
    public static List<EntityCreationRequest> ForCraters(EntityRepository world, IReadOnlyList<Crater> craters, INetworkIdAllocator idAllocator)
    {
        if (idAllocator == null) throw new ArgumentNullException(nameof(idAllocator));
        var requests = new List<EntityCreationRequest>();
        if (craters == null || craters.Count == 0) return requests;
        if (world.IsComponentTypeRegistered<GroundDamage>())
            foreach (var _ in world.Query().With<GroundDamage>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build()) return requests;
        var byCell = new SortedDictionary<(int X, int Y), List<Crater>>();
        foreach (var c in craters)
        {
            var cell = GroundDamages.CellOf(c.Centre);
            if (!byCell.TryGetValue(cell, out var l)) byCell[cell] = l = new List<Crater>();
            l.Add(c);
        }
        foreach (var ((cx, cy), list) in byCell)
            for (int start = 0; start < list.Count; start += GroundDamage.Capacity)
            {
                var d = new GroundDamage { CellX = cx, CellY = cy };
                for (int i = start; i < list.Count && i < start + GroundDamage.Capacity; i++) GroundDamages.Add(ref d, list[i], out _);
                requests.Add(new EntityCreationRequest
                {
                    RequestId             = Guid.NewGuid(),
                    TkbType               = (Fdp.Core.TkbTypeId)GroundDamages.TkbType,
                    PreAllocatedNetworkId = idAllocator.AllocateId(),
                    IsTransient           = true,
                    InitialComponents     = new List<object>
                    {
                        new SimTransform { Position = GroundDamages.Anchor(cx, cy, 0f), Rotation = Quaternion.Identity },
                        d,
                    },
                });
            }
        return requests;
    }

    /// <summary>⭐ <c>CE-4603</c> (B-6) — the saved felled trunks as creation requests: one <see cref="TreeDamage"/> entity per cell (16 each), untargeted (the loader's); none on a re-commit.</summary>
    public static List<EntityCreationRequest> ForTrees(EntityRepository world, IReadOnlyList<(int X, int Y)> trees, INetworkIdAllocator idAllocator)
    {
        if (idAllocator == null) throw new ArgumentNullException(nameof(idAllocator));
        var requests = new List<EntityCreationRequest>();
        if (trees == null || trees.Count == 0) return requests;
        if (world.IsComponentTypeRegistered<TreeDamage>())
            foreach (var _ in world.Query().With<TreeDamage>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build()) return requests;
        var byCell = new SortedDictionary<(int X, int Y), List<(int X, int Y)>>();
        foreach (var t in trees)
        {
            var cell = TreeDamages.CellOf(t);
            if (!byCell.TryGetValue(cell, out var l)) byCell[cell] = l = new List<(int X, int Y)>();
            l.Add(t);
        }
        foreach (var ((cx, cy), list) in byCell)
            for (int start = 0; start < list.Count; start += TreeDamage.Capacity)
            {
                var d = new TreeDamage { CellX = cx, CellY = cy };
                for (int i = start; i < list.Count && i < start + TreeDamage.Capacity; i++) TreeDamages.Add(ref d, list[i], out _);
                requests.Add(new EntityCreationRequest
                {
                    RequestId = Guid.NewGuid(), TkbType = (Fdp.Core.TkbTypeId)TreeDamages.TkbType, PreAllocatedNetworkId = idAllocator.AllocateId(), IsTransient = true,
                    InitialComponents = new List<object> { new SimTransform { Position = GroundDamages.Anchor(cx, cy, 0f), Rotation = Quaternion.Identity }, d },
                });
            }
        return requests;
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (KR-15) — the saved place knowledge as creation requests: one <see cref="MinefieldKnowledge"/> per saved (field, side, kind, chunk), the fields' struck / cleared
    /// mines as <see cref="MinefieldStrikes"/> chunks and their lanes as <see cref="MinefieldLanes"/> chunks (96 / 8 each, more ⇒ several), untargeted (the loading node makes and owns
    /// them — the craters' rule). ⛔ A record family that already has an entity in the world is not loaded again (a re-commit never doubles).
    /// </summary>
    public static List<EntityCreationRequest> ForPlaceKnowledge(EntityRepository world, SavedPlaceKnowledge saved, INetworkIdAllocator idAllocator)
    {
        if (idAllocator == null) throw new ArgumentNullException(nameof(idAllocator));
        var requests = new List<EntityCreationRequest>();
        if (saved == null || saved.IsEmpty) return requests;
        static bool Holds<T>(EntityRepository w) where T : unmanaged
        {
            if (!w.IsComponentTypeRegistered<T>()) return false;
            foreach (var _ in w.Query().With<T>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build()) return true;
            return false;
        }
        EntityCreationRequest Req(long tkb, object record) => new()
        {
            RequestId = Guid.NewGuid(), TkbType = (Fdp.Core.TkbTypeId)tkb, PreAllocatedNetworkId = idAllocator.AllocateId(), IsTransient = true,
            InitialComponents = new List<object> { new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity }, record },
        };

        if (!Holds<MinefieldKnowledge>(world))
            foreach (var k in saved.Knowledge)
            {
                var r = new MinefieldKnowledge();
                r.Header.SubjectKind = SubjectKind.Entity; r.Header.Subject = k.Subject; r.Header.ObjectIndex = -1; r.Header.SidePlus1 = k.SidePlus1;
                r.Header.Kind = k.Kind; r.Header.Chunk = k.Chunk; r.Header.Marked = (byte)(k.Marked ? 1 : 0); r.Header.LearnedAt = k.LearnedAt; r.Header.Source = k.Source;
                foreach (var ((row, col), level) in k.Cells) r.Set(col, row, (MineCellKnowledge)level);
                requests.Add(Req(MinefieldKnowledge.TkbType, r));
            }
        // ⭐ CE-2468 UF-3 (K1) — the places under fire: made LOADED, holding the saved AGE (never an absolute time); the loading node's writer rebases each on its first tick
        if (!Holds<UnderFireKnowledge>(world))
            foreach (var u in saved.UnderFire)
            {
                var r = new UnderFireKnowledge { Cause = u.Cause, Point = u.Point, RangeM = u.RangeM, EnemyStrength = u.EnemyStrength };
                r.Header.SubjectKind = SubjectKind.Place; r.Header.Subject = u.Subject; r.Header.ObjectIndex = -1; r.Header.SidePlus1 = u.SidePlus1;
                r.Header.Kind = PlaceKnowledgeKind.UnderFire; r.Header.Chunk = u.Chunk; r.Header.Source = u.Source;
                r.Header.State = UnderFireKnowledge.StateLoaded; r.Header.LearnedAt = (SimInstant)u.AgeSeconds;
                requests.Add(Req(UnderFireKnowledge.TkbType, r));
            }
        if (!Holds<MinefieldStrikes>(world))
            foreach (var (field, mines) in saved.Strikes)
            {
                var chunk = new MinefieldStrikes { Field = field };
                ushort n = 0;
                foreach (var ((row, col), how) in mines)
                {
                    if (chunk.IsFull) { requests.Add(Req(MinefieldStrikes.TkbType, chunk)); chunk = new MinefieldStrikes { Field = field, Chunk = ++n }; }
                    chunk.TryAdd(new StrikeItem(field, row, col, how));
                }
                if (chunk.Count > 0) requests.Add(Req(MinefieldStrikes.TkbType, chunk));
            }
        if (!Holds<MinefieldLanes>(world))
            foreach (var (field, lanes) in saved.Lanes)
            {
                var chunk = new MinefieldLanes { Field = field };
                ushort n = 0;
                foreach (var (a, b, w, side) in lanes)
                {
                    if (chunk.IsFull) { requests.Add(Req(MinefieldLanes.TkbType, chunk)); chunk = new MinefieldLanes { Field = field, Chunk = ++n }; }
                    chunk.TryAdd(new LaneItem(field, a, b, w, side));
                }
                if (chunk.Count > 0) requests.Add(Req(MinefieldLanes.TkbType, chunk));
            }
        return requests;
    }
}
