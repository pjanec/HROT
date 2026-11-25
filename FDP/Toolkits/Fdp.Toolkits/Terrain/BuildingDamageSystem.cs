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
    /// ⭐ <c>CE-3488</c> (flame FL-4 B-1; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §7c) — "building <see cref="Building"/> is set alight": raised by the flame and
    /// napalm igniters on the record writer's node, read by <see cref="BuildingDamageSystem"/> the same frame. The spot in WORLD metres. LOCAL.
    /// </summary>
    [EventId(TerrainEventIds.BuildingFire)]
    [StructLayout(LayoutKind.Sequential)]
    public struct BuildingFireEvent
    {
        public int Building;
        public float X, Y, Z, Radius;
        public SimInstant Since;
        public float Seconds, Intensity;
        public readonly Fdp.Toolkit.Perception.Signatures.FireSpot Spot => new(X, Y, Z, Radius, Since, Seconds, Intensity);
    }

    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-5) — "a detonation breached this wall": published by <c>AreaEffectSystem.BreachWalls</c> (the deciding node, a
    /// non-remote detonation) for each breach, read by <see cref="BuildingDamageSystem"/> the frame after. LOCAL (never on the wire): the entity the
    /// system writes is what replicates. <see cref="Panel"/> is the global panel index, <see cref="Kind"/> a <see cref="BreachKind"/>.
    /// </summary>
    [EventId(TerrainEventIds.BuildingDamage)]
    [StructLayout(LayoutKind.Sequential)]
    public struct BuildingDamageEvent
    {
        public int Building;
        public int Panel;
        public float From;
        public float To;
        public float Bottom;
        public float Top;
        public byte Kind;
        /// <summary>Who fired the round (for logs; <see cref="Entity.Null"/> when unknown).</summary>
        public Entity Shooter;
        /// <summary>⭐ S-2 — 1 = the building COLLAPSED (the breach fields are unused; <see cref="Panel"/> is −1).</summary>
        public byte Collapse;
        /// <summary>⭐ <c>CE-11051</c> — the BLAST POINT (terrain-local metres): the detonation's world point; for a hull's ram or a blade, the hull's centre. Kept per
        /// breach on <see cref="BuildingDamage"/> (<see cref="BuildingDamage.BlastOf"/>) so a client throws the debris away from it.</summary>
        public float BlastX, BlastY, BlastZ;
        /// <summary>⭐ <c>CE-11054</c> — the <see cref="BreachCause"/> of this breach (or of the collapse): what the producer knows and the record keeps. Unset (0) = Unknown.</summary>
        public byte Cause;
        /// <summary>⭐ <c>CE-11054</c> — a Detonation's explosive kg (the warhead's, as the breach / collapse rule used it); 0 for a contact cause.</summary>
        public float ExplosiveKg;

        public readonly WallBreach Breach => new(Panel, From, To, Bottom, Top, (BreachKind)Kind);
        public readonly Vector3 Blast => new(BlastX, BlastY, BlastZ);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-2, B-4) — turns <see cref="BuildingDamageEvent"/>s into <see cref="BuildingDamage"/> entities. ONE entity per
    /// (building, DECIDING node), made at that node's first breach of the building and owned by it (so there is no cross-node request and no creation
    /// race: two Muscles hitting one untouched house in one frame each make their own, and the view unions them — <see cref="BuildingDamageTable"/>).
    /// <para>A breach overlapping one of this node's on the same panel is MERGED into the union rectangle; past <see cref="BuildingDamage.Capacity"/>
    /// a second entity is opened. A breach for a building whose creation request is still in flight (the entity is made a few frames later, by the creation
    /// system) waits in a system-local list and is applied when the entity appears — matched by the building's key, never by a handle.</para>
    /// <para>⛔ Never runs during a replay (the recording owns the world: <c>AreaEffectSystem</c> does not decide there either). ⛔ Writes only entities THIS
    /// node owns (<see cref="DescriptorKey"/>); a replica or a ghost is never touched.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class BuildingDamageSystem : IEcsModuleSystem
    {
        /// <summary>
        /// The wire ordinal of the building-damage descriptor (<c>EDescriptorType.dtBuildingDamage</c> in the NED assembly, which this toolkit cannot
        /// reference). ⚠ A rail in the SimHost suite pins the two equal.
        /// </summary>
        public const long DescriptorOrdinal = 183;

        /// <summary>⭐ CE-5033 — the wire ordinal of the window descriptor (<c>EDescriptorType.dtWindowDamage</c>; a rail pins the two equal).</summary>
        public const long WindowDescriptorOrdinal = 245;

        /// <summary>The ownership key a building-damage entity is published under — this system and the egress both gate on it.</summary>
        public static readonly long DescriptorKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        /// <summary>A request that never produced an entity within this many ticks is forgotten (a later breach asks again).</summary>
        public const int PendingTimeoutTicks = 3600;

        /// <summary>Breaches written into an entity (a rail and a diagnostics counter read it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of breaches applied")]
        public long Applied { get; private set; }
        /// <summary>Creation requests published.</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of creation requests")]
        public long Requested { get; private set; }

        private sealed class Pending
        {
            public int Building;
            public int Age;
            public readonly List<(WallBreach Breach, BreachOrigin Origin)> Later = new();   // ⭐ CE-11051 / CE-11054 — with its origin (time filled when it is applied)
            public readonly List<WindowHitEvent> LaterWindows = new();   // ⭐ CE-5033
            public readonly List<BuildingFireEvent> LaterFires = new();   // ⭐ CE-3488
            public bool Collapse;
            public BreachOrigin CollapseOrigin;   // ⭐ CE-11054 — what collapsed it
        }

        [Fdp.Core.ProtocolCache("breaches/collapse waiting for the requested damage entity to appear: creation request in flight")]
        private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
        private int _worldEpoch;
        [Fdp.Core.FrameScratch("keys resolved this drain: cleared at 229 and filled in DrainPending")]
        private readonly List<string> _done = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        /// <summary>How many buildings are waiting on a creation request (rails).</summary>
        public int PendingCount => _pending.Count;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _pending.Clear();   // ⭐ R-4297 ④ (CE-1980) — a world replacement forgets the record entities asked for (the requests died with the world)
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<BuildingDamage>() || !repo.HasSingletonManaged<TerrainWorld>()) return;
            var terrain = repo.GetSingletonManaged<TerrainWorld>();
            if (terrain == null) return;

            // ⭐ CE-11051 — the sim time every record write of this frame is stamped with (the clock the recorder and the gateway read)
            double now = Fdp.Toolkit.Time.SimClock.TotalTimeOf(repo);   // allocation-free (Of() boxes 24 B)
            if (_pending.Count > 0) DrainPending(repo, terrain, now);

            if (!repo.Bus.IsRegistered<BuildingDamageEvent>()) return;
            var events = repo.ReadEvents<BuildingDamageEvent>();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if ((uint)evt.Building >= (uint)WallGroups.Count(terrain)) continue;   // ⭐ CE-4599 — a wall group: a building or a free-wall line
                if (evt.Collapse != 0) { if (WallGroups.IsBuilding(terrain, evt.Building)) ApplyCollapse(repo, terrain, evt.Building, now, OriginOf(repo, in evt, now)); continue; }
                if ((uint)evt.Panel >= (uint)terrain.Panels.Count) continue;
                Apply(repo, terrain, evt.Building, evt.Breach, OriginOf(repo, in evt, now));
                BreakWindowsUnder(repo, terrain, evt.Building, evt.Breach, now);   // ⭐ CE-5033 D-10 — the glass goes with its wall
            }

            // ⭐ CE-3488 (flame FL-4 B-1) — the buildings set alight: a FireState on the building's record (TD-6's second holder)
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.FireState>() && repo.Bus.IsRegistered<BuildingFireEvent>())
            {
                var fires = repo.ReadEvents<BuildingFireEvent>();
                for (int i = 0; i < fires.Length; i++)
                    if (WallGroups.IsBuilding(terrain, fires[i].Building)) ApplyFire(repo, terrain, in fires[i]);
            }

            // ⭐ CE-5033 (glass windows GW-2, D-4) — the panes rounds and bursts hit
            if (!repo.IsComponentTypeRegistered<WindowDamage>() || !repo.Bus.IsRegistered<WindowHitEvent>()) return;
            var hits = repo.ReadEvents<WindowHitEvent>();
            for (int i = 0; i < hits.Length; i++)
            {
                ref readonly var h = ref hits[i];
                if (!WallGroups.IsBuilding(terrain, h.Building) || (uint)h.Panel >= (uint)terrain.Panels.Count) continue;
                var openings = terrain.Panels[h.Panel].Openings;
                if (terrain.Panels[h.Panel].Building != h.Building || (uint)h.Opening >= (uint)openings.Count || !openings[h.Opening].Glazed) continue;
                ApplyWindow(repo, terrain, in h, now);
            }
        }

        /// <summary>Buildings set alight (a rail and a diagnostics counter read it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of buildings set alight")]
        public long Ignited { get; private set; }

        /// <summary>
        /// ⭐ CE-3488 (flame FL-4 B-1) — writes a building's fire into THIS node's damage entity for it (the one with a <c>FireState</c>, else the first, which gains one
        /// with its final value), else asks for an entity born holding it — the way a window is written. The spot is held in the record's frame (its anchor). v1: ONE
        /// fire per building — a building already burning takes no second.
        /// </summary>
        private void ApplyFire(EntityRepository repo, TerrainWorld terrain, in BuildingFireEvent f)
        {
            string key = BuildingDamages.KeyOf(terrain, f.Building);
            if (_pending.TryGetValue(key, out var waiting)) { waiting.LaterFires.Add(f); return; }
            Entity target = Entity.Null;
            foreach (var e in Query(repo))
            {
                if (!OwnsDamage(repo, e) || repo.GetComponentRO<BuildingDamage>(e).Building != f.Building) continue;
                if (repo.HasComponent<Fdp.Toolkit.Perception.Signatures.FireState>(e)) { target = e; break; }
                if (target.IsNull) target = e;
            }
            var anchor = WallGroups.Anchor(terrain, f.Building);
            var local = f.Spot.Offset(-anchor);
            double now = f.Since.Seconds;
            bool has = !target.IsNull && repo.HasComponent<Fdp.Toolkit.Perception.Signatures.FireState>(target);
            var fire = has ? repo.GetComponentRO<Fdp.Toolkit.Perception.Signatures.FireState>(target) : new Fdp.Toolkit.Perception.Signatures.FireState();
            if (fire.IsBurning(now) || !fire.TryAdd(in local, now)) return;   // already alight (v1: one fire), or full
            Ignited++;
            if (target.IsNull) Request(repo, terrain, f.Building, key, default, f.Since.Seconds, fire: fire);
            else if (has) repo.SetComponent(target, fire);
            else repo.AddComponent(target, fire);   // a record made before it burned: added with its final value (D-31)
        }

        /// <summary>Panes broken (a rail and a diagnostics counter read it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of broken panes")]
        public long PanesBroken { get; private set; }

        /// <summary>
        /// ⭐ CE-5033 (D-4, D-9) — writes one window hit / shatter into THIS node's damage entity for the building (the one with a <see cref="WindowDamage"/>, else the
        /// first, which gains one), else asks for an entity born holding it. A pane that turns broken raises <see cref="WindowBrokenEvent"/> (unless silent).
        /// </summary>
        private void ApplyWindow(EntityRepository repo, TerrainWorld terrain, in WindowHitEvent h, double now)
        {
            string key = BuildingDamages.KeyOf(terrain, h.Building);
            if (_pending.TryGetValue(key, out var waiting)) { waiting.LaterWindows.Add(h); return; }
            Entity target = Entity.Null;
            foreach (var e in Query(repo))
            {
                if (!OwnsDamage(repo, e) || repo.GetComponentRO<BuildingDamage>(e).Building != h.Building) continue;
                if (repo.HasComponent<WindowDamage>(e)) { target = e; break; }
                if (target.IsNull) target = e;
            }
            var w = !target.IsNull && repo.HasComponent<WindowDamage>(target) ? repo.GetComponentRO<WindowDamage>(target) : new WindowDamage { Building = h.Building };
            var change = h.Shatter != 0 ? WindowDamages.Break(ref w, h.Panel, h.Opening, now) : WindowDamages.Hit(ref w, h.Panel, h.Opening, h.U, h.V, now);   // ⭐ CE-11051 — stamped
            if (change is WindowChange.Unchanged or WindowChange.Full) return;
            if (target.IsNull) { Request(repo, terrain, h.Building, key, default, now, windows: w); }
            else if (repo.HasComponent<WindowDamage>(target)) repo.SetComponent(target, w);
            else repo.AddComponent(target, w);   // an entity made before it had windows: added with its final value
            if (change == WindowChange.Broke)
            {
                PanesBroken++;
                if (h.Silent == 0 && repo.Bus.IsRegistered<WindowBrokenEvent>())
                {
                    var p = terrain.Panels[h.Panel]; var o = p.Openings[h.Opening];
                    var dir = p.B - p.A; float len = dir.Length();
                    var c = p.A + (len > 0f ? dir / len : Vector2.Zero) * (o.At + o.Width * 0.5f);
                    repo.Bus.Publish(new WindowBrokenEvent { Building = h.Building, Panel = h.Panel, Opening = h.Opening, X = c.X, Y = c.Y, Z = (o.SillZ + o.HeadZ) * 0.5f, Source = h.Source });
                }
            }
        }

        /// <summary>⭐ CE-5033 D-10 — every glazed window of <paramref name="breach"/>'s panel that the breach overlaps is BROKEN (silently: the breach's debris is the effect).</summary>
        private void BreakWindowsUnder(EntityRepository repo, TerrainWorld terrain, int building, in WallBreach breach, double now)
        {
            if (!repo.IsComponentTypeRegistered<WindowDamage>()) return;
            var openings = terrain.Panels[breach.Panel].Openings;
            for (int oi = 0; oi < openings.Count; oi++)
            {
                var o = openings[oi];
                if (!o.Glazed || o.At > breach.To || o.At + o.Width < breach.From || o.SillZ > breach.Top || o.HeadZ < breach.Bottom) continue;
                ApplyWindow(repo, terrain, new WindowHitEvent { Building = building, Panel = breach.Panel, Opening = oi, Shatter = 1, Silent = 1 }, now);
            }
        }

        /// <summary>Writes <paramref name="breach"/> into this node's entity for the building (merging or appending), else asks for one.</summary>
        /// <summary>
        /// ⭐ <c>CE-11054</c> — the origin a record keeps for <paramref name="evt"/>: the frame's time, the blast point, the cause and kg the producer set, and — for a contact cause — the acting vehicle's
        /// NETWORK id (<c>NetworkIdentity</c> of <see cref="BuildingDamageEvent.Shooter"/>; 0 when it has none, and always 0 for a detonation). Allocation-free.
        /// </summary>
        internal static BreachOrigin OriginOf(EntityRepository repo, in BuildingDamageEvent evt, double now)
        {
            var cause = (BreachCause)evt.Cause;
            int actor = 0;
            if (cause is BreachCause.Ram or BreachCause.Blade or BreachCause.Push && !evt.Shooter.IsNull && repo.IsAlive(evt.Shooter)
                && repo.IsComponentTypeRegistered<NetworkIdentity>() && repo.HasComponent<NetworkIdentity>(evt.Shooter))
                actor = repo.GetComponentRO<NetworkIdentity>(evt.Shooter).Value;
            return new BreachOrigin(new SimInstant(now), evt.Blast, cause, evt.ExplosiveKg, actor);
        }

        private void Apply(EntityRepository repo, TerrainWorld terrain, int building, in WallBreach breach, in BreachOrigin origin)
        {
            string key = BuildingDamages.KeyOf(terrain, building);
            if (_pending.TryGetValue(key, out var waiting)) { waiting.Later.Add((breach, origin)); return; }   // the entity is not here yet: applied when it is

            Entity space = Entity.Null;   // the first owned entity of this building with room
            foreach (var e in Query(repo))
            {
                if (!OwnsDamage(repo, e)) continue;
                var d = repo.GetComponentRO<BuildingDamage>(e);
                if (d.Building != building) continue;
                bool overlaps = false;
                for (int i = 0; i < d.Count && !overlaps; i++) overlaps = d.Get(i).Overlaps(breach);
                if (overlaps) { Write(repo, terrain, e, d, breach, in origin); return; }   // merging never needs room
                if (space.IsNull && d.Count < BuildingDamage.Capacity) space = e;
            }
            if (!space.IsNull) { Write(repo, terrain, space, repo.GetComponentRO<BuildingDamage>(space), breach, in origin); return; }
            Request(repo, terrain, building, key, breach, origin.At, origin: origin);   // no entity of ours (or all full): make one — it is born holding this breach
        }

        /// <summary>⭐ S-2 — sets <c>Collapsed = 1</c> on this node's entity for the building (written only when it changes), else asks for one born collapsed.</summary>
        private void ApplyCollapse(EntityRepository repo, TerrainWorld terrain, int building, double now, in BreachOrigin cause)
        {
            string key = BuildingDamages.KeyOf(terrain, building);
            if (_pending.TryGetValue(key, out var waiting)) { waiting.Collapse = true; waiting.CollapseOrigin = cause; return; }
            Entity mine = Entity.Null;
            foreach (var e in Query(repo))
            {
                if (!OwnsDamage(repo, e)) continue;
                var d = repo.GetComponentRO<BuildingDamage>(e);
                if (d.Building != building) continue;
                if (d.Collapsed != 0) return;   // already collapsed here
                mine = e;
            }
            if (!mine.IsNull)
            {
                var d = repo.GetComponentRO<BuildingDamage>(mine);
                d.Collapsed = 1;
                d.CollapsedAt = now;   // ⭐ CE-11051
                d.SetCollapse(cause.Cause, cause.Blast, cause.ExplosiveKg);   // ⭐ CE-11054
                repo.SetComponent(mine, d);
                Applied++;
                return;
            }
            Request(repo, terrain, building, key, default, now, collapse: true, origin: cause);
        }

        private void Write(EntityRepository repo, TerrainWorld terrain, Entity e, BuildingDamage d, in WallBreach breach, in BreachOrigin origin)
        {
            // copy → change → write back only when something changed: an already-covered breach writes nothing (no version bump, no republish, no new time)
            if (BuildingDamages.Add(ref d, in breach, terrain, in origin) is BreachAdd.Unchanged or BreachAdd.Full) return;
            repo.SetComponent(e, d);
            Applied++;
        }

        private void DrainPending(EntityRepository repo, TerrainWorld terrain, double now)
        {
            _done.Clear();
            foreach (var (key, p) in _pending)
            {
                bool here = false;
                foreach (var e in Query(repo))
                    if (OwnsDamage(repo, e) && repo.GetComponentRO<BuildingDamage>(e).Building == p.Building) { here = true; break; }
                if (here || ++p.Age > PendingTimeoutTicks) _done.Add(key);
            }
            foreach (var key in _done)
            {
                var p = _pending[key];
                _pending.Remove(key);   // first: Apply must not see it as waiting
                if (p.Collapse) ApplyCollapse(repo, terrain, p.Building, now, p.CollapseOrigin);
                for (int i = 0; i < p.Later.Count; i++) Apply(repo, terrain, p.Building, p.Later[i].Breach, p.Later[i].Origin with { At = new SimInstant(now) });
                for (int i = 0; i < p.LaterWindows.Count; i++) ApplyWindow(repo, terrain, p.LaterWindows[i], now);   // ⭐ CE-5033
                for (int i = 0; i < p.LaterFires.Count; i++) ApplyFire(repo, terrain, p.LaterFires[i]);   // ⭐ CE-3488
            }
        }

        private void Request(EntityRepository repo, TerrainWorld terrain, int building, string key, in WallBreach breach, double now, bool collapse = false, WindowDamage? windows = null,
                             Fdp.Toolkit.Perception.Signatures.FireState? fire = null, BreachOrigin origin = default)
        {
            var d = new BuildingDamage { Building = building };
            if (collapse) { d.Collapsed = 1; d.CollapsedAt = now; d.SetCollapse(origin.Cause, origin.Blast, origin.ExplosiveKg); }   // ⭐ CE-11051 / CE-11054 — born stamped
            else if (windows == null && fire == null) BuildingDamages.Add(ref d, in breach, terrain, in origin);
            var anchor = WallGroups.Anchor(terrain, building);   // ⭐ CE-4599 — a building's position, a line's first post
            int node = repo.HasSingletonManaged<INetworkTopology>() ? (int)(repo.GetSingletonManaged<INetworkTopology>()?.LocalNodeId ?? default) : 0;
            var initial = new List<object> { new SimTransform { Position = anchor, Rotation = Quaternion.Identity }, d, new TerrainObjectKey { Key = key } };
            // ⭐ CE-5033 — every damage entity carries its windows (born with their final value), where the world knows the component
            if (repo.IsComponentTypeRegistered<WindowDamage>()) initial.Add(windows ?? new WindowDamage { Building = building });
            if (fire is { } burning) initial.Add(burning);   // ⭐ CE-3488 — born burning (its final value)
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = node,           // ⭐ B-2: THIS node owns it (0 = no topology: a single node, the default processor is this one)
                TkbType = (Fdp.Core.TkbTypeId)BuildingDamages.TkbType,
                InitType = Fdp.Toolkit.Replication.ReliableInitType.None,   // a breach waits for no peer
                IsTransient = true,                  // never written into a scenario as an entity: the terrain-object section saves the breaches (S-4)
                InitialComponents = initial,
            });
            _pending[key] = new Pending { Building = building };
            Requested++;
        }

        /// <summary>
        /// True when this node writes <paramref name="e"/>: the owner of its <c>dtBuildingDamage</c> descriptor, the same test its egress publishes under.
        /// With no <see cref="NetworkAuthority"/> (one node, the editor) every such entity is ours — ⛔ except a GHOST, a replica whose master has not
        /// arrived (applying there would fork the state).
        /// </summary>
        public static bool OwnsDamage(EntityRepository repo, Entity e)
        {
            if (!repo.IsAlive(e) || !repo.HasComponent<BuildingDamage>(e)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(e))
                && repo.GetLifecycleState(e) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e, DescriptorKey);
        }

        private EntityQuery Query(EntityRepository repo)
        {
            if (!ReferenceEquals(repo, _queryRepo))
            {
                _query = repo.Query().With<BuildingDamage>().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryRepo = repo;
            }
            return _query!;
        }
    }
}
