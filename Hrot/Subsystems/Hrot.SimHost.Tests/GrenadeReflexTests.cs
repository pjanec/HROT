using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
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
    /// ⭐⭐ <c>CE-2360</c> (AQ102 I, R-2072) — the grenade reflex: a live grenade lying among men makes the nearest trained one throw it back
    /// (fuze allowing), the others run clear or drop, each as a REACTION that pauses his task — under command or not — and gives it back after.
    /// 📄 docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md §3.3.
    /// </summary>
    public sealed class GrenadeReflexTests
    {
        private const long GrenadeAmmo = 9691, SoldierType = 9601;

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private readonly RecentSensesSystem _senses = new();
            private readonly ChannelArbitrationSystem _arbitration = new();
            private readonly GrenadeDangerSystem _danger = new();
            private readonly Fdp.Toolkit.Combat.Systems.RoundPickUpSystem _pick = new();
            private readonly Fdp.Interfaces.TkbTemplate _soldier;
            private double _time;
            private long _frame;

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                foreach (var reg in new System.Action[]
                {
                    () => { if (!Repo.IsComponentTypeRegistered<StanceIntent>()) Repo.RegisterComponent<StanceIntent>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponState>()) Repo.RegisterComponent<WeaponState>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponMountInfo>()) Repo.RegisterComponent<WeaponMountInfo>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<AmmoLoadout>()) Repo.RegisterComponent<AmmoLoadout>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponChannel>()) Repo.RegisterComponent<WeaponChannel>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<HeldFuze>()) Repo.RegisterComponent<HeldFuze>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<BallisticProjectile>()) Repo.RegisterComponent<BallisticProjectile>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<CombatProficiency>()) Repo.RegisterComponent<CombatProficiency>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<UnitSubordinate>()) Repo.RegisterComponent<UnitSubordinate>(); },
                }) reg();
                for (int i = 0; i < 5000; i++) Repo.Tick();
                if (!Repo.Bus.IsRegistered<Fdp.Toolkit.Perception.Events.SensorChangedEvent>()) Repo.Bus.Register<Fdp.Toolkit.Perception.Events.SensorChangedEvent>();
                if (!Repo.Bus.IsRegistered<PickUpRoundEvent>()) Repo.Bus.Register<PickUpRoundEvent>();
                if (!Repo.Bus.IsRegistered<PickUpGrantEvent>()) Repo.Bus.Register<PickUpGrantEvent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                _soldier = new Fdp.Interfaces.TkbTemplate("Soldier", SoldierType);
                _soldier.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
                db.Register(_soldier);
                var m67 = new Fdp.Interfaces.TkbTemplate("M67", GrenadeAmmo);
                m67.AddDescriptor(new WarheadDto { Kind = WarheadKind.Fragmentation, Indirect = true, FragmentRadiusM = 15f, Fuze = FuzeKind.Time, FuzeDelayS = 4.5f });
                m67.AddDescriptor(new HandItemDto());
                db.Register(m67);
                PersonHands.EnsureOnPersons(db);
                Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);
                Tick();
            }

            public Entity Man(Vector3 at, float? proficiency = null)
            {
                var m = Repo.CreateEntity();
                Repo.AddComponent(m, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(m, new BehaviorState());
                Repo.AddComponent(m, new LocomotionChannel());
                Repo.AddComponent(m, new WeaponChannel());
                Repo.AddComponent(m, new StanceIntent());
                Repo.AddComponent(m, new Health { Current = 100f, Max = 100f });
                Repo.AddComponent(m, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = SoldierType });
                Repo.SetAuthority<BehaviorState>(m, true);
                new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(Repo, m, _soldier);
                if (proficiency is float v) Repo.AddComponent(m, new CombatProficiency { Authored = v, Set = CombatProficiency.AuthoredSet });
                Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = m, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Superior });
                return m;
            }

            /// <summary>A live M67 LYING at <paramref name="at"/> with <paramref name="fuze"/> s left, thrown by <paramref name="thrower"/>.</summary>
            public Entity Grenade(Vector3 at, float fuze, Entity thrower)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new BallisticProjectile
                {
                    Ammo = GrenadeAmmo, Shooter = thrower, FuzeRemaining = fuze,
                    Warhead = (byte)(WarheadRound.Area | WarheadRound.Arc | WarheadRound.TimeFuze | WarheadRound.Landed),
                });
                return e;
            }

            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                _senses.Execute(Repo, 0.016f);
                _ingress.Execute(Repo, 0.016f);
                _danger.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                _arbitration.Execute(Repo, 0.016f);
                _pick.Execute(Repo, 0.016f);
            }

            public void Run(double seconds) { for (double t = 0; t < seconds; t += 0.016) Tick(); }

            public string? TaskOf(Entity m) => Registry.TryGetName(Repo.GetComponent<BehaviorState>(m).ActiveBehaviorHash, out var n) ? n : null;

            public unsafe Vector3 MoveTarget(Entity m)
            {
                ref readonly var loco = ref Repo.GetComponentRO<LocomotionChannel>(m);
                Assert.Equal(NavigationConstants.ActionIdMoveTo, loco.ActiveAction);
                fixed (byte* p = loco.Params) return ((MoveToParams*)p)->Destination;
            }
        }

        /// <summary>⭐ I-3 — the answer, pure: throw back when he may and the fuze allows it (reach + pick + throw + spare); else run when he
        /// gets clear (lethal + 2 m) before the burst; else flat. Outside the lethal radius he drops; an unknown fuze (a replica) never throws
        /// back and only runs a short way. ✅ Red-proofs: the fuze test dropped from the throw ⇒ a 1 s grenade is thrown back ⇒ red; the run's
        /// time test dropped ⇒ he runs into a 1 s grenade ⇒ red.</summary>
        [Fact]
        public void CE2360_TheAnswer_ThrowBackIfTheFuzeAllows_RunIfHeGetsClear_ElseFlat()
        {
            var g = new LiveGrenade(Entity.Null, Vector3.Zero, GrenadeAmmo, LethalMetres: 5f, FuzeLeft: 4f, From: new Vector3(0, 20, 0));
            Assert.Equal(GrenadeResponse.ThrowBack, GrenadeDanger.Choose(in g, 1.5f, mayThrowBack: true));
            Assert.Equal(GrenadeResponse.Run, GrenadeDanger.Choose(in g, 1.5f, mayThrowBack: false));
            Assert.Equal(GrenadeResponse.Prone, GrenadeDanger.Choose(in g, 8f, mayThrowBack: false));                    // outside its lethal radius
            var short1 = g with { FuzeLeft = 1f };
            Assert.Equal(GrenadeResponse.Prone, GrenadeDanger.Choose(in short1, 1.5f, mayThrowBack: true));               // no time for either
            var unknown = g with { FuzeLeft = -1f };
            Assert.Equal(GrenadeResponse.Run, GrenadeDanger.Choose(in unknown, 6f, mayThrowBack: true));                   // a step clear
            Assert.Equal(GrenadeResponse.Prone, GrenadeDanger.Choose(in unknown, 1f, mayThrowBack: true));                 // too far to run blind
            Assert.True(GrenadeDanger.Endangers(in g, 9.9f));
            Assert.False(GrenadeDanger.Endangers(in g, 10.1f));
        }

        /// <summary>
        /// ⭐⭐ I-2 + I-3 — three men around a live M67 (4 s left), each on an ordered task: the nearest (1.5 m, Regular) runs to it, picks it
        /// up and throws it back at its thrower from his hands; the man at 3 m runs straight away, past its lethal radius; the man at 8 m (outside
        /// it) drops flat. Each is a REACTION at Grenade urgency that paused his task, and the task resumes after the burst. ✅ Red-proofs: the
        /// system not registering reactions ⇒ no EvadeGrenade ⇒ red; every man allowed to throw back ⇒ the 3 m man goes for it ⇒ red.
        /// </summary>
        [Fact]
        public void CE2360_ALiveGrenadeAmongMen_TheNearestThrowsItBack_TheNextRuns_TheFarOneDrops_ThenTheyGoBackToTheirTask()
        {
            var w = new World();
            var thrower = w.Repo.CreateEntity();
            w.Repo.AddComponent(thrower, new SimTransform { Position = new Vector3(0, 20, 0), Rotation = Quaternion.Identity });
            var near = w.Man(new Vector3(1.5f, 0, 0));
            var mid = w.Man(new Vector3(-3f, 0, 0));
            var far = w.Man(new Vector3(0, -8f, 0));
            w.Run(0.1);
            Assert.All(new[] { near, mid, far }, m => Assert.Equal("Idle", w.TaskOf(m)));

            var round = w.Grenade(Vector3.Zero, 4f, thrower);
            w.Run(0.1);
            foreach (var m in new[] { near, mid, far })
            {
                Assert.Equal(BehaviorNames.EvadeGrenade, w.TaskOf(m));
                var task = w.Repo.GetComponent<BehaviorState>(m);
                Assert.Equal(BehaviorOrigin.Reaction, task.Origin);
                Assert.Equal(ReactionUrgency.Grenade, task.Urgency);
                Assert.Equal("Idle", BehaviorIngressSystem.PausedTaskOf(w.Repo, m)!.BehaviorName);
            }
            var run = w.MoveTarget(mid);
            Assert.True(run.X < -6.9f, $"he runs straight away from it, past its lethal radius: {run}");
            w.Run(0.6);
            Assert.Equal(StanceId.Prone, w.Repo.GetComponentRO<StanceIntent>(far).TargetStance);                       // outside it: flat

            // the throw: the first tick his weapon channel carries a FireAtPoint (the executor would take it from there)
            Vector3? throwAt = null;
            int throwMount = -1;
            for (int i = 0; i < 40 && throwAt == null; i++)
            {
                w.Tick();
                unsafe
                {
                    ref readonly var weapon = ref w.Repo.GetComponentRO<WeaponChannel>(near);
                    if (weapon.ActiveAction != CombatConstants.ActionIdFireAtPoint) continue;
                    fixed (byte* p = weapon.Params)
                    {
                        throwAt = ((Fdp.Toolkit.Combat.Executors.FireAtPointParams*)p)->Point;
                        throwMount = ((Fdp.Toolkit.Combat.Executors.FireAtPointParams*)p)->Mount;
                    }
                }
            }
            Assert.False(w.Repo.IsAlive(round), "the nearest man picked it up (the owner granted it)");
            Assert.True(throwAt.HasValue, $"he throws it from his hands (holding a live round: {PersonHands.HoldsLethal(w.Repo, near)})");
            Assert.True(Vector3.Distance(throwAt!.Value, new Vector3(0, 20, 0)) < 0.1f, $"thrown back at its thrower: {throwAt}");
            Assert.Equal(PersonHands.MountIndex(w.Repo, near), throwMount);

            w.Run(EvadeGrenadeBehavior.AfterBurstSeconds + 4.5);                                                         // it is gone; after its burst
            Assert.All(new[] { near, mid, far }, m => Assert.Equal("Idle", w.TaskOf(m)));                                // each back to his task
        }

        /// <summary>⭐ I-1 (measured live, <c>rf-grenade-reaction</c>) — on the BRAIN's node a thrown grenade is only a lying REPLICA
        /// (<c>LightweightRound</c>, flown by the thrower's body node, no fuze here): the men react all the same — its fuze estimated from its
        /// warhead less the time since it appeared, its thrower where it appeared. ✅ Red-proof: the replica pass dropped from
        /// <c>GrenadeDanger.Lying</c> ⇒ nobody reacts ⇒ red (as live).</summary>
        [Fact]
        public void CE2360_AGrenadeSeenOnlyAsAReplica_IsReactedTo_ItsFuzeEstimated()
        {
            var w = new World();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>())
                w.Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.LightweightRound>();
            var runner = w.Man(new Vector3(-3f, 0, 0), ProficiencyOf.Militia);   // he may not throw it back: he runs
            var far = w.Man(new Vector3(0, -8f, 0));
            w.Run(0.1);

            var replica = w.Repo.CreateEntity();   // appears at the thrower's hand, flies, lies by the men
            w.Repo.AddComponent(replica, new SimTransform { Position = new Vector3(0, 20, 0), Rotation = Quaternion.Identity });
            w.Repo.AddComponent(replica, new Fdp.Toolkit.Replication.Components.LightweightRound());
            w.Repo.AddComponent(replica, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = GrenadeAmmo });
            w.Run(0.2);
            w.Repo.GetComponentRW<SimTransform>(replica).Position = Vector3.Zero;
            w.Run(0.2);

            Assert.Equal(BehaviorNames.EvadeGrenade, w.TaskOf(runner));
            Assert.Equal(BehaviorNames.EvadeGrenade, w.TaskOf(far));
            Assert.True(w.MoveTarget(runner).X < -6.9f, "he runs straight away from it");
        }

        /// <summary>⭐ J + R-2062 — a MILITIAMAN never throws it back (he runs); and a man UNDER COMMAND reacts all the same — the reflex is no SOP
        /// row, so a live commander does not stand it aside. ✅ Red-proof: the proficiency test dropped from <c>MayThrowBack</c> ⇒ the militiaman
        /// goes for it ⇒ red.</summary>
        [Fact]
        public void CE2360_AMilitiamanRuns_NotThrowsBack_AndAManUnderCommandReactsAllTheSame()
        {
            var w = new World();
            var thrower = w.Repo.CreateEntity();
            w.Repo.AddComponent(thrower, new SimTransform { Position = new Vector3(0, 20, 0), Rotation = Quaternion.Identity });
            var militia = w.Man(new Vector3(1.5f, 0, 0), ProficiencyOf.Militia);
            var commanded = w.Man(new Vector3(-2f, 0, 0));
            var leader = w.Repo.CreateEntity();
            w.Repo.AddComponent(commanded, new UnitSubordinate { Commander = leader });
            w.Run(0.1);
            Assert.True(Fdp.Toolkit.Behavior.SopConditions.UnderCommand(commanded, w.Repo));

            w.Grenade(Vector3.Zero, 4f, thrower);
            w.Run(0.1);
            Assert.Equal(BehaviorNames.EvadeGrenade, w.TaskOf(militia));
            Assert.Equal(BehaviorNames.EvadeGrenade, w.TaskOf(commanded));
            Assert.True(w.MoveTarget(militia).X > 6.9f, "the militiaman runs away from it, he does not go for it");
        }
    }
}
