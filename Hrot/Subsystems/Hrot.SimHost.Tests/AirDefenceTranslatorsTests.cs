using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Messages;
using WarningSample = Hrot.NED.Messages.MissileWarning;
using MissileWarning = Fdp.Toolkit.Combat.Components.MissileWarning;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2189</c> / <c>CE-2188</c> (<c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3c) — the air-defence loop
    /// crosses the wire twice: the missile warning SimHost → the aircraft's brain, the flare request brain → SimHost. Two worlds with their
    /// own entity maps stand for two nodes; the egress's written sample is what the other node's ingress reads (the DDS transport itself
    /// is the shared machinery every topic rides).
    /// </summary>
    public sealed class AirDefenceTranslatorsTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private const long AircraftId = 4242;
        private readonly EntityRepository _simHost = new(), _brain = new();
        private readonly NetworkEntityMap _simHostMap = new(), _brainMap = new();
        private readonly Entity _onSimHost, _onBrain;

        public AirDefenceTranslatorsTests()
        {
            foreach (var w in new[] { _simHost, _brain })
            {
                w.RegisterComponent<SimTransform>();
                w.RegisterComponent<MissileWarning>();
                w.RegisterEvent<MissileWarningEvent>();
                w.RegisterEvent<DispenseCountermeasureIntent>();
                w.RegisterEvent<SensorChangedEvent>();
            }
            _onSimHost = _simHost.CreateEntity();
            _onBrain = _brain.CreateEntity();
            _brain.AddComponent(_onBrain, new SimTransform { Position = new Vector3(1000, 1000, 300), Rotation = Quaternion.Identity });
            _simHostMap.Register(AircraftId, _onSimHost);
            _brainMap.Register(AircraftId, _onBrain);
        }

        public void Dispose() { _simHost.Dispose(); _brain.Dispose(); }

        private static void Apply(EntityRepository w, Action<IEntityCommandBuffer, ISimulationView> ingress)
        {
            var view = (ISimulationView)w;
            var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
            ingress(cmd, view);
            cmd.Playback(w);
            w.Bus.SwapBuffers();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2189</c> — a missile launched at the aircraft on the SimHost reaches its brain on ANOTHER node in the next frame: the
        /// brain's <see cref="MissileWarningSensingSystem"/> raises <c>SensorChange.MissileWarning</c> on ITS entity and stores the bearing
        /// to the launch point (south-west of the aircraft ⇒ 225°), the seeker and the time to impact. A locally echoed (remote) event is
        /// not sent back out.
        /// </summary>
        [Fact]
        public void CE2189_AMissileWarning_CrossesToTheAircraftsBrain_WithBearingSeekerAndTime()
        {
            var writer = new CapturingWriter<WarningSample>();
            var egress = new MissileWarningEgressTranslator(writer, _simHostMap);
            _simHost.Bus.Publish(new MissileWarningEvent
            {
                Unit = _onSimHost, FromX = 0, FromY = 0, FromZ = 0, Seeker = (byte)Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, TimeToImpactS = 2.1f,
            });
            _simHost.Bus.Publish(new MissileWarningEvent { Unit = _onSimHost, IsRemote = true });   // an ingress echo: never re-sent
            _simHost.Bus.SwapBuffers();
            egress.ScanAndPublish(_simHost);
            var sample = Assert.Single(writer.Written);
            Assert.Equal(AircraftId, sample.UnitEntityId);

            var ingress = new MissileWarningIngressTranslator(participant: null, _brainMap);
            Apply(_brain, (cmd, view) => ingress.ProcessSample(in sample, cmd, view));
            var sensing = new MissileWarningSensingSystem();
            sensing.Execute(_brain, 0.016f);
            var cmd2 = (EntityCommandBuffer)((ISimulationView)_brain).GetCommandBuffer();
            cmd2.Playback(_brain);
            _brain.Bus.SwapBuffers();

            var edge = Assert.Single(_brain.Bus.Read<SensorChangedEvent>().ToArray());
            Assert.Equal((_onBrain, SensorChange.MissileWarning), (edge.Unit, edge.What));
            var w = _brain.GetComponentRO<MissileWarning>(_onBrain);
            Assert.Equal(225f, w.BearingDeg, 1);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, w.Seeker);
            Assert.Equal(2.1f, w.TimeToImpactS, 3);
        }

        /// <summary>
        /// ⭐ <c>CE-2188</c> — the brain's flare request reaches the SimHost as a REMOTE intent on ITS entity; an unknown kind is dropped.
        /// </summary>
        [Fact]
        public void CE2188_AFlareRequest_CrossesToTheSimHost()
        {
            var writer = new CapturingWriter<DispenseCountermeasure>();
            var egress = new DispenseCountermeasureEgressTranslator(writer, _brainMap);
            _brain.Bus.Publish(new DispenseCountermeasureIntent { Unit = _onBrain, Kind = DecoyKind.Flare });
            _brain.Bus.SwapBuffers();
            egress.ScanAndPublish(_brain);
            var sample = Assert.Single(writer.Written);

            var ingress = new DispenseCountermeasureIngressTranslator(participant: null, _simHostMap);
            Apply(_simHost, (cmd, view) =>
            {
                ingress.ProcessSample(in sample, cmd, view);
                ingress.ProcessSample(new DispenseCountermeasure { UnitEntityId = AircraftId, Kind = 9 }, cmd, view);
            });
            var intent = Assert.Single(_simHost.Bus.Read<DispenseCountermeasureIntent>().ToArray());
            Assert.Equal((_onSimHost, DecoyKind.Flare, true), (intent.Unit, intent.Kind, intent.IsRemote));
        }
    }
}
