using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using NavResult = Fdp.Toolkit.Navigation.NavigationResult;

namespace CarKinem.Systems
{
    /// <summary>
    /// ⭐ Buildings Stage 5d-3/5d-4 (📄 docs/DESIGN_Building_Interiors.md §3j "5d") — an agent following a planned path through a
    /// doorway, on the node that MOVES it. The planner marks doorway corners (<see cref="TraversalKind.Door"/>, carried on
    /// <see cref="TrajectoryWaypoint.Traversal"/>); for the next mark ahead this system reads that door's state in THIS node's view:
    /// <list type="bullet">
    ///   <item><description><b>Closed</b> — within reach it HOLDS the agent (<see cref="NavState.IsBlocked"/>: the mover stops, path and
    ///   progress kept), spends the open time (<see cref="DoorRules.ActionSeconds"/>), raises ONE <see cref="DoorCommandEvent"/>
    ///   (Open) — the interaction transport carries it to the door's owner (R-221) — and releases the agent when the door's
    ///   replicated state says Open. No answer in <see cref="AnswerTimeoutSeconds"/> ⇒ treated as a door it cannot pass.</description></item>
    ///   <item><description><b>Locked</b> (or a Closed door that would not open) — the route is stale (R-218 P3): a replan through the
    ///   shared <see cref="NavigationExecutionSystem.RequestReplan"/>, or <see cref="NavResult.FailedBlocked"/> when the intent allows
    ///   none. The planner already treats a locked door as a wall, so the new path goes round it.</description></item>
    ///   <item><description><b>Open / Destroyed</b> — nothing to do.</description></item>
    /// </list>
    /// <para>⚠ The hold state is node-local and transient (a dictionary here): it lives only between "reached the door" and "the door
    /// opened". ⛔ This system never writes the door — only its owner does (<see cref="DoorCommandSystem"/>).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class DoorPassageSystem : IEcsModuleSystem
    {
        /// <summary>How far ahead along the path (m) a door mark is considered.</summary>
        public const float LookAheadMetres = 4f;
        /// <summary>A door mark within this (m, horizontally) of a door's centre belongs to that door.</summary>
        public const float DoorMatchMetres = 1.5f;
        /// <summary>How long to wait (s) for the owner's answer after the Open command was sent.</summary>
        public const float AnswerTimeoutSeconds = 3f;
        /// <summary>After asking for a new path because of a door, how long (s) before asking again for the same door.</summary>
        public const float ReplanQuietSeconds = 2f;

        private readonly TrajectoryPoolManager _pool;
        private readonly Dictionary<Entity, Passage> _passages = new();
        private readonly Dictionary<string, Entity> _doorEntities = new(System.StringComparer.Ordinal);
        private readonly List<Entity> _stale = new();

        private struct Passage
        {
            public int DoorIndex;
            public bool Holding;
            public float Elapsed;
            public bool Sent;
            public float SinceSent;
            public float SinceReplan;   // < ReplanQuietSeconds ⇒ a replan for DoorIndex is already on its way
            public bool Replanned;
            public int Seen;            // frame stamp, to forget agents that left the query
        }

        private int _frame;

        /// <summary>Open commands this system raised (a rail and diagnostics read it).</summary>
        public long OpenCommandsSent { get; private set; }
        /// <summary>Replans this system requested because a door ahead could not be passed.</summary>
        public long DoorReplans { get; private set; }

        public DoorPassageSystem(TrajectoryPoolManager pool) => _pool = pool;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<TerrainWorld>() || repo.GetSingletonManaged<TerrainWorld>() is not { Doors.Count: > 0 } terrain)
            {
                ReleaseAll(repo);
                return;
            }
            _frame++;

            foreach (var e in repo.Query().With<NavState>().With<SimTransform>().Build())
            {
                var nav = repo.GetComponentRO<NavState>(e);
                if (nav.Mode != KinematicsMode.CustomTrajectory || nav.HasArrived != 0
                    || !_pool.TryGetTrajectory(nav.TrajectoryId, out var traj)
                    || NextDoorAhead(traj, nav.ProgressS, terrain) is not int doorIndex)
                {
                    Release(repo, e);
                    continue;
                }

                _passages.TryGetValue(e, out var p);
                if (p.DoorIndex != doorIndex) p = new Passage { DoorIndex = doorIndex };
                p.Seen = _frame;
                p.SinceReplan += deltaTime;

                var def = terrain.Doors[doorIndex];
                var door = DoorEntity(repo, def.Key);
                var state = door.IsNull ? def.Initial : repo.GetComponentRO<DoorState>(door).State;
                var pos = repo.GetComponentRO<SimTransform>(e).Position;
                bool inReach = Vector2.Distance(new Vector2(pos.X, pos.Y), def.Center) <= DoorRules.ReachMetres;

                switch (state)
                {
                    case TerrainDoorState.Open:
                    case TerrainDoorState.Destroyed:
                        Unhold(repo, e, ref p);
                        break;

                    case TerrainDoorState.Locked:
                        Unhold(repo, e, ref p);
                        CannotPass(repo, e, ref p);
                        break;

                    default:   // Closed
                        if (!inReach || door.IsNull) { Unhold(repo, e, ref p); break; }   // not there yet / nothing to command
                        Hold(repo, e, ref p);
                        if (!p.Sent)
                        {
                            p.Elapsed += deltaTime;
                            if (p.Elapsed >= DoorRules.ActionSeconds(DoorVerb.Open))
                            {
                                repo.Bus.Publish(new DoorCommandEvent { Door = door, Verb = DoorVerb.Open, Actor = e });
                                p.Sent = true;
                                OpenCommandsSent++;
                            }
                        }
                        else if ((p.SinceSent += deltaTime) > AnswerTimeoutSeconds)
                        {
                            Unhold(repo, e, ref p);
                            CannotPass(repo, e, ref p);
                        }
                        break;
                }
                _passages[e] = p;
            }

