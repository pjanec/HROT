using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2235</c> (civilians CIV-1, CV-3; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — the SHIPPED <c>CivilianHsm</c> from
    /// the production registry, through the real ingress and brain tick: he starts in his routine and walks about his home; fear past
    /// <c>AlarmAt</c> stops and crouches him; calm again, he goes back to his routine; wounded — from his routine or his alarm — he lies
    /// down and stays a casualty however frightened he becomes. And the new civilian types start it by default.
    /// </summary>
    public sealed class CivilianHsmTests
    {
        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Unit;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private double _time;
            private long _frame;

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                if (!Repo.IsComponentTypeRegistered<StanceIntent>()) Repo.RegisterComponent<StanceIntent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new SimTransform { Position = new Vector3(100, 200, 0), Rotation = Quaternion.Identity });
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.AddComponent(Unit, new LocomotionChannel());
                Repo.AddComponent(Unit, new StanceIntent());
                Repo.AddComponent(Unit, new Health { Current = 100f, Max = 100f });
                // ⭐ the type's injury band, as InjuryTkbTranslator stamps it on every node (a civilian's InjuryDefDto defaults)
                if (!Repo.IsComponentTypeRegistered<InjuryThresholds>()) Repo.RegisterComponent<InjuryThresholds>();
                Repo.AddComponent(Unit, new InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
                Tick();
            }

            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                Repo.FlushCommandBuffers();
            }

            public void Start(string json = "{}")
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = BehaviorNames.CivilianHsm, JsonParams = json, Origin = BehaviorOrigin.Superior });

            public unsafe string State()
            {
                Assert.True(Registry.TryGetId(BehaviorNames.CivilianHsm, out int id));
                Assert.True(Registry.TryGetDefinition(id, out var def));
                if (!RootHsmAccess.TryGetInstance(Repo, Unit, out byte* ptr, out int size)) return "-";
                ushort* leaves = Fhsm.Kernel.HsmKernel.GetActiveLeafIds(ptr, size, out int count);
                return count == 0 || leaves[0] == 0xFFFF ? "-" : def!.HsmMetadata?.GetStateName(leaves[0]) ?? $"State_{leaves[0]}";
            }

            public CivilianMode Mode => CivilianMind.ModeOf(Repo, Unit);
            public void Fear(float fear) => Repo.GetComponentRW<CivilianState>(Unit).Fear = fear;
            public ref readonly LocomotionChannel Loco => ref Repo.GetComponentRO<LocomotionChannel>(Unit);
            public StanceId Stance => Repo.GetComponentRO<StanceIntent>(Unit).TargetStance;
            public void Health(float current) => Repo.GetComponentRW<Health>(Unit).Current = current;
        }

        [Fact]
        public void CE2235_HeStartsInHisRoutine_AndWalksToASpotNearHome()
        {
            var w = new World();
            w.Start("{\"WanderRadius\":8}");
            for (int i = 0; i < 4; i++) w.Tick();

            Assert.Equal("Routine", w.State());
            Assert.Equal(CivilianMode.Routine, w.Mode);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
            ref readonly var mind = ref w.Repo.GetComponentRO<CivilianState>(w.Unit);
            Assert.Equal(new Vector3(100, 200, 0), mind.Home);
        }

        [Fact]
        public void CE2235_Frightened_HeStopsAndCrouches_Calm_HeGoesBackToHisRoutine()
        {
            var w = new World();
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Routine", w.State());

            w.Fear(CivilianBehavior.DefaultAlarmAt - 0.01f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Routine", w.State());   // just under the threshold: not yet

            w.Fear(CivilianBehavior.DefaultAlarmAt);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());
            Assert.Equal(CivilianMode.Alarmed, w.Mode);
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
            Assert.Equal(StanceId.Crouched, w.Stance);

            w.Fear(CivilianBehavior.DefaultCalmBelow);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());   // at the calm line: still alarmed

            w.Fear(0f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Routine", w.State());
            Assert.Equal(StanceId.Standing, w.Stance);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);   // walking again
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CE2235_Wounded_HeLiesDown_AndStaysACasualtyHoweverFrightened(bool alarmedFirst)
        {
            var w = new World();
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();
            if (alarmedFirst)
            {
                w.Fear(CivilianBehavior.DefaultAlarmAt);   // ⭐ CE-2236 — alarmed, not panicked (fear below PanicAt)
                for (int i = 0; i < 3; i++) w.Tick();
                Assert.Equal("Alarmed", w.State());
            }

            w.Health(40f);   // below the default WoundedBelow (0.6)
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Casualty", w.State());
            Assert.Equal(CivilianMode.Casualty, w.Mode);
            Assert.Equal(StanceId.Prone, w.Stance);
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);

            w.Fear(1f);
            for (int i = 0; i < 5; i++) w.Tick();
            w.Fear(0f);
            for (int i = 0; i < 5; i++) w.Tick();
            Assert.Equal("Casualty", w.State());
        }

        /// <summary>⭐ <c>CE-2236</c> (§5e) — terrified with no building in reach (this world has no terrain, so the shelter query never
        /// answers), he panics, waits for the answer, then cowers where he is: crouched, cowering on every map. His fear falling below
        /// panic ends the cowering, back to alarmed. Wounded while cowering, he is a casualty.</summary>
        [Fact]
        public void CE2236_PanickedWithNoShelter_HeCowers_UntilHisFearFallsBelowPanic()
        {
            var w = new World();
            Assert.True(w.Repo.IsComponentTypeRegistered<EffectState>(), "the cowering flag needs EffectState on this node");
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();

            w.Fear(1f);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("Panic", w.State());
            Assert.Equal(CivilianMode.Panic, w.Mode);
            Assert.Equal(StanceId.Standing, w.Stance);

            int waited = 0;
            while (w.State() == "Panic" && waited < 400) { w.Tick(); waited++; }
            Assert.Equal("Cower", w.State());
            Assert.InRange(waited * 0.016, CivilianBehavior.ShelterWaitSeconds - 0.2, CivilianBehavior.ShelterWaitSeconds + 0.2);
            Assert.Equal(CivilianMode.Cower, w.Mode);
            Assert.Equal(StanceId.Crouched, w.Stance);
            Assert.True(Effects.IsCowering(w.Repo, w.Unit));
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);

            w.Fear(CivilianBehavior.DefaultPanicAt);
            for (int i = 0; i < 5; i++) w.Tick();
            Assert.Equal("Cower", w.State());   // at the panic line: still cowering

            w.Fear(CivilianBehavior.DefaultPanicAt - 0.05f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());
            Assert.False(Effects.IsCowering(w.Repo, w.Unit), "the cowering ends with the state");

            w.Fear(1f);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("Panic", w.State());   // a second panic starts afresh: waits again
            for (int i = 0; i < 400 && w.State() == "Panic"; i++) w.Tick();
            Assert.Equal("Cower", w.State());
            w.Health(40f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Casualty", w.State());
            Assert.False(Effects.IsCowering(w.Repo, w.Unit));
            Assert.Equal(StanceId.Prone, w.Stance);
        }

        /// <summary>⭐ <c>CE-2236</c> (R-4035) — inside, he hides for a time drawn from [HideMin, HideMax], then deletes himself: ONE
        /// <c>DestroyEntityCommand</c> naming his network id, not one per frame.</summary>
        [Fact]
        public void CE2236_Inside_HeHidesHisTime_ThenDeletesHimselfOnce()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4242));
            var p = CivilianBehavior.Defaults();
            p.HideMinSeconds = 1f;
            p.HideMaxSeconds = 1.5f;
            var ws = new CivilianBehavior.PanicState();
            int destroys = 0;
            double start = w.Repo.SimulationTime, deletedAt = 0;
            for (int i = 0; i < 150; i++)
            {
                CivilianBehavior.Inside(ref p, ref ws, w.Unit, w.Repo);
                w.Tick();
                foreach (var c in w.Repo.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>())
                {
                    Assert.Equal(4242L, c.NetworkId);
                    destroys++;
                    deletedAt = w.Repo.SimulationTime;
                }
            }
            Assert.Equal(1, destroys);
            Assert.InRange(deletedAt - start, 1.0, 1.5 + 0.05);
            Assert.Equal(CivilianMode.Inside, w.Mode);
        }

        [Fact]
        public void CE2235_TheCivilianTypesStartCivilianHsm_ByDefault()
        {
            var tkb = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Core.Tkb.CivilianTkbCatalog.RegisterAll(tkb);
            int want = BehaviorHash.FromName(BehaviorNames.CivilianHsm);
            foreach (int type in new[] { Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultMale, Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultFemale,
                                         Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianChild, Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianElderly })
            {
                Assert.True(tkb.TryGetByType(type, out var t));
                Assert.Equal(want, t!.GetDescriptor<BehaviorProfileDto>()!.DefaultBehaviorHash);
            }
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            Assert.True(registry.TryGetId(BehaviorNames.CivilianHsm, out int id));
            Assert.Equal(want, id);
        }
    }
}
