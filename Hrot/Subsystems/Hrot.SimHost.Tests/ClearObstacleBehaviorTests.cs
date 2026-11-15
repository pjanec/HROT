using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-7012</c> (City Battle §11 V-2) — <see cref="ClearObstacleBehavior.Clear"/>: the bulldozer drives to the blocker's nearest face, pushes it
    /// through CE-6003's seam (<see cref="ObstacleDamage.Damage"/>) while the blade is on it, and is done when the obstacle's owner has removed it.
    /// The body is faked as in <see cref="ShelterFromAirNodesTests"/> (a MoveTo arrives at its destination); the obstacle's owner runs the real
    /// <see cref="HealthApplicationSystem"/> + <see cref="ObstacleRemovalSystem"/>.
    /// </summary>
    public sealed unsafe class ClearObstacleBehaviorTests : System.IDisposable
    {
        private const float ConcreteHp = 600f, Dt = 0.1f;
        private readonly EntityRepository _w = new();
        private readonly HealthApplicationSystem _apply = new();
        private readonly ObstacleRemovalSystem _remove = new();
        private readonly Entity _dozer;
        private readonly List<Vector3> _moves = new();
        private uint _moveInst;
        private double _time;
        private long _frame;

        public ClearObstacleBehaviorTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            if (!_w.IsComponentTypeRegistered<StaticObstacle>()) _w.RegisterComponent<StaticObstacle>();
            if (!_w.IsComponentTypeRegistered<ObstacleShape>()) _w.RegisterComponent<ObstacleShape>();
            if (!_w.IsComponentTypeRegistered<Health>()) _w.RegisterComponent<Health>();
            if (!_w.Bus.IsRegistered<DamageAssessedEvent>()) _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterManagedEvent<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>();
            _dozer = _w.CreateEntity();
            _w.AddComponent(_dozer, new SimTransform { Position = new Vector3(-40f, 0f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(_dozer, new BehaviorState());
            _w.AddComponent(_dozer, new LocomotionChannel());
        }

        public void Dispose() => _w.Dispose();

        /// <summary>A concrete roadblock (4 × 1 × 1 m, its length across the street) at the origin.</summary>
        private Entity Roadblock(float hp = ConcreteHp)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f) });
            _w.AddComponent(e, new StaticObstacle { Material = new FixedString32("concrete") });
            _w.AddComponent(e, new ObstacleShape { Length = 4f, Width = 1f, Height = 1f });
            _w.AddComponent(e, new Health { Current = hp, Max = hp });
            return e;
        }

        /// <summary>One frame: the dozer's tick, its body, then the obstacle owner's (the damage applied, the removal played back).</summary>
        private NodeStatus Step(ref ClearObstacleParams p)
        {
            _w.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = Dt, TimeScale = 1f, FrameNumber = ++_frame });
            _w.SetSimulationTime((float)_time);
            var status = ClearObstacleBehavior.Clear(ref p, _dozer, _w);
            _w.FlushCommandBuffers();
            Body();
            _w.Bus.SwapBuffers();
            _apply.Execute(_w, Dt);
            _remove.Execute(_w, Dt);
            ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
            _time += Dt;
            return status;
        }

        /// <summary>The fake hull: a new MoveTo is noted; the next frame it has driven there and arrived.</summary>
        private void Body()
        {
            ref var loco = ref _w.GetComponentRW<LocomotionChannel>(_dozer);
            if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo || loco.Status != NodeStatus.Running) return;
            if (loco.ActionInstanceId != _moveInst)
            {
                _moveInst = loco.ActionInstanceId;
                fixed (byte* src = loco.Params) _moves.Add(((MoveToParams*)src)->Destination);
                return;
            }
            _w.GetComponentRW<SimTransform>(_dozer).Position = _moves[^1];
            loco.Status = NodeStatus.Success;
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE7012_R1</c> — DRIVE, PUSH, GONE: from 40 m the dozer drives to <c>StandOffMetres</c> in front of the block's nearest face (not its
        /// centre), pushes nothing on the way, then pushes <c>Max / PushSeconds × dt</c> a tick until the owner removes it — 600 hp in 10 s of contact,
        /// whatever its health — and succeeds. 🔴 Red-proof: push <c>Current</c> instead of <c>Max</c> per second (a decaying push) ⇒ the block outlives
        /// the 10 s ⇒ red.
        /// </summary>
        [Fact]
        public void CE7012_R1_TheDozer_DrivesToTheFace_PushesForTenSeconds_TheBlockIsGone()
        {
            var block = Roadblock();
            var p = new ClearObstacleParams { Obstacle = block, Speed = 4f, PushSeconds = ClearObstacleBehavior.DefaultPushSeconds };

            Assert.Equal(NodeStatus.Running, Step(ref p));
            var to = Assert.Single(_moves);
            Assert.Equal(-0.5f - ClearObstacleBehavior.StandOffMetres, to.X, 2);   // the face is at x = −0.5 (the block's width across x)
            Assert.Equal(0f, to.Y, 2);
            Assert.Equal(0f, p.Pushed);

            int pushTicks = 0;
            NodeStatus status = NodeStatus.Running;
            for (int i = 0; i < 200 && status == NodeStatus.Running; i++)
            {
                float before = p.Pushed;
                status = Step(ref p);
                if (p.Pushed > before) pushTicks++;
            }
            Assert.Equal(NodeStatus.Success, status);
            Assert.False(_w.IsAlive(block));
            Assert.InRange(pushTicks, 100, 101);                                        // 600 / (60 hp/s × 0.1 s) = 100 ticks = 10 s
            Assert.InRange(p.Pushed, ConcreteHp, ConcreteHp + 6.01f);
            Assert.Single(_moves);                                                      // it arrived: no second drive
        }

        /// <summary>⭐ <c>CE7012_R2</c> — the rate is per OBSTACLE: a crate (60 hp) also goes in 10 s of contact (the handoff's "any obstacle in 10 s").
        /// And a non-obstacle is refused: a man with health is not pushed (Failure, nothing sent).</summary>
        [Fact]
        public void CE7012_R2_AnyObstacle_TenSeconds_AManIsNotAnObstacle()
        {
            var crate = Roadblock(60f);
            _w.GetComponentRW<SimTransform>(_dozer).Position = new Vector3(-4f, 0f, 0f);   // blade already on it
            var p = new ClearObstacleParams { Obstacle = crate, Speed = 4f, PushSeconds = 10f };
            int ticks = 0;
            while (Step(ref p) == NodeStatus.Running && ticks < 200) ticks++;
            Assert.False(_w.IsAlive(crate));
            Assert.InRange(ticks, 100, 101);
            Assert.Empty(_moves);

            var man = _w.CreateEntity();
            _w.AddComponent(man, new SimTransform { Position = new Vector3(-2f, 0f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(man, new Health { Current = 100f, Max = 100f });
            var q = new ClearObstacleParams { Obstacle = man, Speed = 4f, PushSeconds = 10f };
            Assert.Equal(NodeStatus.Failure, Step(ref q));
            Assert.Equal(0f, q.Pushed);
            Assert.Equal(100f, _w.GetComponentRO<Health>(man).Current);
        }
    
        // ── ⭐ CE-4601 (B-4; docs/DESIGN_Vehicle_Breaching.md §7.6) — the point order ──────────────────────────────────────────

        /// <summary>A field with a 1.8 m brick garden wall along x = 0 (y −20..20) and a lone oak at (0, 30).</summary>
        private const string Field = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-60,-60,60,60],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"wall","label":"Garden","height":1.8,"thickness":0.3,"material":"brick"},"geometry":{"type":"LineString","coordinates":[[0,-20],[0,20]]}},
             {"type":"Feature","properties":{"kind":"tree","label":"oak"},"geometry":{"type":"Point","coordinates":[0,30]}}]}
            """;

        private TerrainWorld Terrain()
        {
            var t = TerrainWorldParser.Parse(Field, "range");
            _w.SetSingletonManaged(t);
            return t;
        }

        /// <summary>One frame of the point order with the Muscle's cut and a stand-in for the record writers (each event written straight into a record entity).</summary>
        private NodeStatus CutStep(ref ClearObstacleParams p, DozerCutSystem cut, TerrainWorld t)
        {
            var status = Step(ref p);
            cut.Execute(_w, Dt);
            _w.Bus.SwapBuffers();
            foreach (var ev in _w.Bus.Read<BuildingDamageEvent>().ToArray())
            {
                var d = new BuildingDamage { Building = ev.Building };
                BuildingDamages.Add(ref d, ev.Breach, t);
                var e = _w.CreateEntity(); _w.AddComponent(e, d); _w.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(t, ev.Building) });
            }
            foreach (var ev in _w.Bus.Read<TreeFellEvent>().ToArray())
            {
                var d = new TreeDamage(); (d.CellX, d.CellY) = TreeDamages.CellOf((ev.X, ev.Y)); TreeDamages.Add(ref d, (ev.X, ev.Y), out _);
                var e = _w.CreateEntity(); _w.AddComponent(e, d);
            }
            return status;
        }

        private ClearObstacleParams PointOrder(Vector3 at)
        {
            var block = new ClearObstacleBehavior.ClearObstacleBlackboard();
            ClearObstacleBehavior.Resolve(new ClearObstacleOrder { At = at, PushSeconds = 3f }, ref block, _w, _dozer);
            return block.Clear;
        }

        /// <summary>
        /// ⭐⭐ B-4 — "clear HERE" on a garden wall: the resolver names the WALL, the dozer drives to the point, marks its blade (<see cref="BladeCut"/>), the Muscle cuts a
        /// blade-wide full-height Passage after the push (the one breach writer's event), the order succeeds and the mark is cleared. 🔴 Red-proof: drop the
        /// <c>Mark</c> call ⇒ nothing is cut and the order times out.
        /// </summary>
        [Fact]
        public void CE4601_APointOnAWall_DrivesThere_MarksTheBlade_TheMuscleCutsAPassage_AndTheOrderSucceeds()
        {
            var t = Terrain();
            var p = PointOrder(new Vector3(0f, 1f, 0f));   // ⚠ not the origin: a zero At means "no point" (the order DTO's zero-default rule)
            Assert.Equal(BladeCutTarget.Wall, p.Target);
            var cut = new DozerCutSystem();
            NodeStatus status = NodeStatus.Running;
            bool marked = false;
            for (int i = 0; i < 200 && status == NodeStatus.Running; i++)
            {
                status = CutStep(ref p, cut, t);
                marked |= _w.HasComponent<BladeCut>(_dozer) && _w.GetComponentRO<BladeCut>(_dozer).Active == 1;
            }
            Assert.Equal(NodeStatus.Success, status);
            Assert.True(marked);
            Assert.Equal(1, cut.Cuts);
            Assert.Equal(0, _w.GetComponentRO<BladeCut>(_dozer).Active);
            Assert.False(t.SegmentBlocked(new Vector3(-5f, 1f, 1f), new Vector3(5f, 1f, 1f), DoorStates.Of(_w, t)));   // the gap is open
            Assert.True(t.SegmentBlocked(new Vector3(-5f, 10f, 1f), new Vector3(5f, 10f, 1f), DoorStates.Of(_w, t)));   // the rest of the wall stands
        }

        /// <summary>⭐ B-4 — a point on the lone oak fells it (the trunk wins over nothing); a point on open ground fails at once; a point beside a roadblock is that roadblock.</summary>
        [Fact]
        public void CE4601_APointOnATrunk_FellsIt_OpenGroundFails_AndAPointByARoadblockClearsTheRoadblock()
        {
            var t = Terrain();
            var p = PointOrder(new Vector3(0.5f, 30.5f, 0f));
            Assert.Equal(BladeCutTarget.Tree, p.Target);
            var cut = new DozerCutSystem();
            NodeStatus status = NodeStatus.Running;
            for (int i = 0; i < 200 && status == NodeStatus.Running; i++) status = CutStep(ref p, cut, t);
            Assert.Equal(NodeStatus.Success, status);
            Assert.True(DoorStates.Of(_w, t).Trees!.Count == 1);

            var none = PointOrder(new Vector3(30f, -40f, 0f));
            Assert.Equal(BladeCutTarget.None, none.Target);
            Assert.Equal(NodeStatus.Failure, ClearObstacleBehavior.Clear(ref none, _dozer, _w));

            var block = Roadblock();
            var at = PointOrder(new Vector3(1.5f, 0f, 0f));
            Assert.Equal(block, at.Obstacle);
            Assert.Equal(BladeCutTarget.None, at.Target);
        }
    }
}
