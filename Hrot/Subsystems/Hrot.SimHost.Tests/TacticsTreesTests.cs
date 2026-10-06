using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2092</c> / <c>CE-2093</c> — the SHIPPED <c>TakeCover</c> / <c>FallBack</c> trees
    /// (<c>Assets/BTrees/Tactics/</c>) run from the PRODUCTION registry through the real ingress and brain: an order starts
    /// the tree, the tree's node owns a sensor pointed at the threat, an answer moves the unit, and the run ends when
    /// nothing is left to hide from. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §2 (R-204).
    /// </summary>
    public sealed class TacticsTreesTests
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
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                Fdp.Toolkit.Utility.StandardInputs.RegisterAll();   // ⭐ CE-2073 — as CgfLogicPack's input scan does in production
                _ingress = new BehaviorIngressSystem(Registry);
                _brain   = new BrainTickSystem(Registry);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.AddComponent(Unit, new LocomotionChannel());
                Tick();
            }

            public string? TaskName => Registry.TryGetName(Repo.GetComponent<BehaviorState>(Unit).ActiveBehaviorHash, out var n) ? n : null;

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

            public void Order(string behaviour, string json = "{}")
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior });

            /// <summary>⭐ CE-2073 — the unit as a soldier: healthy, armed, able to fire.</summary>
            public void Arm(float health01 = 1f)
            {
                Repo.AddComponent(Unit, new Fdp.Toolkit.Combat.Components.Health { Current = health01 * 100f, Max = 100f });
                Repo.AddComponent(Unit, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 30, MaxAmmo = 30, MuzzleVelocity = 800f });
                Repo.AddComponent(Unit, new WeaponChannel());
            }

            /// <summary>⭐ CE-2073 — a contact remembered FRESH (tracked to saturation), armed or not.</summary>
            public unsafe Entity Contact(bool armed)
            {
                var e = Repo.CreateEntity();
                if (armed) Repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 30, MaxAmmo = 30 });
                if (!Repo.HasComponent<TargetMemory>(Unit)) Repo.AddComponent(Unit, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Unit);
                mem.EntityIds[mem.Count] = (long)e.PackedValue;
                mem.Freshness[mem.Count] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[mem.Count] = (byte)SensorModality.Visual;
                mem.Count++;
                return e;
            }

            public bool Finished(out Fbt.NodeStatus result)
            {
                result = default;
                foreach (var evt in Repo.Bus.Read<BehaviorFinishedEvent>())
                    if (evt.Entity.Equals(Unit)) { result = evt.Result; return true; }
                return false;
            }

            public unsafe void Remember(Entity threat)
            {
                if (!Repo.HasComponent<TargetMemory>(Unit)) Repo.AddComponent(Unit, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Unit);
                if (threat.IsNull) { mem.Count = 0; return; }
                mem.EntityIds[0] = (long)threat.PackedValue;
                mem.Freshness[0] = 10f;
                mem.Count = 1;
            }

            /// <summary>⭐ CE-3063 — a HEARD contact (no entity) in the unit's memory; returns its memory id.</summary>
            public long Hear(float x, float y, float radius)
            {
                if (!Repo.HasComponent<TargetMemory>(Unit)) Repo.AddComponent(Unit, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Unit);
                return TargetMemory.HearContact(ref mem, x, y, 0f, radius, 4 /* small arms */, 100f, (uint)_frame);
            }

            public void Answer(Entity sensor, uint tick, float x, float y)
            {
                var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = tick };
                buf.GetSpanRW()[0] = new EqsResult { PositionX = x, PositionY = y, Score = 1f };
                Repo.SetComponent(sensor, buf);
            }

            /// <summary>⭐ CE-3082 — the names of the HSM behaviour's active leaves (one per region).</summary>
            public unsafe string[] ActiveStates(string behaviour)
            {
                Assert.True(Registry.TryGetId(behaviour, out int id));
                Assert.True(Registry.TryGetDefinition(id, out var def));
                if (!Fdp.Toolkit.Behavior.RootHsmAccess.TryGetInstance(Repo, Unit, out byte* ptr, out int size)) return System.Array.Empty<string>();
                ushort* leaves = Fhsm.Kernel.HsmKernel.GetActiveLeafIds(ptr, size, out int count);
                var names = new string[count];
                for (int i = 0; i < count; i++) names[i] = leaves[i] == 0xFFFF ? "-" : def!.HsmMetadata?.GetStateName(leaves[i]) ?? $"State_{leaves[i]}";
                return names;
            }

            public void SetHealth(float health01) => Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(Unit).Current = health01 * 100f;

            /// <summary>⭐ CE-3082 — the decision's CURRENT WINNER as the host keeps it: <c>St.choice</c> in the behaviour's own block.</summary>
            public unsafe byte Winner(string behaviour)
            {
                Assert.True(Registry.TryGetId(behaviour, out int hash));
                if (behaviour == "CombatPosture")
                {
                    Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetBlockFor<global::Hrot.AI.Behaviors.Trees.CombatPosture_Block>(Repo, Unit, hash, out var b));
                    return b->St.choice.Winner;
                }
                if (behaviour == "CombatPostureBp")
                {
                    // ⭐ CE-3083 — the blueprint keeps its winner in its own Vars (St) member `Winner`; its generated class name
                    //   carries the asset hash, so the block is found by reflection and read at its field offset.
                    var bp = typeof(PostureNodes).Assembly.GetTypes().Single(t => t.Name.StartsWith("CombatPostureBp_") && t.Name.EndsWith("_Bp"));
                    var block = bp.GetNestedType("Block")!; var vars = bp.GetNestedType("Vars")!;
                    int off = (int)System.Runtime.InteropServices.Marshal.OffsetOf(block, "St") + (int)System.Runtime.InteropServices.Marshal.OffsetOf(vars, "Winner");
                    Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetRootBytes(Repo, Unit, out byte* root, out int len) && off < len);
                    return root[off];
                }
                Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetBlockFor<global::Hrot.AI.Behaviors.Machines.CombatPostureHsm_Block>(Repo, Unit, hash, out var h));
                return h->St.choice.Winner;
            }

            public unsafe Vector3 Destination()
            {
                ref readonly var ch = ref Repo.GetComponentRO<LocomotionChannel>(Unit);
                fixed (byte* src = ch.Params) return ((MoveToParams*)src)->Destination;
            }
        }

        // ── ⭐ CE-2073 — the CombatPosture tree (docs/DESIGN_Decision_Layer.md §3.3b) ───────────────────────────────────

        private const string Objective = "{\"advance\":{\"Objective\":[200,0,0],\"Speed\":3,\"ArrivalRadius\":5,\"CooldownSeconds\":1}}";

        [Fact]
        public void CE2073_CombatPosture_IsCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("CombatPosture", out _), "a mission task must be able to name it");
        }

        [Fact]
        public void CE2073_AgainstAWeakEnemy_ThePostureAdvancesFiring_AndEndsAtTheObjective()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: false);                       // a weak enemy ⇒ AdvanceAndAttack
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());  // towards the OBJECTIVE
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the posture keeps its own cover sensor");
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.RetreatSite).IsNull, "…and its retreat sensor");

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            w.Tick();
            w.Tick();
            Assert.NotEqual("CombatPosture", w.TaskName);              // the mission task is done
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the run's sensors go with it");
        }

        [Fact]
        /// <summary>⭐ <c>CE-2105</c> (R-208, 🔒 user: "advance without enemy") — with nothing to fight a healthy, armed unit ADVANCES
        /// on to its objective, firing at nothing. ⛔ SUPERSEDED (CE-2073): it held, and an advance whose enemy fell short of the
        /// objective never reached it.</summary>
        public void CE2105_WithNothingToFight_ThePostureAdvancesToTheObjective_WithoutFiring()
        {
            var w = new World();
            w.Arm();
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);                 // still on its way
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        [Fact]
        public void CE2073_HurtWithCover_ThePostureSwitchesToTakeCover()
        {
            var w = new World();
            w.Arm(health01: 0.3f);
            var enemy = w.Contact(armed: true);
            w.Contact(armed: true);                                    // outnumbered
            w.Order("CombatPosture", Objective);
            w.Tick();
            w.Tick();
            var cover = EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite);
            Assert.False(cover.IsNull);
            w.Answer(cover, 5, 30f, 40f);                              // good cover nearby ⇒ TakeCover wins
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite).IsNull,
                "the TakeCover child runs, with its own sensor");
        }

        // ── ⭐ CE-3082 (G4) — the SAME posture decision hosted as an HSM (docs/DESIGN_Decision_Layer.md §3.3c) ───────────────

        private const string HsmPosture = "CombatPostureHsm";

        [Fact]
        public void CE3082_CombatPostureHsm_IsCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId(HsmPosture, out _), "a mission task (U3) must be able to name it");
        }

        /// <summary>⭐ D1 — a weak enemy ⇒ the Advance leaf, moving to the objective firing; arrival ⇒ the HSM reaches its Final
        /// state (its activity's Success is discarded, so the Arrived guard is the finish) and the run's sensors go.</summary>
        [Fact]
        public void CE3082_AgainstAWeakEnemy_TheHsmAdvancesFiring_AndFinishesAtTheObjective()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            w.Order(HsmPosture, Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(HsmPosture, w.TaskName);
            Assert.Contains("Advance", w.ActiveStates(HsmPosture));
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the Sense region keeps the cover sensor");

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.NotEqual(HsmPosture, w.TaskName);                     // Final ⇒ the mission task is done
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the run's sensors go with it");
        }

        /// <summary>⭐⭐ D3 CLOSED — the HSM switch railed at RUNTIME, both ways: a weak enemy ⇒ Advance (firing); hurt +
        /// outnumbered + cover ⇒ TakeCover, and the advance's FIRE STOPS because leaving the Advance leaf ran
        /// <c>Deactivate_AdvanceAndAttack</c> (D2 — TakeCover never touches the weapon, so nothing else stops it); healthy with
        /// nothing to fight ⇒ back to Advance.</summary>
        [Fact]
        public void CE3082_HealthEdits_SwitchTheHsmPosture_BothWays_AndLeavingALeafRunsItsDeactivator()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);                                   // a weak enemy ⇒ AdvanceAndAttack
            w.Order(HsmPosture, Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Contains("Advance", w.ActiveStates(HsmPosture));
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);

            w.SetHealth(0.3f);
            w.Remember(Entity.Null);
            w.Contact(armed: true);
            w.Contact(armed: true);                                    // hurt and outnumbered
            w.Tick();
            w.Tick();
            var cover = EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite);
            Assert.False(cover.IsNull);
            w.Answer(cover, 5, 30f, 40f);                              // good cover nearby ⇒ TakeCover wins
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Contains("TakeCover", w.ActiveStates(HsmPosture));
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite).IsNull, "the TakeCover leaf runs, with its own sensor");
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);

            w.SetHealth(1f);
            w.Remember(Entity.Null);                                   // nothing left to fight ⇒ AdvanceAndAttack (CE-2105)
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Contains("Advance", w.ActiveStates(HsmPosture));
            Assert.DoesNotContain("TakeCover", w.ActiveStates(HsmPosture));
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());   // the advance's move replaced the cover move
        }

        /// <summary>⭐ U3's premise — the BTree and the HSM make the SAME decision for the same inputs, step by step, and the HSM's
        /// active posture leaf is that decision's option.</summary>
        [Fact]
        public void CE3082_TheHsmAndTheBTree_MakeTheSameDecisions_ForTheSameInputs()
        {
            string[] leafOf = { "-", "Advance", "TakeCover", "Suppress", "FallBack", "Hold" };
            var worlds = new[] { (w: new World(), name: "CombatPosture"), (w: new World(), name: HsmPosture) };
            foreach (var (w, name) in worlds)
            {
                w.Arm();
                w.Contact(armed: false);
                w.Order(name, Objective);
            }
            var steps = new System.Action<World>[]
            {
                w => { },                                                                 // weak enemy ⇒ advance
                w => { w.SetHealth(0.3f); w.Remember(Entity.Null); w.Contact(true); w.Contact(true); },   // hurt, outnumbered
                w => { w.SetHealth(1f); w.Remember(Entity.Null); },                       // healthy, nothing to fight
            };
            var seen = new List<byte>();
            foreach (var step in steps)
            {
                foreach (var (w, _) in worlds) { step(w); for (int i = 0; i < 4; i++) w.Tick(); }
                byte bt = worlds[0].w.Winner("CombatPosture"), hsm = worlds[1].w.Winner(HsmPosture);
                Assert.Equal(bt, hsm);
                Assert.True(System.Array.IndexOf(worlds[1].w.ActiveStates(HsmPosture), leafOf[hsm]) >= 0,
                    $"top {hsm} ⇒ {leafOf[hsm]}, HSM leaves [{string.Join(",", worlds[1].w.ActiveStates(HsmPosture))}]");
                seen.Add(hsm);
            }
            Assert.True(seen.Distinct().Count() >= 2, $"the inputs must move the decision (saw {string.Join(",", seen)})");
        }

        // ── ⭐ CE-3083 (G5) — the SAME posture decision hosted as a BLUEPRINT (docs/DESIGN_Decision_Layer.md §3.3d) ─────────────

        private const string BpPosture = "CombatPostureBp";
        private const string BpObjective = "{\"advance\":{\"advance\":{\"Objective\":[200,0,0],\"Speed\":3,\"ArrivalRadius\":5,\"CooldownSeconds\":1}}}";

        [Fact]
        public void CE3083_CombatPostureBp_AndItsOptionBehaviours_AreRegistered()
        {
            var w = new World();
            foreach (var name in new[] { BpPosture, "PostureAdvance", "PostureSuppress", "PostureHold", "PostureSense" })
                Assert.True(w.Registry.TryGetId(name, out _), $"{name} must be registered (U3 orders the blueprint; each option is a behaviour)");
        }

        /// <summary>A weak enemy ⇒ the blueprint starts the advance task (moving to the objective, firing); arrival ⇒ the task
        /// succeeds ⇒ the blueprint returns Success and the run's sensors go.</summary>
        [Fact]
        public void CE3083_AgainstAWeakEnemy_TheBlueprintAdvancesFiring_AndFinishesAtTheObjective()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            w.Order(BpPosture, BpObjective);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal(BpPosture, w.TaskName);
            Assert.Equal((byte)1, w.Winner(BpPosture));
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the Sense task keeps the cover sensor");

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.NotEqual(BpPosture, w.TaskName);
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the run's sensors go with it");
        }

        /// <summary>⭐ the switch, both ways: hurt + outnumbered + cover ⇒ the advance task is ABORTED (its fire stops) and TakeCover
        /// starts; healthy with nothing to fight ⇒ back to the advance.</summary>
        [Fact]
        public void CE3083_HealthEdits_SwitchTheBlueprintsTask_BothWays()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            w.Order(BpPosture, BpObjective);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);

            w.SetHealth(0.3f);
            w.Remember(Entity.Null);
            w.Contact(armed: true);
            w.Contact(armed: true);
            w.Tick();
            w.Tick();
            var cover = EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite);
            Assert.False(cover.IsNull);
            w.Answer(cover, 5, 30f, 40f);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal((byte)2, w.Winner(BpPosture));
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite).IsNull, "the TakeCover task runs, with its own sensor");
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);

            w.SetHealth(1f);
            w.Remember(Entity.Null);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal((byte)1, w.Winner(BpPosture));
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
        }

        /// <summary>⭐ U3's premise, all three hosts — the BTree, the HSM and the blueprint pick the same winner at every step.</summary>
        [Fact]
        public void CE3083_AllThreeHosts_MakeTheSameDecisions_ForTheSameInputs()
        {
            var worlds = new[] { (w: new World(), name: "CombatPosture", json: Objective), (w: new World(), name: HsmPosture, json: Objective),
                                 (w: new World(), name: BpPosture, json: BpObjective) };
            foreach (var (w, name, json) in worlds) { w.Arm(); w.Contact(armed: false); w.Order(name, json); }
            var steps = new System.Action<World>[]
            {
                w => { },
                w => { w.SetHealth(0.3f); w.Remember(Entity.Null); w.Contact(true); w.Contact(true); },
                w => { w.SetHealth(1f); w.Remember(Entity.Null); },
            };
            foreach (var step in steps)
            {
                foreach (var (w, _, _) in worlds) { step(w); for (int i = 0; i < 6; i++) w.Tick(); }
                var winners = worlds.Select(x => x.w.Winner(x.name)).ToArray();
                Assert.True(winners.Distinct().Count() == 1, $"BTree / HSM / blueprint winners differ: {string.Join(",", winners)}");
            }
        }

        [Fact]
        public void CE2092_BothTrees_AreCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("TakeCover", out _), "TakeCover must be registered so an order, an SOP reaction or a mission task can name it");
            Assert.True(w.Registry.TryGetId("FallBack", out _), "FallBack must be registered");
        }

        [Fact]
        public void CE2108_FlankAndFiringPosition_AreCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("Flank", out _), "Flank must be registered so an order, an SOP row or a mission task can name it");
            Assert.True(w.Registry.TryGetId("FiringPosition", out _), "FiringPosition must be registered");
        }

        /// <summary>
        /// ⭐ <c>CE-2108</c> — the FiringPosition TREE through the real ingress and brain: it moves to the answer FIRING on the way
        /// (the tree's default <c>FireWhileMoving = 1</c>), and on arrival its sensor goes and the tree's <c>Engage</c> keeps the
        /// weapon on the threat. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §9.
        /// </summary>
        [Fact]
        public void CE2108_FiringPosition_Ordered_MovesFiring_ThenEngagesOnArrival()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: true);
            w.Order("FiringPosition");
            w.Tick();
            w.Tick();
            Assert.Equal("FiringPosition", w.TaskName);
            var sensor = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FiringPositionSite);
            Assert.False(sensor.IsNull);
            Assert.Equal(enemy, w.Repo.GetComponentRO<EqsSensor>(sensor).ContextSlot1);

            w.Answer(sensor, 5, 30f, 40f);
            w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(30f, 40f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            w.Tick();
            w.Tick();
            Assert.Equal("FiringPosition", w.TaskName);   // Engage keeps the run going
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FiringPositionSite).IsNull, "the sensor goes on arrival");
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        // ── ⭐ CE-3079 H2 — the Sentry tree (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.5) ──────────────────────────────────

        /// <summary>Puts the unit at <paramref name="x"/>,0 and remembers one ARMED contact at (<paramref name="cx"/>, 0).</summary>
        private static unsafe Entity SentryAt(World w, float x, float cx)
        {
            if (!w.Repo.HasComponent<Fdp.Core.SimTransform>(w.Unit)) w.Repo.AddComponent(w.Unit, new Fdp.Core.SimTransform());
            w.Repo.GetComponentRW<Fdp.Core.SimTransform>(w.Unit).Position = new Vector3(x, 0f, 0f);
            var enemy = w.Contact(armed: true);
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            mem.PositionsX[mem.Count - 1] = cx;
            return enemy;
        }

        /// <summary>
        /// 🔴 <c>CE-3079</c> H2 — the Sentry run ENDS (Success) only after a dangerous contact is within its radius (90 m) AND
        /// the 15 s wait has run; a contact farther away keeps it watching. It never writes the weapon channel (no fire node).
        /// </summary>
        [Fact]
        public void CE3079_Sentry_EndsOnlyAfterAContactIsNear_AndTheWaitElapses_AndNeverFires()
        {
            var w = new World();
            w.Arm();
            Assert.True(w.Registry.TryGetId("Sentry", out _), "Sentry must be registered so a mission task can name it");
            SentryAt(w, 0f, 150f);                                    // an armed contact 150 m away: outside 90 m
            w.Order("Sentry");
            for (int i = 0; i < 1200; i++) { w.Tick(); Assert.False(w.Finished(out _), $"must keep watching while the contact is far (tick {i})"); }
            Assert.Equal("Sentry", w.TaskName);

            unsafe { w.Repo.GetComponentRW<TargetMemory>(w.Unit).PositionsX[0] = 60f; }   // it comes within 90 m
            int ticks = 0;
            bool done = false; Fbt.NodeStatus result = default;
            while (!done && ticks < 2000) { w.Tick(); ticks++; done = w.Finished(out result); }
            Assert.True(done, "the run must end once the contact is near and the wait has run");
            Assert.Equal(Fbt.NodeStatus.Success, result);
            Assert.True(ticks * 0.016 >= 14.9, $"it must wait the 15 s first; ended after {ticks * 0.016:F1} s");
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.Equal(0u, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActionInstanceId);   // never written
        }

        /// <summary>
        /// ⭐ <c>CE-3079</c> H2 — the point of the Sentry: a two-task mission ([Sentry, BehaviorFinished] → [next, BehaviorFinished])
        /// hands over to task 2 when the Sentry ends itself — the scenario's "sequencing of actions" (§10.5).
        /// </summary>
        [Fact]
        public void CE3079_ATwoTaskMission_AdvancesFromSentry_WhenItEndsItself()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("Sentry", out int sentry));
            Assert.True(w.Registry.TryGetId("FallBack", out int next));
            var queue = new MissionPlanQueue { PhaseCount = 2 };
            queue.Phases[0] = new MissionPhase { BehaviorId = sentry, Trigger = MissionTrigger.BehaviorFinished };
            queue.Phases[1] = new MissionPhase { BehaviorId = next,   Trigger = MissionTrigger.BehaviorFinished };
            w.Repo.AddComponent(w.Unit, queue);
            var director = new MissionDirectorSystem();
            SentryAt(w, 0f, 30f);                                     // a contact already near
            w.Order("Sentry");

            int ticks = 0;
            while (w.Repo.GetComponentRO<MissionPlanQueue>(w.Unit).CurrentPhase == 0 && ticks < 2000)
            {
                w.Tick();
                director.Execute(w.Repo, 0.016f);
                ticks++;
            }
            Assert.Equal(1, w.Repo.GetComponentRO<MissionPlanQueue>(w.Unit).CurrentPhase);
            Assert.True(ticks * 0.016 >= 14.9, $"task 2 only after the Sentry's wait; advanced after {ticks * 0.016:F1} s");
        }

        // ── ⭐ CE-3079 H5 — the DangerCrossing tree (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.4) ───────────────────────────────

        private const string Crossing = "{\"sensor\":{\"RouteTo\":[300,300,0]},\"walk\":{\"X\":300,\"Y\":300,\"Speed\":1.5,\"ArrivalRadius\":3}}";

        private static Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor Area(uint id, float threat, float along,
            Fdp.Toolkit.Squad.DangerArea.DangerAreaKind kind = Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.StreetCrossing)
            => new()
            {
                FeatureId = id, ThreatRating = threat, Kind = kind, DistanceAlongRoute = along,
                NearSideHandle = new Vector3(100f + id, 100f, 0f), FarSideHandle = new Vector3(120f + id, 100f, 0f),
            };

        private static void DangerAnswer(World w, Entity sensor, uint tick, params Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[] areas)
        {
            var buf = new Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer { Count = areas.Length, LastUpdateTick = tick };
            for (int i = 0; i < areas.Length; i++) buf.GetSpanRW()[i] = areas[i];
            w.Repo.SetComponent(sensor, buf);
        }

        /// <summary>One frame, with the MoveTo executor's half emulated: a NEW action instance enters Running (its <c>OnEnter</c>).</summary>
        private static void Step(World w)
        {
            uint before = w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActionInstanceId;
            w.Tick();
            ref var ch = ref w.Repo.GetComponentRW<LocomotionChannel>(w.Unit);
            if (ch.ActionInstanceId != before && ch.ActiveAction == NavigationConstants.ActionIdMoveTo) ch.Status = Fbt.NodeStatus.Running;
        }

        private static void Arrive(World w) => w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;

        private static unsafe float Speed(World w)
        {
            ref readonly var ch = ref w.Repo.GetComponentRO<LocomotionChannel>(w.Unit);
            fixed (byte* src = ch.Params) return ((MoveToParams*)src)->Speed;
        }

        /// <summary>
        /// 🔴 <c>CE-3079</c> H5 — the shipped <c>DangerCrossing</c> tree through the real ingress and brain, against a HAND-FILLED
        /// answer on the run's own sensor: it walks → rushes across a 0-threat crossing (and does not cross it again on the stale
        /// answer) → walks on → holds short of a 0.8 crossing (and keeps holding at 0.45: hysteresis) → resumes when the route is
        /// clear → arrives, and the run ends Success.
        /// </summary>
        [Fact]
        public void CE3079_DangerCrossing_Walks_RushesACrossing_HoldsShortOfAWatchedOne_ThenArrives()
        {
            var w = new World();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>();
            Assert.True(w.Registry.TryGetId("DangerCrossing", out _), "DangerCrossing must be registered so an order can name it");
            w.Order("DangerCrossing", Crossing);
            Step(w); Step(w); Step(w);
            Assert.Equal("DangerCrossing", w.TaskName);

            var sensor = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(w.Repo, w.Unit, SensorModality.DangerArea);
            Assert.False(sensor.IsNull, "the tree's first step makes the run's danger sensor");
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Repo.GetComponentRO<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(sensor).Settings.RoutePoint);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // no answer yet: walks
            Assert.Equal(1.5f, Speed(w));

            DangerAnswer(w, sensor, 1, Area(1, 0f, 10f));                               // an unwatched crossing, 10 m ahead
            Step(w);
            Assert.Equal(new Vector3(101f, 100f, 0f), w.Destination());               // rush: to the near side…
            Assert.Equal(4.5f, Speed(w));
            Arrive(w); Step(w);
            Assert.Equal(new Vector3(121f, 100f, 0f), w.Destination());               // …and across
            Arrive(w); Step(w);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // walks on
            Assert.Equal(1.5f, Speed(w));
            for (int i = 0; i < 10; i++) Step(w);                                      // the stale answer still lists it:
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // ⛔ not crossed back

            DangerAnswer(w, sensor, 2, Area(2, 0.8f, 50f));                             // a WATCHED crossing, 50 m ahead
            Step(w);
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());               // hold short: to its near side
            Arrive(w);
            for (int i = 0; i < 300; i++) { Step(w); Assert.False(w.Finished(out _)); }
            DangerAnswer(w, sensor, 3, Area(2, 0.45f, 2f));                             // 0.45: still ≥ 0.5 − 0.1
            for (int i = 0; i < 5; i++) Step(w);
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());               // still holding

            DangerAnswer(w, sensor, 4);                                                 // the route is clear
            Step(w);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // resumes
            Arrive(w);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 5 && !done; i++) { Step(w); done = w.Finished(out result); }
            Assert.True(done, "arriving at the objective ends the run");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>
        /// 🔴 <c>CE-3079</c> H7 / <c>CE-3078</c> — the same behaviour as a BLUEPRINT built only from the per-kind sensor nodes
        /// (<c>DangerCrossingBp</c>: SpawnSensor(DangerArea) → When SensorResult(ThreatCrossed ≥ 0.5) → ReadSensorResult(0) near /
        /// far handles → MoveTo), through the real ingress and brain, against a hand-filled answer: walks → rushes across a
        /// 0-threat crossing to its far side → walks on → holds at a 0.8 crossing's near side → resumes when the route is clear →
        /// arrives, and the run ends Success.
        /// </summary>
        [Fact]
        public void CE3079_DangerCrossingBlueprint_Walks_RushesACrossing_HoldsShortOfAWatchedOne_ThenArrives()
        {
            var w = new World();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>();
            Assert.True(w.Registry.TryGetId("DangerCrossingBp", out _), "the blueprint must be registered so an order can name it");
            w.Order("DangerCrossingBp", "{\"Objective\":[300,300,0]}");
            Step(w); Step(w); Step(w);
            Assert.Equal("DangerCrossingBp", w.TaskName);

            var sensor = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(w.Repo, w.Unit, SensorModality.DangerArea);
            Assert.False(sensor.IsNull, "SpawnSensor(DangerArea) makes the run's sensor");
            var settings = w.Repo.GetComponentRO<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(sensor).Settings;
            Assert.Equal(Fdp.Toolkit.Squad.DangerArea.DangerRouteSource.ToPoint, settings.RouteSource);   // never the unit's own move
            Assert.Equal(new Vector3(300f, 300f, 0f), settings.RoutePoint);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());   // no answer: walks
            Assert.Equal(1.5f, Speed(w));

            DangerAnswer(w, sensor, 1, Area(1, 0f, 10f));                  // an unwatched crossing 10 m ahead
            Step(w);
            Assert.Equal(new Vector3(121f, 100f, 0f), w.Destination());   // rush: straight to the FAR side
            Assert.Equal(4.5f, Speed(w));
            Arrive(w);
            for (int i = 0; i < 5; i++) Step(w);                           // the stale answer still lists it: no move back
            Assert.Equal(new Vector3(121f, 100f, 0f), w.Destination());

            DangerAnswer(w, sensor, 2, Area(2, 0.8f, 50f));                 // refreshed: past it; a WATCHED crossing 50 m on
            Step(w); Step(w);
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());   // When ThreatCrossed → hold at its near side
            Assert.Equal(1.5f, Speed(w));
            Arrive(w);
            for (int i = 0; i < 300; i++) { Step(w); Assert.False(w.Finished(out _)); }
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());   // still holding

            DangerAnswer(w, sensor, 3);                                     // the route is clear (threat falls with it)
            Step(w); Step(w);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());   // resumes the walk
            Arrive(w);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 5 && !done; i++) { Step(w); done = w.Finished(out result); }
            Assert.True(done, "arriving at the objective ends the run");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        [Fact]
        public void CE2092_TakeCover_Ordered_MovesToTheAnswer_AndEndsWhenNothingIsLeftToHideFrom()
        {
            var w = new World();
            var threat = w.Repo.CreateEntity();
            w.Remember(threat);
            w.Order("TakeCover");
            w.Tick();
            w.Tick();
            Assert.Equal("TakeCover", w.TaskName);

            var sensor = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite);
            Assert.False(sensor.IsNull);
            Assert.Equal(threat, w.Repo.GetComponentRO<EqsSensor>(sensor).ContextSlot1);

            w.Answer(sensor, 5, 30f, 40f);
            w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(30f, 40f, 0f), w.Destination());

            w.Remember(Entity.Null);   // forgotten
            w.Tick();
            w.Tick();
            Assert.NotEqual("TakeCover", w.TaskName);
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite).IsNull, "the run's sensor must go with it");
        }

        /// <summary>
        /// ⭐ <c>CE-3063</c> ③ — a unit that only HEARD a shot hides from where it came from (🔒 user: "hide from a point is OK"):
        /// the run's sensor names no entity and carries the heard contact's point; the answer moves it. A NEW heard estimate far
        /// away re-points the sensor; a refresh of the same contact a little off does not. 📄 DESIGN_Thermal_And_Acoustic_Sensing §8.
        /// </summary>
        [Fact]
        public void CE3063_TakeCover_FromAHeardShot_PointsTheSensorAtThePoint_AndMoves()
        {
            var w = new World();
            w.Hear(80f, 20f, 15f);
            w.Order("TakeCover");
            w.Tick();
            w.Tick();
            Assert.Equal("TakeCover", w.TaskName);

            var sensor = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite);
            Assert.False(sensor.IsNull, "a heard contact is something to hide from — TakeCover must not end at once");
            var cfg = w.Repo.GetComponentRO<EqsSensor>(sensor);
            Assert.True(cfg.ContextSlot1.IsNull, "a heard contact is not an entity");
            Assert.Equal(EqsSensor.Point1Bit, cfg.ContextPointMask);
            Assert.Equal(new Vector3(80f, 20f, 0f), cfg.ContextPoint1);
            uint epoch = cfg.Epoch;

            w.Answer(sensor, 5, -30f, -40f);
            w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(-30f, -40f, 0f), w.Destination());

            w.Hear(81f, 20.5f, 15f);   // the same shot heard again: fused, it hardly moves ⇒ no re-point
            w.Tick();
            Assert.Equal(epoch, w.Repo.GetComponentRO<EqsSensor>(sensor).Epoch);
        }
    }
}
