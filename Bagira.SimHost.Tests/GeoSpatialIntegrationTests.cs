using System.Collections.Generic;
using Bagira.DDS.DM;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Network.Cyclone.Services;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Tests
{
    public class GeoSpatialIntegrationTests
    {
        [Fact]
        public void SimTransform_To_GeoSpatial_IntegrationFlow()
        {
            // ── 1. World & geodetic transform ────────────────────────────────────
            var world = new EntityRepository();

            var wgs84 = new WGS84Transform();
            wgs84.SetOrigin(0, 0, 0);   // equator / prime meridian origin

            // ── 2. Register component types ──────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<GeoTransform>();
            world.RegisterComponent<GeoVelocity>();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkAuthority>();

            // ── 3. Create test entity ─────────────────────────────────────────────
            // WGS84Transform uses an ENU (East-North-Up) local frame:
            //   X = East,  Y = North,  Z = Up
            // 100 m East + 100 m North → both Latitude and Longitude ≈ +0.0009°
            var entity = world.CreateEntity();

            world.AddComponent(entity, new SimTransform
            {
                Position = new System.Numerics.Vector3(100f, 100f, 0f),
                Rotation = System.Numerics.Quaternion.Identity
            });
            world.AddComponent(entity, new SimVelocity
            {
                Linear  = System.Numerics.Vector3.Zero,
                Angular = System.Numerics.Vector3.Zero
            });
            world.AddComponent(entity, new NetworkIdentity(123));
            world.AddComponent(entity, new NetworkOwnership { PrimaryOwnerId = 1, LocalNodeId = 1 });
            world.AddComponent(entity, new NetworkAuthority(1, 1));
            world.AddComponent(entity, new GeoTransform());
            world.AddComponent(entity, new GeoVelocity());

            // ── 4. Execute SimTransformBridgeSystem directly ──────────────────────
            var system  = new SimTransformBridgeSystem(wgs84);
            var view    = (ISimulationView)world;
            var cmdBuf  = (EntityCommandBuffer)view.GetCommandBuffer();

            // Diagnostic A: confirm wgs84 conversion is non-trivial
            var (diagLat, diagLon, _) = wgs84.ToGeodetic(new System.Numerics.Vector3(100f, 100f, 0f));
            Assert.True(diagLat > 0.0001 || diagLon > 0.0001,
                $"WGS84 conversion should be non-zero: lat={diagLat}, lon={diagLon}");

            // Diagnostic B: confirm query finds the entity
            var q = view.Query().With<SimTransform>().With<NetworkOwnership>().Build();
            Assert.True(q.Any(), "Query<SimTransform,NetworkOwnership> returned 0 entities");

            system.Execute(view, 0.016f);   // queues SetComponent<GeoTransform>

            // Diagnostic C: confirm the system queued at least one command.
            Assert.True(cmdBuf.HasCommands, "SimTransformBridgeSystem queued no commands – query returned 0 entities");

            cmdBuf.Playback(world);          // flush: writes GeoTransform into world

            // Diagnostic D: show values before final assertion
            var geoTfDiag = world.GetComponent<GeoTransform>(entity);
            Assert.True(geoTfDiag.Latitude > 0.0001 || geoTfDiag.Longitude > 0.0001,
                $"GeoTransform not updated after Playback: lat={geoTfDiag.Latitude}, lon={geoTfDiag.Longitude}");

            // ── 5. Verify SimTransform → GeoTransform conversion ─────────────────
            // 100 m at the equator ≈ 0.000899° (≈ 1/111 000 deg/m)
            var geoTf = world.GetComponent<GeoTransform>(entity);

            Assert.InRange(geoTf.Latitude,  0.0008, 0.0010);   // Y=100 m North
            Assert.InRange(geoTf.Longitude, 0.0008, 0.0010);   // X=100 m East

            // ── 6. Egress-translator smoke test ───────────────────────────────────
            var p       = new DdsParticipant();
            var tkb     = new TkbDatabase();
            var map     = new NetworkEntityMap();
            var idAlloc = new DdsIdAllocator(p, "test");
            var elm     = new FDP.Toolkit.Lifecycle.EntityLifecycleModule(tkb, new List<int>());
            var spawner = new NetworkSpawningSystem(tkb, elm, map, idAlloc, 1);
            var doctrineRegistry = new DoctrineRegistry();
            var simHost = new SimHostModule(p, tkb, idAlloc, 1, spawner, map, doctrineRegistry, wgs84);

            var translator = simHost.GeoEgressTranslator;
            Assert.NotNull(translator);

            translator.ScanAndPublish(world);   // must not throw
        }
    }
}
