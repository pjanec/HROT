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
    /// crosses the wire: the missile warning SimHost → the aircraft's brain, the flare release brain → SimHost (an ordinary fire request,
    /// BS-1-DESIGN §5.4c) and the SimHost's fire back to every viewer. Two worlds with their
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
                w.RegisterEvent<WeaponFireIntent>();
                w.RegisterEvent<WeaponFireNotification>();
                w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
                w.RegisterEvent<SensorChangedEvent>();
            }
            _onSimHost = _simHost.CreateEntity();
            _onBrain = _brain.CreateEntity();
            _brain.AddComponent(_onBrain, new SimTransform { Position = new Vector3(1000, 1000, 300), Rotation = Quaternion.Identity });
            _brain.AddComponent(_onBrain, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
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
        /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (BS-1-DESIGN §5.4c) — a flare release crosses as an ORDINARY fire both ways: the brain's
        /// <see cref="WeaponFireIntent"/> (the dispenser mount, the flare's type, at the aircraft) reaches the SimHost as a remote intent on
        /// ITS entity with the type intact; the SimHost's <see cref="WeaponFireNotification"/> reaches a viewer with every field of the shot
        /// (type, origin, velocity, quantity, time; no round). 🔴 Red-proof: before F1 the wire carried only shooter, target and slot.
        /// </summary>
        [Fact]
        public void CE2188_AFlareRelease_CrossesAsAnOrdinaryFire_BothWays()
        {
            const long flare = 6034;
            var request = new CapturingWriter<WeaponFireRequest>();
            var intentEgress = new WeaponFireIntentEgressTranslator(request, _brainMap);
            _brain.Bus.Publish(new WeaponFireIntent
            {
                Shooter = _onBrain, Target = Entity.Null, WeaponIndex = 2, AtPoint = true,
                TargetPoint = new Vector3(1000, 1000, 300), AmmoGuid = flare, RaisedAt = 50.0,
            });
            _brain.Bus.SwapBuffers();
            intentEgress.ScanAndPublish(_brain);
            var asked = Assert.Single(request.Written);
            var requestIngress = new WeaponFireRequestIngressTranslator(participant: null, _simHostMap);
            Apply(_simHost, (cmd, view) => requestIngress.ProcessSample(in asked, cmd, view));
            var intent = Assert.Single(_simHost.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal((_onSimHost, 2, flare, true, true), (intent.Shooter, intent.WeaponIndex, intent.AmmoGuid, intent.AtPoint, intent.IsRemote));

            var fire = new CapturingWriter<WeaponFire>();
            var fireEgress = new WeaponFireNotificationEgressTranslator(fire, _simHostMap, localNodeId: 1);   // SimHost
            _simHost.Bus.Publish(new WeaponFireNotification
            {
                Shooter = _onSimHost, Target = Entity.Null, WeaponIndex = 2, AmmoGuid = flare, Round = Entity.Null,
                Origin = new Vector3(1000, 1000, 300), Velocity = new Vector3(0, 60, 0), Quantity = 1, FiredAt = 50.1,
            });
            _simHost.Bus.SwapBuffers();
            fireEgress.ScanAndPublish(_simHost);
            var sent = Assert.Single(fire.Written);
            Assert.Equal(0L, sent.RoundEntityId);
            var fireIngress = new Hrot.Network.NED.IG.WeaponFireIngressTranslator(participant: null, _brainMap, localNodeId: 2);   // the brain: another node's shot is kept
            Apply(_brain, (cmd, view) => fireIngress.ProcessSample(in sent, cmd));
            var seen = Assert.Single(_brain.Bus.Read<WeaponFireNotification>().ToArray());
            Assert.Equal((_onBrain, 2, flare, Entity.Null, 1, true), (seen.Shooter, seen.WeaponIndex, seen.AmmoGuid, seen.Round, seen.Quantity, seen.IsRemote));
            Assert.Equal(new Vector3(1000, 1000, 300), seen.Origin);
            Assert.Equal(new Vector3(0, 60, 0), seen.Velocity);
            Assert.Equal(50.1, seen.FiredAt, 6);
        }
    }
}
