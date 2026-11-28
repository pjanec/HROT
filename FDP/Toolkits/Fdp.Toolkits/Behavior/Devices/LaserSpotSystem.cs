using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8, LS-15 / LS-12 / R5-9 / R5-10) — THE LASER'S SPOT, on
    /// the MUSCLE (the node that owns the bodies; registered beside the round path in <c>CombatModule</c>): for every <see cref="LaserPointing"/>
    /// that is on, a SYNCHRONOUS trace from the device's world pose (<see cref="DevicePoses.TryWorld"/>) along its direction — against the
    /// terrain (<see cref="IWorldQuery.Trace"/> for <see cref="TracePurpose.Fire"/>: what is solid, the ground included) and against the bodies
    /// (the entity grid's <see cref="SpatialHashGrid.QuerySegment"/>, then the part test a round makes, <see cref="BodyPartRay.TryHit"/>, else
    /// the collider's cylinder) — nearest first, into the device's <see cref="LaserSpot"/> (its ONE writer).
    /// <para>⛔ Never the combat ray-cast EVENTS (<c>RaycastRequestEvent</c> is solved <c>WhenTimeAdvances</c> and would never answer in pause).
    /// ⭐ Every frame, paused or not: a spot written on a paused frame is a <see cref="LaserSpot.Preview"/> (R5-10). ⚠ The entity grid is the last
    /// advanced tick's — an entity moved in pause is met where it was (accepted for a preview, LS-15). ⛔ Nothing while a replay plays (the
    /// recording owns the spot). 0 B per steady frame (R-2084): the candidate and box buffers are on the stack, the crossing list is reused,
    /// a component is written only when its value changed.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    [Fdp.Core.NotState("a system/module, classified on its own")]
    public sealed class LaserSpotSystem : IEcsModuleSystem
    {
        /// <summary>The broad phase's candidate buffer per beam.</summary>
        public const int MaxCandidates = 512;

        private EntityQuery? _lasers;
        private EntityRepository? _queryFor;
        [Fdp.Core.FrameScratch("the terrain crossings of the beam being traced, cleared per trace")]
        private readonly List<TraceCrossing> _crossings = new(32);

        /// <summary>Traces made (a rail reads it; never reset).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter, a rail reads it")]
        public long Traces { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<LaserPointing>() || !repo.IsComponentTypeRegistered<LaserSpot>()) return;
            if (_lasers == null || !ReferenceEquals(repo, _queryFor))
            {
                _lasers = repo.Query().With<LaserPointing>().Build();
                _queryFor = repo;
            }
            bool preview = deltaTime <= 0f;
            IWorldQuery? terrain = null;
            bool terrainAsked = false;
            foreach (var e in _lasers)
            {
                ref readonly var pointing = ref repo.GetComponentRO<LaserPointing>(e);
                LaserSpot next = default;
                if (pointing.On != 0)
                {
                    if (!terrainAsked) { terrain = WorldQuery.Of(view); terrainAsked = true; }
                    next.PointingSeq = pointing.Seq;
                    next.Preview = preview ? (byte)1 : (byte)0;
                    if (DevicePoses.TryWorld(repo, e, out var origin, out var rotation))
                    {
                        var local = pointing.Direction.LengthSquared() > 1e-12f ? Vector3.Normalize(pointing.Direction) : Vector3.UnitX;
                        var dir = Vector3.Normalize(Vector3.Transform(local, rotation));
                        Trace(repo, terrain, e, origin, dir, pointing.Power, ref next);
                    }
                }
                Store(repo, e, in next);
            }
        }

        private void Trace(EntityRepository repo, IWorldQuery? terrain, Entity device, Vector3 origin, Vector3 dir, float power, ref LaserSpot spot)
        {
            Traces++;
            var end = origin + dir * LaserSpots.MaxRangeM;
            float bestT = float.MaxValue;
            Entity bestEntity = Entity.Null;
            int bestPart = -1;

            // ① the terrain — the first solid crossing (the ground, a wall, a roof); a wood's crossing only stops a share of rounds and is no surface
            if (terrain != null)
            {
                terrain.Trace(origin, end, TracePurpose.Fire, _crossings);
                for (int i = 0; i < _crossings.Count; i++)
                {
                    var c = _crossings[i];
                    if (c.Kind == Fdp.Toolkit.Terrain.TerrainWorld.WoodsKind) continue;
                    if (c.T >= 0f && c.T < bestT) bestT = c.T;
                    break;
                }
            }

            // ② the bodies — the entity grid along the beam, then each candidate's parts (the round's test) or its collider
            if (repo.HasSingleton<SpatialGridData>())
            {
                var gridData = repo.GetSingleton<SpatialGridData>();
                if (repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) VehicleBody.Prime(repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>());   // the pad covers every type's body (as the round's)
                float pad = gridData.MaxRadius > 0f
                    ? MathF.Max(MathF.Max(gridData.MaxRadius, PersonBody.MaxBoundRadius + Fdp.Toolkit.Combat.Leans.MaxShift), VehicleBody.MaxBoundRadius)
                    : Fdp.Toolkit.Physics.PhysicsConstants.QueryExpansionRadius;
                Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[MaxCandidates];
                Span<BodyBox> boxes = stackalloc BodyBox[VehicleBody.MaxBoxes];
                int n = gridData.Grid.QuerySegment(new Vector2(origin.X, origin.Y), new Vector2(end.X, end.Y), pad, candidates, out _);
                for (int j = 0; j < n; j++)
                {
                    var c = candidates[j].entity;
                    if (c == device || !repo.IsAlive(c) || !repo.HasComponent<SimTransform>(c)) continue;
                    if (RidesAlong(repo, device, c) || DeviceMarker.IsDevice(repo, c)) continue;   // never its own carrier, his ride, nor another device
                    if (Fdp.Toolkit.Combat.Items.IsMountable(repo, c) && Embarked(repo, c)) continue;   // carried / mounted gear is met through its carrier
                    float t;
                    int part = -1;
                    if (BodyPartRay.TryHit(repo, c, origin, end, boxes, out var hit, out bool hasParts)) { t = hit.T; part = hit.Part; }
                    else if (hasParts) continue;   // between his arm and his chest
                    else if (!ColliderHit(repo, c, origin, end, out t)) continue;
                    if (t < bestT) { bestT = t; bestEntity = c; bestPart = part; }
                }
            }

            if (bestT > 1f) return;   // no return within range: Count stays 0, the request answered
            float range = bestT * LaserSpots.MaxRangeM;
            spot.Points[0] = new LaserSpotPoint
            {
                Point = origin + (end - origin) * bestT, Range = range, Strength = LaserSpots.StrengthAt(power, range), Hit = bestEntity, Part = bestPart,
            };
            spot.Count = 1;
        }

        /// <summary>A body with no parts: its collider's cylinder (feet … feet + height; a height of 0 = the circle at any height).</summary>
        private static bool ColliderHit(EntityRepository repo, Entity c, Vector3 a, Vector3 b, out float t)
        {
            t = 0f;
            if (!repo.HasComponent<PhysicsCollider>(c)) return false;
            ref readonly var col = ref repo.GetComponentRO<PhysicsCollider>(c);
            if (col.Radius <= 0f) return false;
            var p = repo.GetComponentRO<SimTransform>(c).Position;
            if (!Intersection2D.SegmentInCircle(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), new Vector2(p.X, p.Y), col.Radius, out float tIn, out float tOut)) return false;
            if (col.Height <= 0f) { t = tIn; return true; }
            // the part of [tIn, tOut] inside the height band
            float za = a.Z, dz = b.Z - a.Z, low = p.Z, high = p.Z + col.Height;
            if (MathF.Abs(dz) < 1e-9f) { if (za < low || za > high) return false; t = tIn; return true; }
            float t0 = (low - za) / dz, t1 = (high - za) / dz;
            if (t0 > t1) (t0, t1) = (t1, t0);
            float s = MathF.Max(tIn, t0), e = MathF.Min(tOut, t1);
            if (s > e) return false;
            t = s;
            return true;
        }

        private static bool Embarked(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<IsEmbarkedTag>() && repo.HasComponent<IsEmbarkedTag>(e);

        private static Entity HostOf(EntityRepository repo, Entity e)
            => Embarked(repo, e) ? repo.GetComponentRO<IsEmbarkedTag>(e).VehicleEntity : Entity.Null;

        /// <summary>
        /// <paramref name="c"/> moves with the device: one of its hosts (the man carrying it, the vehicle he rides — up to four levels), or something
        /// riding one of those hosts (his fellow passengers, the vehicle's other stations). The beam leaves from inside that group.
        /// </summary>
        private static bool RidesAlong(EntityRepository repo, Entity device, Entity c)
        {
            var cHost = HostOf(repo, c);
            var h = HostOf(repo, device);
            for (int depth = 0; depth < 4 && !h.IsNull; depth++)
            {
                if (c == h || cHost == h) return true;
                h = HostOf(repo, h);
            }
            return false;
        }

        private static void Store(EntityRepository repo, Entity e, in LaserSpot next)
        {
            if (repo.HasComponent<LaserSpot>(e))
            {
                ref readonly var now = ref repo.GetComponentRO<LaserSpot>(e);
                if (Same(in now, in next)) return;   // unchanged: no chunk version bump, nothing re-sent
                repo.SetComponent(e, next);
                return;
            }
            if (next.PointingSeq == 0 && next.Count == 0) return;   // never lit: nothing to add
            repo.AddComponent(e, next);
        }

        private static bool Same(in LaserSpot a, in LaserSpot b)
        {
            if (a.Count != b.Count || a.Preview != b.Preview || a.PointingSeq != b.PointingSeq) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var p = a.Points[i]; var q = b.Points[i];
                if (p.Point != q.Point || p.Range != q.Range || p.Strength != q.Strength || p.Hit != q.Hit || p.Part != q.Part) return false;
            }
            return true;
        }
    }
}