            // agents that left the query (despawned, lost their path component) must not stay held
            _stale.Clear();
            foreach (var kv in _passages) if (kv.Value.Seen != _frame) _stale.Add(kv.Key);
            foreach (var e in _stale) Release(repo, e);
        }

        /// <summary>
        /// The first door mark on <paramref name="traj"/> from just behind <paramref name="progressS"/> to
        /// <see cref="LookAheadMetres"/> ahead, as the index of the terrain door it stands in; null when there is none.
        /// </summary>
        public static int? NextDoorAhead(in CustomTrajectory traj, float progressS, TerrainWorld terrain)
        {
            var wps = traj.Waypoints;
            for (int i = 0; i < wps.Length; i++)
            {
                var wp = wps[i];
                if (wp.CumulativeDistance < progressS - DoorMatchMetres) continue;
                if (wp.CumulativeDistance > progressS + LookAheadMetres) break;
                if (wp.Traversal != (byte)TraversalKind.Door) continue;
                int best = -1; float bestD = DoorMatchMetres;
                var at = new Vector2(wp.Position.X, wp.Position.Y);
                for (int d = 0; d < terrain.Doors.Count; d++)
                {
                    float dist = Vector2.Distance(at, terrain.Doors[d].Center);
                    if (dist <= bestD && System.MathF.Abs(wp.Position.Z - terrain.Doors[d].SillZ) < DoorRules.ReachHeightMetres) { best = d; bestD = dist; }
                }
                if (best >= 0) return best;
            }
            return null;
        }

        private void CannotPass(EntityRepository repo, Entity e, ref Passage p)
        {
            if (p.Replanned && p.SinceReplan < ReplanQuietSeconds) return;   // one request per door, until it lands
            if (!repo.HasComponent<NavigationIntent>(e) || !repo.HasComponent<NavigationStatus>(e)) return;
            var intent = repo.GetComponentRO<NavigationIntent>(e);
            var status = repo.GetComponent<NavigationStatus>(e);
            if (status.Result != NavResult.InProgress) return;
            float elapsed = repo.HasComponent<FrustrationTicks>(e) ? repo.GetComponentRO<FrustrationTicks>(e).ElapsedSinceFirstReplan : 0f;
            if (NavigationExecutionSystem.CanReplan(in intent, in status, elapsed))
            {
                NavigationExecutionSystem.RequestReplan(repo, e, in intent, ref status, repo.GetComponentRO<SimTransform>(e).Position);
                DoorReplans++;
            }
            else NavigationExecutionSystem.FailBlocked(repo, e, in intent, ref status);
            p.Replanned = true;
            p.SinceReplan = 0f;
            p.Sent = false; p.Elapsed = 0f; p.SinceSent = 0f;
        }

        private static void Hold(EntityRepository repo, Entity e, ref Passage p)
        {
            if (p.Holding) return;
            var nav = repo.GetComponent<NavState>(e);
            nav.IsBlocked = 1;
            repo.SetComponent(e, nav);
            p.Holding = true;
        }

        private static void Unhold(EntityRepository repo, Entity e, ref Passage p)
        {
            if (!p.Holding) return;
            if (repo.IsAlive(e) && repo.HasComponent<NavState>(e))
            {
                var nav = repo.GetComponent<NavState>(e);
                nav.IsBlocked = 0;
                repo.SetComponent(e, nav);
            }
            p.Holding = false;
        }

        private void Release(EntityRepository repo, Entity e)
        {
            if (!_passages.TryGetValue(e, out var p)) return;
            Unhold(repo, e, ref p);
            _passages.Remove(e);
        }

        private void ReleaseAll(EntityRepository repo)
        {
            if (_passages.Count == 0) return;
            _stale.Clear();
            _stale.AddRange(_passages.Keys);
            foreach (var e in _stale) Release(repo, e);
        }

        /// <summary>The door entity of terrain-object <paramref name="key"/> on this node (cached; re-read when it is gone).</summary>
        private Entity DoorEntity(EntityRepository repo, string key)
        {
            if (_doorEntities.TryGetValue(key, out var e) && repo.IsAlive(e) && repo.HasComponent<DoorState>(e)) return e;
            if (!repo.IsComponentTypeRegistered<DoorState>()) return Entity.Null;
            e = TerrainObjects.Find(repo, key);
            if (!e.IsNull && !repo.HasComponent<DoorState>(e)) e = Entity.Null;
            if (e.IsNull) _doorEntities.Remove(key); else _doorEntities[key] = e;
            return e;
        }
    }
}
