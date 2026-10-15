using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Common.EntityCreation;
using Hrot.Core.Network;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Airborne insertion B-8 / B-9 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §7d, D-14…D-17) — an aircraft's crew EJECTS
    /// on the node owning its Health: the pilot is REQUESTED through the entity-creation path (at the seat, the aircraft's velocity + the
    /// rocket, <c>Descent{Ejection}</c>), THEN the aircraft gets all its health as damage (it falls as a killed one). Once; only while alive;
    /// never on a node that does not own its Health; on an order or on its own below the type's threshold.
    /// </summary>
    public sealed class EjectionSystemTests : System.IDisposable
    {
        private const long JetType = 7405, PilotType = 7206;
        private readonly EntityRepository _w = new();
        private readonly EjectionSystem _sys = new();
        private readonly ScenarioEntityCreationRequestSource _requests = new();

        public EjectionSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterManagedComponent<DescriptorOwnership>();
            _w.RegisterEvent<EjectRequestEvent>();
            _w.RegisterEvent<DamageAssessedEvent>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var jet = new Fdp.Interfaces.TkbTemplate("Jet", JetType);
            jet.AddDescriptor(new EjectionDto { PilotType = PilotType, SeatX = 4.5f, SeatZ = 1.2f, AutoEjectBelowFraction = 0.2f });
            db.Register(jet);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            _w.SetSingletonManaged(_requests);
        }

        public void Dispose() => _w.Dispose();

        private Entity Jet(float health = 600f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(100, 200, 300), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
            _w.AddComponent(e, new SimVelocity { Linear = new Vector3(0, 150, 0) });
            _w.AddComponent(e, new Health { Current = health, Max = 600 });
            _w.AddComponent(e, new TkbIdentity { TkbType = JetType });
            return e;
        }

        private (List<EntityCreationRequest> Spawned, float Damage) Frame()
        {
            _w.Bus.SwapBuffers();
            _sys.Execute(_w, 1f / 60f);
            var spawned = new List<EntityCreationRequest>();
            _requests.ProcessRequests(spawned.Add);
            _w.Bus.SwapBuffers();
            float damage = _w.Bus.Read<DamageAssessedEvent>().ToArray().Sum(d => d.TotalDamage);
            return (spawned, damage);
        }

        /// <summary>⭐ The order: one pilot at the seat (the jet faces north, so the seat 4.5 m ahead is 4.5 m north), with the jet's velocity
        /// plus the rocket's climb and a <c>Descent{Ejection}</c>; the jet gets all its health as damage; a second order does nothing.
        /// ✅ Red-proof: the latch removed ⇒ the second order spawns a second pilot ⇒ red.</summary>
        [Fact]
        public void B8_AnOrderedEjection_SpawnsOnePilotAtTheSeat_AndAbandonsTheJet()
        {
            var jet = Jet();
            _w.Bus.Publish(new EjectRequestEvent { Aircraft = jet });
            var (spawned, damage) = Frame();
            var pilot = Assert.Single(spawned);
            Assert.Equal(PilotType, pilot.TkbType);
            var tf = pilot.InitialComponents!.OfType<SimTransform>().Single();
            Assert.True(Vector3.Distance(new Vector3(100, 204.5f, 301.2f), tf.Position) < 1e-3f, $"at {tf.Position}");
            Assert.Equal(new Vector3(0, 150, ParachuteModel.EjectUpMps), pilot.InitialComponents!.OfType<SimVelocity>().Single().Linear);
            Assert.Equal(DescentKind.Ejection, pilot.InitialComponents!.OfType<Descent>().Single().Kind);
            Assert.Equal(600f, damage);
            Assert.Equal(1, _sys.Ejections);

            _w.Bus.Publish(new EjectRequestEvent { Aircraft = jet });
            Assert.Empty(Frame().Spawned);
        }

        /// <summary>⭐ B-9 — on its own: at 25 % health nothing; at 15 % (below the type's 20 %) once; a jet already dead never.</summary>
        [Fact]
        public void B9_TheCrewEjectsOnItsOwn_BelowTheTypesThreshold_Once()
        {
            var jet = Jet(health: 150f);
            Assert.Empty(Frame().Spawned);
            _w.SetComponent(jet, new Health { Current = 90f, Max = 600 });
            Assert.Single(Frame().Spawned);
            Assert.Empty(Frame().Spawned);
            Jet(health: 0f);
            Assert.Empty(Frame().Spawned);
        }

        /// <summary>⭐ D-14 — a node that does not own the jet's Health ejects nobody (the owner does).</summary>
        [Fact]
        public void B8_ANodeNotOwningTheJetsHealth_EjectsNobody()
        {
            var jet = Jet();
            _w.AddComponent(jet, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            _w.Bus.Publish(new EjectRequestEvent { Aircraft = jet });
            Assert.Empty(Frame().Spawned);
        }

        /// <summary>⭐ B-9 — the behaviour's Eject action raises the order once and succeeds once the aircraft is down.</summary>
        [Fact]
        public void B9_TheEjectAction_RaisesTheOrderOnce_AndSucceedsWhenTheJetIsDown()
        {
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.InteractionChannel>();
            var jet = Jet();
            var exec = new Fdp.Toolkit.Behavior.Executors.EjectExecutor();
            var channel = new Fdp.Toolkit.Behavior.Components.InteractionChannel();
            exec.OnEnter(jet, ref channel, _w);
            exec.Execute(jet, ref channel, _w, 1f / 60f);
            exec.Execute(jet, ref channel, _w, 1f / 60f);
            _w.Bus.SwapBuffers();
            Assert.Single(_w.Bus.Read<EjectRequestEvent>().ToArray());
            Assert.Equal(Fbt.NodeStatus.Running, channel.Status);
            _w.SetComponent(jet, new Health { Current = 0f, Max = 600 });
            exec.Execute(jet, ref channel, _w, 1f / 60f);
            Assert.Equal(Fbt.NodeStatus.Success, channel.Status);
        }
    }
}
