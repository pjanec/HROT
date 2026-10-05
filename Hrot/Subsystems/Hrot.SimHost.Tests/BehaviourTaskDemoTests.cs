using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>The §7 demos on the REAL children</b> — <c>CE-2023</c> ③, 📄 <c>docs/blueprints/DESIGN_Unified_Behaviour_Run.md</c>
    /// "S8n-2". <c>Demo_MissionPlan</c> / <c>Demo_TaskChain</c> (shipped, compiled by the production generator) host the curated
    /// <c>MoveToLocation</c> and <c>FireAtTarget</c>, each started with the demo's own Parameter as its contract (one JSON assign
    /// carries the whole mission). The registry is the production one (<c>CgfBehaviorSetup.LoadFromAiAssembly</c>), arbitration
    /// is real; the rail plays only the two DISPATCHERS (a new action ⇒ Running, a few frames later ⇒ Success) and advances sim
    /// time for the <c>Delay</c>-based demo children (Retreat, Take Cover).
    /// <para>⭐ Re-homed here from <c>Hrot.Blueprints.Tests</c> (<c>BlueprintBehaviourTests.Demo_*</c>): only this world holds the
    /// curated children. Each claim of those rails is kept — the order of the legs, abort ⇒ retreat, cover taken alongside, low
    /// health aborts the defence.</para>
    /// </summary>
    public sealed unsafe class BehaviourTaskDemoTests
    {
        private const long HostileNetId = 777;

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Unit, Hostile;
            public readonly List<Vector3> Destinations = new();
            public readonly List<Entity> FireTargets = new();
            public BehaviorFinishedEvent? Finished;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private readonly ChannelArbitrationSystem _arbitration = new();
            private float _time;
            private int _locoFrames;

            public World(bool weaponReady)
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                Repo.RegisterComponent<NetworkIdentity>();
                BlueprintTierTable.RegisterAll(Repo);
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                Hostile = Repo.CreateEntity();
                Repo.AddComponent(Hostile, new NetworkIdentity { Value = HostileNetId });
                var map = new NetworkEntityMap();
                map.Register(HostileNetId, Hostile);
                Repo.SetSingletonManaged<NetworkEntityMap>(map);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.AddComponent(Unit, new LocomotionChannel());
                Repo.AddComponent(Unit, new WeaponChannel());
                Repo.AddComponent(Unit, new WeaponState { CooldownSecondsRemaining = weaponReady ? 0f : 1f });
                var memory = new TargetMemory { Count = 1 };
                memory.EntityIds[0] = (long)Hostile.PackedValue;
                memory.Freshness[0] = 1f;
                Repo.AddComponent(Unit, memory);
            }

            public void Assign(string behaviour, string json)
            {
                Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = behaviour, JsonParams = json });
                Repo.Bus.SwapBuffers();
                _ingress.Execute(Repo, 0.016f);
            }

            /// <summary>One frame: brain → arbitration (production order), then the played dispatchers.</summary>
            public void Frame()
            {
                _time += 0.016f;
                Repo.SetSimulationTime(_time);
                _brain.Execute(Repo, 0.016f);
                foreach (var f in Repo.Bus.Read<BehaviorFinishedEvent>())
                    if (f.Entity.Index == Unit.Index) Finished = f;
                Repo.Bus.SwapBuffers();
                _arbitration.Execute(Repo, 0.016f);

                ref var loco = ref Repo.GetComponentRW<LocomotionChannel>(Unit);
                if (loco.ActionInstanceId != loco.DispatchedInstanceId)
                {
                    loco.DispatchedInstanceId = loco.ActionInstanceId;
                    if (loco.ActiveAction == NavigationConstants.ActionIdMoveTo)
                    {
                        loco.Status = Fbt.NodeStatus.Running;
                        Destinations.Add(Unsafe.As<byte, MoveToParams>(ref loco.Params[0]).Destination);
                        _locoFrames = 0;
                    }
                }
                else if (loco.ActiveAction == NavigationConstants.ActionIdMoveTo && loco.Status == Fbt.NodeStatus.Running
                         && ++_locoFrames >= 3)
                {
                    loco.Status = Fbt.NodeStatus.Success;   // "arrived"
                }

                ref var weapon = ref Repo.GetComponentRW<WeaponChannel>(Unit);
                if (weapon.ActionInstanceId != weapon.DispatchedInstanceId)
                {
                    weapon.DispatchedInstanceId = weapon.ActionInstanceId;
                    if (weapon.ActiveAction != 0)
                    {
                        weapon.Status = Fbt.NodeStatus.Running;
                        FireTargets.Add(Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref weapon.Params[0]).Target);
                    }
                }
            }

            public int ReadInt(string name)
            {
                Assert.True(Registry.TryGetDefinition(BehaviorHash.FromName(Behaviour), out var def));
                if (!RootParamsAccess.TryGetRootBytes(Repo, Unit, out byte* root)) return _last.TryGetValue(name, out int v) ? v : 0;
                var block = def!.BlackboardLayoutType!;
                int offset = TypeLayout.OffsetOf(block, "St") + TypeLayout.OffsetOf(block.GetField("St")!.FieldType, name);
                return _last[name] = *(int*)(root + offset);
            }

            public void SetTrue(string name)
            {
                Assert.True(Registry.TryGetDefinition(BehaviorHash.FromName(Behaviour), out var def));
                Assert.True(RootParamsAccess.TryGetRootBytes(Repo, Unit, out byte* root));
                var block = def!.BlackboardLayoutType!;
                *(bool*)(root + TypeLayout.OffsetOf(block, "St") + TypeLayout.OffsetOf(block.GetField("St")!.FieldType, name)) = true;
            }

            public string Behaviour = "";
            private readonly Dictionary<string, int> _last = new();   // the block is cleared at the end: keep the last seen
        }

        private static string MissionJson(int maxRounds) =>
            "{\"Move\":{\"x\":100,\"y\":0,\"speed\":5,\"arrivalRadius\":2}," +
            "\"Fire\":{\"targetNetworkId\":" + HostileNetId + ",\"maxRounds\":" + maxRounds + ",\"cooldownSeconds\":0.5}," +
            "\"Home\":{\"x\":0,\"y\":0,\"speed\":5,\"arrivalRadius\":2}}";

        private static World Start(string behaviour, string json, bool weaponReady = true)
        {
            var w = new World(weaponReady) { Behaviour = behaviour };
            w.Assign(behaviour, json);
            return w;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Demo_MissionPlan, untouched — Advance, Defend, Return on the real children, each from its own Parameter.</b>
        /// ① <c>MoveToLocation</c> drives to <c>Move</c> (100, 0) — the host bytes through the typed resolver's from-bytes arm;
        /// ② <c>FireAtTarget</c> engages the entity <c>Fire.targetNetworkId</c> names (resolved through the world's
        /// <c>NetworkEntityMap</c>) and ends at <c>maxRounds</c>; ③ the second <c>MoveToLocation</c> drives HOME (0, 0) —
        /// 🔴 <c>CE-2052</c>: before, it inherited ① 's finished channel and ended at once, never driving. ⇒ Success.
        /// </summary>
        [Fact]
        public void DemoMissionPlan_RunsItsLegsOnTheRealChildren_EachFromItsOwnParameter()
        {
            var w = Start("Demo_MissionPlan", MissionJson(maxRounds: 3));
            var phases = new List<int>();
            for (int f = 0; f < 400 && w.Finished is null; f++)
            {
                w.Frame();
                int p = w.ReadInt("Phase");
                if (phases.Count == 0 || phases[^1] != p) phases.Add(p);
            }

            Assert.NotNull(w.Finished);
            Assert.Equal(Fbt.NodeStatus.Success, w.Finished!.Value.Result);
            Assert.Equal(new[] { new Vector3(100, 0, 0), new Vector3(0, 0, 0) }, w.Destinations);
            Assert.Equal(new[] { w.Hostile }, w.FireTargets);
            Assert.Equal(new[] { 0, 1, 2 }, phases);            // Phase 3 and the end share the last frame
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Demo_MissionPlan — hits take cover ALONGSIDE, and low health aborts the defence.</b> While Defend fires (a
        /// weapon on cooldown: no round completes, so it keeps running), eight hits on self each cost 10 Health and start Take
        /// Cover alongside; at Health 20 Defend's While Running aborts it ⇒ Retreat (0.5 s) ⇒ Failure, and the Return leg never
        /// drives.
        /// </summary>
        [Fact]
        public void DemoMissionPlan_Hits_TakeCoverAlongside_AndLowHealthAbortsTheDefence()
        {
            var w = Start("Demo_MissionPlan", MissionJson(maxRounds: 0), weaponReady: false);
            for (int f = 0; f < 200 && w.ReadInt("Phase") != 1; f++) w.Frame();
            Assert.Equal(1, w.ReadInt("Phase"));
            w.Frame();
            Assert.Equal(new[] { w.Hostile }, w.FireTargets);   // Defend is firing
            for (int k = 0; k < 8; k++)
            {
                w.Repo.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.HitEvent { HitEntity = w.Unit });
                w.Repo.Bus.SwapBuffers();
                w.Frame();
                Assert.Equal(k + 1, w.ReadInt("CoverTaken"));
            }
            Assert.Equal(20, w.ReadInt("Health"));
            int retreat = 0;
            for (; retreat < 120 && w.Finished is null; retreat++) w.Frame();
            Assert.Equal(Fbt.NodeStatus.Failure, w.Finished!.Value.Result);
            Assert.InRange(retreat, 25, 45);                     // Retreat's 0.5 s after the abort
            Assert.Equal(new[] { new Vector3(100, 0, 0) }, w.Destinations);   // the Return leg never drove
        }

        /// <summary>
        /// ⭐⭐ <b>Demo_TaskChain — Advance then Engage on the real children, in order, and the chain succeeds.</b>
        /// </summary>
        [Fact]
        public void DemoTaskChain_RunsItsTasksOnTheRealChildren_InOrder()
        {
            var w = Start("Demo_TaskChain", MissionJson(maxRounds: 3));
            int stage1At = -1;
            for (int f = 1; f <= 200 && w.Finished is null; f++)
            {
                w.Frame();
                if (stage1At < 0 && w.ReadInt("Stage") == 1) stage1At = f;
            }
            Assert.Equal(Fbt.NodeStatus.Success, w.Finished!.Value.Result);
            Assert.True(stage1At > 0, "Stage 1 is written when the Advance task succeeds");
            Assert.Equal(new[] { new Vector3(100, 0, 0) }, w.Destinations);
            Assert.Equal(new[] { w.Hostile }, w.FireTargets);
        }

        /// <summary>
        /// ⭐⭐ <b>Demo_TaskChain — CallOff aborts the running Engage (FireAtTarget) and the chain retreats.</b> Stage never
        /// reaches 2; the Retreat's 0.5 s precedes the Failure.
        /// </summary>
        [Fact]
        public void DemoTaskChain_CallOff_AbortsTheRunningEngage_AndRetreats()
        {
            var w = Start("Demo_TaskChain", MissionJson(maxRounds: 0), weaponReady: false);
            for (int f = 0; f < 200 && w.ReadInt("Stage") != 1; f++) w.Frame();
            Assert.Equal(1, w.ReadInt("Stage"));
            for (int k = 0; k < 10; k++) w.Frame();
            Assert.Null(w.Finished);                              // Engage is running …
            Assert.Equal(new[] { w.Hostile }, w.FireTargets);    // … and firing at the target its Fire parameter names
            w.SetTrue("CallOff");
            int after = 0;
            for (; after < 120 && w.Finished is null; after++) w.Frame();
            Assert.Equal(Fbt.NodeStatus.Failure, w.Finished!.Value.Result);
            Assert.Equal(1, w.ReadInt("Stage"));
            Assert.InRange(after, 25, 45);
        }
    }
}
