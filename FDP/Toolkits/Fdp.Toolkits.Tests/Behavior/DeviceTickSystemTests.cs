using System;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.5, D-72, R6-2, R7-2, R3-3) — THE DEVICE TICK: the brain tick's own
    /// routine over <see cref="DeviceInfo"/> entities only, every frame, paused or not; it STARTS a device's behaviour from its type (in pause too) and
    /// restarts it after a finish; nothing while a replay plays; the brain tick never ticks a device and the device tick never a unit; 0 B per steady frame.
    /// </summary>
    public class DeviceTickSystemTests
    {
        private const int DeviceRunId = 9701, UnitRunId = 9702, OnceId = 9703;

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = TestWorldFactory.Create();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly BrainTickSystem Brains;
            public readonly DeviceTickSystem Devices;
            public int DeviceTicks, UnitTicks, OnceTicks;

            public Rig()
            {
                W.RegisterComponent<DeviceInfo>();
                Register(DeviceRunId, "DeviceRun", () => { DeviceTicks++; return NodeStatus.Running; });
                Register(UnitRunId, "UnitRun", () => { UnitTicks++; return NodeStatus.Running; });
                Register(OnceId, "Once", () => { OnceTicks++; return NodeStatus.Success; });   // a logic that ends after one tick
                Ingress = new BehaviorIngressSystem(Registry);
                Brains = new BrainTickSystem(Registry);
                Devices = new DeviceTickSystem(Registry, Ingress);
            }

            private void Register(int id, string name, Func<NodeStatus> body)
            {
                var actions = new ActionRegistry<byte, BTreeContext>();
                actions.Register(name, (ref byte _, ref BehaviorTreeState _, ref BTreeContext _, int _) => body());
                var blob = new BehaviorTreeBlob
                {
                    TreeName = name, Nodes = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
                    MethodNames = new[] { name }, FloatParams = Array.Empty<float>(), IntParams = Array.Empty<int>(),
                };
                Registry.Register(id, name, new BehaviorDefinition
                {
                    Name = name, BrainTier = BehaviorConstants.BrainTierBTree, BTreeInterpreter = new Interpreter<byte, BTreeContext>(blob, actions),
                });
            }

            /// <summary>A device: a brain with NO run yet, and the behaviour its type names.</summary>
            public Entity Device(string behavior)
            {
                var e = W.CreateEntity();
                W.AddComponent(e, new BehaviorState());
                W.AddComponent(e, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(behavior) });
                return e;
            }

            /// <summary>A unit already running UnitRun (as the brain tick's rails build one).</summary>
            public Entity Unit()
            {
                var u = W.CreateEntity();
                W.AddComponent(u, new BehaviorState { ActiveBehaviorHash = UnitRunId, BrainTier = BehaviorConstants.BrainTierBTree, InstanceId = 1 });
                RootStateAccess.EnsureRootState(W, u);
                return u;
            }

            /// <summary>One frame: dt 0 = a paused frame (the brain tick's WhenTimeAdvances gate would skip it; the device tick runs).</summary>
            public void Frame(float dt)
            {
                W.Bus.SwapBuffers();
                if (dt > 0f) { Ingress.Execute(W, dt); Brains.Execute(W, dt); }
                Devices.Execute(W, dt);
            }

            public void Dispose() => W.Dispose();
        }

        [Fact]
        public void S1a_ADevice_IsStartedFromItsType_AndTicks_EvenWhileTimeNeverAdvances()
        {
            using var r = new Rig();
            var d = r.Device("DeviceRun");
            for (int i = 0; i < 3; i++) r.Frame(0f);   // a session that boots paused: time has never advanced
            Assert.Equal(DeviceRunId, r.W.GetComponentRO<BehaviorState>(d).ActiveBehaviorHash);
            Assert.Equal(1, r.Devices.Starts);
            Assert.True(r.DeviceTicks >= 2, $"the device's logic runs in pause (ticks {r.DeviceTicks})");
        }

        [Fact]
        public void S1a_TheBrainTickNeverTicksADevice_AndTheDeviceTickNeverAUnit()
        {
            using var r = new Rig();
            r.Device("DeviceRun");
            r.Unit();
            r.Frame(0f);                      // the device starts (paused frame)
            int devicesBefore = r.DeviceTicks;
            r.Brains.Execute(r.W, 0.016f);    // ONLY the brain tick
            Assert.Equal(devicesBefore, r.DeviceTicks);
            Assert.Equal(1, r.UnitTicks);
            r.Devices.Execute(r.W, 0.016f);   // ONLY the device tick
            Assert.Equal(1, r.UnitTicks);
            Assert.Equal(devicesBefore + 1, r.DeviceTicks);
        }

        [Fact]
        public void S1a_ADevicesLogicThatEnds_IsRestarted_ItNeverStops()
        {
            using var r = new Rig();
            var d = r.Device("Once");
            for (int i = 0; i < 6; i++) r.Frame(0f);
            Assert.True(r.Devices.Starts >= 2, $"finished, then started again (starts {r.Devices.Starts})");
            Assert.True(r.OnceTicks >= 2, $"ticked after the restart (ticks {r.OnceTicks})");
            Assert.True(r.W.IsAlive(d));
        }

        [Fact]
        public void S1a_ADeviceWhoseStartIsRefused_IsNotRetriedEveryFrame_AndAllocatesNothing()
        {
            using var r = new Rig();
            r.Device("NoSuchBehaviour");
            for (int i = 0; i < 30; i++) r.Frame(0f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) r.Devices.Execute(r.W, 0f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(0, r.Devices.Starts);
        }

        [Fact]
        public void S1a_NothingStartsOrTicks_WhileAReplayPlays()
        {
            using var r = new Rig();
            var d = r.Device("DeviceRun");
            Fdp.Toolkit.Replay.ReplayState.Enter(r.W);
            for (int i = 0; i < 3; i++) r.Frame(0f);
            Assert.Equal(BehaviorIds.None, r.W.GetComponentRO<BehaviorState>(d).ActiveBehaviorHash);
            Assert.Equal(0, r.DeviceTicks);
            Assert.Equal(0, r.Devices.Starts);
        }

        [Fact]
        public void S1a_ASteadyDeviceFrame_AllocatesNothing()
        {
            using var r = new Rig();
            r.Device("DeviceRun");
            for (int i = 0; i < 30; i++) r.Frame(0f);   // warm: the start, the queries, JIT tiering (as CE505_R4's brain rail warms 24)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) r.Devices.Execute(r.W, 0f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
