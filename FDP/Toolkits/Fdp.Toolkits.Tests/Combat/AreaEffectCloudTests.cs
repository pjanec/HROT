using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Weather;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — the gas / smoke cloud: it
    /// blocks sight through it at the world-query seam (and thins at its end), it drifts on the ground wind, a tear-gas cloud irritates
    /// whoever stands in it once a second, it ends at its time, and a tear-gas / smoke burst asks for one.
    /// </summary>
    public sealed class AreaEffectCloudTests : System.IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly AreaEffectCloudSystem _sys = new();
        private double _t;

        public AreaEffectCloudTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<AreaEffectCloud>();
            _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            _w.SetSingletonManaged(new TerrainWorld { BoundsMin = new Vector2(-100, -100), BoundsMax = new Vector2(100, 100) });
            Clock(0);
        }

        public void Dispose() => _w.Dispose();

        private void Clock(double t)
        {
            _t = t;
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });
        }

        private Entity Cloud(CloudKind kind, Vector3 at, float radius = 5f, float seconds = 30f, float transmittance = 0.1f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new AreaEffectCloud
            {
                Kind = kind, Radius = radius, Height = 3f, SightTransmittance = transmittance,
                IrritantSeconds = kind == CloudKind.TearGas ? 1.5f : 0f, BornAtSimSeconds = _t, Seconds = seconds,
            });
            return e;
        }

        private Entity Man(float x, float y)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Pedestrian });   // a person
            return e;
        }

        private Entity Car(float x, float y)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Health { Current = 500f, Max = 500f });
            _w.AddComponent(e, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.PersonalCar });
            return e;
        }

        private void Wind(Vector2 wind)
        {
            _w.RegisterComponent<ScenarioClock>();
            _w.RegisterComponent<WeatherConditions>();
            var env = _w.CreateEntity();
            _w.AddComponent(env, ScenarioClock.Default);
            _w.AddComponent(env, WeatherConditions.SteadyWind(wind));
        }

        /// <summary>One frame at <paramref name="t"/>: the system, then its deletes, then the events it published.</summary>
        private DamageAssessedEvent[] Frame(double t, float dt = 0.5f)
        {
            Clock(t);
            _sys.Execute(_w, dt);
            ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
            _w.Bus.SwapBuffers();
            return _w.Bus.Read<DamageAssessedEvent>().ToArray();
        }

        // ── sight ───────────────────────────────────────────────────────────────────────────

        [Fact]
        public void ASmokeCloud_BlocksSightThroughIt_NotBesideIt_AndThinsAtItsEnd()
        {
            var eye = new Vector3(-20, 0, 1.7f);
            var target = new Vector3(20, 0, 1.7f);
            var beside = new Vector3(20, 15, 1.7f);
            Assert.False(WorldQuery.Of(_w)!.SightBlocked(eye, target));   // 🔴 control: no cloud, clear

            Cloud(CloudKind.Smoke, Vector3.Zero, radius: 5f, seconds: 30f);
            var q = WorldQuery.Of(_w)!;
            Assert.True(q.SightBlocked(eye, target));
            Assert.False(q.SightBlocked(eye, beside));
            Assert.False(q.SightBlocked(new Vector3(-20, 0, 5f), new Vector3(20, 0, 5f)));   // over its 3 m top

            var trace = new List<TraceCrossing>();
            q.Trace(eye, target, TracePurpose.Sight, trace);
            var c = Assert.Single(trace, x => x.Kind == "cloud");
            Assert.InRange(c.T, 0.36f, 0.39f);   // it enters at x = -5 of a -20..20 line
            Assert.Equal(0.1f, c.Loss, 3);

            Clock(29);   // nine tenths through its thinning third: nearly clear
            Assert.False(WorldQuery.Of(_w)!.SightBlocked(eye, target));
            Clock(31);   // over: not read at all
            Assert.Null(AreaEffectClouds.Of(_w));
        }

        [Fact]
        public void ATearGasCloud_ThinsSight_ButASingleOneDoesNotBlockIt_TwoDo()
        {
            var eye = new Vector3(-30, 0, 1.7f);
            var target = new Vector3(30, 0, 1.7f);
            Cloud(CloudKind.TearGas, new Vector3(-10, 0, 0), transmittance: 0.6f);
            Assert.False(WorldQuery.Of(_w)!.SightBlocked(eye, target));   // 0.6 ≥ 0.5
            Cloud(CloudKind.TearGas, new Vector3(10, 0, 0), transmittance: 0.6f);
            Assert.True(WorldQuery.Of(_w)!.SightBlocked(eye, target));    // 0.36 < 0.5: the clouds multiply
        }

        // ── the owner's side ────────────────────────────────────────────────────────────────

        /// <summary>🔒 User: <i>"not every tick"</i> — the cloud steps once a second, by the wind × the time since its last step.</summary>
        [Fact]
        public void ACloud_DriftsOnTheGroundWind_OnceASecond()
        {
            Wind(new Vector2(2f, 0f));
            var cloud = Cloud(CloudKind.Smoke, Vector3.Zero);
            float X() => _w.GetComponentRO<SimTransform>(cloud).Position.X;
            Frame(0.5);
            Assert.Equal(1f, X(), 3);    // its first step: 2 m/s × the 0.5 s since it burst
            Frame(1.0);
            Assert.Equal(1f, X(), 3);    // not a second yet: it stays
            Frame(1.5);
            Assert.Equal(3f, X(), 3);    // a second later: 2 m/s × 1 s
            for (int i = 4; i <= 11; i++) Frame(i * 0.5);
            Assert.Equal(11f, X(), 3);   // 2 m/s for 5.5 s, in whole steps
            Assert.Equal(0f, _w.GetComponentRO<SimTransform>(cloud).Position.Y, 3);
        }

        [Fact]
        public void ACloud_WithNoWeather_StaysWhereItBurst()
        {
            var cloud = Cloud(CloudKind.Smoke, new Vector3(3, 4, 0));
            for (int i = 1; i <= 4; i++) Frame(i * 0.5);
            Assert.Equal(new Vector3(3, 4, 0), _w.GetComponentRO<SimTransform>(cloud).Position);
        }

        [Fact]
        public void ATearGasCloud_IrritatesWhoeverIsInside_OnceASecond_NotTheManOutside()
        {
            var inside = Man(2, 1);
            var outside = Man(9, 0);
            Cloud(CloudKind.TearGas, Vector3.Zero);

            var hits = new List<DamageAssessedEvent>();
            for (int i = 0; i <= 4; i++) hits.AddRange(Frame(i * 0.5));   // t = 0 .. 2 s

            Assert.Equal(3, hits.Count(h => h.HitEntity == inside));          // t = 0, 1, 2
            Assert.DoesNotContain(hits, h => h.HitEntity == outside);
            Assert.All(hits, h =>
            {
                Assert.Equal(HitEffect.Irritant, h.Effect);
                Assert.Equal(0f, h.TotalDamage);
                Assert.Equal(1.5f, h.EffectSeconds);
            });
        }

        [Fact]
        public void ATearGasCloud_GassesPeople_NotAVehicleInIt()
        {
            var man = Man(1, 0);
            var car = Car(-1, 0);
            Cloud(CloudKind.TearGas, Vector3.Zero);
            var hits = Frame(0);
            Assert.Contains(hits, h => h.HitEntity == man);
            Assert.DoesNotContain(hits, h => h.HitEntity == car);
        }

        [Fact]
        public void ASmokeCloud_IrritatesNobody()
        {
            Man(0, 0);
            Cloud(CloudKind.Smoke, Vector3.Zero);
            Assert.Empty(Frame(0));
            Assert.Equal(0, _sys.Pulses);
        }

        [Fact]
        public void ACloud_EndsAtItsTime()
        {
            var cloud = Cloud(CloudKind.Smoke, Vector3.Zero, seconds: 2f);
            Frame(1.5);
            Assert.True(_w.IsAlive(cloud));
            Frame(2.0);
            Assert.False(_w.IsAlive(cloud));
        }

        /// <summary>⭐ <c>CE-3228</c> — a REPLICATED cloud (it has a network identity) ends through the one teardown path: a
        /// <c>DestroyEntityCommand</c>, published ONCE, never a local destroy (which left every replica behind — the cross-node rail).
        /// ✅ Red-proof: the direct destroy back ⇒ the cloud is gone here and no command is published ⇒ red.</summary>
        [Fact]
        public void CE3228_AReplicatedCloud_EndsThroughTheTeardownCommand_Once()
        {
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            _w.RegisterManagedEvent<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>();
            var cloud = Cloud(CloudKind.Smoke, Vector3.Zero, seconds: 2f);
            _w.AddComponent(cloud, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 77 });

            Frame(2.0);
            var asked = _w.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>().ToArray();
            Assert.True(_w.IsAlive(cloud));                     // the teardown, not this system, removes it
            Assert.Equal(77, Assert.Single(asked).NetworkId);

            Frame(2.5);
            Assert.Empty(_w.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>());   // asked once
        }

        [Fact]
        public void ACloudThisNodeDoesNotOwn_IsNeitherMovedNorEnded()
        {
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
            Wind(new Vector2(2f, 0f));
            var cloud = Cloud(CloudKind.TearGas, Vector3.Zero, seconds: 1f);
            _w.AddComponent(cloud, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var man = Man(0, 0);
            var hits = Frame(1.5);
            Assert.True(_w.IsAlive(cloud));
            Assert.Equal(Vector3.Zero, _w.GetComponentRO<SimTransform>(cloud).Position);
            Assert.Empty(hits);
        }

        // ── the burst asks for it ───────────────────────────────────────────────────────────

        [Fact]
        public void ATearGasBurst_AsksForACloud_AKineticOneDoesNot()
        {
            const long Gas = 6101, Plain = 6102;
            var db = new TkbDatabase();
            var gas = new TkbTemplate("test tear-gas grenade", Gas);
            gas.AddDescriptor(new WarheadDto { Kind = WarheadKind.Irritant, CloudRadiusM = 6f, CloudHeightM = 2.5f, CloudSeconds = 40f, EffectSeconds = 2f });
            db.Register(gas);
            db.Register(new TkbTemplate("test ball", Plain));
            _w.SetSingletonManaged<ITkbDatabase>(db);
            Clock(12);

            var area = new AreaEffectSystem();
            _w.Bus.Publish(new DetonationNotification { HitX = 4, HitY = 5, HitZ = 0, Ammo = Gas });
            _w.Bus.Publish(new DetonationNotification { HitX = 9, HitY = 9, HitZ = 0, Ammo = Plain });
            _w.Bus.Publish(new DetonationNotification { HitX = 7, HitY = 7, HitZ = 0, Ammo = Gas, IsRemote = true });   // another node's
            _w.Bus.SwapBuffers();
            area.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();

            // ⭐ CE-3228 — THE entity-creation request on the FDP bus, owner 0 (the system picks): the same request every author and the wire use
            var request = Assert.Single(_w.Bus.ReadManaged<EntityCreationRequest>().ToArray());
            Assert.Equal(AreaEffectClouds.TkbType, request.TkbType);
            Assert.Equal(0, request.OwnerAppInstanceId);
            Assert.NotEqual(System.Guid.Empty, request.RequestId);
            Assert.Equal(new Vector3(4, 5, 0), Assert.Single(request.InitialComponents!.OfType<SimTransform>()).Position);
            var cloud = Assert.Single(request.InitialComponents!.OfType<AreaEffectCloud>());
            Assert.Equal(CloudKind.TearGas, cloud.Kind);
            Assert.Equal(6f, cloud.Radius);
            Assert.Equal(2.5f, cloud.Height);
            Assert.Equal(40f, cloud.Seconds);
            Assert.Equal(12.0, cloud.BornAtSimSeconds);
            Assert.Equal(2f, cloud.IrritantSeconds);
            Assert.True(cloud.SightTransmittance is > 0f and < 1f);
        }

        // ── ⭐ CE-3236 (B-14, §7.15) — the cloud's SOURCE ───────────────────────────────────

        /// <summary>
        /// ⭐ VC-4 — a cloud SITS on its venting canister while that lives (no drift in the wind), and once the canister is gone (picked up)
        /// it lets go and drifts from where it was. ✅ Red-proof: <c>SitsOnItsSource</c> returning false ⇒ it drifts while the canister vents
        /// ⇒ red.
        /// </summary>
        [Fact]
        public void CE3236_ACloud_SitsOnItsCanister_ThenDriftsFreeWhenItIsGone()
        {
            Wind(new Vector2(2f, 0f));
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var canister = _w.CreateEntity();
            _w.AddComponent(canister, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.AddComponent(canister, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 777 });
            var cloud = Cloud(CloudKind.TearGas, Vector3.Zero);
            _w.GetComponentRW<AreaEffectCloud>(cloud).SourceNetworkId = 777;
            float X() => _w.GetComponentRO<SimTransform>(cloud).Position.X;

            for (int i = 1; i <= 6; i++) Frame(i * 0.5);
            Assert.Equal(0f, X(), 3);                                                       // venting: it stays on its canister
            Assert.Equal(777, _w.GetComponentRO<AreaEffectCloud>(cloud).SourceNetworkId);

            _w.DestroyEntity(canister);                                                     // picked up
            Frame(3.5);
            Assert.Equal(0, _w.GetComponentRO<AreaEffectCloud>(cloud).SourceNetworkId);     // let go (replicates: free) …
            Assert.Equal(2f, X(), 3);                                                       // … and drifts from that step: 2 m/s × 1 s
            Frame(4.5);
            Assert.Equal(4f, X(), 3);
        }

        /// <summary>
        /// ⭐ VC-2 / VC-3 — a burst that names its round (a canister lying, venting) asks for a cloud that lasts the round's BURN and sits on it
        /// (its network id). ✅ Red-proof: <c>RequestCloud</c> ignoring the round ⇒ the warhead's 40 s and no source ⇒ red.
        /// </summary>
        [Fact]
        public void CE3236_AVentingCanistersBurst_AsksForACloud_ForItsBurn_SittingOnIt()
        {
            const long Gas = 6101;
            var db = new TkbDatabase();
            var gas = new TkbTemplate("test tear-gas canister", Gas);
            gas.AddDescriptor(new WarheadDto { Kind = WarheadKind.Irritant, CloudRadiusM = 6f, CloudSeconds = 40f, EffectSeconds = 2f });
            db.Register(gas);
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.RegisterComponent<BallisticProjectile>();
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var round = _w.CreateEntity();
            _w.AddComponent(round, new BallisticProjectile
            {
                Ammo = Gas, BurnSeconds = 17.5f, FuzeRemaining = 17.5f,
                Warhead = WarheadRound.Area | WarheadRound.Vents | WarheadRound.Landed | WarheadRound.Lingers,
            });
            _w.AddComponent(round, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 4711 });
            Clock(3);

            _w.Bus.Publish(new DetonationNotification { HitX = 1, HitY = 2, HitZ = 0, Ammo = Gas, Round = round });
            _w.Bus.SwapBuffers();
            new AreaEffectSystem().Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();

            var cloud = Assert.Single(Assert.Single(_w.Bus.ReadManaged<EntityCreationRequest>().ToArray()).InitialComponents!.OfType<AreaEffectCloud>());
            Assert.Equal((17.5f, 4711L), (cloud.Seconds, cloud.SourceNetworkId));
        }
    }
}
