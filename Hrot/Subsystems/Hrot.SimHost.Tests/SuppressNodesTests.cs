using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-b, Squad Tactics §7.5) — the library <c>Suppress</c>: a covering soldier puts rounds on a NAMED contact
    /// whether or not he can see him. 🔴 Red before: no order made a soldier fire — a <c>Hold</c> fires only when the soldier's own
    /// posture decision picks Suppress and he sees or remembers a target (§7.5.1).
    /// </summary>
    public sealed unsafe class SuppressNodesTests
    {
        private static readonly Vector3 EnemyAt = new(30f, 0f, 0f);

        private sealed class Coverer : IDisposable
        {
            public readonly EntityRepository Repo = new();
            public readonly Entity Self, Enemy;
            public SuppressContactParams P;
            public SuppressContactState Ws;

            public void Dispose() => Repo.Dispose();

            public Coverer(bool remembers, bool sees)
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                if (!Repo.IsComponentTypeRegistered<ActiveSensorTracks>()) Repo.RegisterComponent<ActiveSensorTracks>();
                Self = Repo.CreateEntity();
                Repo.AddComponent(Self, new BehaviorState());
                Repo.AddComponent(Self, new LocomotionChannel());
                Repo.AddComponent(Self, new WeaponChannel());
                Repo.AddComponent(Self, new WeaponState { Ammo = 150, MaxAmmo = 150, MagazineSize = 30, MagazineRounds = 30, ReloadSeconds = 3f });
                Repo.AddComponent(Self, new ActiveSensorTracks());
                Repo.AddComponent(Self, new TargetMemory());
                Enemy = Repo.CreateEntity();
                Repo.AddComponent(Enemy, new SimTransform { Position = EnemyAt, Rotation = Quaternion.Identity });
                if (remembers)
                {
                    ref var mem = ref Repo.GetComponentRW<TargetMemory>(Self);
                    mem.EntityIds[0] = (long)Enemy.PackedValue;
                    mem.Freshness[0] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                    mem.Modalities[0] = (byte)SensorModality.Visual;
                    mem.PositionsX[0] = EnemyAt.X; mem.PositionsY[0] = EnemyAt.Y; mem.PositionsZ[0] = EnemyAt.Z;
                    mem.Count = 1;
                }
                if (sees)
                {
                    ref var t = ref Repo.GetComponentRW<ActiveSensorTracks>(Self);
                    t.EntityIds[0] = (long)Enemy.PackedValue;
                    t.Modalities[0] = (byte)SensorModality.Visual;
                    t.Count = 1;
                }
                Repo.SetSingleton(new GlobalTime { TotalTime = 1, DeltaTime = 0.1f, TimeScale = 1f, FrameNumber = 1 });
                P = new SuppressContactParams { Target = Enemy, Point = EnemyAt + new Vector3(2, 0, 0), HasPoint = 1, CooldownSeconds = 1f };
            }

            public NodeStatus Tick() => SuppressNodes.SuppressContact(ref P, ref Ws, Self, Repo);
            public ref WeaponChannel Channel => ref Repo.GetComponentRW<WeaponChannel>(Self);
        }

        [Fact]
        public void CE2152_ASuppressorWhoSeesHim_FiresAimedAtHim()
        {
            using var c = new Coverer(remembers: true, sees: true);
            Assert.Equal(NodeStatus.Running, c.Tick());
            Assert.Equal(CombatConstants.ActionIdAimAndFire, c.Channel.ActiveAction);
            var fire = Unsafe.As<byte, AimAndFireParams>(ref c.Channel.Params[0]);
            Assert.Equal(c.Enemy, fire.Target);
            Assert.Equal(1, fire.BlindFire);   // the executor fires aimed while seen, blind at the memory when he hides
        }

        [Fact]
        public void CE2152_ASuppressorWhoRemembersButCannotSeeHim_StillFiresAtHim_Blind()
        {
            using var c = new Coverer(remembers: true, sees: false);
            Assert.Equal(NodeStatus.Running, c.Tick());
            Assert.Equal(CombatConstants.ActionIdAimAndFire, c.Channel.ActiveAction);
            var fire = Unsafe.As<byte, AimAndFireParams>(ref c.Channel.Params[0]);
            Assert.Equal(c.Enemy, fire.Target);
            Assert.Equal(1, fire.BlindFire);
        }

        [Fact]
        public void CE2152_ASuppressorWhoNeverSawHim_FiresAtTheSquadsReportedPoint()
        {
            using var c = new Coverer(remembers: false, sees: false);
            Assert.Equal(NodeStatus.Running, c.Tick());
            Assert.Equal(CombatConstants.ActionIdFireAtPoint, c.Channel.ActiveAction);
            var fire = Unsafe.As<byte, FireAtPointParams>(ref c.Channel.Params[0]);
            Assert.Equal(c.P.Point, fire.Point);
            uint started = c.Channel.ActionInstanceId;
            c.Tick();
            Assert.Equal(started, c.Channel.ActionInstanceId);   // one action, not restarted every tick
        }

        [Fact]
        public void CE2152_WhenHeIsDown_TheSuppressorStops()
        {
            using var c = new Coverer(remembers: false, sees: false);
            c.Tick();
            c.Repo.AddComponent(c.Enemy, new Health { Current = 0f, Max = 100f });
            c.Tick();
            Assert.Equal(0, c.Channel.ActiveAction);
        }
    }
}
