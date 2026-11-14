using System.Collections.Generic;
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
    }
}
