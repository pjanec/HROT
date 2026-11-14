using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4591</c> (P-4, G-7) — "a burst made this crater": published by <c>AreaEffectSystem</c> (the deciding node, a non-remote detonation), read by
    /// <see cref="GroundDamageSystem"/> the frame after. LOCAL (never on the wire): the record the system writes is what replicates.
    /// </summary>
    [EventId(TerrainEventIds.Crater)]
    [StructLayout(LayoutKind.Sequential)]
    public struct CraterEvent
    {
        public float X;
        public float Y;
        public float Radius;
        public float Depth;
        public float Rim;
        public SimInstant BornAt;
        public byte Kind;
        /// <summary>Who fired the round (for logs; <see cref="Entity.Null"/> when unknown).</summary>
        public Entity Shooter;

        public readonly Crater Crater => new(new Vector2(X, Y), Radius, Depth, Rim, BornAt, (CraterKind)Kind);

        public static CraterEvent Of(in Crater c, Entity shooter) => new()
        {
            X = c.Centre.X, Y = c.Centre.Y, Radius = c.Radius, Depth = c.Depth, Rim = c.Rim, BornAt = c.BornAt, Kind = (byte)c.Kind, Shooter = shooter,
        };
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4, G-7, V-7) — turns <see cref="CraterEvent"/>s into <see cref="GroundDamage"/> entities: THE cell-record writer
    /// (<see cref="CellRecordSystem{TRecord, TItem}"/>, CE-5000 — one per (cell, deciding node), owned by it; the view unions them, <see cref="CraterTable"/>) with the
    /// crater event and a crater's cell. ⛔ Never in a replay; writes only entities this node owns.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class GroundDamageSystem : CellRecordSystem<GroundDamage, Crater>
    {
        /// <summary>The wire ordinal of the ground-damage descriptor (<c>EDescriptorType.dtGroundDamage</c> in the NED assembly). ⚠ A rail pins the two equal.</summary>
        public const long DescriptorOrdinal = 184;

        /// <summary>The ownership key a ground-damage entity is published under — this system and the egress both gate on it.</summary>
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => GroundDamages.TkbType;
        protected override (int X, int Y) CellOf(in Crater item) => GroundDamages.CellOf(item.Centre);

        protected override void ReadItems(EntityRepository repo, List<Crater> into)
        {
            if (!repo.Bus.IsRegistered<CraterEvent>()) return;
            var events = repo.ReadEvents<CraterEvent>();
            for (int i = 0; i < events.Length; i++) { var c = events[i].Crater; if (c.Radius > 0f) into.Add(c); }
        }

        /// <summary>True when this node writes <paramref name="e"/>: the owner of its <c>dtGroundDamage</c> descriptor (⛔ never a ghost).</summary>
        public static bool OwnsDamage(EntityRepository repo, Entity e) => OwnsRecord(repo, e, OwnershipKey);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-11, V-8) — RE-GROUNDING: when a crater appears (the view's crater table gains one), every
    /// entity this node OWNS that stood on the ground inside its changed disc takes the cratered ground (a man does not float over the hole; a moving one re-samples by
    /// itself anyway). ⛔ Never a replica (its owner re-grounds it), never a static obstacle (the loader owns it; moving it would re-trigger its own bake — V-8), never an
    /// entity off the ground (an aircraft, a man on a floor: more than <see cref="OnGroundMetres"/> from the bare ground), never during a replay.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class CraterRegroundSystem : IEcsModuleSystem
    {
        /// <summary>How far from the ground (before the crater) an entity may stand and still count as standing on it.</summary>
        public const float OnGroundMetres = 0.5f;

        private CraterTable? _last;
        private readonly HashSet<Crater> _before = new();
        private readonly List<Crater> _added = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        /// <summary>Entities moved (rails, diagnostics).</summary>
        public long Regrounded { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<GroundDamage>() || !repo.IsComponentTypeRegistered<SimTransform>()) return;
            if (!repo.HasSingletonManaged<TerrainWorld>() || repo.GetSingletonManaged<TerrainWorld>() is not { } terrain) return;
            var craters = DoorStates.Of(repo, terrain).Craters;
            if (ReferenceEquals(craters, _last)) return;   // the steady frame: one reference compare
            var was = _last; _last = craters;
            if (craters == null) return;
            _before.Clear(); _added.Clear();
            if (was != null) for (int i = 0; i < was.Count; i++) _before.Add(was[i]);
            for (int i = 0; i < craters.Count; i++) if (!_before.Contains(craters[i])) _added.Add(craters[i]);
            if (_added.Count == 0) return;

            if (!ReferenceEquals(repo, _queryRepo)) { _query = repo.Query().With<SimTransform>().Build(); _queryRepo = repo; }
            bool obstacles = repo.IsComponentTypeRegistered<StaticObstacle>(), damage = repo.IsComponentTypeRegistered<BuildingDamage>();
            foreach (var e in _query!)
            {
                if (obstacles && repo.HasComponent<StaticObstacle>(e)) continue;
                if (repo.HasComponent<GroundDamage>(e) || (damage && repo.HasComponent<BuildingDamage>(e))) continue;   // the records themselves
                var t = repo.GetComponentRO<SimTransform>(e);
                var p = new Vector2(t.Position.X, t.Position.Y);
                bool inside = false;
                for (int k = 0; k < _added.Count && !inside; k++) inside = Vector2.DistanceSquared(p, _added[k].Centre) < _added[k].OuterRadius * _added[k].OuterRadius;
                if (!inside) continue;
                if (!Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e)) continue;   // a replica: its owner re-grounds it
                if (MathF.Abs(t.Position.Z - terrain.BareGroundHeightAt(p.X, p.Y) - (was?.Offset(p.X, p.Y) ?? 0f)) > OnGroundMetres) continue;   // off the ground
                float z = craters.GroundHeightAt(terrain, p.X, p.Y);
                if (z == t.Position.Z) continue;
                t.Position = new Vector3(t.Position.X, t.Position.Y, z);
                repo.SetComponent(e, t);
                Regrounded++;
            }
        }
    }
}
