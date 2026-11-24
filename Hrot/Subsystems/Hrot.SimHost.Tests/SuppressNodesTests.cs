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

        /// <summary>
        /// ⭐⭐ <c>CE-8314</c> — the squad-ordered suppressor uses D-15's ONE placement (<c>PostureNodes.PlaceSuppressor</c>): where he holds, the
        /// lowest stance whose round reaches the man he suppresses — open ground ⇒ prone; a chest-high (1.2 m) wall 2 m in front ⇒ standing over it;
        /// and the same for a man who never saw him, aimed at the squad's reported point. 📐 live pc-corridor-fight: Blue Squad 3's covering men
        /// crouched in the open (no stance rule). 🔴 Red-proof: drop the placement call ⇒ no stance asked ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(0f, true, Fdp.Toolkit.Tkb.Domain.StanceId.Prone)]
        [InlineData(1.2f, true, Fdp.Toolkit.Tkb.Domain.StanceId.Standing)]
        [InlineData(0f, false, Fdp.Toolkit.Tkb.Domain.StanceId.Prone)]
        [InlineData(1.2f, false, Fdp.Toolkit.Tkb.Domain.StanceId.Standing)]
        public void CE8314_AnOrderedSuppressor_TakesTheLowestStanceThatReachesHim(float wall, bool remembers, Fdp.Toolkit.Tkb.Domain.StanceId expected)
        {
            using var c = new Coverer(remembers, sees: false);
            string feature = wall <= 0f ? "" : $$"""
                { "type": "Feature", "properties": { "kind": "wall", "height": {{wall.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "material": "concrete", "thickness": 0.3 },
                  "geometry": { "type": "LineString", "coordinates": [[102, 95], [102, 105]] } }
                """;
            c.Repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse($$"""
                { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 }, "features": [ {{feature}} ] }
                """, "stance-range"));
            // he holds at (100, 100), the enemy 30 m east of him (the fixture's EnemyAt moved into the range)
            c.Repo.AddComponent(c.Self, new SimTransform { Position = new Vector3(100f, 100f, 0f), Rotation = Quaternion.Identity });
            c.Repo.AddComponent(c.Self, new Hrot.MuscleCharacter.Animation.Components.StanceIntent());
            var enemyAt = new Vector3(130f, 100f, 0f);
            c.Repo.GetComponentRW<SimTransform>(c.Enemy).Position = enemyAt;
            if (remembers)
            {
                ref var mem = ref c.Repo.GetComponentRW<TargetMemory>(c.Self);
                mem.PositionsX[0] = enemyAt.X; mem.PositionsY[0] = enemyAt.Y; mem.PositionsZ[0] = 0f;
            }
            c.P.Point = enemyAt;
            Assert.Equal(NodeStatus.Running, c.Tick());
            Assert.Equal(expected, c.Repo.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(c.Self).TargetStance);
            Assert.NotEqual(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, c.Repo.GetComponentRO<LocomotionChannel>(c.Self).ActiveAction);   // reach 0: he holds
        }

        /// <summary>
        /// ⭐⭐ <c>CE-8319</c> — a round at the squad's REPORTED point (a man who never saw him) goes at that point ON THE GROUND: a reported post is 2-D
        /// (Z 0), and on ground 50 m up the round flew at a point 50 m under it and "expired" (📐 village-street, ground ≈ 176 m: 220 of 262 rounds).
        /// 🔴 Red-proof: aim the point as given ⇒ Z 0 ⇒ red.
        /// </summary>
        [Fact]
        public void CE8319_ARoundAtTheReportedPoint_IsAimedOnTheGround_NotUnderIt()
        {
            using var c = new Coverer(remembers: false, sees: false);
            c.Repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
                { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 50 }, "features": [] }
                """, "raised"));
            c.Repo.AddComponent(c.Self, new SimTransform { Position = new Vector3(100f, 100f, 50f), Rotation = Quaternion.Identity });
            c.P.Point = new Vector3(130f, 100f, 0f);   // 2-D, as the squad reports it
            Assert.Equal(NodeStatus.Running, c.Tick());
            Assert.Equal(CombatConstants.ActionIdFireAtPoint, c.Channel.ActiveAction);
            var fire = Unsafe.As<byte, FireAtPointParams>(ref c.Channel.Params[0]);
            Assert.InRange(fire.Point.Z, 50f, 52f);   // on the ground + the blind aim height
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
